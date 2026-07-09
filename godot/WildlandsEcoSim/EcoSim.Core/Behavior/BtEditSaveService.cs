using System.Text.Json;
using System.Text.Json.Nodes;
using EcoSim.Core.Data;

namespace EcoSim.Core.Behavior;

/// <summary>
/// Persists visual-BT-editor changes to <c>data/behaviors/{stem}.json</c> (self-contained tree)
/// plus a layout sidecar, then recompiles the affected species so live sim picks up edits.
/// Writes only succeed in dev/editor contexts where the behaviors directory is on a writable disk.
/// </summary>
public sealed class BtEditSaveService
{
  private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

  private readonly BehaviorLibrary _library;
  private readonly SpeciesCatalog _catalog;

  public BtEditSaveService(BehaviorLibrary library, SpeciesCatalog catalog)
  {
    _library = library;
    _catalog = catalog;
  }

  public bool CanWrite
  {
    get
    {
      try
      {
        return Directory.Exists(DataPaths.BehaviorsDirectory);
      }
      catch
      {
        return false;
      }
    }
  }

  public string BehaviorStem(string speciesKey)
  {
    if (_catalog.TryGet(speciesKey, out var def) && def != null && !string.IsNullOrEmpty(def.Behavior))
    {
      return def.Behavior;
    }
    return speciesKey;
  }

  public sealed class SaveResult
  {
    public bool Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<BehaviorValidationError> ValidationErrors { get; init; } = [];
    public BehaviorConfig? Config { get; init; }
  }

  public SaveResult Save(string speciesKey, BtEditorDocument doc)
  {
    if (!CanWrite)
    {
      return new SaveResult { Success = false, Error = "Behaviors directory is not writable (exported build)." };
    }

    string stem = BehaviorStem(speciesKey);

    JsonObject fileJson;
    try
    {
      fileJson = BtSpeciesSerializer.ToSpeciesJson(doc);
    }
    catch (Exception ex)
    {
      return new SaveResult { Success = false, Error = $"Serialization failed: {ex.Message}" };
    }

    var errors = _library.ValidateBehaviorFile(stem, fileJson);
    if (errors.Count > 0)
    {
      return new SaveResult { Success = false, Error = "Validation failed.", ValidationErrors = errors };
    }

    try
    {
      File.WriteAllText(DataPaths.BehaviorFile(stem), fileJson.ToJsonString(WriteOptions));
      WriteLayoutSidecar(stem, doc);
      _library.ReloadBehaviorFile(stem);
      var config = _library.RecompileSpecies(_catalog, stem);
      return new SaveResult { Success = true, Config = config };
    }
    catch (Exception ex)
    {
      return new SaveResult { Success = false, Error = ex.Message };
    }
  }

  /// <summary>Recompiles from the on-disk file, discarding in-memory edits, and returns the fresh config.</summary>
  public BehaviorConfig? Revert(string speciesKey)
  {
    string stem = BehaviorStem(speciesKey);
    try
    {
      _library.ReloadBehaviorFile(stem);
      return _library.RecompileSpecies(_catalog, stem);
    }
    catch
    {
      return null;
    }
  }

  private static void WriteLayoutSidecar(string stem, BtEditorDocument doc)
  {
    var sidecar = new BehaviorLayoutSidecar();
    foreach (var node in doc.Nodes.Values)
    {
      if (string.IsNullOrEmpty(node.Uid)) continue;
      sidecar.Nodes[node.Uid] = new BehaviorLayoutNode { X = node.X, Y = node.Y };
    }

    if (sidecar.Nodes.Count == 0) return;

    string path = DataPaths.BehaviorEditorLayout(stem);
    string? dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    File.WriteAllText(path, JsonSerializer.Serialize(sidecar, WriteOptions));
  }
}
