using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Apocrypha.Abstractions.Loadouts.Files.Diff;
using Apocrypha.Abstractions.Loadouts.Synchronizers;
using Apocrypha.App.UI.Controls.Trees.Common;
using Apocrypha.App.UI.Extensions;
using ReactiveUI;

namespace Apocrypha.App.UI.Controls.Trees.Files;

public partial class FileTreeNodeView : ReactiveUserControl<IFileTreeNodeViewModel>
{
    private FileTreeNodeIconType _lastType = FileTreeNodeIconType.File;
    
    public FileTreeNodeView()
    {
        InitializeComponent();
        this.WhenActivated(d =>
            {
                ViewModel.WhenAnyValue(vm => vm.Icon)
                    .Subscribe(iconType =>
                    {
                        EntryIcon.Classes.Remove(_lastType.GetIconClass());
                        EntryIcon.Classes.Add(iconType.GetIconClass());
                        _lastType = iconType;
                    })
                    .DisposeWith(d);
                
                this.WhenAnyValue(v => v.ViewModel)
                    .WhereNotNull()
                    .Do(PopulateFromViewModel)
                    .Subscribe()
                    .DisposeWith(d);
            }
        );
    }
    
    private void PopulateFromViewModel(IFileTreeNodeViewModel vm)
    {
        FileNameTextBlock.Text = vm.Name;
        if (vm.IsDeletion)
        {
            FileNameTextBlock.TextDecorations = TextDecorations.Strikethrough;
        }

        switch (vm.ChangeType)
        {
            case FileChangeType.Added:
                FileNameTextBlock.Classes.Add("Success");
                EntryIcon.Classes.Add("Success");
                break;
            case FileChangeType.Modified:
                FileNameTextBlock.Classes.Add("Suggestion");
                EntryIcon.Classes.Add("Suggestion");
                break;
            case FileChangeType.Removed:
                FileNameTextBlock.Classes.Add("Critical");
                EntryIcon.Classes.Add("Critical");
                FileNameTextBlock.TextDecorations = TextDecorations.Strikethrough;
                break;
            case FileChangeType.None:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

