using Keyvert.Core;

namespace Keyvert.Services;

/// <summary>Profiles are JSON files named after the profile in one folder.</summary>
public sealed class ProfileStore
{
    private const string Extension = ".json";
    private const int MaxNameLength = 60;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public ProfileStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Directory { get; }

    public IReadOnlyList<string> List()
    {
        System.IO.Directory.CreateDirectory(Directory);
        return System.IO.Directory.EnumerateFiles(Directory, "*" + Extension)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool Exists(string name) => File.Exists(PathFor(name));

    /// <exception cref="ConfigException">The file is not a valid profile.</exception>
    public ProfileData Load(string name) => ProfileSerializer.Parse(File.ReadAllText(PathFor(name)));

    public void Save(string name, ProfileData profile) =>
        FileUtil.WriteAllTextAtomic(PathFor(name), ProfileSerializer.Serialize(profile));

    public void Rename(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        // A case-only rename is a no-op for File.Move on Windows, so go through a temporary name.
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            string temp = PathFor(newName) + ".renaming";
            File.Move(PathFor(oldName), temp);
            File.Move(temp, PathFor(newName));
            return;
        }

        File.Move(PathFor(oldName), PathFor(newName));
    }

    public void Delete(string name) => File.Delete(PathFor(name));

    public string Export(string name, string destination)
    {
        File.Copy(PathFor(name), destination, overwrite: true);
        return destination;
    }

    /// <returns>The name the imported profile was saved under.</returns>
    /// <exception cref="ConfigException">The file is not a valid profile.</exception>
    public string Import(string sourcePath)
    {
        var profile = ProfileSerializer.Parse(File.ReadAllText(sourcePath));
        string baseName = SanitizeName(Path.GetFileNameWithoutExtension(sourcePath));
        string name = UniqueName(baseName.Length > 0 ? baseName : "Imported");
        Save(name, profile);
        return name;
    }

    public string UniqueName(string baseName)
    {
        if (!Exists(baseName))
            return baseName;

        for (int i = 2; ; i++)
        {
            string candidate = $"{baseName} ({i})";
            if (!Exists(candidate))
                return candidate;
        }
    }

    /// <returns>A message describing why the name can't be used, or null if it can.</returns>
    public string? ValidateName(string name, string? renamingFrom = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a name.";
        if (name != name.Trim())
            return "The name can't start or end with a space.";
        if (name.Length > MaxNameLength)
            return $"Use at most {MaxNameLength} characters.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.'))
            return "The name can't contain \\ / : * ? \" < > | or end with a dot.";
        if (ReservedNames.Contains(name))
            return "Windows reserves that name. Try another one.";

        bool isSameProfile = renamingFrom is not null && string.Equals(name, renamingFrom, StringComparison.OrdinalIgnoreCase);
        if (!isSameProfile && Exists(name))
            return "A profile with this name already exists.";

        return null;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength].Trim() : cleaned;
    }

    private string PathFor(string name) => Path.Combine(Directory, name + Extension);
}
