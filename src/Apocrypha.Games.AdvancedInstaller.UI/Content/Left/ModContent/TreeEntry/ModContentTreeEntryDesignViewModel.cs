using System.Diagnostics.CodeAnalysis;

namespace Apocrypha.Games.AdvancedInstaller.UI.ModContent;

/// <summary>
///     Design ViewModel for root node.
/// </summary>
[ExcludeFromCodeCoverage]
// ReSharper disable once UnusedType.Global
internal class ModContentTreeEntryDesignViewModel : ModContentTreeEntryViewModel
{
    public ModContentTreeEntryDesignViewModel() : base("", true) { }
}
