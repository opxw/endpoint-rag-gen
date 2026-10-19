using System.Text.Json;

namespace TextRag.Core;

public sealed record TextRagProject(
    string Format,
    int Version,
    TextRagOptions Options,
    IReadOnlyList<TextSource> Sources,
    string EditorName,
    string EditorContent,
    int SelectedSourceIndex);

public static class TextRagProjectFile
{
    public const string Format = "text-rag-project";
    public const int MaximumFileBytes = 32 * 1024 * 1024;

    public static string Serialize(TextRagProject project)
    {
        Validate(project);
        return TextRagJson.Serialize(project);
    }

    public static async Task SaveAsync(string path, TextRagProject project)
    {
        var json = Serialize(project);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaximumFileBytes)
            throw new InvalidOperationException("File proyek maksimal 32 MB.");
        var destination = Path.GetFullPath(path);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task<TextRagProject> LoadAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumFileBytes)
            throw new InvalidOperationException("File proyek maksimal 32 MB.");
        var project = await JsonSerializer.DeserializeAsync<TextRagProject>(stream, TextRagJson.Options)
            ?? throw new InvalidOperationException("File proyek kosong atau tidak valid.");
        Validate(project);
        return project;
    }

    private static void Validate(TextRagProject project)
    {
        if (project.Format != Format || project.Version != 1)
            throw new InvalidOperationException("Format atau versi file proyek tidak didukung.");
        if (project.Options is null || project.Options.AssistantName is null || project.Options.Domain is null ||
            project.Options.Language is null || project.Sources is null || project.EditorName is null ||
            project.EditorContent is null || project.SelectedSourceIndex < -1 ||
            project.SelectedSourceIndex >= project.Sources.Count)
            throw new InvalidOperationException("Data proyek tidak lengkap atau tidak valid.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in project.Sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Name) || string.IsNullOrWhiteSpace(source.Content) ||
                source.Name != Path.GetFileName(source.Name) || !names.Add(source.Name))
                throw new InvalidOperationException("Sumber dalam file proyek tidak valid atau duplikat.");
        }
    }
}
