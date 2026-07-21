using System.Text.Json.Serialization;

namespace NovaClient.Minecraft;

// Models for Mojang's version manifest and the (legacy-format) 1.8.9 version JSON.

public sealed class VersionManifest
{
    [JsonPropertyName("versions")] public List<ManifestVersion> Versions { get; set; } = new();
}

public sealed class ManifestVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
}

public sealed class VersionJson
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("mainClass")] public string MainClass { get; set; } = "";
    [JsonPropertyName("minecraftArguments")] public string? MinecraftArguments { get; set; }   // legacy (≤1.12.2)
    [JsonPropertyName("arguments")] public ModernArguments? Arguments { get; set; }            // modern (1.13+)
    [JsonPropertyName("assets")] public string? Assets { get; set; }
    [JsonPropertyName("assetIndex")] public AssetIndexRef? AssetIndex { get; set; }
    [JsonPropertyName("downloads")] public Dictionary<string, ArtifactRef>? Downloads { get; set; }
    [JsonPropertyName("libraries")] public List<Library> Libraries { get; set; } = new();
    [JsonPropertyName("inheritsFrom")] public string? InheritsFrom { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("javaVersion")] public JavaVersionRef? JavaVersion { get; set; }

    /// <summary>Minimum Java major version (8 when the JSON predates the field).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int RequiredJavaMajor => JavaVersion?.MajorVersion ?? 8;
}

public sealed class JavaVersionRef
{
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; set; } = 8;
}

/// <summary>1.13+ argument lists. Entries are plain strings or rule-guarded objects.</summary>
public sealed class ModernArguments
{
    [JsonPropertyName("game")] public List<System.Text.Json.JsonElement> Game { get; set; } = new();
    [JsonPropertyName("jvm")] public List<System.Text.Json.JsonElement> Jvm { get; set; } = new();
}

public sealed class AssetIndexRef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("totalSize")] public long TotalSize { get; set; }
}

public sealed class ArtifactRef
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
}

public sealed class Library
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; set; }
    [JsonPropertyName("natives")] public Dictionary<string, string>? Natives { get; set; }
    [JsonPropertyName("rules")] public List<Rule>? Rules { get; set; }
    [JsonPropertyName("extract")] public ExtractRules? Extract { get; set; }
    // Used by generated/custom version JSONs (no "downloads" block, Maven-style resolution):
    [JsonPropertyName("url")] public string? MavenBaseUrl { get; set; }

    /// <summary>Evaluates the library's OS rules for Windows.</summary>
    public bool AppliesToWindows()
    {
        if (Rules is null || Rules.Count == 0) return true;
        var allowed = false;
        foreach (var rule in Rules)
        {
            var matches = rule.Os is null || rule.Os.Name is null or "windows";
            if (matches) allowed = rule.Action == "allow";
        }
        return allowed;
    }

    /// <summary>"group:artifact:version" → "group/path/artifact/version/artifact-version.jar"</summary>
    public string MavenPath(string? classifier = null)
    {
        var parts = Name.Split(':');
        var (group, artifact, version) = (parts[0], parts[1], parts[2]);
        var suffix = classifier is null ? "" : "-" + classifier;
        return $"{group.Replace('.', '/')}/{artifact}/{version}/{artifact}-{version}{suffix}.jar";
    }
}

public sealed class LibraryDownloads
{
    [JsonPropertyName("artifact")] public ArtifactRef? Artifact { get; set; }
    [JsonPropertyName("classifiers")] public Dictionary<string, ArtifactRef>? Classifiers { get; set; }
}

public sealed class Rule
{
    [JsonPropertyName("action")] public string Action { get; set; } = "allow";
    [JsonPropertyName("os")] public OsRule? Os { get; set; }
}

public sealed class OsRule
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public sealed class ExtractRules
{
    [JsonPropertyName("exclude")] public List<string>? Exclude { get; set; }
}

public sealed class AssetIndex
{
    [JsonPropertyName("objects")] public Dictionary<string, AssetObject> Objects { get; set; } = new();
}

public sealed class AssetObject
{
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}
