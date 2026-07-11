using JetBrains.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Apocrypha.Abstractions.Diagnostics;

namespace Apocrypha.App.Generators.Diagnostics.Tests;

public class DiagnosticTemplateIncrementalSourceGeneratorTests
{
    [LanguageInjection("csharp")]
    private const string SourceText = """"
using Apocrypha.Generators.Diagnostics;
using Apocrypha.Abstractions.Diagnostic;
using Apocrypha.Abstractions.Diagnostics.References;

namespace TestNamespace;

internal partial class MyClass
{
    private const string Source = "Example";

    [DiagnosticTemplate]
    private static readonly IDiagnosticTemplate Diagnostic1Template = DiagnosticTemplateBuilder
        .Start()
        .WithId(new DiagnosticId(source: Source, number: 1))
        .WithTitle("Diagnostic 1")
        .WithSeverity(DiagnosticSeverity.Warning)
        .WithSummary(
"""
Mod conflicts with because it's missing '{Something}' and {Count} other stuff!
""")
        .WithoutDetails()
        .WithMessageData(messageBuilder => messageBuilder
            .AddValue<string>("Something")
            .AddValue<int>("Count")
        )
        .Finish();
}
"""";

    [Fact]
    public Task TestGenerator()
    {
        var generator = new DiagnosticTemplateIncrementalSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        var compilation = CSharpCompilation.Create(nameof(DiagnosticTemplateIncrementalSourceGenerator),
            new[] { CSharpSyntaxTree.ParseText(SourceText) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DiagnosticTemplateBuilder).Assembly.Location),
            }
        );

        var runResult = driver.RunGenerators(compilation).GetRunResult();
        return Verify(runResult);
    }
}
