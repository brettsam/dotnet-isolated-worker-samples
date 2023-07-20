This sample demonstrates how to filter logs coming from the worker when using the [Microsoft.Azure.Functions.Worker.ApplicationInsights package](https://www.nuget.org/packages/Microsoft.Azure.Functions.Worker.ApplicationInsights).

This package enables the worker to send logs and other telemetry directly to Application Insights, without traveling through the host. To simplify it -- logs would normally flow:
`worker -> host -> application insights`
But as soon as you call `ConfigureFunctionsApplicationInsights()`, they will now flow:
`worker -> application insights`

This gives a developer more control over how Application Insights captures telemetry and emits it than before. Now the `host.json` settings are not as important and the worker (mostly) interacts with Application Insights as if it is "just another console application". All the traditional Application Insights and .NET logging documentation applies.

This sample shows a scenario where adding external packages introduces some unwanted verbose logging at the "Information" level. With in-proc Functions (and even when using dotnet-isoated without the Worker.ApplicationInsights package), all filtering was controlled in host.json. But now, it's all controlled via the standard .NET logging filters.

I've commented the code explaining what is happening, but before the two filters are applied, the logs looked like this:
- Green logs came from `IHttpClientFactory`
- Blue logs came from Azure SDK
  
![image](https://github.com/brettsam/dotnet-isolated-worker-samples/assets/1089915/d97ef1ff-1406-4585-8715-551c4c532a3e)

After applying the filters, we now see these are gone:

![image](https://github.com/brettsam/dotnet-isolated-worker-samples/assets/1089915/a146e0e3-cc33-4658-8079-b7b0e43e6554)
