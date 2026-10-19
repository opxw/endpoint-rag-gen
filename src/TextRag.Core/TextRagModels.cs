using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TextRag.Core;

public sealed record TextSource(string Name, string Content);

public sealed record TextRagChunk(
    string Id,
    string Kind,
    string Source,
    string Title,
    string Text,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> EndpointIds,
    IReadOnlyList<string> RelatedKnowledgeIds,
    string ContentHash);

public sealed record TextRagOptions(string AssistantName, string Domain, string Language = "Indonesia");

public sealed record TextRagBundle(
    IReadOnlyList<TextRagChunk> Chunks,
    IReadOnlyDictionary<string, string> Files);

public static class TextRagJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
