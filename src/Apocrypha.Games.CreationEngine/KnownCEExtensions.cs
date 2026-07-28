using NexusMods.Paths;

namespace Apocrypha.Games.CreationEngine;

public static class KnownCEExtensions
{
    public static readonly Extension BSA = new Extension(".bsa");
    public static readonly Extension BA2 = new Extension(".ba2");
    public static readonly Extension ESM = new Extension(".esm");
    public static readonly Extension ESP = new Extension(".esp");
    public static readonly Extension ESL = new Extension(".esl");
    
    /// <summary>
    /// The plugin-file extensions the load-order machinery manages.
    /// </summary>
    /// <remarks>
    /// Kept in sync with three places that cannot reference this array directly:
    /// <c>CollectionPluginWhitelist.KnownPluginExtensions</c> (Apocrypha.Collections does not
    /// reference this project), <c>LoadOrderSorting.LoadOrderClassOf</c> (string suffixes),
    /// and the extension regex in <c>PluginSortOrderQueries.sql</c>.
    /// </remarks>
    public static readonly Extension[] PluginFiles = [ESM, ESP, ESL];
}
