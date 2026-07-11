using Apocrypha.App.UI.Controls.Filters;
using Apocrypha.App.UI.Controls.TreeDataGrid.Filters;
using Apocrypha.UI.Sdk;

namespace Apocrypha.App.UI.Controls;

public class FileEntryComponent : ReactiveR3Object, IItemModelComponent<FileEntryComponent>, IComparable<FileEntryComponent>, IComparable
{
    public StringComponent Name { get; }
    public ValueComponent<bool> IsDeleted { get; }
    
    public FileEntryComponent(
        StringComponent name,
        ValueComponent<bool> isDeleted)
    {
        Name = name;
        IsDeleted = isDeleted;
    }
    
    /// <inheritdoc/>
    public FilterResult MatchesFilter(Filter filter) => Name.MatchesFilter(filter); // Delegate to StringComponent

    public int CompareTo(FileEntryComponent? other) => Name.CompareTo(other?.Name);

    public int CompareTo(object? obj) =>  obj is FileEntryComponent other ? CompareTo(other) : 1;
}
