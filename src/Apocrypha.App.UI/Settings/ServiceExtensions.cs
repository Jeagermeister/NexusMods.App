using Microsoft.Extensions.DependencyInjection;
using Apocrypha.Sdk.Settings;

namespace Apocrypha.App.UI.Settings;

public static class ServiceExtensions
{
    public static IServiceCollection AddUISettings(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddSettings<LanguageSettings>()
            .AddSettings<TextEditorSettings>()
            .AddSettings<TreeDataGridSortingStateSettings>()
            .AddSettings<AlertSettings>()
            .AddSettings<BehaviorSettings>()
            .AddSettings<UpdaterSettings>()
            .AddSettings<WelcomeSettings>();
    }
}
