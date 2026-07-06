using EcoSim.Core.Sim;

namespace WildlandsEcoSim.Gpu;

/// <summary>
/// Phase 7 spike: GPU compute path placeholder. CPU sim remains authoritative until
/// RenderingDevice compute passes are validated against EcoSim.Core.
/// </summary>
public sealed class GpuSimulationBackend
{
    public bool Enabled { get; private set; }
    public string Status { get; private set; } = "cpu";

    public bool TryEnable(SimSession session)
    {
        Enabled = false;
        Status = "cpu (GPU spike pending — binCreatures/resolveIntegrate not yet ported)";
        return false;
    }

    public void Disable()
    {
        Enabled = false;
        Status = "cpu";
    }

    public void Step(double dt)
    {
    }
}
