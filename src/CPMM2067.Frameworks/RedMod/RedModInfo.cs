using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CPMM2067.Frameworks.RedMod;

public sealed record RedModInfo
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("customSounds")] public List<RedModCustomSound>? CustomSounds { get; init; }
}

public sealed record RedModCustomSound
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
}

public sealed record RedModsJson
{
    [JsonPropertyName("mods")] public List<RedModsJsonEntry> Mods { get; init; } = new();
}

public sealed record RedModsJsonEntry
{
    [JsonPropertyName("folder")] public string Folder { get; init; } = string.Empty;
    [JsonPropertyName("enabled")] public bool Enabled { get; init; } = true;
    [JsonPropertyName("deployed")] public bool Deployed { get; init; }
    [JsonPropertyName("deployedVersion")] public string? DeployedVersion { get; init; }
}
