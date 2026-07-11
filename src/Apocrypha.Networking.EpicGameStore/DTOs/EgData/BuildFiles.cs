using System.Text.Json.Serialization;

namespace Apocrypha.Networking.EpicGameStore.DTOs.EgData;

public class BuildFiles
{
    [JsonPropertyName("total")]
    public int Total { get; set; }
    
    [JsonPropertyName("files")]
    public BuildFile[] Files { get; set; } = [];
}
