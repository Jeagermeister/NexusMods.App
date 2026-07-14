using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.GuidedInstallers;
using Apocrypha.Sdk.Jobs;

namespace Apocrypha.Games.FOMOD.CoreDelegates;

/// <summary>
/// A IGuidedInstaller implementation that uses a preset list of steps to make the same choices
/// a user previously made for specific steps, hardened per CODE_REVIEW.md §7 #16:
/// <list type="bullet">
/// <item>The step index is BOUNDED — a FOMOD that presents more steps than the preset recorded
/// (installer updated since the collection was curated, or an empty preset) no longer throws
/// <see cref="IndexOutOfRangeException"/>; extra steps fall through to defaults.</item>
/// <item>Preset name-matches that fail (group/option renamed) are logged instead of silently
/// dropping the selection.</item>
/// <item>After applying the preset, every group is repaired to a VALID selection
/// (<see cref="GuidedInstallerValidation"/> semantics): Required options are always included, and
/// ExactlyOne/AtLeastOne groups that ended up empty get the PreSelected (or first selectable)
/// option — so a stale preset degrades to installer defaults rather than a silently broken mod.</item>
/// </list>
/// An EMPTY preset therefore behaves as a non-interactive "defaults" installer, which the
/// collection flow uses for FOMODs whose <c>choices</c> were never recorded.
/// </summary>
public class PresetGuidedInstaller : IGuidedInstaller
{
    private readonly FomodOption[] _steps;
    private readonly ILogger? _logger;
    private int _currentStep = 0;

    /// <summary>
    /// Constructor.
    /// </summary>
    public PresetGuidedInstaller(FomodOption[] steps, ILogger? logger = null)
    {
        _steps = steps;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Dispose() { }

    /// <inheritdoc/>
    public void SetupInstaller(string name) { }

    /// <inheritdoc/>
    public void CleanupInstaller() { }

    /// <inheritdoc/>
    public Task<UserChoice> RequestUserChoice(GuidedInstallationStep installationStep, Percent progress, CancellationToken cancellationToken)
    {
        var selected = new List<SelectedOption>();

        if (_currentStep < _steps.Length)
        {
            var step = _steps[_currentStep];

            // Map through the two trees matching by name; log what fails to match instead of
            // silently dropping the curator's choice.
            foreach (var srcGroup in step.groups)
            {
                var installGroup = installationStep.Groups.FirstOrDefault(g => g.Name == srcGroup.name);
                if (installGroup is null)
                {
                    _logger?.LogWarning("FOMOD preset group `{Group}` not found in step `{Step}`; falling back to defaults for it", srcGroup.name, installationStep.Name);
                    continue;
                }

                foreach (var srcChoice in srcGroup.choices)
                {
                    var installChoice = installGroup.Options.FirstOrDefault(o => o.Name == srcChoice.name);
                    if (installChoice is null)
                    {
                        _logger?.LogWarning("FOMOD preset option `{Option}` not found in group `{Group}`; skipping it", srcChoice.name, srcGroup.name);
                        continue;
                    }

                    selected.Add(new SelectedOption(installGroup.Id, installChoice.Id));
                }
            }
        }
        else if (_steps.Length > 0)
        {
            _logger?.LogWarning("FOMOD presents more steps than the preset recorded ({Recorded}); using defaults for step `{Step}`", _steps.Length, installationStep.Name);
        }

        _currentStep++;

        ApplyDefaultsAndRepair(installationStep, selected);
        return Task.FromResult(new UserChoice(new UserChoice.GoToNextStep(selected.ToArray())));
    }

    /// <summary>
    /// Ensures the selection is valid for every group: Required options are always included, and
    /// groups whose type demands a selection (ExactlyOne / AtLeastOne) that ended up empty get the
    /// installer's default (PreSelected, else the first selectable option). ExactlyOne groups with
    /// multiple selections (duplicate option names in the preset) are trimmed to the first.
    /// </summary>
    private void ApplyDefaultsAndRepair(GuidedInstallationStep installationStep, List<SelectedOption> selected)
    {
        foreach (var group in installationStep.Groups)
        {
            // Required options are always part of a valid selection.
            foreach (var option in group.Options.Where(static o => o.Type == OptionType.Required))
            {
                if (!selected.Any(s => s.GroupId == group.Id && s.OptionId == option.Id))
                    selected.Add(new SelectedOption(group.Id, option.Id));
            }

            var groupSelections = selected.Where(s => s.GroupId == group.Id).ToArray();

            if (group.Type == OptionGroupType.ExactlyOne && groupSelections.Length > 1)
            {
                _logger?.LogWarning("FOMOD preset selected {Count} options in exactly-one group `{Group}`; keeping the first", groupSelections.Length, group.Name);
                foreach (var extra in groupSelections.Skip(1))
                    selected.Remove(extra);
                groupSelections = [groupSelections[0]];
            }

            var needsOne = group.Type is OptionGroupType.ExactlyOne or OptionGroupType.AtLeastOne;
            if (!needsOne || groupSelections.Length != 0) continue;

            var fallback = group.Options.FirstOrDefault(static o => o.Type == OptionType.PreSelected)
                           ?? group.Options.FirstOrDefault(static o => o.Type != OptionType.Disabled);
            if (fallback is null)
            {
                _logger?.LogWarning("FOMOD group `{Group}` requires a selection but has no selectable options", group.Name);
                continue;
            }

            selected.Add(new SelectedOption(group.Id, fallback.Id));
        }
    }
}
