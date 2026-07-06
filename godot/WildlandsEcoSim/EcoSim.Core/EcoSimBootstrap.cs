using EcoSim.Core.Behavior;
using EcoSim.Core.Data;

namespace EcoSim.Core;

/// <summary>Bootstraps shared data catalogs for sandbox, batch, and tests.</summary>
public static class EcoSimBootstrap
{
    public static (SpeciesCatalog Species, BehaviorLibrary Behaviors) LoadBaseData(string? dataRoot = null)
    {
        if (dataRoot != null)
        {
            DataPaths.SetDataRoot(dataRoot);
        }

        var species = SpeciesCatalog.LoadFromFile();
        var behaviors = new BehaviorLibrary();
        behaviors.Load(species);
        return (species, behaviors);
    }
}
