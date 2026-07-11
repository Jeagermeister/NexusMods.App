using Apocrypha.Sdk.ProxyConsole;
using Rendering = Spectre.Console.Rendering;
using Console = Spectre.Console;

namespace Apocrypha.ProxyConsole.RenderDefinitions;

/// <summary>
/// A definition for rendering <see cref="Text"/>s to the console using Spectre.Console.
/// </summary>
public class TextRenderDefinition() : ARenderableDefinition<Text>("718E119C-FABF-4A63-9B52-674747A9CFD0")
{
    /// <inheritdoc />
    protected override ValueTask<Rendering.IRenderable> ToSpectreAsync(Text renderable, Func<IRenderable,
        ValueTask<Rendering.IRenderable>> subConvert)
    {
        return ValueTask.FromResult<Rendering.IRenderable>(new Console.Text(string.Format(renderable.Template, renderable.Arguments)));
    }
}
