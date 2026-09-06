using System.IO.Pipes;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tappy.InputBroker;

internal sealed class BrokerPipeServer
{
    public const string PipeName = "Tappy.InputBroker.v1";
    private const int MaximumRequestsPerConnection = 128;

    private readonly BrokerBootstrapConfiguration _configuration;
    private readonly ILogger<BrokerPipeServer> _logger;

    public BrokerPipeServer(
        BrokerBootstrapConfiguration configuration,
        ILogger<BrokerPipeServer> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                BrokerPipeClientIdentity.RequireLocalClient(pipe);
                await ServeConnectedClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or CryptographicException or Win32Exception)
            {
                _logger.LogWarning("Rejected or disconnected a local broker client: {Reason}", ex.Message);
            }
        }
    }

    internal async Task ServeConnectedClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        ulong incomingSequence = 1;
        ulong outgoingSequence = 1;

        var hello = await BrokerPipeProtocol.ReadAsync(
            stream,
            _configuration.AuthenticationKey,
            incomingSequence++,
            cancellationToken).ConfigureAwait(false);
        if (hello.Kind != BrokerMessageKind.Hello || hello.Payload.Length != 16)
        {
            throw new InvalidDataException("The broker client did not begin with a valid hello challenge.");
        }

        var serverNonce = RandomNumberGenerator.GetBytes(16);
        var acknowledgement = new byte[32];
        hello.Payload.CopyTo(acknowledgement, 0);
        serverNonce.CopyTo(acknowledgement, 16);
        await BrokerPipeProtocol.WriteAsync(
            stream,
            _configuration.AuthenticationKey,
            BrokerMessageKind.HelloAcknowledged,
            outgoingSequence++,
            acknowledgement,
            cancellationToken).ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(acknowledgement);
        CryptographicOperations.ZeroMemory(serverNonce);

        for (var requestNumber = 0; requestNumber < MaximumRequestsPerConnection; requestNumber++)
        {
            var request = await BrokerPipeProtocol.ReadAsync(
                stream,
                _configuration.AuthenticationKey,
                incomingSequence++,
                cancellationToken).ConfigureAwait(false);
            if (request.Kind != BrokerMessageKind.StatusRequest || request.Payload.Length != 0)
            {
                var error = JsonSerializer.SerializeToUtf8Bytes(new BrokerStatusDocument(
                    false,
                    "control-disabled",
                    "This build exposes status only. Exclusive input cannot be armed."));
                await BrokerPipeProtocol.WriteAsync(
                    stream,
                    _configuration.AuthenticationKey,
                    BrokerMessageKind.Error,
                    outgoingSequence,
                    error,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var status = JsonSerializer.SerializeToUtf8Bytes(new BrokerStatusDocument(
                false,
                "identity-binding-incomplete",
                "The broker is authenticated, but stable device binding and signed-client verification are not complete."));
            await BrokerPipeProtocol.WriteAsync(
                stream,
                _configuration.AuthenticationKey,
                BrokerMessageKind.StatusResponse,
                outgoingSequence++,
                status,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private NamedPipeServerStream CreatePipe() => NamedPipeServerStreamAcl.Create(
        PipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.WriteThrough,
        64 * 1024,
        64 * 1024,
        BrokerPipeAccessControl.Create(_configuration.AllowedUser));

    private sealed record BrokerStatusDocument(bool ExclusiveControlAvailable, string Code, string Message);
}
