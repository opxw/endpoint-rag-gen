using System.Text;
using System.Text.RegularExpressions;

namespace TextRag.Core;

public static partial class TextChunker
{
    public const int MaximumSourceCharacters = 180_000;
    public const int TargetChunkCharacters = 4_000;
    public const int MaximumChunkCharacters = 6_000;

    public static IReadOnlyList<TextRagChunk> Build(IReadOnlyList<TextSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0) throw new InvalidOperationException("Tambahkan minimal satu sumber teks.");

        var normalized = sources.Select(NormalizeSource).ToArray();
        var total = normalized.Sum(source => source.Content.Length);
        if (total == 0) throw new InvalidOperationException("Konten sumber tidak boleh kosong.");
        if (total > MaximumSourceCharacters)
            throw new InvalidOperationException($"Total teks maksimal {MaximumSourceCharacters:N0} karakter per paket. Pecah menjadi beberapa paket tanpa memotong sumber.");

        var chunks = new List<TextRagChunk>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in normalized)
        {
            if (!seenNames.Add(source.Name)) throw new InvalidOperationException($"Nama sumber duplikat: {source.Name}");
            var sections = SplitSections(source.Content);
            var ordinal = 0;
            foreach (var section in sections)
            {
                foreach (var text in SplitBounded(section.Text))
                {
                    ordinal++;
                    var hash = TextRagJson.Hash(text);
                    var slug = Slug(Path.GetFileNameWithoutExtension(source.Name));
                    var id = $"knowledge:{slug}:{ordinal:0000}:{hash[..12].ToLowerInvariant()}";
                    chunks.Add(new(id, "knowledge", source.Name, section.Title, text, [], [], [], hash));
                }
            }
        }

        return chunks;
    }

    private static TextSource NormalizeSource(TextSource source)
    {
        var name = Path.GetFileName(source.Name?.Trim());
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180)
            throw new InvalidOperationException("Nama sumber wajib 1–180 karakter.");
        var text = (source.Content ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException($"Sumber {name} kosong.");
        return new(name, text);
    }

    private static IReadOnlyList<(string Title, string Text)> SplitSections(string content)
    {
        var result = new List<(string, string)>();
        var title = "Dokumen";
        var body = new StringBuilder();
        foreach (var line in content.Split('\n'))
        {
            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                Flush();
                title = heading.Groups[1].Value.Trim();
                continue;
            }
            body.AppendLine(line);
        }
        Flush();
        return result;

        void Flush()
        {
            var text = body.ToString().Trim();
            if (text.Length > 0) result.Add((title, text));
            body.Clear();
        }
    }

    private static IEnumerable<string> SplitBounded(string section)
    {
        if (section.Length <= MaximumChunkCharacters) { yield return section; yield break; }
        var paragraphs = ParagraphRegex().Split(section).Where(value => !string.IsNullOrWhiteSpace(value));
        var current = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            foreach (var piece in SplitLongParagraph(trimmed))
            {
                if (current.Length > 0 && current.Length + 2 + piece.Length > TargetChunkCharacters)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                if (current.Length > 0) current.AppendLine().AppendLine();
                current.Append(piece);
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static IEnumerable<string> SplitLongParagraph(string paragraph)
    {
        while (paragraph.Length > MaximumChunkCharacters)
        {
            var boundary = paragraph.LastIndexOfAny(['.', '!', '?', ';', '\n', ' '], MaximumChunkCharacters - 1);
            if (boundary < TargetChunkCharacters / 2) boundary = MaximumChunkCharacters;
            yield return paragraph[..boundary].Trim();
            paragraph = paragraph[boundary..].Trim();
        }
        if (paragraph.Length > 0) yield return paragraph;
    }

    private static string Slug(string value)
    {
        var slug = NonSlugRegex().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "source" : slug[..Math.Min(48, slug.Length)];
    }

    [GeneratedRegex("^#{1,6}\\s+(.+?)\\s*$", RegexOptions.Compiled)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex("\\n\\s*\\n+", RegexOptions.Compiled)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex NonSlugRegex();
}
