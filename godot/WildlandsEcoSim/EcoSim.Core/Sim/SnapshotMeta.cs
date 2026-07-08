namespace EcoSim.Core.Sim;

public sealed class SnapshotMeta
{
    public double T { get; set; }
    public int Day { get; set; }
    public double TimeOfDay { get; set; }
    public int NextId { get; set; }
    public int GenerationMax { get; set; }
    public int GrowRow { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}
