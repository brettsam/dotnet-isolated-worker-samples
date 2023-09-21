using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureLogging(logBuilder =>
    {
        // .NET defaults the minimum log level to Information, so setting this to Debug will allow any Debug or higher logs
        // to flow from the worker to the host. At that point, the host.json log level settings will be applied.
        logBuilder.SetMinimumLevel(LogLevel.Debug);
    })
    .ConfigureServices(services =>
    {
        // Adding a background service so we can show what happens when logs are written outside the scope of a 
        // function invocation
        services.AddHostedService<MyBackgroundService>();
    })
    .Build();

host.Run();

class MyBackgroundService : BackgroundService
{
    private readonly ILogger<MyBackgroundService> _logger;

    public MyBackgroundService(ILogger<MyBackgroundService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // This log is sent over grpc to the host, but because it does not happen in the
            // context of an invocation, it is never logged.
            _logger.LogInformation("ILogger from background");

            // This log is sent to the host and logged with category "Host.Function.Console"
            Console.WriteLine("Console from background");
        }
    }
}