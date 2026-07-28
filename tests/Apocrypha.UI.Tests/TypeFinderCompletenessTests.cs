using System.Runtime.CompilerServices;
using FluentAssertions;
using Apocrypha.App.UI;
using Apocrypha.App.UI.WorkspaceSystem;

namespace Apocrypha.UI.Tests;

/// <summary>
/// Guards the hand-maintained <see cref="TypeFinder"/> list. App.UI has no reflection-based
/// fallback: a serializable context type missing from the list makes window-restore hit an
/// unknown $type discriminator, and <c>WindowManager.RestoreWindowState</c> responds by
/// wiping the ENTIRE saved window layout — not just the offending tab (the PR #87 bug class,
/// with a bigger blast radius than it looks).
/// </summary>
public class TypeFinderCompletenessTests
{
    [Fact]
    public void EveryPageFactoryContextIsRegisteredOrEphemeral()
    {
        var finder = new TypeFinder();
        var registered = finder.DescendentsOf(typeof(IPageFactoryContext)).ToHashSet();

        var unregistered = typeof(TypeFinder).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && t.IsAssignableTo(typeof(IPageFactoryContext)))
            .Where(t => !registered.Contains(t))
            .ToArray();

        // An unregistered context is only safe if it can never be serialized -- i.e. it is
        // ephemeral, so PanelTabViewModel.ToData() never writes its $type in the first place.
        foreach (var type in unregistered)
        {
            var instance = (IPageFactoryContext)RuntimeHelpers.GetUninitializedObject(type);
            instance.IsEphemeral.Should().BeTrue(
                $"{type.Name} implements IPageFactoryContext but is not in TypeFinder.AllTypes; " +
                "a persisted tab of this type would wipe the whole saved window layout on restore. " +
                "Register it in TypeFinder, or mark it IsEphemeral if it must never be persisted");
        }
    }

    [Fact]
    public void EveryWorkspaceContextIsRegistered()
    {
        var finder = new TypeFinder();
        var registered = finder.DescendentsOf(typeof(IWorkspaceContext)).ToHashSet();

        var unregistered = typeof(TypeFinder).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && t.IsAssignableTo(typeof(IWorkspaceContext)))
            .Where(t => !registered.Contains(t))
            .ToArray();

        unregistered.Should().BeEmpty(
            "workspace contexts are always persisted as part of WindowData, so every concrete " +
            "IWorkspaceContext must be in TypeFinder.AllTypes");
    }
}
