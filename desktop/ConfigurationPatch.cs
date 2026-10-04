using System.Linq;
using System.Text.Json.Nodes;

namespace CodexPresence;

public static class ConfigurationPatch
{
    public static JsonObject Merge(JsonObject original, JsonObject edited, JsonObject current)
    {
        var result = current.DeepClone().AsObject();
        foreach (var key in original.Select(pair => pair.Key).Union(edited.Select(pair => pair.Key)))
        {
            if (JsonNode.DeepEquals(original[key], edited[key])) continue;
            if (!edited.ContainsKey(key)) { result.Remove(key); continue; }
            result[key] = original[key] is JsonObject before && edited[key] is JsonObject after && result[key] is JsonObject latest
                ? Merge(before, after, latest) : edited[key]?.DeepClone();
        }
        return result;
    }
}
