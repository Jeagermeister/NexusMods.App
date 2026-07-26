using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Apocrypha.Abstractions.Collections.Json;

/// <summary>
/// A plugin listed by a collection, in the curator's intended order.
/// </summary>
/// <remarks>
/// Gamebryo/Creation Engine collections carry these on top of the shared collection schema:
/// https://github.com/Nexus-Mods/Vortex/blob/master/src/renderer/src/extensions/collections/util/gameSupport/gamebryo.tsx
/// </remarks>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class CollectionPlugin
{
    /// <summary>
    /// Plugin file name, e.g. `MyMod.esp`.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Whether the curator has this plugin enabled. Absent means enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }
}

/// <summary>
/// LOOT-style ordering rules for a Gamebryo collection: per-plugin `after` constraints plus an
/// optional group graph. Groups are a second ordering layer — a plugin belongs to a group, and
/// groups themselves are ordered relative to one another, so group ordering has to be resolved
/// down to plugin-level ordering before it can be used.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class GamebryoPluginRules
{
    [JsonPropertyName("plugins")]
    public UserlistEntry[] Plugins { get; init; } = [];

    [JsonPropertyName("groups")]
    public UserlistEntry[] Groups { get; init; } = [];
}

/// <summary>
/// A single LOOT userlist entry: either a plugin or a group, depending on which collection it
/// appears in.
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class UserlistEntry
{
    /// <summary>
    /// Name of the plugin or group this entry describes.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// For plugin entries, the group this plugin belongs to.
    /// </summary>
    [JsonPropertyName("group")]
    public string? Group { get; init; }

    /// <summary>
    /// Names this entry must load after. LOOT allows each element to be either a bare string or a
    /// reference object (<c>{ name, display, condition }</c>), so this is normalised on read.
    /// </summary>
    [JsonPropertyName("after")]
    [JsonConverter(typeof(LootReferenceArrayConverter))]
    public string[] After { get; init; } = [];
}

/// <summary>
/// Reads LOOT `after` entries, which are a mix of bare strings and
/// <c>{ "name": ..., "display": ..., "condition": ... }</c> objects, into plain names.
/// </summary>
/// <remarks>
/// Conditional references are kept: the condition describes when LOOT would apply the rule, and
/// dropping the rule entirely would silently lose curator intent. A reference we can't resolve to
/// an installed plugin is skipped later, when rules are turned into sort constraints.
/// </remarks>
internal sealed class LootReferenceArrayConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return [];
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array for `after`");

        var results = new List<string>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    return results.ToArray();

                case JsonTokenType.String:
                    var value = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) results.Add(value);
                    break;

                case JsonTokenType.StartObject:
                    var name = ReadNameFromObject(ref reader);
                    if (!string.IsNullOrWhiteSpace(name)) results.Add(name);
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of array for `after`");
    }

    private static string? ReadNameFromObject(ref Utf8JsonReader reader)
    {
        string? name = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return name;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, "name", StringComparison.OrdinalIgnoreCase) && reader.TokenType == JsonTokenType.String)
                name = reader.GetString();
            else
                reader.Skip();
        }

        throw new JsonException("Unexpected end of object in `after`");
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
