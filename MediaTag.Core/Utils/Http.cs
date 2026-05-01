using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using PowerKit.Extensions;

namespace MediaTag.Core.Utils;

public static class Http
{
    public static HttpClient Client { get; } =
        new()
        {
            DefaultRequestHeaders =
            {
                // Required by some of the services we're using
                UserAgent =
                {
                    new ProductInfoHeaderValue(
                        "MediaTag",
                        Assembly.GetExecutingAssembly().TryGetVersionString()
                    ),
                },
            },
        };
}
