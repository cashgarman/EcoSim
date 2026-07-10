namespace EcoSim.Core.Tests;

/// <summary>
/// Resolves the shared data root for tests. Game data lives under
/// godot/WildlandsEcoSim/data (the repo-root data/ duplicate was removed).
/// </summary>
public static class TestPaths
{
    public static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "data", "species.json")))
            {
                return dir;
            }

            string godotRoot = Path.Combine(dir, "godot", "WildlandsEcoSim");
            if (File.Exists(Path.Combine(godotRoot, "data", "species.json")))
            {
                return godotRoot;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repo root not found (expected data/species.json)");
    }
}
