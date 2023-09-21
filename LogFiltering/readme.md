This sample attempts to explain the various logging behaviors when using the dotnet-isolated worker. This sample does not use Application Insights (there is another sample for this in this repo).

> A lot of the (confusing) behavior here is simplified when working with the  [Microsoft.Azure.Functions.Worker.ApplicationInsights package](https://www.nuget.org/packages/Microsoft.Azure.Functions.Worker.ApplicationInsights), as the logs do not travel through the host when this package is used.

For background, dotnet-isolated logging works (by default) by having all logs emitted via an `ILogger` flow back through the host via gRPC to be logged in various destinations:
- The console when using Core Tools (debugging via VS)
- File/streaming logs in Azure
- Application Insights -- when **not** using the [Microsoft.Azure.Functions.Worker.ApplicationInsights package](https://www.nuget.org/packages/Microsoft.Azure.Functions.Worker.ApplicationInsights).

# ILogger
The worker registers an `ILoggerProvider` that writes logs via gRPC back to the host.

`ilogger --grpc--> host -> log destinations`

Some notes:
- Logs from `ILogger` only appear if they are logged in the context of a function invocation. Logs outside of an invocation (such as from a background service) are ignored by the host.
- No matter what the logger category is in the worker, the log emitted by the host will always be `Function.{FunctionName}.User`.

# Console.WriteLine()
The host also listens to stdout (Console logs) and writes them to various log destinations.

`Console.WriteLine() --stdout--> host -> log destinations`

Some notes:
- Logs from stdout are always written by the host. These do not have to be written in the context of a function invocation.
- These logs will have the category `Host.Function.Console`.
- These logs will use level `Information`.

Because the host and the dotnet-isolated worker are separate dotnet applications communicating via gRPC/stdout, there are some log filtering behaviors that can cause troubles.

# Host and worker log level mismatch
Let's say you have a log like this coming from within your function:

```csharp
_logger.LogDebug("Debug");
```

When using the dotnet in-proc model with Functions, simply changing the host.json log level would allow this log to flow through:

```json
{
  "version": "2.0",
  "logging": {
    "logLevel": {
      "default": "Debug"
    }
  }
}
```

However, this is not enough to allow the log to appear with the dotnet-isolated worker. This is because the `ILogger` is using the .NET logging infrastructure and filters are applied at both the worker and the host level. The default .NET log level is `Information`, which means the logs never actually make it from the worker to the host.

In order to properly flow logs from the worker to the host, you must **also adjust the log level in the dotnet-isolated worker**. You can do this any number of ways -- including via code. See the (.NET logging documentation)[] for all other possiblilities.

```csharp
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureLogging(logBuilder =>
    {
        // .NET defaults the minimum log level to Information, so setting this to Debug will allow any Debug or higher logs
        // to flow from the worker to the host. At that point, the host.json log level settings will be applied.
        logBuilder.SetMinimumLevel(LogLevel.Debug);
    })
    .Build();
```