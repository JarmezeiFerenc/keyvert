using System.Text;
using System.Text.Json;

namespace Keyvert.Core;

/// <summary>Everything the input router needs, combined from the app settings and the active profile.</summary>
public sealed class MappingProfile
{
    public required int ToggleKey { get; init; }
    public bool StartEnabled { get; init; }
    public bool SuppressMappedKeys { get; init; } = true;
    public double WalkScale { get; init; } = 0.5;
    public required IReadOnlyDictionary<int, ControllerAction> Bindings { get; init; }
}

/// <summary>A profile file: the key bindings for one game.</summary>
public sealed class ProfileData
{
    public const double MinWalkScale = 0.1;
    public const double MaxWalkScale = 1.0;

    public double WalkScale { get; set; } = 0.5;
    public Dictionary<int, ControllerAction> Bindings { get; init; } = [];

    public ProfileData Clone() => new() { WalkScale = WalkScale, Bindings = new(Bindings) };
}

public sealed class ConfigException(IReadOnlyList<string> errors)
    : Exception(string.Join("\n", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class ProfileSerializer
{
    public const string DefaultJson = """
        {
          "walkScale": 0.5,
          "bindings": {
            "W": "LeftStickUp",
            "A": "LeftStickLeft",
            "S": "LeftStickDown",
            "D": "LeftStickRight",

            "Space": "A",
            "2": "B",
            "O": "X",
            "1": "Y",

            "R": "LB",
            "3": "RB",
            "E": "LT",
            "P": "RT",

            "Q": "LS",
            "G": "RS",

            "I": "DpadUp",
            "F": "DpadDown",
            "X": "DpadLeft",
            "J": "DpadRight",

            "K": "Start",
            "M": "Back"
          }
        }
        """;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ProfileData CreateDefault() => Parse(DefaultJson);

    /// <summary>Unknown properties are ignored, so old single-file configs import as profiles.</summary>
    public static ProfileData Parse(string json)
    {
        ProfileFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ProfileFile>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException([$"JSON syntax error: {ex.Message}"]);
        }

        if (file is null)
            throw new ConfigException(["The profile is empty."]);

        var errors = new List<string>();

        double walkScale = file.WalkScale ?? 0.5;
        if (double.IsNaN(walkScale) || walkScale < ProfileData.MinWalkScale || walkScale > ProfileData.MaxWalkScale)
            errors.Add("walkScale must be between 0.1 and 1.0.");

        var bindings = new Dictionary<int, ControllerAction>();
        var keyNameByCode = new Dictionary<int, string>();
        foreach (var (keyName, actionName) in file.Bindings ?? [])
        {
            if (!KeyNames.TryParse(keyName, out int vk, out var keyError))
            {
                errors.Add($"bindings: {keyError}");
                continue;
            }

            if (!ControllerActions.TryParse(actionName ?? "", out var action))
            {
                errors.Add($"bindings: \"{keyName}\" has unknown action \"{actionName}\"");
                continue;
            }

            if (keyNameByCode.TryGetValue(vk, out var existing))
            {
                errors.Add($"bindings: \"{keyName}\" and \"{existing}\" are the same key");
                continue;
            }

            keyNameByCode[vk] = keyName;
            bindings[vk] = action;
        }

        if (errors.Count > 0)
            throw new ConfigException(errors);

        return new ProfileData { WalkScale = walkScale, Bindings = bindings };
    }

    public static string Serialize(ProfileData profile)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("walkScale", Math.Round(profile.WalkScale, 2));
            writer.WriteStartObject("bindings");
            foreach (var (vk, action) in profile.Bindings.OrderBy(b => b.Value).ThenBy(b => b.Key))
                writer.WriteString(KeyNames.Describe(vk), action.ToString());
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private sealed class ProfileFile
    {
        public double? WalkScale { get; set; }
        public Dictionary<string, string?>? Bindings { get; set; }
    }
}
