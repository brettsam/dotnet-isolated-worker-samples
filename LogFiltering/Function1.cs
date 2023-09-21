using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace LogFiltering
{
    public class Function1
    {
        private readonly ILogger _injectedLogger;

        public Function1(ILoggerFactory loggerFactory)
        {
            _injectedLogger = loggerFactory.CreateLogger<Function1>();
        }

        [Function("Function1")]
        public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req, FunctionContext context)
        {
            // This log will be logged by the host with category "Function.Function1.User"
            var contextLogger = context.GetLogger("Function.Function1.User");
            contextLogger.LogDebug("Debug from context logger");

            // This log will also be logged by the host with category "Function.Function1.User", even though in 
            // the worker process the category is "LogFiltering.Function1"
            _injectedLogger.LogDebug("Debug from injected logger");

            // This log will be logged by the host with category "Host.Function.Console"
            Console.WriteLine("Console log from invocation");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.WriteString("Welcome to Azure Functions!");
            return response;
        }
    }
}
