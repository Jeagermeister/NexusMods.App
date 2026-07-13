using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Abstractions.Cli;
using Apocrypha.Abstractions.Library;
using Apocrypha.Abstractions.ModIo;
using Apocrypha.Sdk.ProxyConsole;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.Networking.ModIo.CLI;

public static class Verbs
{
    internal static IServiceCollection AddModIoVerbs(this IServiceCollection collection) =>
        collection
            .AddModule("modio", "Verbs for interacting with mod.io")
            .AddVerb(() => SetApiKey)
            .AddVerb(() => DownloadMod);

    [Verb("modio set-api-key", "Stores your mod.io API key (get a free one at mod.io/me/access)")]
    private static async Task<int> SetApiKey(
        [Injected] IRenderer renderer,
        [Injected] ISettingsManager settingsManager,
        [Option("k", "key", "The API key")] string key,
        [Injected] CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            await renderer.TextLine("The key can't be empty. Get a free one at {0}", ModIoUrls.ApiKeyPageUri);
            return 1;
        }

        var settings = settingsManager.Get<ModIoSettings>();
        settings.ApiKey = key.Trim();
        settingsManager.Set(settings);

        await renderer.TextLine("mod.io API key stored.");
        return 0;
    }

    [Verb("modio download", "Downloads a mod's latest file from mod.io into the Library")]
    private static async Task<int> DownloadMod(
        [Injected] IRenderer renderer,
        [Injected] IModIoLibrary modIoLibrary,
        [Injected] ILibraryService libraryService,
        [Option("u", "url", "The mod page link (https://mod.io/g/{game}/m/{mod}); alternatively pass -g and -m", true)] string? url,
        [Option("g", "game", "The game's mod.io slug (e.g. baldursgate3)", true)] string? game,
        [Option("m", "mod", "The mod's mod.io slug", true)] string? mod,
        [Injected] CancellationToken token)
    {
        string gameNameId;
        string modNameId;

        if (url is not null)
        {
            if (!ModIoUrls.TryParseModUrl(url, out var parsedGame, out var parsedMod))
            {
                await renderer.TextLine("`{0}` doesn't look like a mod.io mod link (expected https://mod.io/g/{{game}}/m/{{mod}}).", url);
                return 1;
            }

            (gameNameId, modNameId) = (parsedGame, parsedMod);
        }
        else if (game is not null && mod is not null)
        {
            (gameNameId, modNameId) = (game, mod);
        }
        else
        {
            await renderer.TextLine("Pass either --url or both --game and --mod.");
            return 1;
        }

        try
        {
            var file = await modIoLibrary.ResolveLatestFile(gameNameId, modNameId, token);

            if (modIoLibrary.IsAlreadyDownloaded(file.FileId))
            {
                await renderer.TextLine("{0} is already in the Library; nothing to do.", file.Mod.Name);
                return 0;
            }

            var job = await modIoLibrary.CreateDownloadJob(file, token);
            await libraryService.AddDownload(job);

            await renderer.TextLine("Downloaded `{0}` ({1}) into the Library.", file.Mod.Name, file.FileName);
            return 0;
        }
        catch (ModIoApiKeyMissingException e)
        {
            await renderer.TextLine("{0}", e.Message);
            return 1;
        }
        catch (KeyNotFoundException e)
        {
            await renderer.TextLine("{0}", e.Message);
            return 1;
        }
    }
}
