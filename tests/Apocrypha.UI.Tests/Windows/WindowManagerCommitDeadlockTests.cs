using System.Collections.Concurrent;
using System.Reactive;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Apocrypha.App.UI.Windows;
using Apocrypha.App.UI.WorkspaceSystem;
using ReactiveUI;

namespace Apocrypha.UI.Tests.Windows;

/// <summary>
/// Regression test for the UI-thread commit deadlock in <see cref="WindowManager"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>SaveWindowState</c> (window close) and <c>ResetSavedData</c> (missing/broken saved state)
/// both have to commit a MnemonicDB transaction from a synchronous method that runs on the
/// Avalonia UI thread. They used to do <c>tx.Commit().GetAwaiter().GetResult()</c>: because the
/// Avalonia dispatcher installs a <see cref="SynchronizationContext"/> on that thread, an
/// <c>await</c> inside the commit captured the context and posted its continuation back to the
/// very thread that was blocked in <c>GetResult()</c> — a hard deadlock. The real-world symptom
/// was that clicking the window's close button did nothing at all and the process had to be
/// killed. The fix routes both through <c>CommitBlocking</c>, which wraps the commit in
/// <c>Task.Run</c> so the await chain runs on the thread pool, where there is no context to
/// capture.
/// </para>
/// <para>
/// The test deliberately does NOT use the shared Avalonia dispatcher. It installs its own
/// single-threaded <see cref="SynchronizationContext"/> — one dedicated thread servicing a work
/// queue, which is exactly the property of the dispatcher context that causes the deadlock — so
/// that a regression wedges one throwaway thread instead of the test class's UI thread, which
/// would cascade into every other UI test.
/// </para>
/// <para>
/// <c>RestoreWindowState</c> is the entry point because it reaches the blocking commit without
/// needing a real window: against an empty datastore it takes the <c>!HasValue</c> branch
/// straight into <c>ResetSavedData()</c>. (Should another test have left window data behind, the
/// stub's <c>WorkspaceController</c> throws, which the method catches and answers with the same
/// <c>ResetSavedData()</c> call — so the commit is exercised and <c>false</c> is returned either
/// way.)
/// </para>
/// </remarks>
public class WindowManagerCommitDeadlockTests : AUiTest
{
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<WindowManager> _logger;

    public WindowManagerCommitDeadlockTests(IServiceProvider provider) : base(provider)
    {
        _logger = provider.GetRequiredService<ILogger<WindowManager>>();
    }

    [Fact]
    public async Task RestoreWindowState_CommitsWithoutDeadlocking_OnASingleThreadedSynchronizationContext()
    {
        using var context = new SingleThreadedSynchronizationContext(nameof(WindowManagerCommitDeadlockTests));
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Post(_ =>
        {
            try
            {
                var windowManager = new WindowManager(_logger, Connection);
                result.SetResult(windowManager.RestoreWindowState(new StubWorkspaceWindow()));
            }
            catch (Exception e)
            {
                result.SetException(e);
            }
        }, state: null);

        var returned = await Task.WhenAny(result.Task, Task.Delay(DeadlockTimeout)) == result.Task;
        returned.Should().BeTrue(
            $"WindowManager.RestoreWindowState did not return within {DeadlockTimeout.TotalSeconds}s — " +
            "the UI-thread commit deadlock has regressed (see CommitBlocking)"
        );

        var restored = await result.Task;
        restored.Should().BeFalse("the saved state was missing or unusable, so the ResetSavedData path must have run");
    }

    /// <summary>
    /// A <see cref="SynchronizationContext"/> backed by exactly one thread and a work queue, which
    /// is what makes the Avalonia dispatcher context deadlock-prone: a posted continuation can only
    /// ever run on the single thread, so it can never run while that thread is blocked.
    /// </summary>
    private sealed class SingleThreadedSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public SingleThreadedSynchronizationContext(string name)
        {
            _thread = new Thread(Loop)
            {
                Name = name,
                IsBackground = true,
            };

            _thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (InvalidOperationException)
            {
                // The queue was completed while a continuation was still in flight; nothing left to run it.
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            throw new NotSupportedException("Blocking sends are not supported by this test context");
        }

        private void Loop()
        {
            SetSynchronizationContext(this);

            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();

            // A wedged thread will never join: it is a background thread, so it can't keep the
            // process alive, and the queue is deliberately left undisposed so a late continuation
            // posted by the deadlocked commit can't fault on a disposed collection.
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>
    /// Minimal <see cref="IWorkspaceWindow"/> stand-in: the tested path never dereferences the
    /// window, so every member throws to make that explicit.
    /// </summary>
    private sealed class StubWorkspaceWindow : IWorkspaceWindow
    {
        public WindowId WindowId => throw new NotSupportedException();
        public bool IsActive => throw new NotSupportedException();
        public IWorkspaceController WorkspaceController => throw new NotSupportedException();
        public ReactiveCommand<Unit, bool> BringWindowToFront => throw new NotSupportedException();
    }
}
