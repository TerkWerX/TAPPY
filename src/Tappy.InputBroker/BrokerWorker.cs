using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tappy.InputBroker;

internal sealed class BrokerWorker : BackgroundService
{
    private readonly BrokerPipeServer _server;
    private readonly ILogger<BrokerWorker> _logger;

    public BrokerWorker(BrokerPipeServer server, ILogger<BrokerWorker> logger)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Tappy Input Broker started in status-only mode; exclusive keyboard control is disabled.");
        await _server.RunAsync(stoppingToken).ConfigureAwait(false);
    }
}
