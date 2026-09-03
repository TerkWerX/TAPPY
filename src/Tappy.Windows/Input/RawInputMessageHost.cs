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

    private readonly object _gate = new();
    private readonly RawInputNativeMethods.WindowProcedure _windowProcedure;
    private Thread? _thread;
    private TaskCompletionSource? _ready;
    private nint _windowHandle;
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
                _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _thread = new Thread(MessageThreadMain)
                {
                    IsBackground = true,
                    Name = "Tappy Raw Input Message Thread",
                };
                readyTask = _ready.Task;
                _thread.Start();
            }
        }

        await readyTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Thread? thread;
        nint window;
        lock (_gate)
        {
            thread = _thread;
            window = _windowHandle;
        }

        if (thread is null || !thread.IsAlive)
        {
            return;
        }

        if (window != nint.Zero)
        {
            _ = RawInputNativeMethods.PostMessage(window, RawInputNativeMethods.WmClose, 0, nint.Zero);
        }

        await Task.Run(
            () =>
            {
                while (!thread.Join(millisecondsTimeout: 100))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            },
            cancellationToken).ConfigureAwait(false);
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

    private void MessageThreadMain()
    {
        nint instance = nint.Zero;
        ushort classAtom = 0;
        nint window = nint.Zero;
        var sessionNotificationsRegistered = false;
        nint powerNotificationRegistration = nint.Zero;
        try
        {
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
                    new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Could not register Tappy for vendor HID Raw Input; keyboard capture remains available."));
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

            _ready?.TrySetResult();

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
            if (!(_ready?.TrySetException(exception) ?? false))
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
                _thread = null;
                className = _windowClassName;
                _windowClassName = null;
            }

            if (classAtom != 0 && instance != nint.Zero && className is not null)
            {
                _ = RawInputNativeMethods.UnregisterClass(className, instance);
            }
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
