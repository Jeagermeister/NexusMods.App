using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.App;
using Apocrypha.Backend;
using Apocrypha.DataModel;
using Apocrypha.Games.RedEngine.Cyberpunk2077;
using NexusMods.Paths;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Settings;
using Apocrypha.StandardGameLocators.TestHelpers;
using Apocrypha.UI.Tests.Framework;
using Xunit.DependencyInjection.Logging;

namespace Apocrypha.UI.Tests;

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        const KnownPath baseKnownPath = KnownPath.EntryDirectory;
        var baseDirectory = $"Apocrypha.UI.Tests.Tests-{Guid.NewGuid()}";
        
        var path = FileSystem.Shared.GetKnownPath(KnownPath.EntryDirectory).Combine("temp").Combine(Guid.NewGuid().ToString());
        path.CreateDirectory();

        services.AddUniversalGameLocator<Cyberpunk2077Game>(new Version("1.61"))
                .AddApp(startupMode: new StartupMode()
                {
                    ShowUI = false,
                    ExecuteCli = false,
                    RunAsMain = true,
                })
                .OverrideSettingsForTests<DataModelSettings>(settings => settings with
                {
                    UseInMemoryDataModel = true,
                    MnemonicDBPath = new ConfigurablePath(baseKnownPath, $"{baseDirectory}/MnemonicDB.rocksdb"),
                    ArchiveLocations = [
                        new ConfigurablePath(baseKnownPath, $"{baseDirectory}/Archives"),
                    ],
                })
                .AddSingleton<AvaloniaApp>()
                .AddLogging(builder => builder.AddXunitOutput().SetMinimumLevel(LogLevel.Debug))
                .Validate();
    }
}

