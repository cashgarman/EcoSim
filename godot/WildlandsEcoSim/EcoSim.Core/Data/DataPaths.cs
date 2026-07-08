namespace EcoSim.Core.Data;

/// <summary>Resolves shared <c>data/</c> directory for tests, CLI, and Godot.</summary>
public static class DataPaths
{
    private static string? _overrideRoot;

    public static void SetDataRoot(string absolutePath)
    {
        _overrideRoot = Path.GetFullPath(absolutePath);
    }

    public static string RepoRoot
    {
        get
        {
            if (_overrideRoot != null)
            {
                return _overrideRoot;
            }

            string? dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "data", "species.json")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new InvalidOperationException(
                "Could not locate EcoSim repo root (expected data/species.json). " +
                "Call DataPaths.SetDataRoot() or run from the repository.");
        }
    }

    public static string DataDirectory => Path.Combine(RepoRoot, "data");

    public static string SpeciesJson => Path.Combine(DataDirectory, "species.json");

    public static string BehaviorsDirectory => Path.Combine(DataDirectory, "behaviors");

    public static string BehaviorLibraryJson => Path.Combine(BehaviorsDirectory, "library.json");

    public static string BehaviorSchemaJson => Path.Combine(BehaviorsDirectory, "schema.json");

    public static string BehaviorFile(string stem) => Path.Combine(BehaviorsDirectory, stem + ".json");

    public static string BehaviorEditorLayout(string stem) =>
        Path.Combine(BehaviorsDirectory, "_editor", stem + ".layout.json");
}
