using System.Text.Json;
using System.Text.Json.Nodes;
using EcoSim.Core.Behavior;
using EcoSim.Core.Data;
using NUnit.Framework;

namespace EcoSim.Core.Tests;

[TestFixture]
public class BehaviorCompilerTests
{
  private string _repoRoot = "";
  private BehaviorLibraryRoot _library = null!;

  [OneTimeSetUp]
  public void SetUp()
  {
    _repoRoot = FindRepoRoot();
    DataPaths.SetDataRoot(_repoRoot);
    string json = File.ReadAllText(DataPaths.BehaviorLibraryJson);
    _library = JsonSerializer.Deserialize<BehaviorLibraryRoot>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true,
      ReadCommentHandling = JsonCommentHandling.Skip,
      AllowTrailingCommas = true,
    })!;
  }

  [Test]
  public void UnknownActionRef_ProducesValidationError()
  {
    var speciesFile = new BehaviorSpeciesFile { Extends = "herbivore_prey" };
    var badLibrary = JsonSerializer.Deserialize<BehaviorLibraryRoot>(
      File.ReadAllText(DataPaths.BehaviorLibraryJson),
      new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    badLibrary.Trees["herbivore_prey"] = JsonNode.Parse(
      """
      {
        "type": "selector",
        "children": ["NotARealAction"]
      }
      """)!;

    var errors = BehaviorValidator.Validate("test_species", speciesFile, badLibrary);
    Assert.That(errors.Any(e => e.Code == "unknown_node_ref"), Is.True);
  }

  [Test]
  public void DeterministicUid_StableAcrossRecompile()
  {
    var speciesFile = new BehaviorSpeciesFile { Extends = "herbivore_prey" };
    var a = BehaviorCompiler.Compile("rabbit", speciesFile, _library);
    var b = BehaviorCompiler.Compile("rabbit", speciesFile, _library);

  string FindUid(BehaviorTreeNode node, string leafId)
    {
      if (node.Type == BehaviorNodeType.Action && node.Id == leafId) return node.Uid;
      foreach (var child in node.Children)
      {
        string found = FindUid(child, leafId);
        if (!string.IsNullOrEmpty(found)) return found;
      }
      return "";
    }

    Assert.That(FindUid(a.Root, "Graze"), Is.EqualTo(FindUid(b.Root, "Graze")));
    Assert.That(FindUid(a.Root, "Graze"), Does.Contain("Graze"));
  }

  [Test]
  public void ExplicitUid_InJson_IsPreserved()
  {
    var customLibrary = JsonSerializer.Deserialize<BehaviorLibraryRoot>(
      File.ReadAllText(DataPaths.BehaviorLibraryJson),
      new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    customLibrary.Trees["herbivore_prey"] = JsonNode.Parse(
      """
      {
        "type": "selector",
        "uid": "custom-root",
        "children": ["Wander"]
      }
      """)!;

    var config = BehaviorCompiler.Compile("rabbit", new BehaviorSpeciesFile { Extends = "herbivore_prey" }, customLibrary);
    Assert.That(config.Root.Uid, Is.EqualTo("custom-root"));
  }

  [Test]
  public void GraphAdapter_ProducesNodesAndEdges()
  {
    var config = BehaviorCompiler.Compile("rabbit", new BehaviorSpeciesFile { Extends = "herbivore_prey" }, _library);
    var doc = BehaviorGraphAdapter.ToFlatDocument(config);

    Assert.That(doc.Nodes.Count, Is.GreaterThan(10));
    Assert.That(doc.Edges.Count, Is.EqualTo(doc.Nodes.Count - 1));
    Assert.That(doc.Nodes.Any(n => n.Type == "actionRef" && n.Ref == "Graze"), Is.True);
    Assert.That(doc.Nodes.Any(n => n.Id == "thirst_branch"), Is.True);
  }

  [Test]
  public void PatchInsertBefore_ResolvesBranchId()
  {
    var speciesFile = new BehaviorSpeciesFile
    {
      Extends = "herbivore_prey",
      Tree = new BehaviorTreePatch
      {
        InsertBefore = new Dictionary<string, string>
        {
          ["survival_needs"] = "Wander",
        },
      },
    };

    var config = BehaviorCompiler.Compile("rabbit_patch", speciesFile, _library);
    int survivalIdx = config.Root.Children.FindIndex(ch => ch.Id == "survival_needs");
    Assert.That(survivalIdx, Is.GreaterThan(0));
    Assert.That(config.Root.Children[survivalIdx - 1].Id, Is.EqualTo("Wander"));
  }

  [Test]
  public void SelfContainedRoot_CompilesWithoutTemplate()
  {
    var speciesFile = new BehaviorSpeciesFile
    {
      Root = JsonNode.Parse(
        """
        { "type": "selector", "children": [
          { "type": "sequence", "id": "flee_branch", "children": ["HasThreat", "Flee"] },
          "Wander"
        ] }
        """),
    };

    var errors = BehaviorValidator.Validate("custom_species", speciesFile, _library);
    Assert.That(errors, Is.Empty, () => string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));

    var config = BehaviorCompiler.Compile("custom_species", speciesFile, _library);
    Assert.That(config.Root.Type, Is.EqualTo(BehaviorNodeType.Selector));
    Assert.That(config.Root.Children.Count, Is.EqualTo(2));
    Assert.That(config.Root.Children[0].Id, Is.EqualTo("flee_branch"));
  }

  [Test]
  public void SerializeThenRecompile_PreservesTreeStructure()
  {
    var original = BehaviorCompiler.Compile("rabbit", new BehaviorSpeciesFile { Extends = "herbivore_prey" }, _library);

    var json = BtSpeciesSerializer.ToSpeciesJson(original);
    var speciesFile = json.Deserialize<BehaviorSpeciesFile>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    Assert.That(speciesFile.Root, Is.Not.Null);

    var errors = BehaviorValidator.Validate("rabbit", speciesFile, _library);
    Assert.That(errors, Is.Empty, () => string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));

    var recompiled = BehaviorCompiler.Compile("rabbit", speciesFile, _library);
    Assert.That(BehaviorGraphAdapter.TreesEquivalent(original.Root, recompiled.Root), Is.True);
  }

  [Test]
  public void EditorDocument_RoundTripsThroughSerializer()
  {
    var original = BehaviorCompiler.Compile("wolf", new BehaviorSpeciesFile { Extends = "carnivore" }, _library);
    var doc = BehaviorGraphAdapter.ToEditorDocument(original);

    Assert.That(doc.Root, Is.Not.Null);
    Assert.That(doc.Nodes.Values.Any(n => n.X != 0 || n.Y != 0), Is.True, "auto-layout should assign positions");

    var json = BtSpeciesSerializer.ToSpeciesJson(doc);
    var speciesFile = json.Deserialize<BehaviorSpeciesFile>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    var recompiled = BehaviorCompiler.Compile("wolf", speciesFile, _library);

    Assert.That(BehaviorGraphAdapter.TreesEquivalent(original.Root, recompiled.Root), Is.True);
  }

  [Test]
  public void AllSpeciesBehaviorFiles_ValidateClean()
  {
    var schema = BehaviorSchema.Load();
    foreach (string path in Directory.GetFiles(DataPaths.BehaviorsDirectory, "*.json"))
    {
      string name = Path.GetFileNameWithoutExtension(path);
      if (name is "library" or "schema") continue;

      var file = JsonSerializer.Deserialize<BehaviorSpeciesFile>(File.ReadAllText(path), new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true,
      })!;
      var errors = BehaviorValidator.Validate(name, file, _library, schema);
      Assert.That(errors, Is.Empty, () => string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));
    }
  }

  private static string FindRepoRoot()
  {
    string? dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
      if (File.Exists(Path.Combine(dir, "data", "species.json"))) return dir;
      dir = Directory.GetParent(dir)?.FullName;
    }
    throw new InvalidOperationException("Repo root not found");
  }
}
