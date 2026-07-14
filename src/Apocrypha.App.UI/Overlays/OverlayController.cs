using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using ReactiveUI;

namespace Apocrypha.App.UI.Overlays;

/// <inheritdoc />
public class OverlayController : ReactiveObject, IOverlayController
{
    private readonly object _lockObject = new();
    public IOverlayViewModel? CurrentOverlay { get; set; }
    
    private Queue<IOverlayViewModel> _queue = new();
    public void Enqueue(IOverlayViewModel overlayViewModel)
    {
        bool changed;
        lock (_lockObject)
        {
            overlayViewModel.Controller = this;
            _queue.Enqueue(overlayViewModel);

            changed = ProcessNext();
        }

        // DEADLOCK GUARD (CODE_REVIEW.md §7 #14): the notification synchronously marshals to the
        // UI thread, so it must NEVER run while holding _lockObject — a background thread blocking
        // on the UI thread while the UI thread blocks on the lock is a hard UI freeze. State
        // mutations stay under the lock; the notification is raised after releasing it.
        if (changed) NotifyCurrentOverlayChanged();
    }

    public async Task<TResult?> EnqueueAndWait<TResult>(IOverlayViewModel<TResult> overlayViewModel)
    {
        Enqueue(overlayViewModel);
        await overlayViewModel.CompletionTask;
        return overlayViewModel.Result;
    }

    private bool ProcessNext()
    {
        if (CurrentOverlay != null)
        {
            return false;
        }
        if (_queue.Count == 0)
        {
            return false;
        }

        CurrentOverlay = _queue.Dequeue();
        CurrentOverlay.Status = Status.Visible;
        return true;
    }

    private void NotifyCurrentOverlayChanged()
    {
        Dispatcher.UIThread.Invoke(() =>
            {
                this.RaisePropertyChanged(nameof(CurrentOverlay));
            }
        );
    }

    public void Remove(IOverlayViewModel model)
    {
        var changed = false;
        lock (_lockObject)
        {
            if (CurrentOverlay == model)
            {
                var oldOverlay = CurrentOverlay;
                oldOverlay.Status = Status.Closed;
                CurrentOverlay = null;
                ProcessNext();
                changed = true;
            }
            else
            {
                var items = _queue.ToArray();
                _queue.Clear();
                foreach (var item in items)
                {
                    if (item == model)
                        continue;
                    _queue.Enqueue(item);
                }
            }
        }

        // See the deadlock guard note in Enqueue: never notify while holding the lock.
        if (changed) NotifyCurrentOverlayChanged();
    }
}
