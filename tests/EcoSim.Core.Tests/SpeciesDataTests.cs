using EcoSim.Core.Data;
using EcoSim.Core.Sim;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class SpeciesDataTests
{
    private string _repoRoot = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        _repoRoot = TestPaths.FindRepoRoot();
        DataPaths.SetDataRoot(_repoRoot);
    }

    [Test]
    public void EverySpecies_HasABlurb()
    {
        var session = SimSession.Create(_repoRoot, 1);
        foreach (string sp in session.Species.SpeciesKeys)
        {
            Assert.That(session.Species.Get(sp).Blurb, Is.Not.Empty,
                $"species '{sp}' needs a blurb for the selection screen");
        }
    }

    [Test]
    public void Blurbs_SurviveOverrideResetRoundTrip()
    {
        var session = SimSession.Create(_repoRoot, 1);
        string before = session.Species.Get("rabbit").Blurb;
        session.Species.ApplyOverrides(null);
        Assert.That(session.Species.Get("rabbit").Blurb, Is.EqualTo(before),
            "Blurb must survive the CloneSpeciesMap JSON round-trip");
    }
}
