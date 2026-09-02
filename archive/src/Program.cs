using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Archive.Functions;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration(configuration => configuration.AddEnvironmentVariables())
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddSingleton<ArchiveOptions>(serviceProvider =>
            ArchiveOptions.FromConfiguration(serviceProvider.GetRequiredService<IConfiguration>()));
        services.AddSingleton<ArchiveCoordinator>();
        services.AddSingleton<IArchiveLogSource, AzureMonitorLogSource>();
        services.AddSingleton<IArchiveStore, BlobArchiveStore>();
        services.AddSingleton<IArchiveClock, SystemArchiveClock>();
    })
    .Build();

host.Run();
