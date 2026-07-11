namespace Apocrypha.Abstractions.Thunderstore;

/// <summary>
/// Builders for the (stable, documented) Thunderstore URL shapes. Lives in abstractions
/// (not networking) so UI surfaces can link Thunderstore pages without the API client.
/// </summary>
public static class ThunderstoreUrls
{
    private const string BaseUrl = "https://thunderstore.io";

    /// <summary>
    /// A community's browse page on thunderstore.io.
    /// </summary>
    public static Uri GetCommunityUri(string communitySlug)
        => new($"{BaseUrl}/c/{Uri.EscapeDataString(communitySlug)}/");

    /// <summary>
    /// The package page on thunderstore.io (not community-qualified — packages are global).
    /// </summary>
    public static Uri GetPackagePageUri(PackageRef package)
        => new($"{BaseUrl}/package/{Uri.EscapeDataString(package.Namespace)}/{Uri.EscapeDataString(package.Name)}/");

    /// <summary>
    /// The download endpoint for an exact package version (302s to the CDN zip).
    /// </summary>
    public static Uri GetDownloadUri(PackageVersionRef version)
        => new($"{BaseUrl}/package/download/{Uri.EscapeDataString(version.Package.Namespace)}/{Uri.EscapeDataString(version.Package.Name)}/{Uri.EscapeDataString(version.Version)}/");

    /// <summary>
    /// The experimental API endpoint for a package (includes its latest version).
    /// </summary>
    public static Uri GetPackageApiUri(PackageRef package)
        => new($"{BaseUrl}/api/experimental/package/{Uri.EscapeDataString(package.Namespace)}/{Uri.EscapeDataString(package.Name)}/");

    /// <summary>
    /// The experimental API endpoint for an exact package version.
    /// </summary>
    public static Uri GetVersionApiUri(PackageVersionRef version)
        => new($"{BaseUrl}/api/experimental/package/{Uri.EscapeDataString(version.Package.Namespace)}/{Uri.EscapeDataString(version.Package.Name)}/{Uri.EscapeDataString(version.Version)}/");
}
