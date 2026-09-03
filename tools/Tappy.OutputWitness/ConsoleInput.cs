using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tappy.OutputWitness;

internal interface IConsoleInputSource : IDisposable
{
    void FlushPendingInput();

    bool TryRead(
        TimeSpan maximumWait,
        CancellationToken cancellationToken,
        out ConsoleKeyObservation observation);
}

/// <summary>
/// Reads ordinary input records from this process's focused console buffer. It
/// registers no Raw Input device, low-level hook, or global hotkey. Only virtual
/// key/down-up/repeat data leave this native boundary; Unicode characters, scan
/// codes, modifier state, and non-key records are discarded.
/// </summary>
internal sealed class WindowsConsoleInputSource : IConsoleInputSource
{
    private const int StandardInputHandle = -10;
    private const uint EnableProcessedInput = 0x0001;
    private const ushort KeyEvent = 0x0001;
    private const uint WaitObject0 = 0x00000000;
    private const uint WaitTimeout = 0x00000102;
    private const uint WaitFailed = 0xFFFFFFFF;

    private readonly nint _inputHandle;
    private readonly uint _originalMode;
    private readonly bool _modeChanged;
    private readonly ushort _originalVirtualKey;
    private readonly ushort _outputVirtualKey;
    private bool _disposed;

    internal WindowsConsoleInputSource(
        WitnessKeySpec originalKey,
        WitnessKeySpec outputKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows console input is required.");
        }

        if (!WitnessKeyCatalog.IsAllowedOriginal(originalKey))
        {
            throw new ArgumentOutOfRangeException(nameof(originalKey));
        }

        if (!WitnessKeyCatalog.IsAllowedOutput(outputKey))
        {
            throw new ArgumentOutOfRangeException(nameof(outputKey));
        }

        _originalVirtualKey = originalKey.VirtualKeyCode;
        _outputVirtualKey = outputKey.VirtualKeyCode;
        _inputHandle = NativeMethods.GetStdHandle(StandardInputHandle);
        if (_inputHandle == 0 || _inputHandle == new nint(-1))
        {
            throw CreateWin32Exception("The standard input console handle is unavailable.");
        }

        if (!NativeMethods.GetConsoleMode(_inputHandle, out _originalMode))
        {
            throw CreateWin32Exception("Standard input is not an interactive Windows console.");
        }

        var requiredMode = _originalMode | EnableProcessedInput;
        if (requiredMode != _originalMode)
        {
            if (!NativeMethods.SetConsoleMode(_inputHandle, requiredMode))
            {
                throw CreateWin32Exception("Ctrl+C could not be enabled for the console witness.");
            }

            _modeChanged = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_modeChanged && !NativeMethods.SetConsoleMode(_inputHandle, _originalMode))
        {
            throw CreateWin32Exception("The original console input mode could not be restored.");
        }
    }

    public void FlushPendingInput()
    {
        ThrowIfDisposed();
        if (!NativeMethods.FlushConsoleInputBuffer(_inputHandle))
        {
            throw CreateWin32Exception("The pre-arm console input buffer could not be cleared.");
        }
    }

    public bool TryRead(
        TimeSpan maximumWait,
        CancellationToken cancellationToken,
        out ConsoleKeyObservation observation)
    {
        ThrowIfDisposed();
        if (maximumWait < TimeSpan.Zero || maximumWait > TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWait));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        var remainingWait = maximumWait;
        while (true)
        {
            var waitMilliseconds = checked((uint)Math.Ceiling(remainingWait.TotalMilliseconds));
            var waitResult = NativeMethods.WaitForSingleObject(_inputHandle, waitMilliseconds);
            if (waitResult == WaitTimeout)
            {
                observation = default;
                return false;
            }

            if (waitResult == WaitFailed)
            {
                throw CreateWin32Exception("Waiting for focused console input failed.");
            }

            if (waitResult != WaitObject0)
            {
                throw new InvalidOperationException("The focused console input wait ended unexpectedly.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var records = new NativeMethods.InputRecord[1];
            if (!NativeMethods.ReadConsoleInputW(
                    _inputHandle,
                    records,
                    1,
                    out var recordsRead))
            {
                throw CreateWin32Exception("Reading focused console input failed.");
            }

            if (recordsRead == 1 &&
                records[0].EventType == KeyEvent &&
                records[0].KeyEvent.RepeatCount > 0)
            {
                var keyEvent = records[0].KeyEvent;
                if (keyEvent.VirtualKeyCode == _originalVirtualKey ||
                    keyEvent.VirtualKeyCode == _outputVirtualKey)
                {
                    observation = new ConsoleKeyObservation(
                        keyEvent.VirtualKeyCode,
                        keyEvent.KeyDown,
                        keyEvent.RepeatCount);
                    return true;
                }
            }

            // Discard non-key and non-allowlisted records without returning their
            // identities. Once the requested wait has elapsed, continue only as
            // a non-blocking drain of records already queued at that boundary.
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            remainingWait = elapsed < maximumWait
                ? maximumWait - elapsed
                : TimeSpan.Zero;
        }
    }

    private static Win32Exception CreateWin32Exception(string message) =>
        new(Marshal.GetLastWin32Error(), message);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct KeyEventRecord
        {
            [MarshalAs(UnmanagedType.Bool)]
            internal bool KeyDown;

            internal ushort RepeatCount;

            internal ushort VirtualKeyCode;

            internal ushort VirtualScanCode;

            internal char UnicodeChar;

            internal uint ControlKeyState;
        }

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode, Size = 20)]
        internal struct InputRecord
        {
            [FieldOffset(0)]
            internal ushort EventType;

            [FieldOffset(4)]
            internal KeyEventRecord KeyEvent;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GetStdHandle(int standardHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetConsoleMode(nint consoleHandle, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetConsoleMode(nint consoleHandle, uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FlushConsoleInputBuffer(nint consoleInput);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(nint handle, uint milliseconds);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadConsoleInputW(
            nint consoleInput,
            [Out] InputRecord[] buffer,
            uint length,
            out uint eventsRead);
    }
}
