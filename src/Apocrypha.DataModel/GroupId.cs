using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Apocrypha.DataModel.JsonConverters;
using TransparentValueObjects;

namespace Apocrypha.DataModel;

/// <summary>
/// Represents a unique identifier for a mod group.
/// </summary>
[PublicAPI]
[ValueObject<Guid>]
[JsonConverter(typeof(GroupIdConverter))]
public readonly partial struct GroupId { }
