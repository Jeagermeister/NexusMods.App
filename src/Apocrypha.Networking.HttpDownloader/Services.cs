using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Apocrypha.Sdk;
using Polly;

namespace Apocrypha.Networking.HttpDownloader;

public static class Services
{
    /// <summary>
    /// Add the default HTTP downloader services
    /// </summary>
    public static IServiceCollection AddHttpDownloader(this IServiceCollection services)
    {
        return services
            .AddSingleton<HttpTrafficMonitor>()
            .AddTransient<HttpTrafficMonitorHandler>()
            .AddSingleton<HttpClient>(serviceProvider =>
            {
                var client = BuildClient(serviceProvider.GetRequiredService<HttpTrafficMonitor>());
                return client;
            });
    }

    private static HttpClient BuildClient(HttpTrafficMonitor trafficMonitor)
    {
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new HttpRetryStrategyOptions
            {
                BackoffType = DelayBackoffType.Exponential,
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(3),
                UseJitter = true,
            })
            .Build();

        // Traffic monitor sits outermost so it sees the logical request count, not the resilience
        // pipeline's retries — a request retried three times is one intent, and counting it as
        // four would misattribute a storm to whichever endpoint happens to be flaky.
        HttpMessageHandler handler = new HttpTrafficMonitorHandler(trafficMonitor)
        {
            InnerHandler = new ResilienceHandler(pipeline)
            {
                // Negotiate gzip/deflate/brotli so responses served compressed (e.g. the multi-MB
                // Thunderstore v1 community index, and all Nexus/GraphQL JSON) are transferred
                // compressed instead of identity-encoded — a large bandwidth/latency win on the
                // modpack-resolution path. The handler transparently decompresses the body.
                InnerHandler = new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.All,
                },
            },
        };

        var client = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };

        client.DefaultRequestHeaders.UserAgent.Add(ApplicationConstants.UserAgent);

        return client;
    }
}
