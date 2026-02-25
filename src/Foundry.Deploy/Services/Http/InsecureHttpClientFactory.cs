using System.Net.Http;

namespace Foundry.Deploy.Services.Http;

public static class InsecureHttpClientFactory
{
    public static HttpClient Create(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        return new HttpClient(handler)
        {
            Timeout = timeout
        };
    }
}
