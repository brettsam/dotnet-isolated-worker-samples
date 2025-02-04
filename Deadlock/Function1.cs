using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Deadlock;

public class Function1
{
    private readonly SomeFactory _factory;

    public Function1(SomeFactory factory)
    {
        _factory = factory;
    }

    [Function("Function1")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
    {
        var data = _factory.GetData();
        return new OkObjectResult($"Welcome to Azure Functions! We have {data.Count} data!");
    }
}