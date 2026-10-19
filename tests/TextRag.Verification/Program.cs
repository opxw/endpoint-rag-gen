using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TextRag.Core;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}

Check(HtmlTextConverter.Convert("<h1>Judul</h1><p>Satu <b>dua</b> &amp; tiga&nbsp;empat.<br>Baris baru.</p>") ==
    "Judul\nSatu dua & tiga empat.\nBaris baru.", "HTML headings, paragraphs, inline text, entities and breaks become plain text");
Check(HtmlTextConverter.Convert("<head><title>Metadata</title></head><body><!-- comment --><script>alert('x')</script><style>body{color:red}</style><template>Template</template><p hidden>Hidden</p><p>Isi</p></body>") == "Isi",
    "HTML metadata, scripts, styles, comments, templates and hidden nodes are excluded");
Check(HtmlTextConverter.Convert("<p>te<b>ks</b> &lt;b&gt; &amp;lt; &copy; &#128512;</p>") == "teks <b> &lt; © 😀",
    "HTML entities are decoded once and adjacent inline text stays intact");
Check(HtmlTextConverter.Convert("<ul><li>Satu<li>Dua</ul><ol start='3'><li>Tiga<li value='7'>Tujuh</ol>") ==
    "• Satu\n• Dua\n3. Tiga\n7. Tujuh", "HTML lists and omitted closing tags preserve ordered items");
Check(HtmlTextConverter.Convert("<ol reversed><li>Dua<li>Satu</ol>") == "2. Dua\n1. Satu", "Reversed HTML list numbering is preserved");
Check(HtmlTextConverter.Convert("<table><tr><th>Nama</th><th>Nilai</th></tr><tr><td>A</td><td>10</td></tr></table>") ==
    "Nama\tNilai\nA\t10", "HTML table cells and rows remain separated");
Check(HtmlTextConverter.Convert("<pre>  first\n  second\n\tthird</pre>") == "  first\n  second\n\tthird", "HTML preformatted indentation is preserved");
Check(HtmlTextConverter.Convert("<p><a href='https://example.invalid'>Panduan</a><img src='https://example.invalid/x.png' alt=' diagram'></p>") ==
    "Panduan diagram", "HTML links retain labels and images retain alt text without fetching resources");
Check(HtmlTextConverter.Convert("<script>only script</script>") == "", "HTML without textual content produces no knowledge");
Check(HtmlTextConverter.Convert("nilai 1 < 2 dan 3 > 2") == "nilai 1 < 2 dan 3 > 2", "Literal comparisons survive HTML parsing");
Check(HtmlTextConverter.IsHtmlFile("REPORT.HTML") && HtmlTextConverter.IsHtmlFile("a.htm") && !HtmlTextConverter.IsHtmlFile("a.md"),
    "HTML import is selected only by explicit HTML file extension");
var htmlLimitRejected = false;
try { HtmlTextConverter.Convert(new string('x', HtmlTextConverter.MaximumInputCharacters + 1)); }
catch (InvalidOperationException) { htmlLimitRejected = true; }
Check(htmlLimitRejected, "HTML exceeding the conversion limit is rejected without truncation");

var sources = new[]
{
    new TextSource("cuti.md", "# Tujuan\nPanduan cuti tahunan.\n\n# Prosedur\nAjukan melalui HR sesuai kebijakan."),
    new TextSource("absensi.txt", "Kehadiran aktual harus dibaca dari sistem yang berwenang.")
};
var first = TextChunker.Build(sources);
var second = TextChunker.Build(sources);
Check(first.Count == 3, "Markdown heading forms readable chunks");
Check(first.Select(chunk => chunk.Id).SequenceEqual(second.Select(chunk => chunk.Id)), "Chunk IDs are deterministic");
Check(first.All(chunk => chunk.Kind == "knowledge" && chunk.EndpointIds.Count == 0), "Text chunks never declare endpoints");
Check(first.Sum(chunk => chunk.Text.Length) > 0 && sources.All(source => first.Any(chunk => chunk.Text.Contains(source.Content.Split('\n').Last(), StringComparison.Ordinal))), "Source facts are preserved");

var bundle = TextRagExporter.Build(sources, new("HR Assistant", "HR"));
using (var catalog = JsonDocument.Parse(bundle.Files["api-rag-knowledge.json"]))
{
    Check(catalog.RootElement.GetProperty("generator").GetString() == "text-rag-gen", "Catalog identifies text generator");
    Check(catalog.RootElement.GetProperty("documentCount").GetInt32() == 0 && catalog.RootElement.GetProperty("documents").GetArrayLength() == 0, "Catalog contains no endpoint");
}
Check(bundle.Files["rag-system-prompt.md"].Contains("tidak mendeklarasikan endpoint API", StringComparison.Ordinal), "System prompt forbids invented endpoints");

var requestedParent = args.Length == 1 ? Path.GetFullPath(args[0]) : null;
var parent = requestedParent ?? Path.Combine(Path.GetTempPath(), "text-rag-verification-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(parent);
try
{
    var projectPath = Path.Combine(parent, "roundtrip.textrag.json");
    var htmlSource = new TextSource("panduan.html", HtmlTextConverter.Convert("<h1>Panduan</h1><p>Gunakan &lt;b&gt; sebagai contoh.</p><script>bad()</script>"));
    var htmlProject = new TextRagProject(TextRagProjectFile.Format, 1, new("Asisten", "Panduan"), [htmlSource], "", "", -1);
    await TextRagProjectFile.SaveAsync(projectPath, htmlProject);
    var restoredHtml = await TextRagProjectFile.LoadAsync(projectPath);
    Check(restoredHtml.Sources[0] == htmlSource, "Converted HTML remains plain text after project save/load without a second conversion");
    var htmlBundle = TextRagExporter.Build(restoredHtml.Sources, restoredHtml.Options);
    Check(htmlBundle.Chunks.Any(chunk => chunk.Text.Contains("Gunakan <b> sebagai contoh.", StringComparison.Ordinal)) &&
        htmlBundle.Chunks.All(chunk => !chunk.Text.Contains("bad()", StringComparison.Ordinal)), "Converted HTML facts survive RAG generation without script content");
    var project = new TextRagProject(TextRagProjectFile.Format, 1, new("Asisten 日本語", "SDM"),
        sources, "cuti.md", "# Draft\nPerubahan belum ditambahkan 😀", 0);
    await TextRagProjectFile.SaveAsync(projectPath, project);
    var loaded = await TextRagProjectFile.LoadAsync(projectPath);
    Check(loaded.Options == project.Options && loaded.Sources.SequenceEqual(sources), "Project restores identity and all source contents");
    Check(loaded.EditorName == project.EditorName && loaded.EditorContent == project.EditorContent && loaded.SelectedSourceIndex == 0,
        "Project restores unsaved Unicode editor draft and selection separately from sources");
    Check(TextChunker.Build(loaded.Sources).Select(chunk => chunk.Id).SequenceEqual(first.Select(chunk => chunk.Id)),
        "Project round trip preserves deterministic chunk IDs");
    var empty = project with { Sources = [], EditorName = "", EditorContent = "", SelectedSourceIndex = -1 };
    await TextRagProjectFile.SaveAsync(projectPath, empty);
    Check((await TextRagProjectFile.LoadAsync(projectPath)).Sources.Count == 0, "Empty project can be saved and loaded");
    await TextRagProjectFile.SaveAsync(projectPath, project);
    await TextRagProjectFile.SaveAsync(projectPath, project with { EditorContent = "draft pengganti" });
    Check((await TextRagProjectFile.LoadAsync(projectPath)).EditorContent == "draft pengganti", "Saving replaces an existing project");
    try
    {
        await TextRagProjectFile.SaveAsync(projectPath, project with { Version = 2 });
        Check(false, "Unsupported version must fail saving");
    }
    catch (InvalidOperationException)
    {
        Check((await TextRagProjectFile.LoadAsync(projectPath)).EditorContent == "draft pengganti", "Failed save preserves the existing project");
    }
    async Task RejectProject(string json, string name)
    {
        var invalidPath = Path.Combine(parent, "invalid.textrag.json");
        await File.WriteAllTextAsync(invalidPath, json);
        var rejected = false;
        try { await TextRagProjectFile.LoadAsync(invalidPath); }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { rejected = true; }
        Check(rejected, name);
    }
    await RejectProject("{ broken", "Malformed JSON is rejected");
    await RejectProject("{}", "Missing project data is rejected");
    await RejectProject("null", "Null project is rejected");
    await RejectProject(TextRagJson.Serialize(project with { Version = 99 }), "Future project version is rejected");
    await RejectProject(TextRagJson.Serialize(project with { Sources = [sources[0], sources[0]] }), "Duplicate project sources are rejected");
    await RejectProject(TextRagJson.Serialize(project with { SelectedSourceIndex = 10 }), "Invalid editor selection is rejected");
    await RejectProject(TextRagJson.Serialize(project with { Options = null! }), "Missing project options are rejected");
    var largePath = Path.Combine(parent, "large.textrag.json");
    using (var large = File.Create(largePath)) large.SetLength(TextRagProjectFile.MaximumFileBytes + 1L);
    var oversizedRejected = false;
    try { await TextRagProjectFile.LoadAsync(largePath); }
    catch (InvalidOperationException) { oversizedRejected = true; }
    Check(oversizedRejected, "Oversized project file is rejected before deserialization");

    var output = await TextRagExporter.ExportAsync(parent, bundle);
    var zipPath = Path.Combine(output, "rag-package.zip");
    if (requestedParent is not null) Console.WriteLine("PACKAGE: " + zipPath);
    Check(File.Exists(zipPath), "ZIP package is created");
    using var archive = ZipFile.OpenRead(zipPath);
    Check(archive.Entries.All(entry => !entry.FullName.Contains('/') && !entry.FullName.Contains('\\')), "ZIP contains root files only");
    Check(TextRagExporter.CoreFileNames.All(name => archive.GetEntry(name) is not null), "ZIP contains six core files");
    using var manifestReader = new StreamReader(archive.GetEntry("manifest.json")!.Open(), Encoding.UTF8);
    using var manifest = JsonDocument.Parse(await manifestReader.ReadToEndAsync());
    Check(manifest.RootElement.GetProperty("chunkCount").GetInt32() == first.Count, "Manifest chunk count matches JSONL");
    foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
    {
        var name = file.GetProperty("path").GetString()!;
        await using var stream = archive.GetEntry(name)!.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        Check(Convert.ToHexString(SHA256.HashData(memory.ToArray())) == file.GetProperty("sha256").GetString(), "Manifest hash matches " + name);
    }
}
finally
{
    if (requestedParent is null) Directory.Delete(parent, true);
}

Console.WriteLine($"PASS: {checks} checks");
