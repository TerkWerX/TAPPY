using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Tappy.InputBroker;

internal static class BrokerPipeClientIdentity
{
    private const int MaximumComputerNameLength = 256;
    private const int ErrorPipeLocal = 229;

    public static void RequireLocalClient(NamedPipeServerStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        var computerName = new StringBuilder(MaximumComputerNameLength);
        if (!GetNamedPipeClientComputerName(
                pipe.SafePipeHandle,
                computerName,
                checked((uint)computerName.Capacity)))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorPipeLocal)
            {
                return;
            }

            throw new Win32Exception(
                error,
                $"Windows could not verify the broker pipe client's computer (error {error}).");
        }

        var normalizedClient = computerName.ToString().TrimStart('\\');
        var dot = normalizedClient.IndexOf('.');
        if (dot >= 0)
        {
            normalizedClient = normalizedClient[..dot];
        }

        if (!normalizedClient.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Remote clients are not allowed to use the Tappy Input Broker.");
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetNamedPipeClientComputerNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientComputerName(
        SafePipeHandle pipe,
        StringBuilder clientComputerName,
        uint clientComputerNameLength);
}
