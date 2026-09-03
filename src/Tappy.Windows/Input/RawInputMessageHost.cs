using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Tappy.Windows.Interop;

namespace Tappy.Windows.Input;

/// <summary>
/// Owns Raw Input registration, parsing, and the native message pump on a dedicated
/// background thread. No WPF dispatcher participates in this path.
/// </summary>
public sealed class RawInputMessageHost : IRawHidInputMessageHost
{
    internal const uint MaximumRawInputByteCount = 4 * 1024;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly RawInputNativeMethods.WindowProcedure _windowProcedure;
    private Thread? _thread;
    private TaskCompletionSource? _ready;
    private TaskCompletionSource? _stopped;
    private nint _windowHandle;
    private uint _nativeThreadId;
    private string? _windowClassName;
    private bool _disposed;

    public RawInputMessageHost()
    {
        _windowProcedure = WindowProcedure;
    }

    public event EventHandler<RawKeyboardPacketEventArgs>? KeyboardPacketReceived;

    public event EventHandler<RawHidInputPacketEventArgs>? HidPacketReceived;

    public event EventHandler<NativeDeviceChangeEventArgs>? DeviceChanged;

    public event EventHandler<WindowsLifecycleSignalEventArgs>? LifecycleChanged;

    public event EventHandler<Exception>? Faulted;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _thread is { IsAlive: true } && _windowHandle != nint.Zero;
            }
        }
    }

    public nint WindowHandle
    {
        get
        {
            lock (_gate)
            {
                return _windowHandle;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Task readyTask;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_thread is { IsAlive: true })
                {
                    readyTask = _ready?.Task ?? Task.CompletedTask;
                }
                else
                {
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _ready = ready;
                    _stopped = stopped;
                    _thread = new Thread(() => MessageThreadMain(ready, stopped))
                    {
                        IsBackground = true,
                        Name = "Tappy Raw Input Message Thread",
                    };
                    readyTask = ready.Task;
                    _thread.Start();
                }
            }

            await readyTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Task? readyTask;
            Task? stoppedTask;
            lock (_gate)
            {
                if (_thread is not { IsAlive: true })
                {
                    return;
                }

                readyTask = _ready?.Task;
                stoppedTask = _stopped?.Task;
            }

            if (readyTask is not null)
            {
                try
                {
                    await WaitForStopPhaseAsync(
                        readyTask,
                        "The Raw Input message thread did not finish starting before shutdown.",
                        cancellationToken).ConfigureAwait(false);
                }
                catch when (readyTask.IsFaulted)
                {
                    if (stoppedTask is not null)
                    {
                        await WaitForStopPhaseAsync(
                            stoppedTask,
                            "The failed Raw Input message thread did not terminate.",
                            cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }
            }

            nint window;
            uint nativeThreadId;
            lock (_gate)
            {
                window = _windowHandle;
                nativeThreadId = _nativeThreadId;
                stoppedTask = _stopped?.Task;
            }

            if (stoppedTask is null || stoppedTask.IsCompleted)
            {
                return;
            }

            if (!TryRequestMessageLoopStop(
                    window,
                    nativeThreadId,
                    RawInputNativeMethods.PostMessage,
                    RawInputNativeMethods.PostThreadMessage))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not request the Tappy Raw Input message thread to stop.");
            }

            await WaitForStopPhaseAsync(
                stoppedTask,
                "The Raw Input message thread did not stop within five seconds.",
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static async Task WaitForStopPhaseAsync(
        Task phase,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StopTimeout);
        try
        {
            await phase.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage);
        }
    }

    internal static bool TryRequestMessageLoopStop(
        nint window,
        uint nativeThreadId,
        Func<nint, uint, nuint, nint, bool> postWindowMessage,
        Func<uint, uint, nuint, nint, bool> postThreadMessage)
    {
        ArgumentNullException.ThrowIfNull(postWindowMessage);
        ArgumentNullException.ThrowIfNull(postThreadMessage);

        if (window != nint.Zero &&
            postWindowMessage(window, RawInputNativeMethods.WmClose, 0, nint.Zero))
        {
            return true;
        }

        return nativeThreadId != 0 &&
            postThreadMessage(nativeThreadId, RawInputNativeMethods.WmQuit, 0, nint.Zero);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
    }

    private void MessageThreadMain(TaskCompletionSource ready, TaskCompletionSource stopped)
    {
        nint instance = nint.Zero;
        ushort classAtom = 0;
        nint window = nint.Zero;
        var sessionNotificationsRegistered = false;
        nint powerNotificationRegistration = nint.Zero;
        try
        {
            lock (_gate)
            {
                _nativeThreadId = RawInputNativeMethods.GetCurrentThreadId();
            }

            instance = RawInputNativeMethods.GetModuleHandle(null);
            if (instance == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not obtain the Tappy module handle.");
            }

            var className = $"Tappy.RawInput.{Environment.ProcessId}.{Guid.NewGuid():N}";
            var windowClass = new RawInputNativeMethods.WindowClassEx
            {
                Size = checked((uint)Marshal.SizeOf<RawInputNativeMethods.WindowClassEx>()),
                WindowProcedure = _windowProcedure,
                Instance = instance,
                ClassName = className,
            };
            classAtom = RawInputNativeMethods.RegisterClassEx(ref windowClass);
            if (classAtom == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the Tappy Raw Input window class.");
            }

            window = RawInputNativeMethods.CreateWindowEx(
                0,
                className,
                "Tappy Raw Input",
                0,
                0,
                0,
                0,
                0,
                RawInputNativeMethods.HwndMessage,
                nint.Zero,
                instance,
                nint.Zero);
            if (window == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the Tappy Raw Input message window.");
            }

            var devices = CreateRawInputRegistrations(window);
            if (!RawInputNativeMethods.RegisterRawInputDevices(
                    [devices[0]],
                    1,
                    checked((uint)Marshal.SizeOf<RawInputNativeMethods.RawInputDevice>())))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register Tappy for keyboard Raw Input.");
            }

            // The G13 collection advertises usage page FF00 with usage 0000. A
            // PAGEONLY registration is the narrowest Raw Input registration
            // Windows provides for that top-level collection. Identity is still
            // enforced later by VID/PID/usage and explicit user confirmation.
            // Failure is isolated so keyboard capture remains available.
            if (!RawInputNativeMethods.RegisterRawInputDevices(
                    [devices[1]],
                    1,
                    checked((uint)Marshal.SizeOf<RawInputNativeMethods.RawInputDevice>())))
            {
                Faulted?.Invoke(
                    this,
                    new RawInputCapabilityException(
                        RawInputCapability.LogitechG13,
                        "Could not register Tappy for vendor HID Raw Input; keyboard capture remains available.",
                        new Win32Exception(Marshal.GetLastWin32Error())));
            }

            sessionNotificationsRegistered = RawInputNativeMethods.WTSRegisterSessionNotification(
                window,
                RawInputNativeMethods.NotifyForThisSession);
            if (!sessionNotificationsRegistered)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not register the Tappy message-only window for lock/unlock notifications.");
            }
            powerNotificationRegistration = RawInputNativeMethods.RegisterSuspendResumeNotification(
                window,
                RawInputNativeMethods.DeviceNotifyWindowHandle);
            if (powerNotificationRegistration == nint.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not register the Tappy message-only window for suspend/resume notifications.");
            }

            lock (_gate)
            {
                _windowHandle = window;
                _windowClassName = className;
            }

            ready.TrySetResult();

            while (true)
            {
                var result = RawInputNativeMethods.GetMessage(out var message, nint.Zero, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result < 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The Tappy Raw Input message loop failed.");
                }

                _ = RawInputNativeMethods.TranslateMessage(in message);
                _ = RawInputNativeMethods.DispatchMessage(in message);
            }
        }
        catch (Exception exception)
        {
            if (!ready.TrySetException(exception))
            {
                Faulted?.Invoke(this, exception);
            }
        }
        finally
        {
            if (powerNotificationRegistration != nint.Zero)
            {
                _ = RawInputNativeMethods.UnregisterSuspendResumeNotification(powerNotificationRegistration);
            }

            if (sessionNotificationsRegistered && window != nint.Zero)
            {
                _ = RawInputNativeMethods.WTSUnRegisterSessionNotification(window);
            }

            if (window != nint.Zero)
            {
                _ = RawInputNativeMethods.DestroyWindow(window);
            }

            string? className;
            lock (_gate)
            {
                _windowHandle = nint.Zero;
                _nativeThreadId = 0;
                _thread = null;
                className = _windowClassName;
                _windowClassName = null;
            }

            if (classAtom != 0 && instance != nint.Zero && className is not null)
            {
                _ = RawInputNativeMethods.UnregisterClass(className, instance);
            }

            stopped.TrySetResult();
        }
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case RawInputNativeMethods.WmInput:
                    HandleRawInput(lParam);
                    return wParam == RawInputNativeMethods.RimInput
                        ? RawInputNativeMethods.DefWindowProc(window, message, wParam, lParam)
                        : nint.Zero;

                case RawInputNativeMethods.WmInputDeviceChange:
                    var kind = wParam == RawInputNativeMethods.GidcRemoval
                        ? RawInputDeviceChangeKind.Removal
                        : RawInputDeviceChangeKind.Arrival;
                    DeviceChanged?.Invoke(this, new NativeDeviceChangeEventArgs(lParam, kind));
                    return nint.Zero;

                case RawInputNativeMethods.WmPowerBroadcast:
                    if (wParam == RawInputNativeMethods.PbtApmSuspend)
                    {
                        RaiseLifecycle(WindowsLifecycleSignal.Suspending);
                    }
                    else if (wParam is RawInputNativeMethods.PbtApmResumeSuspend or RawInputNativeMethods.PbtApmResumeAutomatic)
                    {
                        RaiseLifecycle(WindowsLifecycleSignal.Resuming);
                    }

                    return new nint(1);

                case RawInputNativeMethods.WmWtsSessionChange:
                    if (wParam == RawInputNativeMethods.WtsSessionLock)
                    {
                        RaiseLifecycle(WindowsLifecycleSignal.SessionLocked);
                    }
                    else if (wParam == RawInputNativeMethods.WtsSessionUnlock)
                    {
                        RaiseLifecycle(WindowsLifecycleSignal.SessionUnlocked);
                    }

                    return nint.Zero;

                case RawInputNativeMethods.WmQueryEndSession:
                    RaiseLifecycle(WindowsLifecycleSignal.ShutdownRequested);
                    return new nint(1);

                case RawInputNativeMethods.WmEndSession when wParam != 0:
                    RaiseLifecycle(WindowsLifecycleSignal.Shutdown);
                    return nint.Zero;

                case RawInputNativeMethods.WmClose:
                    _ = RawInputNativeMethods.DestroyWindow(window);
                    return nint.Zero;

                case RawInputNativeMethods.WmDestroy:
                    RawInputNativeMethods.PostQuitMessage(0);
                    return nint.Zero;
            }
        }
        catch (Exception exception)
        {
            Faulted?.Invoke(this, exception);
        }

        return RawInputNativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    private void HandleRawInput(nint rawInputHandle)
    {
        var headerSize = IntPtr.Size == 8 ? 24u : 16u;
        uint byteCount = 0;
        var result = RawInputNativeMethods.GetRawInputData(
            rawInputHandle,
            RawInputNativeMethods.RidInput,
            nint.Zero,
            ref byteCount,
            headerSize);
        if (result == uint.MaxValue || !IsSupportedRawInputByteCount(byteCount))
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(checked((int)byteCount));
        try
        {
            var copiedByteCount = byteCount;
            result = RawInputNativeMethods.GetRawInputData(
                rawInputHandle,
                RawInputNativeMethods.RidInput,
                buffer,
                ref copiedByteCount,
                headerSize);
            if (result == uint.MaxValue || result != copiedByteCount)
            {
                return;
            }

            var managed = new byte[copiedByteCount];
            RawKeyboardPacket keyboardPacket = default;
            RawHidInputPacket hidPacket = default;
            bool isKeyboard;
            bool isG13Candidate;
            try
            {
                Marshal.Copy(buffer, managed, 0, managed.Length);
                isKeyboard = RawInputPacketParser.TryParseKeyboard(
                    managed,
                    IntPtr.Size,
                    out keyboardPacket);
                isG13Candidate = !isKeyboard && RawInputPacketParser.TryParseLogitechG13Candidate(
                    managed,
                    IntPtr.Size,
                    out hidPacket);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(managed);
            }

            if (isKeyboard)
            {
                KeyboardPacketReceived?.Invoke(this, new RawKeyboardPacketEventArgs(keyboardPacket));
            }
            else if (isG13Candidate)
            {
                HidPacketReceived?.Invoke(this, new RawHidInputPacketEventArgs(hidPacket));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void RaiseLifecycle(WindowsLifecycleSignal signal) =>
        LifecycleChanged?.Invoke(this, new WindowsLifecycleSignalEventArgs(signal));

    internal static RawInputNativeMethods.RawInputDevice[] CreateRawInputRegistrations(nint window) =>
    [
        new RawInputNativeMethods.RawInputDevice
        {
            UsagePage = 0x0001,
            Usage = 0x0006,
            Flags = RawInputNativeMethods.RidevInputSink | RawInputNativeMethods.RidevDeviceNotify,
            Target = window,
        },
        new RawInputNativeMethods.RawInputDevice
        {
            UsagePage = 0xFF00,
            Usage = 0x0000,
            Flags = RawInputNativeMethods.RidevPageOnly |
                RawInputNativeMethods.RidevInputSink |
                RawInputNativeMethods.RidevDeviceNotify,
            Target = window,
        },
    ];

    internal static bool IsSupportedRawInputByteCount(uint byteCount) =>
        byteCount is > 0 and <= MaximumRawInputByteCount;
}
