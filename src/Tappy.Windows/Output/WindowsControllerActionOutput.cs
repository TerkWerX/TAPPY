using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Tappy.Core.Models;
using Tappy.Core.Output;
using Tappy.Core.Profiles;

namespace Tappy.Windows.Output;

public sealed class WindowsControllerActionOutput : IControllerActionOutput, IDisposable
{
    private readonly ConcurrentDictionary<string, ActiveExecution> _active = new(StringComparer.Ordinal);
    private readonly object _dispatchGate = new();
    private readonly SendInputKeyboardOutput _keyboard;
    private readonly WinMmMidiOutput _midi = new();
    private readonly UdpClient _osc = new();
    private int _disposed;

    public WindowsControllerActionOutput(SendInputKeyboardOutput? keyboard = null)
    {
        _keyboard = keyboard ?? new SendInputKeyboardOutput();
    }

    public event EventHandler<ControllerActionOutputFault>? Faulted;

    public bool Start(ControllerActionOutputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (request.InjectionMarker != InjectedInputMarker.Value || request.Sequence.IsEmpty)
        {
            return false;
        }

        var execution = new ActiveExecution(this, request);
        if (!_active.TryAdd(request.OwnerId, execution))
        {
            execution.Dispose();
            return false;
        }

        execution.Start();
        return true;
    }

    public bool ReleaseOwner(string ownerId)
    {
        if (!_active.TryGetValue(ownerId, out var execution))
        {
            return true;
        }

        return execution.CancelAndRelease();
    }

    public bool ReleaseScope(string scopeId)
    {
        var success = true;
        foreach (var execution in _active.Values.Where(item =>
                     item.Request.ScopeId.Equals(scopeId, StringComparison.Ordinal)).ToArray())
        {
            success &= execution.CancelAndRelease();
        }

        return success;
    }

    public bool ReleaseAll()
    {
        var success = true;
        foreach (var execution in _active.Values.ToArray())
        {
            success &= execution.CancelAndRelease();
        }

        try
        {
            _midi.Reset();
        }
        catch
        {
            success = false;
        }

        return success;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _ = ReleaseAll();
        _osc.Dispose();
        _midi.Dispose();
    }

    private async Task RunAsync(ActiveExecution execution)
    {
        try
        {
            var sequence = execution.Request.Sequence;
            switch (sequence.Mode)
            {
                case ControllerActionSequenceMode.RepeatWhileHeld:
                    execution.CancelAfter(TimeSpan.FromSeconds(20));
                    do
                    {
                        await ExecuteStepsAsync(execution).ConfigureAwait(false);
                        await Task.Delay(10, execution.Token).ConfigureAwait(false);
                    } while (!execution.Token.IsCancellationRequested);
                    break;
                case ControllerActionSequenceMode.WhileHeld:
                    await ExecuteStepsAsync(execution).ConfigureAwait(false);
                    await Task.Delay(Timeout.InfiniteTimeSpan, execution.Token).ConfigureAwait(false);
                    break;
                default:
                    await ExecuteStepsAsync(execution).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException) when (execution.Token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, new ControllerActionOutputFault(
                execution.Request.OwnerId,
                execution.Request.ScopeId,
                exception.Message,
                exception));
        }
        finally
        {
            _ = execution.ReleaseHeld();
            _active.TryRemove(execution.Request.OwnerId, out _);
            execution.Dispose();
        }
    }

    private async Task ExecuteStepsAsync(ActiveExecution execution)
    {
        var steps = execution.Request.Sequence.Steps;
        if (steps.Count > 500)
        {
            throw new InvalidOperationException("An action sequence cannot exceed 500 steps.");
        }

        using var duration = CancellationTokenSource.CreateLinkedTokenSource(execution.Token);
        duration.CancelAfter(TimeSpan.FromSeconds(30));
        foreach (var step in steps)
        {
            duration.Token.ThrowIfCancellationRequested();
            await ExecuteStepAsync(execution, step, duration.Token).ConfigureAwait(false);
        }
    }

    private async Task ExecuteStepAsync(
        ActiveExecution execution,
        ControllerActionStepSnapshot step,
        CancellationToken cancellationToken)
    {
        var owner = execution.Request.OwnerId;
        var keyboardRequest = new KeyboardOutputRequest(
            owner, step.Keys, execution.Request.InjectionMarker, execution.Request.Ancestry);
        switch (step.Type)
        {
            case ControllerActionStepType.KeyboardChord:
                execution.KeyDown(keyboardRequest);
                if (execution.Request.Sequence.Mode == ControllerActionSequenceMode.WhileHeld)
                {
                    return;
                }
                try
                {
                    await Task.Delay(Math.Clamp(step.DurationMs, 1, 5_000), cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    execution.KeyUp(keyboardRequest);
                }
                break;
            case ControllerActionStepType.KeyDown:
                execution.KeyDown(keyboardRequest);
                break;
            case ControllerActionStepType.KeyUp:
                execution.KeyUp(keyboardRequest);
                break;
            case ControllerActionStepType.Text:
                SendText(execution, step.Value);
                break;
            case ControllerActionStepType.Delay:
                await Task.Delay(Math.Clamp(step.DurationMs, 1, 600_000), cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ControllerActionStepType.MouseButton:
                execution.Mouse(step.Value);
                break;
            case ControllerActionStepType.MouseMove:
                Dispatch(execution, () => SendMouse(
                    MouseFlags.Move, 0, step.X, step.Y, execution.Request.InjectionMarker));
                break;
            case ControllerActionStepType.MouseWheel:
                Dispatch(execution, () => SendMouse(
                    step.Value.Equals("horizontal", StringComparison.OrdinalIgnoreCase)
                        ? MouseFlags.HorizontalWheel
                        : MouseFlags.VerticalWheel,
                    unchecked((uint)step.Amount), 0, 0, execution.Request.InjectionMarker));
                break;
            case ControllerActionStepType.LaunchProgram:
                Dispatch(execution, () => LaunchProgram(step));
                break;
            case ControllerActionStepType.PowerShellCommand:
                Dispatch(execution, () => LaunchPowerShell(step));
                break;
            case ControllerActionStepType.Midi:
                execution.SendMidi(step);
                break;
            case ControllerActionStepType.Osc:
                await SendOscAsync(step, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Unsupported controller action step: {step.Type}.");
        }
    }

    private static void LaunchProgram(ControllerActionStepSnapshot step)
    {
        var path = Environment.ExpandEnvironmentVariables(step.Value.Trim());
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("The program step has no path or document.");
        }

        var info = new ProcessStartInfo
        {
            FileName = path,
            Arguments = step.Arguments,
            UseShellExecute = true
        };
        var workingDirectory = Environment.ExpandEnvironmentVariables(step.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException($"The working directory does not exist: {workingDirectory}");
            }

            info.WorkingDirectory = workingDirectory;
        }

        if (Process.Start(info) is null)
        {
            throw new InvalidOperationException("Windows could not launch the requested program or document.");
        }
    }

    public static ProcessStartInfo BuildPowerShellStartInfo(ControllerActionStepSnapshot step)
    {
        if (string.IsNullOrWhiteSpace(step.Value))
        {
            throw new InvalidOperationException("The PowerShell step has no command.");
        }

        var executable = step.Target switch
        {
            "PowerShell 7" or "pwsh" or "pwsh.exe" => "pwsh.exe",
            "" or "Windows PowerShell 5.1" or "powershell" or "powershell.exe" => "powershell.exe",
            _ => throw new InvalidOperationException("The PowerShell host must be Windows PowerShell 5.1 or PowerShell 7.")
        };
        var result = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        result.ArgumentList.Add("-NoLogo");
        result.ArgumentList.Add("-NoProfile");
        result.ArgumentList.Add("-NonInteractive");
        result.ArgumentList.Add("-Command");
        result.ArgumentList.Add(step.Value);
        var workingDirectory = Environment.ExpandEnvironmentVariables(step.WorkingDirectory);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException($"The PowerShell working directory does not exist: {workingDirectory}");
            }

            result.WorkingDirectory = workingDirectory;
        }

        return result;
    }

    private static void LaunchPowerShell(ControllerActionStepSnapshot step)
    {
        if (Process.Start(BuildPowerShellStartInfo(step)) is null)
        {
            throw new InvalidOperationException("Windows could not start PowerShell.");
        }
    }

    private async Task SendOscAsync(ControllerActionStepSnapshot step, CancellationToken cancellationToken)
    {
        var packet = OscPacketBuilder.Build(step.Value, step.Arguments);
        var host = string.IsNullOrWhiteSpace(step.Target) ? "127.0.0.1" : step.Target;
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        var address = addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork)
                      ?? addresses.FirstOrDefault()
                      ?? throw new InvalidOperationException($"OSC host could not be resolved: {host}");
        var endpoint = new IPEndPoint(address, Math.Clamp(step.Amount == 0 ? 8000 : step.Amount, 1, 65535));
        cancellationToken.ThrowIfCancellationRequested();
        Dispatch(execution: null, cancellationToken, () => _osc.Send(packet, endpoint));
    }

    private void SendText(ActiveExecution execution, string text)
    {
        foreach (var character in text)
        {
            Dispatch(execution, () => SendNative([
                NativeInput.Unicode(character, false, execution.Request.InjectionMarker),
                NativeInput.Unicode(character, true, execution.Request.InjectionMarker)
            ]));
        }
    }

    private void Dispatch(ActiveExecution execution, Action action) =>
        Dispatch(execution, execution.Token, action);

    private void Dispatch(ActiveExecution? execution, CancellationToken cancellationToken, Action action)
    {
        lock (_dispatchGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
        }
    }

    private static void SendMouse(MouseFlags flags, uint data, int x, int y, ulong marker) =>
        SendNative([NativeInput.Mouse(flags, data, x, y, marker)]);

    private static void SendNative(NativeInput[] inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        var inserted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (inserted != inputs.Length)
        {
            throw new InvalidOperationException(
                $"Windows accepted {inserted} of {inputs.Length} action output events.");
        }
    }

    private sealed class ActiveExecution : IDisposable
    {
        private readonly WindowsControllerActionOutput _owner;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _heldGate = new();
        private readonly HashSet<Tappy.Core.Output.KeyboardOutputKey> _heldKeys = [];
        private readonly HashSet<string> _heldMouseButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Device, MidiShortMessage NoteOff)> _heldMidi = [];
        private int _released;

        internal ActiveExecution(WindowsControllerActionOutput owner, ControllerActionOutputRequest request)
        {
            _owner = owner;
            Request = request;
        }

        internal ControllerActionOutputRequest Request { get; }
        internal CancellationToken Token => _cancellation.Token;

        internal void CancelAfter(TimeSpan timeout) => _cancellation.CancelAfter(timeout);

        internal void Start() => _ = Task.Run(() => _owner.RunAsync(this));

        internal void KeyDown(KeyboardOutputRequest request)
        {
            if (request.Keys.Count == 0)
            {
                throw new InvalidOperationException("A keyboard action requires at least one key.");
            }

            _owner.Dispatch(this, () =>
            {
                _owner._keyboard.KeyDown(request);
                lock (_heldGate)
                {
                    foreach (var key in request.Keys)
                    {
                        _heldKeys.Add(key);
                    }
                }
            });
        }

        internal void KeyUp(KeyboardOutputRequest request)
        {
            KeyboardOutputKey[] releasing;
            lock (_heldGate)
            {
                releasing = request.Keys.Where(_heldKeys.Remove).ToArray();
            }

            if (releasing.Length > 0)
            {
                lock (_owner._dispatchGate)
                {
                    _owner._keyboard.KeyUp(request with { Keys = releasing });
                }
            }
        }

        internal void Mouse(string description)
        {
            var normalized = (description ?? string.Empty).Trim().ToLowerInvariant();
            var button = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "left";
            var (down, up, data) = MouseButtonFlags(button);
            if (normalized.EndsWith("down", StringComparison.Ordinal) ||
                Request.Sequence.Mode == ControllerActionSequenceMode.WhileHeld)
            {
                _owner.Dispatch(this, () =>
                {
                    SendMouse(down, data, 0, 0, Request.InjectionMarker);
                    lock (_heldGate)
                    {
                        _heldMouseButtons.Add(button);
                    }
                });
            }
            else if (normalized.EndsWith("up", StringComparison.Ordinal))
            {
                var shouldRelease = false;
                lock (_heldGate)
                {
                    shouldRelease = _heldMouseButtons.Remove(button);
                }

                if (shouldRelease)
                {
                    lock (_owner._dispatchGate)
                    {
                        SendMouse(up, data, 0, 0, Request.InjectionMarker);
                    }
                }
            }
            else
            {
                _owner.Dispatch(this, () => SendNative([
                        NativeInput.Mouse(down, data, 0, 0, Request.InjectionMarker),
                        NativeInput.Mouse(up, data, 0, 0, Request.InjectionMarker)
                    ]));
            }
        }

        internal void SendMidi(ControllerActionStepSnapshot step)
        {
            var message = MidiMessageParser.Parse(step.Value);
            _owner.Dispatch(this, () =>
            {
                _owner._midi.Send(step.Target, message);
                if (message.IsNoteOn && Request.Sequence.Mode == ControllerActionSequenceMode.WhileHeld)
                {
                    lock (_heldGate)
                    {
                        _heldMidi.Add((step.Target, message.ToNoteOff()));
                    }
                }
                else if (message.Kind is MidiShortMessageKind.NoteOff or MidiShortMessageKind.NoteOn)
                {
                    lock (_heldGate)
                    {
                        _heldMidi.RemoveAll(item =>
                            item.Device.Equals(step.Target, StringComparison.OrdinalIgnoreCase) &&
                            item.NoteOff.Channel == message.Channel &&
                            item.NoteOff.Data1 == message.Data1);
                    }
                }
            });
        }

        internal bool CancelAndRelease()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            return ReleaseHeld();
        }

        internal bool ReleaseHeld()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return true;
            }

            KeyboardOutputKey[] keys;
            string[] mouseButtons;
            (string Device, MidiShortMessage NoteOff)[] midi;
            lock (_heldGate)
            {
                keys = _heldKeys.Reverse().ToArray();
                mouseButtons = _heldMouseButtons.ToArray();
                midi = _heldMidi.ToArray();
                _heldKeys.Clear();
                _heldMouseButtons.Clear();
                _heldMidi.Clear();
            }

            var success = true;
            lock (_owner._dispatchGate)
            {
                try
                {
                    if (keys.Length > 0)
                    {
                        _owner._keyboard.KeyUp(new KeyboardOutputRequest(
                            Request.OwnerId, keys, Request.InjectionMarker, Request.Ancestry));
                    }
                }
                catch
                {
                    success = false;
                }

                foreach (var button in mouseButtons)
                {
                    try
                    {
                        var (_, up, data) = MouseButtonFlags(button);
                        SendMouse(up, data, 0, 0, Request.InjectionMarker);
                    }
                    catch
                    {
                        success = false;
                    }
                }

                foreach (var (device, noteOff) in midi)
                {
                    try
                    {
                        _owner._midi.Send(device, noteOff);
                    }
                    catch
                    {
                        success = false;
                    }
                }
            }

            return success;
        }

        public void Dispose() => _cancellation.Dispose();
    }

    private static (MouseFlags Down, MouseFlags Up, uint Data) MouseButtonFlags(string button) =>
        button.Trim().ToLowerInvariant() switch
        {
            "right" => (MouseFlags.RightDown, MouseFlags.RightUp, 0),
            "middle" => (MouseFlags.MiddleDown, MouseFlags.MiddleUp, 0),
            "x1" => (MouseFlags.XDown, MouseFlags.XUp, 1),
            "x2" => (MouseFlags.XDown, MouseFlags.XUp, 2),
            _ => (MouseFlags.LeftDown, MouseFlags.LeftUp, 0)
        };

    [Flags]
    private enum MouseFlags : uint
    {
        Move = 0x0001,
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        MiddleDown = 0x0020,
        MiddleUp = 0x0040,
        XDown = 0x0080,
        XUp = 0x0100,
        VerticalWheel = 0x0800,
        HorizontalWheel = 0x1000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;

        public static NativeInput Unicode(char character, bool keyUp, ulong marker) => new()
        {
            Type = 1,
            Data = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    ScanCode = character,
                    Flags = (uint)(KeyboardInjectionFlags.Unicode |
                                   (keyUp ? KeyboardInjectionFlags.KeyUp : KeyboardInjectionFlags.None)),
                    ExtraInformation = checked((nuint)marker)
                }
            }
        };

        public static NativeInput Mouse(MouseFlags flags, uint data, int x, int y, ulong marker) => new()
        {
            Type = 0,
            Data = new NativeInputUnion
            {
                Mouse = new NativeMouseInput
                {
                    X = x,
                    Y = y,
                    MouseData = data,
                    Flags = (uint)flags,
                    ExtraInformation = checked((nuint)marker)
                }
            }
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)] public NativeMouseInput Mouse;
        [FieldOffset(0)] public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int structureSize);
}
