using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using Godot;

namespace WildlandsEcoSim.Render;

public partial class CreatureRenderer : Node2D
{
    private MultiMeshInstance2D _mesh = null!;
    private SimSession? _session;
    private SpeciesCatalog? _catalog;
    private int _capacity;
    private string? _lockedSpecies;

    public override void _Ready()
    {
        _mesh = new MultiMeshInstance2D();
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One },
        };
        _mesh.Multimesh = multiMesh;
        AddChild(_mesh);
    }

    public void Bind(SimSession session, SpeciesCatalog catalog)
    {
        _session = session;
        _catalog = catalog;
        _capacity = Math.Max(256, session.State.Creatures.Count + 64);
        _mesh.Multimesh.InstanceCount = _capacity;
    }

    public void SetLockedSpecies(string? speciesKey)
    {
        _lockedSpecies = speciesKey;
    }

    public void Refresh()
    {
        if (_session == null || _catalog == null) return;

        var creatures = _session.State.Creatures;
        int alive = 0;
        for (int i = 0; i < creatures.Count; i++)
        {
            var c = creatures[i];
            if (c.Dead) continue;
            if (alive >= _capacity)
            {
                _capacity *= 2;
                _mesh.Multimesh.InstanceCount = _capacity;
            }

            var def = _catalog.Get(c.Sp);
            float r = 0.35f + (float)c.Genome.Size * 0.12f;
            var xform = Transform2D.Identity
                .Translated(new Vector2((float)c.X, (float)c.Y))
                .Scaled(new Vector2(r, r));
            _mesh.Multimesh.SetInstanceTransform2D(alive, xform);
            _mesh.Multimesh.SetInstanceColor(alive, SpeciesColor(def, c.Sp == _lockedSpecies));
            alive++;
        }

        _mesh.Multimesh.InstanceCount = Math.Max(alive, 1);
        _mesh.Multimesh.VisibleInstanceCount = alive;
    }

    private static Color SpeciesColor(SpeciesDefinition def, bool locked)
    {
        Color baseCol;
        if (def.Col.Length >= 3)
        {
            baseCol = new Color(def.Col[0] / 255f, def.Col[1] / 255f, def.Col[2] / 255f);
        }
        else
        {
            baseCol = Colors.White;
        }
        return locked ? baseCol.Lightened(0.45f) : baseCol;
    }
}
