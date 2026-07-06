using System.Text.Json;
using System.Text.Json.Nodes;

namespace EcoSim.Core.Util;

public static class DeepMerge
{
    public static JsonNode? Merge(JsonNode? baseNode, JsonNode? overrideNode)
    {
        if (overrideNode == null) return baseNode?.DeepClone();
        if (baseNode == null) return overrideNode.DeepClone();

        if (baseNode is JsonObject baseObj && overrideNode is JsonObject overrideObj)
        {
            var result = baseObj.DeepClone().AsObject();
            foreach (var (key, value) in overrideObj)
            {
                if (value == null)
                {
                    result[key] = null;
                    continue;
                }

                if (result[key] is JsonObject baseChild && value is JsonObject overrideChild
                    && baseChild.Count > 0 && !IsJsonArray(baseChild))
                {
                    result[key] = Merge(baseChild, overrideChild);
                }
                else
                {
                    result[key] = value.DeepClone();
                }
            }
            return result;
        }

        return overrideNode.DeepClone();
    }

    public static T MergeObjects<T>(T baseObj, T? overrideObj) where T : class
    {
        if (overrideObj == null) return baseObj;
        var baseJson = JsonSerializer.SerializeToNode(baseObj)!.AsObject();
        var overrideJson = JsonSerializer.SerializeToNode(overrideObj)!.AsObject();
        var merged = Merge(baseJson, overrideJson)!.AsObject();
        return merged.Deserialize<T>()!;
    }

    private static bool IsJsonArray(JsonObject obj)
    {
        return obj.Count > 0 && obj.First().Value is JsonArray;
    }
}
