using DynamicData.Kernel;
using JetBrains.Annotations;
using NexusMods.Abstractions.NexusWebApi.Types;
using NexusMods.Sdk.NexusModsApi;

namespace NexusMods.Abstractions.NexusWebApi;

/// <summary>
/// Builds URLs to Nexus Mods pages. Anything that exposes a URL to the user
/// that points to Nexus Mods should use this class.
/// </summary>
/// <remarks>
/// Apocrypha: upstream attached Matomo campaign-tracking parameters
/// (mtm_source, mtm_campaign, ...) to every URL built here. Those are gone;
/// these are plain URLs.
/// </remarks>
[PublicAPI]
public static class NexusModsUrlBuilder
{
    private const string BaseUrl = "https://www.nexusmods.com";
    private const string UsersBaseUrl = "https://users.nexusmods.com";

    /// <summary>
    /// Uri for the user settings page.
    /// </summary>
    /// <example>
    /// <c>https://users.nexusmods.com</c>
    /// </example>
    public static readonly Uri UserSettingsUri = new(UsersBaseUrl);

    /// <summary>
    /// Returns a URI for a user profile.
    /// </summary>
    public static Uri GetProfileUri(UserId userId)
    {
        // https://www.nexusmods.com/users/6672467
        return new Uri($"{BaseUrl}/users/{userId}");
    }

    /// <summary>
    /// Returns a URI for a game page.
    /// </summary>
    public static Uri GetGameUri(GameDomain gameDomain)
    {
        // https://www.nexusmods.com/games/stardewvalley
        return new Uri($"{BaseUrl}/games/{gameDomain}");
    }

    /// <summary>
    /// Returns a URI for a mod page.
    /// </summary>
    public static Uri GetModUri(GameDomain gameDomain, ModId modId)
    {
        // https://www.nexusmods.com/stardewvalley/mods/2400
        return new Uri($"{BaseUrl}/{gameDomain}/mods/{modId}");
    }

    /// <summary>
    /// Returns a URI for a file download page.
    /// </summary>
    /// <remarks>
    /// <paramref name="useNxmLink"/> changes how the download button on the page behaves. If set to
    /// <c>true</c>, the download button will open an NXM link, if set to <c>false</c> the download
    /// will happen in the browser.
    /// </remarks>
    public static Uri GetFileDownloadUri(GameDomain gameDomain, ModId modId, FileId fileId, bool useNxmLink)
    {
        // https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=128328&nmm=0
        // https://www.nexusmods.com/stardewvalley/mods/2400?tab=files&file_id=128328&nmm=1
        return new Uri($"{BaseUrl}/{gameDomain}/mods/{modId}?tab=files&file_id={fileId}&nmm={Convert.ToInt32(useNxmLink)}");
    }

    /// <summary>
    /// Returns a URI for a game's browse collections page.
    /// </summary>
    public static Uri GetBrowseCollectionsUri(GameDomain gameDomain)
    {
        // https://www.nexusmods.com/games/stardewvalley/collections
        return new Uri($"{BaseUrl}/games/{gameDomain}/collections");
    }

    /// <summary>
    /// Returns a URI for a collection page.
    /// </summary>
    public static Uri GetCollectionUri(GameDomain gameDomain, CollectionSlug collectionSlug, Optional<RevisionNumber> revisionNumber)
    {
        // https://www.nexusmods.com/games/stardewvalley/collections/tckf0m
        // https://www.nexusmods.com/games/stardewvalley/collections/tckf0m/revisions/80
        return new Uri($"{BaseUrl}/games/{gameDomain}/collections/{collectionSlug}{(revisionNumber.HasValue ? $"/revisions/{revisionNumber.Value}" : string.Empty)}");
    }

    /// <summary>
    /// Returns a URI for the bugs page of a collection.
    /// </summary>
    public static Uri GetCollectionBugsUri(GameDomain gameDomain, CollectionSlug collectionSlug, Optional<RevisionNumber> revisionNumber)
    {
        // https://www.nexusmods.com/games/stardewvalley/collections/tckf0m/revisions/80/bugs
        return new Uri($"{BaseUrl}/games/{gameDomain}/collections/{collectionSlug}{(revisionNumber.HasValue ? $"/revisions/{revisionNumber.Value}" : string.Empty)}/bugs");
    }

    /// <summary>
    /// Returns a URI for the changelog page of a collection.
    /// </summary>
    public static Uri GetCollectionChangelogUri(GameDomain gameDomain, CollectionSlug collectionSlug, Optional<RevisionNumber> revisionNumber)
    {
        // https://www.nexusmods.com/games/stardewvalley/collections/tckf0m/revisions/80/changelog
        return new Uri($"{BaseUrl}/games/{gameDomain}/collections/{collectionSlug}{(revisionNumber.HasValue ? $"/revisions/{revisionNumber.Value}" : string.Empty)}/changelog");
    }

    /// <summary>
    /// Uri for the premium benefits page.
    /// </summary>
    /// <example>
    /// <c>https://www.nexusmods.com/premium</c>
    /// </example>
    public static readonly Uri LearnAboutPremiumUri = new($"{BaseUrl}/premium");

    /// <summary>
    /// Uri for the upgrade to premium page.
    /// </summary>
    public static readonly Uri UpgradeToPremiumUri = new($"{UsersBaseUrl}/account/billing/premium");
}
