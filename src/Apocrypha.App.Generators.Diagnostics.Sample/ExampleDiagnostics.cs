using Apocrypha.Abstractions.Diagnostics;
using Apocrypha.Abstractions.Diagnostics.References;
using Apocrypha.Generators.Diagnostics;

namespace Apocrypha.App.Generators.Diagnostics.Sample;

public partial class ExampleDiagnostics
{
    [DiagnosticTemplate]
    private static readonly IDiagnosticTemplate Diagnostic1Template = DiagnosticTemplateBuilder
        .Start()
        .WithId(new DiagnosticId(source: "Example", number: 1))
        .WithTitle("Diagnostic 1")
        .WithSeverity(DiagnosticSeverity.Warning)
        .WithSummary("Mod conflicts because it's missing '{Something}'!")
        .WithoutDetails()
        .WithMessageData(messageBuilder => messageBuilder
            .AddValue<string>("Something")
        )
        .Finish();
}
