using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.Games;
using Apocrypha.Abstractions.Loadouts;
using Apocrypha.Sdk.Loadouts;

namespace Apocrypha.Games.BepInEx;

/// <summary>
/// Launches a BepInEx game (via <c>steam://run</c> for Steam installs). Before launching,
/// ensures the game's Proton prefix loads the BepInEx <c>winhttp.dll</c> proxy by writing the
/// Wine DLL override into <c>user.reg</c> — the same mechanism r2modman uses — so the user
/// never has to set <c>WINEDLLOVERRIDES</c> launch options by hand. No-ops when there is no
/// prefix (native Linux build, or the game has never been launched), so it is safe for every
/// family game (DESIGN-bepinex-family.md §7).
/// </summary>
public abstract class ABepInExRunGameTool<T> : RunGameTool<T>
    where T : IGame
{
    private readonly ILogger _logger;
    private readonly string _displayName;

    protected ABepInExRunGameTool(IServiceProvider provider, T game) : base(provider, game)
    {
        _logger = provider.GetRequiredService<ILogger<ABepInExRunGameTool<T>>>();
        _displayName = game.DisplayName;
    }

    public override async Task Execute(Loadout.ReadOnly loadout, CancellationToken cancellationToken, string[]? commandLineArgs)
    {
        try
        {
            await EnsureWinhttpOverride(loadout, cancellationToken);
        }
        catch (Exception e)
        {
            // Never block the launch on this — worst case the game starts unmodded.
            _logger.LogWarning(e, "Failed to ensure the Wine winhttp override for {Game}; BepInEx may not load", _displayName);
        }

        await base.Execute(loadout, cancellationToken, commandLineArgs);
    }

    private async Task EnsureWinhttpOverride(Loadout.ReadOnly loadout, CancellationToken cancellationToken)
    {
        var installation = loadout.InstallationInstance;
        var prefixPath = installation.LocatorResult.LinuxCompatabilityDataProvider?.WinePrefixDirectoryPath;
        if (prefixPath is null)
        {
            // Not a Proton install (native build, or the prefix doesn't exist yet because the
            // game has never been launched). Wine creates the prefix on first launch; the
            // override lands on the next one.
            _logger.LogInformation("No Proton prefix found for {Game}; skipping winhttp override", _displayName);
            return;
        }

        var userReg = prefixPath.Value.Combine("user.reg");
        if (!userReg.FileExists)
        {
            _logger.LogInformation("user.reg not found at `{Path}`; skipping winhttp override", userReg);
            return;
        }

        var content = await userReg.ReadAllTextAsync(cancellationToken);
        var patched = WinhttpRegPatcher.Patch(content, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (patched is null) return; // Override already present and correct.

        // Backup once, then patch (mirrors r2modman's user.reg handling).
        var backup = userReg.AppendExtension(new NexusMods.Paths.Extension(".bak"));
        if (!backup.FileExists) File.Copy(userReg.ToString(), backup.ToString());

        // Atomic write (CODE_REVIEW.md §7 #14): user.reg is Wine's registry — a crash or power
        // loss mid-write leaves a truncated registry and a broken prefix. Write a sibling temp
        // file and rename over the target (same pattern as CachedHttpStreamFactory); rename on
        // the same filesystem is atomic.
        var temp = userReg.Parent.Combine($"{userReg.FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await temp.WriteAllTextAsync(patched, cancellationToken);
            File.Move(temp.ToString(), userReg.ToString(), overwrite: true);
        }
        catch
        {
            if (temp.FileExists) temp.Delete();
            throw;
        }

        _logger.LogInformation("Added winhttp DLL override to `{Path}` so BepInEx loads under Proton", userReg);
    }
}

/// <summary>
/// The family's launch tool — one instance is minted per <see cref="GenericBepInExGame"/>.
/// </summary>
public sealed class RunBepInExGameTool : ABepInExRunGameTool<GenericBepInExGame>
{
    public RunBepInExGameTool(IServiceProvider provider, GenericBepInExGame game) : base(provider, game)
    {
    }
}
