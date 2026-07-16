using System.Collections.Frozen;
using System.Collections.Immutable;
using GameFinder.Launcher.Heroic;
using Microsoft.Extensions.Logging;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Games;

namespace Apocrypha.Backend.Games.Locators;

internal class HeroicGOGLocator : IGameLocator
{
    private readonly ILogger _logger;
    private readonly HeroicGOGHandler _handler;
    private readonly FrozenDictionary<long, IGameData> _registeredGames;

    private static readonly GameStore Store = GameStore.GOG;

    public HeroicGOGLocator(IEnumerable<IGameData> games, ILoggerFactory loggerFactory, IFileSystem fileSystem)
    {
        _logger = loggerFactory.CreateLogger<HeroicGOGHandler>();

        _handler = new HeroicGOGHandler(
            fileSystem: fileSystem,
            logger: loggerFactory.CreateLogger<HeroicGOGHandler>()
        );

        _registeredGames = games
            .SelectMany(game => game.StoreIdentifiers.GOGProductIds, (game, storeIdentifier) => new KeyValuePair<long, IGameData>(storeIdentifier, game))
            .ToFrozenDictionary();
    }

    public IEnumerable<GameLocatorResult> Locate()
    {
        foreach (var result in _handler.FindAllGames())
        {
            if (result.TryPickT1(out var errorMessage, out var gameFinderGame))
            {
                _logger.LogWarning("Error locating games: {ErrorMessage}", errorMessage.Message);
                continue;
            }

            var storeIdentifier = gameFinderGame.Id.Value;
            _logger.LogDebug("Found game '{GameName}' with store identifier '{StoreIdentifier}'", gameFinderGame.Name, storeIdentifier);

            if (!_registeredGames.TryGetValue(storeIdentifier, out var game)) continue;

            var path = gameFinderGame.Path;
            var dlcIds = gameFinderGame.InstalledDLCs.Select(x => LocatorId.From(x.Value.ToString()));

            ImmutableArray<LocatorId> locatorIds =
            [
                LocatorId.From(gameFinderGame.BuildId.ToString()),
                ..dlcIds,
            ];

            var winePrefix = gameFinderGame.GetWinePrefix();
            var linuxCompatibilityDataProvider = winePrefix is not null
                ? new LinuxCompatibilityDataProvider(gameFinderGame, winePrefix.ConfigurationDirectory)
                : null;

            yield return new GameLocatorResult
            {
                Game = game,
                Path = path,
                LocatorIds = locatorIds,
                // Heroic records each install's actual platform (it can install native Linux OR
                // Windows-via-Wine GOG builds); trust it instead of defaulting to the host
                // platform, which mislabeled Wine installs as native (CODE_REVIEW.md §7 #17).
                Platform = gameFinderGame.Platform,
                StoreIdentifier = storeIdentifier.ToString(),
                Store = Store,
                Locator = this,
                LinuxCompatabilityDataProvider = linuxCompatibilityDataProvider,
            };
        }
    }

    private class LinuxCompatibilityDataProvider : ILinuxCompatabilityDataProvider
    {
        private readonly HeroicGOGGame _game;

        public AbsolutePath WinePrefixDirectoryPath { get; }

        public LinuxCompatibilityDataProvider(HeroicGOGGame game, AbsolutePath winePrefixDirectoryPath)
        {
            _game = game;
            WinePrefixDirectoryPath = winePrefixDirectoryPath;
        }

        public ValueTask<ImmutableHashSet<string>> GetInstalledWinetricksComponents(CancellationToken cancellationToken)
        {
            var filePath = WineParser.GetWinetricksLogFilePath(WinePrefixDirectoryPath);
            var result = WineParser.ParseWinetricksLogFile(filePath);
            return new ValueTask<ImmutableHashSet<string>>(result);
        }

        public ValueTask<ImmutableArray<WineDllOverride>> GetWineDllOverrides(CancellationToken cancellationToken)
        {
            // Heroic stores per-game Wine env vars (incl. WINEDLLOVERRIDES, if the user set one
            // via its Settings > Advanced tab) directly on the game record — no launch-options
            // string to parse like Steam's localconfig.vdf.
            if (_game.WineData is null || !_game.WineData.EnvironmentVariables.TryGetValue(WineParser.WineDllOverridesEnvironmentVariableName, out var value))
                return new ValueTask<ImmutableArray<WineDllOverride>>(ImmutableArray<WineDllOverride>.Empty);

            var result = WineParser.ParseEnvironmentVariable(value);
            return new ValueTask<ImmutableArray<WineDllOverride>>(result);
        }
    }
}
