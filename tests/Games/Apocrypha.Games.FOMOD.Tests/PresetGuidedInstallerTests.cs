using FluentAssertions;
using Apocrypha.Abstractions.GuidedInstallers;
using Apocrypha.Abstractions.GuidedInstallers.ValueObjects;
using Apocrypha.Games.FOMOD.CoreDelegates;
using Apocrypha.Sdk.Jobs;
using Xunit;

namespace Apocrypha.Games.FOMOD.Tests;

/// <summary>
/// Directed tests for the hardened preset installer (CODE_REVIEW.md §7 #16): bounded step index,
/// name-mismatch fallback, group-validity repair, and the empty-preset "defaults" mode the
/// collection flow uses for FOMODs without recorded choices.
/// </summary>
public class PresetGuidedInstallerTests
{
    private static GuidedInstallationStep MakeStep(params OptionGroup[] groups) => new()
    {
        Id = StepId.From(Guid.NewGuid()),
        Name = "Step",
        Groups = groups,
    };

    private static OptionGroup MakeGroup(string name, OptionGroupType type, params (string Name, OptionType Type)[] options) => new()
    {
        Id = GroupId.From(Guid.NewGuid()),
        Name = name,
        Type = type,
        Options = options.Select(o => new Option
        {
            Id = OptionId.From(Guid.NewGuid()),
            Name = o.Name,
            Type = o.Type,
        }).ToArray(),
    };

    private static async Task<SelectedOption[]> Run(PresetGuidedInstaller sut, GuidedInstallationStep step)
    {
        var choice = await sut.RequestUserChoice(step, Percent.Zero, CancellationToken.None);
        choice.IsGoToNextStep.Should().BeTrue();
        return choice.AsT2.SelectedOptions;
    }

    [Fact]
    public async Task EmptyPreset_DoesNotThrow_AndSelectsDefaults()
    {
        // The old code indexed _steps[_currentStep] unconditionally: an empty preset (or a FOMOD
        // with more steps than recorded) threw IndexOutOfRangeException mid-collection-install.
        var sut = new PresetGuidedInstaller([]);
        var step = MakeStep(
            MakeGroup("A", OptionGroupType.ExactlyOne, ("first", OptionType.Available), ("second", OptionType.PreSelected)),
            MakeGroup("B", OptionGroupType.Any, ("opt", OptionType.Available)),
            MakeGroup("C", OptionGroupType.Any, ("must", OptionType.Required), ("maybe", OptionType.Available)));

        var selected = await Run(sut, step);

        // ExactlyOne group A: the PreSelected option wins; Any group B: nothing; C: Required only.
        selected.Should().HaveCount(2);
        selected.Should().Contain(s => s.GroupId == step.Groups[0].Id && s.OptionId == step.Groups[0].Options[1].Id);
        selected.Should().Contain(s => s.GroupId == step.Groups[2].Id && s.OptionId == step.Groups[2].Options[0].Id);
    }

    [Fact]
    public async Task MoreStepsThanPreset_FallsBackToDefaults()
    {
        var preset = new[]
        {
            new FomodOption { name = "step1", groups = [new FomodGroup { name = "A", choices = [new FomodChoice { name = "first" }] }] },
        };
        var sut = new PresetGuidedInstaller(preset);

        var step1 = MakeStep(MakeGroup("A", OptionGroupType.ExactlyOne, ("first", OptionType.Available), ("second", OptionType.PreSelected)));
        var selected1 = await Run(sut, step1);
        selected1.Should().ContainSingle(s => s.OptionId == step1.Groups[0].Options[0].Id, "the preset choice wins on step 1");

        // Step 2 was never recorded: must not throw, must take defaults.
        var step2 = MakeStep(MakeGroup("X", OptionGroupType.ExactlyOne, ("only", OptionType.Available)));
        var selected2 = await Run(sut, step2);
        selected2.Should().ContainSingle(s => s.OptionId == step2.Groups[0].Options[0].Id);
    }

    [Fact]
    public async Task NameMismatch_RepairsToDefault_InsteadOfEmptySelection()
    {
        // The mod author renamed the group since the collection was curated: the old name-only
        // join silently selected NOTHING, leaving a required group unsatisfied.
        var preset = new[]
        {
            new FomodOption { name = "s", groups = [new FomodGroup { name = "OldName", choices = [new FomodChoice { name = "first" }] }] },
        };
        var sut = new PresetGuidedInstaller(preset);
        var step = MakeStep(MakeGroup("NewName", OptionGroupType.ExactlyOne, ("first", OptionType.Available), ("second", OptionType.Available)));

        var selected = await Run(sut, step);

        selected.Should().ContainSingle("the ExactlyOne group must be repaired to a valid selection");
        selected[0].OptionId.Should().Be(step.Groups[0].Options[0].Id, "no PreSelected option exists, so the first selectable wins");
    }

    [Fact]
    public async Task ExactlyOneGroup_MultipleMatches_TrimmedToFirst()
    {
        var preset = new[]
        {
            new FomodOption
            {
                name = "s",
                groups = [new FomodGroup { name = "A", choices = [new FomodChoice { name = "first" }, new FomodChoice { name = "second" }] }],
            },
        };
        var sut = new PresetGuidedInstaller(preset);
        var step = MakeStep(MakeGroup("A", OptionGroupType.ExactlyOne, ("first", OptionType.Available), ("second", OptionType.Available)));

        var selected = await Run(sut, step);

        selected.Should().ContainSingle();
        selected[0].OptionId.Should().Be(step.Groups[0].Options[0].Id);
    }

    [Fact]
    public async Task RequiredOptions_AlwaysIncluded_EvenWhenPresetOmitsThem()
    {
        var preset = new[]
        {
            new FomodOption { name = "s", groups = [new FomodGroup { name = "A", choices = [new FomodChoice { name = "optional" }] }] },
        };
        var sut = new PresetGuidedInstaller(preset);
        var step = MakeStep(MakeGroup("A", OptionGroupType.Any, ("mandatory", OptionType.Required), ("optional", OptionType.Available)));

        var selected = await Run(sut, step);

        selected.Should().HaveCount(2);
        selected.Should().Contain(s => s.OptionId == step.Groups[0].Options[0].Id, "Required options are part of every valid selection");
    }

    [Fact]
    public async Task PresetChoices_AreHonored()
    {
        var preset = new[]
        {
            new FomodOption
            {
                name = "s",
                groups =
                [
                    new FomodGroup { name = "A", choices = [new FomodChoice { name = "second" }] },
                    new FomodGroup { name = "B", choices = [new FomodChoice { name = "extra" }] },
                ],
            },
        };
        var sut = new PresetGuidedInstaller(preset);
        var step = MakeStep(
            MakeGroup("A", OptionGroupType.ExactlyOne, ("first", OptionType.PreSelected), ("second", OptionType.Available)),
            MakeGroup("B", OptionGroupType.Any, ("extra", OptionType.Available)));

        var selected = await Run(sut, step);

        selected.Should().HaveCount(2);
        selected.Should().Contain(s => s.GroupId == step.Groups[0].Id && s.OptionId == step.Groups[0].Options[1].Id, "the curator's choice beats the PreSelected default");
        selected.Should().Contain(s => s.GroupId == step.Groups[1].Id && s.OptionId == step.Groups[1].Options[0].Id);
    }
}
