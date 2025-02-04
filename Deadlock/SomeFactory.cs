
using System.Text;
using Azure.Storage.Blobs;

namespace Deadlock;

public class SomeFactory
{
    private IDictionary<string, string>? _data;
    private object _lock = new();

    public IDictionary<string, string> GetData()
    {
        if (_data == null)
        {
            lock (_lock)
            {
                _data ??= RetrieveDataFromExternalServiceAsync();
            }
        }

        return _data;
    }

    private static IDictionary<string, string> RetrieveDataFromExternalServiceAsync()
    {
        var connStr = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        BlobServiceClient service = new(connStr);
        BlobContainerClient container = service.GetBlobContainerClient("deadlock");
        container.CreateIfNotExists();

        var blobs = container.GetBlobs();

        if (!blobs.Any())
        {
            for (int i = 0; i < 1000; i++)
            {
                var blob = container.GetBlobClient($"{Guid.NewGuid()}.json");
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{ \"key1\": \"value1\", \"key2\": \"value2\" }"));
                blob.Upload(stream);
            }
        }

        blobs = container.GetBlobs();
        return blobs.ToDictionary(
            static k => k.Name,
            static v => v.Properties.ContentLength.ToString());
    }
}
