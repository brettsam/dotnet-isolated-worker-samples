using System.Net;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;

namespace ApplicationInsightsFiltering
{
    public class Function1
    {
        private readonly HttpClient _httpClient;
        private readonly QueueServiceClient _queueClient;
        private readonly ILogger _logger;

        public Function1(IHttpClientFactory httpClientFactory, IAzureClientFactory<QueueServiceClient> queueClientFactory, ILoggerFactory loggerFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
            _queueClient = queueClientFactory.CreateClient("queue");
            _logger = loggerFactory.CreateLogger<Function1>();
        }

        [Function("Function1")]
        public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            // Do this just to demonstrate the additional logging we'll get.
            _ = await _httpClient.GetAsync("http://microsoft.com");

            // This will also generate a bunch of logs.
            _ = await _queueClient.CreateQueueAsync("test");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("Welcome to Azure Functions!");
            return response;
        }
    }
}
