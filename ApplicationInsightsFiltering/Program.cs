using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Adds standard Application Insights setup. Logs from worker go directly to Application Insights
        // Fully configurable as as if this were a console application.
        // Follow Application Insights docs at https://learn.microsoft.com/en-us/azure/azure-monitor/app/worker-service
        services.AddApplicationInsightsTelemetryWorkerService();

        // Does a few things:
        // - Adds dotnet-isolated TelemetryModule to connect to the worker's ActivitySource
        // - Tells the host to *not* log to Application Insights
        // - Adds some standard Functions values to Telemetry properties.
        // - Now, filtering in host.json will not apply to Application Insights telemetry coming from the worker. Instead, 
        //   use normal.NET log configuration to control these:
        //   https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-7.0#configure-logging
        services.ConfigureFunctionsApplicationInsights();

        // Registers IHttpClientFactory.
        // By default this sends a lot of Information-level logs.
        services.AddHttpClient();

        // Registers Azure Clients.
        // These also send a lot of Information-level logs.
        services.AddAzureClients(builder =>
        {
            builder.AddQueueServiceClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage")).WithName("queue");
        });
    })
    .ConfigureLogging(logging =>
    {
        // Allow all Information-level App Insights logs through (this defaults to Warning).
        RemoveApplicationInsightsFilter(logging.Services);

        // Disable IHttpClientFactory Informational logs.
        // Note -- you can also handler that does the logging: https://github.com/aspnet/HttpClientFactory/issues/196#issuecomment-432755765 
        logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

        // Disable Informational logs from Azure SDK. These get wired up automatically when using AddAzureClients()
        // Docs -- https://learn.microsoft.com/en-us/dotnet/azure/sdk/logging#logging-with-client-registration
        logging.AddFilter("Azure.Core", LogLevel.Warning);
    })
    .Build();

host.Run();


static void RemoveApplicationInsightsFilter(IServiceCollection services)
{
    services.Configure<LoggerFilterOptions>(options =>
    {
        // The Application Insights SDK adds a default logging filter that instructs ILogger to capture only Warning and more severe logs. Application Insights requires an explicit override.
        // Log levels can also be configured using appsettings.json. For more information, see https://learn.microsoft.com/en-us/azure/azure-monitor/app/worker-service#ilogger-logs
        LoggerFilterRule toRemove = options.Rules.FirstOrDefault(rule => rule.ProviderName
            == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");

        if (toRemove is not null)
        {
            options.Rules.Remove(toRemove);
        }
    });
}