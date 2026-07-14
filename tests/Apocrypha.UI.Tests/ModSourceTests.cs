using System.Collections.Frozen;
using DynamicData.Kernel;
using FluentAssertions;
using Apocrypha.Abstractions.ModIo;
using Apocrypha.Abstractions.ModSources;
using Apocrypha.Abstractions.NexusWebApi;
using Apocrypha.Abstractions.NexusWebApi.Types;
using Apocrypha.Abstractions.Thunderstore;
using Apocrypha.App.UI.Pages.LibraryPage.ModSources;
using System.Collections.Immutable;
using Apocrypha.Sdk.Games;
using Apocrypha.Sdk.IO;
using Apocrypha.Sdk.NexusModsApi;
using Apocrypha.Sdk.Settings;
using NexusMods.Paths;
using R3;

namespace Apocrypha.UI.Tests;

/// <summary>
/// The <see cref="IModSource"/> adapters are the seam that lets source-agnostic consumers
/// enumerate mod sources instead of hardcoding a per-source property (CODE_REVIEW.md §5). These
/// lock the capability + browse-URL behaviour each adapter must uphold so a fourth source
/// (Modrinth) can be added by implementing the same contract.
/// </summary>
public class ModSourceTests
{
    [Fact]
    public void WellKnownIds_AreDistinct()
    {
        var ids = new[] { ModSourceId.NexusMods, ModSourceId.Thunderstore, ModSourceId.ModIo };
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Thunderstore_ReportsCapabilityAndBrowseUri()
    {
        var source = new ThunderstoreModSource();

        source.Id.Should().Be(ModSourceId.Thunderstore);
        source.DisplayName.Should().Be("Thunderstore");
        source.IsEnabled.Should().BeTrue();

        // No Thunderstore community → not supported, no browse link.
        source.SupportsGame(new FakeGame()).Should().BeFalse();
        source.GetBrowseUri(new FakeGame()).HasValue.Should().BeFalse();

        var game = new FakeThunderstoreGame { ThunderstoreCommunitySlug = "riskofrain2" };
        source.SupportsGame(game).Should().BeTrue();
        var uri = source.GetBrowseUri(game);
        uri.HasValue.Should().BeTrue();
        uri.Value.ToString().Should().Contain("riskofrain2");
    }

    [Fact]
    public void Nexus_CapabilityGatedOnGameId()
    {
        var source = new NexusModSource(new FakeMappingCache());

        source.Id.Should().Be(ModSourceId.NexusMods);
        source.DisplayName.Should().Be("Nexus Mods");
        source.IsEnabled.Should().BeTrue();

        source.SupportsGame(new FakeGame()).Should().BeFalse();
        source.GetBrowseUri(new FakeGame()).HasValue.Should().BeFalse();

        var game = new FakeGame { NexusModsGameIdValue = NexusModsGameId.From(1234) };
        source.SupportsGame(game).Should().BeTrue();
        source.GetBrowseUri(game).HasValue.Should().BeTrue();
    }

    [Fact]
    public void ModIo_IsEnabledFollowsSettings_AndCapabilityGatedOnMarker()
    {
        var enabled = new ModIoModSource(new FakeSettingsManager(enableModIo: true));
        var disabled = new ModIoModSource(new FakeSettingsManager(enableModIo: false));

        enabled.Id.Should().Be(ModSourceId.ModIo);
        enabled.DisplayName.Should().Be("mod.io");
        enabled.IsEnabled.Should().BeTrue();
        disabled.IsEnabled.Should().BeFalse();

        enabled.SupportsGame(new FakeGame()).Should().BeFalse();

        var game = new FakeModIoGame { ModIoGameNameId = "baldursgate3" };
        enabled.SupportsGame(game).Should().BeTrue();
        enabled.GetBrowseUri(game).Value.ToString().Should().Contain("baldursgate3");
    }

    // --- test doubles ---

    private class FakeGame : IGameData
    {
        public Optional<NexusModsGameId> NexusModsGameIdValue { get; init; } = Optional<NexusModsGameId>.None;

        public Optional<NexusModsGameId> NexusModsGameId => NexusModsGameIdValue;
        public string DisplayName => "Fake Game";

        // Not exercised by the adapters under test.
        public GameId GameId => throw new NotSupportedException();
        public StoreIdentifiers StoreIdentifiers => throw new NotSupportedException();
        public IStreamFactory IconImage => throw new NotSupportedException();
        public IStreamFactory TileImage => throw new NotSupportedException();
        public GamePath GetPrimaryFile(GameInstallation installation) => throw new NotSupportedException();
        public ImmutableDictionary<LocationId, AbsolutePath> GetLocations(IFileSystem fileSystem, GameLocatorResult gameLocatorResult) => throw new NotSupportedException();
    }

    private sealed class FakeThunderstoreGame : FakeGame, IThunderstoreCommunityGame
    {
        public string ThunderstoreCommunitySlug { get; init; } = "riskofrain2";
    }

    private sealed class FakeModIoGame : FakeGame, IModIoGame
    {
        public string ModIoGameNameId { get; init; } = "baldursgate3";
    }

    private sealed class FakeMappingCache : IGameDomainToGameIdMappingCache
    {
        public GameDomain GetDomain(NexusModsGameId id) => GameDomain.From("riskofrain2");
        public NexusModsGameId GetId(GameDomain domain) => throw new NotSupportedException();
    }

    private sealed class FakeSettingsManager : ISettingsManager
    {
        private readonly bool _enableModIo;
        public FakeSettingsManager(bool enableModIo) => _enableModIo = enableModIo;

        public T Get<T>(string? key = null) where T : class, ISettings, new()
            => (T)(object)new ModIoSettings { EnableModIo = _enableModIo };

        public void Set<T>(T value, string? key = null) where T : class, ISettings, new() => throw new NotSupportedException();
        public bool TryGet<T>(out T? value, string? key = null) where T : class, ISettings, new() => throw new NotSupportedException();
        public T GetDefault<T>() where T : class, ISettings, new() => throw new NotSupportedException();
        public T Update<T>(Func<T, T> updater, string? key = null) where T : class, ISettings, new() => throw new NotSupportedException();
        public Observable<T> GetChanges<T>(string? key = null, bool prependCurrent = false) where T : class, ISettings, new() => throw new NotSupportedException();
        public FrozenDictionary<Type, SettingsConfig> Configs => throw new NotSupportedException();
    }
}
