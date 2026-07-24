using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.NexusModsLibrary;
using Apocrypha.Abstractions.NexusWebApi;
using Apocrypha.Abstractions.NexusWebApi.Types;
using Apocrypha.Networking.NexusWebApi.Auth;
using Apocrypha.Networking.NexusWebApi.UpdateFilters;
using Apocrypha.Networking.NexusWebApi.V1Interop;
using Apocrypha.Sdk;
using Apocrypha.Sdk.NexusModsApi;

namespace Apocrypha.Networking.NexusWebApi;

/// <summary>
/// Helps with registration of services for Microsoft DI container.
/// </summary>
public static class Services
{
    /// <summary>
    /// Adds the Nexus Web API to your DI Container's service collection.
    /// </summary>
    public static IServiceCollection AddNexusWebApi(this IServiceCollection collection, bool? apiKeyAuth = null)
    {
        collection.AddLoginVerbs();

        apiKeyAuth ??= Environment.GetEnvironmentVariable(ApiKeyMessageFactory.NexusApiKeyEnvironmentVariable) != null;

        if (apiKeyAuth!.Value)
        {
            collection
                .AddAllSingleton<IHttpMessageFactory, ApiKeyMessageFactory>()
                .AddSingleton<IAuthenticatingMessageFactory, ApiKeyMessageFactory>();
        }
        else
        {
            collection
                .AddAllSingleton<IHttpMessageFactory, OAuth2MessageFactory>()
                .AddSingleton<IAuthenticatingMessageFactory, OAuth2MessageFactory>();
        }
        collection.AddSingleton<OAuth>();
        collection.AddSingleton<IIDGenerator, IDGenerator>();

        collection.AddJWTTokenModel();
        collection.AddApiKeyModel();
        collection.AddSingleton(TimeProvider.System);
        collection.AddIgnoreFileUpdateModel();

        collection.AddGameDomainToGameIdMappingModel();
        collection.AddSingleton<GameDomainToGameIdMappingCache>();
        collection.AddSingleton<IGameDomainToGameIdMappingCache>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<LocalMappingCache>>();

            var didParse = LocalMappingCache.TryParseJsonFile(logger, out var gameIdToDomain, out var gameDomainToId);
            Debug.Assert(didParse);

            if (!didParse) logger.LogWarning("Failed to parse local mapping cache data, all mapping requests will be handled by the fallback cache");
            else
            {
                Debug.Assert(gameIdToDomain is not null && gameDomainToId is not null);
                logger.LogInformation("Loaded local mapping cache data with {Count} entries", gameDomainToId?.Count ?? 0);
            }

            var fallbackCache = serviceProvider.GetRequiredService<GameDomainToGameIdMappingCache>();
            return new LocalMappingCache(logger, gameIdToDomain ?? FrozenDictionary<NexusModsGameId, GameDomain>.Empty, gameDomainToId ?? FrozenDictionary<GameDomain, NexusModsGameId>.Empty, fallbackCache);
        });

        collection
            .AddNexusModsLibraryModels()
            .AddSingleton<IGraphQlClient, GraphQlClient>()
            .AddSingleton<NexusModsLibrary>()
            .AddAllSingleton<ILoginManager, LoginManager>()
            .AddAllSingleton<INexusApiClient, NexusApiClient>()
            .AddSingleton<IModUpdateFilterService, ModUpdateFilterService>()
            .AddAllSingleton<IModUpdateService, ModUpdateService>()
            .AddHostedService<NexusModRequirementsBackfill>()
            .AddNexusApiVerbs();

        collection
            .AddNexusGraphQLClient()
            .ConfigureHttpClient((serviceProvider, httpClient) =>
            {
                httpClient.BaseAddress = ClientConfig.GraphQlEndpoint;
                httpClient.DefaultRequestHeaders.UserAgent.Add(ApplicationConstants.UserAgent);

                httpClient.DefaultRequestHeaders.Add(BaseHttpMessageFactory.HeaderApplicationName, ApplicationConstants.UserAgent.ApplicationName);
                httpClient.DefaultRequestHeaders.Add(BaseHttpMessageFactory.HeaderApplicationVersion, ApplicationConstants.UserAgent.ApplicationVersion);

                var authenticationHeaderValue = serviceProvider.GetRequiredService<IHttpMessageFactory>().GetAuthenticationHeaderValue();
                if (authenticationHeaderValue is null) return;

                httpClient.DefaultRequestHeaders.Authorization = authenticationHeaderValue;
            });

        return collection;
    }
}
