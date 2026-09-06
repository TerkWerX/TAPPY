using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tappy.InputBroker;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
        {
            return RunSelfTest();
        }

        var bootstrapPath = GetRequiredOption(args, "--bootstrap");
        using var configuration = BrokerBootstrapConfiguration.Load(bootstrapPath);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options => options.ServiceName = "Tappy Input Broker");
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<BrokerPipeServer>();
        builder.Services.AddHostedService<BrokerWorker>();
        builder.Services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

        using var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static string GetRequiredOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                return args[index + 1];
            }
        }

        throw new InvalidOperationException(
            $"The broker refuses to start without {name} pointing to installer-provisioned configuration.");
    }

    private static int RunSelfTest()
    {
        var key = RandomNumberGenerator.GetBytes(BrokerPipeProtocol.MinimumKeySize);
        var payload = RandomNumberGenerator.GetBytes(16);
        try
        {
            var encoded = BrokerPipeProtocol.Encode(key, BrokerMessageKind.Hello, 1, payload);
            var decoded = BrokerPipeProtocol.Decode(key, encoded, 1);
            return decoded.Kind == BrokerMessageKind.Hello && decoded.Payload.SequenceEqual(payload) ? 0 : 1;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
