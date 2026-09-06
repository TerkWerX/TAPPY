using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Tappy.InputBroker.Tests;

public sealed class BrokerPipeServerTests
{
    [Fact]
    public async Task Authenticated_client_can_read_status_but_not_arm_exclusive_input()
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The Windows test identity has no SID.");
        var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        using var configuration = BrokerBootstrapConfiguration.Create(
            currentUser.Value,
            Convert.ToBase64String(key));
        var logger = new CapturingLogger<BrokerPipeServer>();
        var server = new BrokerPipeServer(configuration, logger);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = server.RunAsync(timeout.Token);

        await using var client = new NamedPipeClientStream(
            ".",
            BrokerPipeServer.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync(timeout.Token);

        var helloChallenge = Enumerable.Repeat((byte)0x44, 16).ToArray();
        await BrokerPipeProtocol.WriteAsync(
            client,
            key,
            BrokerMessageKind.Hello,
            1,
            helloChallenge,
            timeout.Token);
        BrokerFrame hello;
        try
        {
            hello = await BrokerPipeProtocol.ReadAsync(client, key, 1, timeout.Token);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidOperationException(logger.LastMessage ?? "The server rejected the test client.", ex);
        }
        Assert.Equal(BrokerMessageKind.HelloAcknowledged, hello.Kind);
        Assert.Equal(helloChallenge, hello.Payload[..16]);

        await BrokerPipeProtocol.WriteAsync(
            client,
            key,
            BrokerMessageKind.StatusRequest,
            2,
            ReadOnlyMemory<byte>.Empty,
            timeout.Token);
        var status = await BrokerPipeProtocol.ReadAsync(client, key, 2, timeout.Token);
        Assert.Equal(BrokerMessageKind.StatusResponse, status.Kind);
        Assert.Contains(
            "identity-binding-incomplete",
            Encoding.UTF8.GetString(status.Payload),
            StringComparison.Ordinal);

        timeout.Cancel();
        await client.DisposeAsync();
        await serverTask;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public string? LastMessage { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => LastMessage = formatter(state, exception);
    }
}
