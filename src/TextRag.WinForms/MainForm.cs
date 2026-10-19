using System.Diagnostics;
using System.Text;
using TextRag.Core;

namespace TextRag.WinForms;

public sealed class MainForm : Form
{
    private readonly TextBox assistant = new() { Text = "Asisten Organisasi", Dock = DockStyle.Fill };
    private readonly TextBox domain = new() { Text = "Pengetahuan organisasi", Dock = DockStyle.Fill };
    private readonly TextBox sourceName = new() { PlaceholderText = "contoh: prosedur-cuti.md", Dock = DockStyle.Fill };
    private readonly RichTextBox content = new() { Dock = DockStyle.Fill, AcceptsTab = true, DetectUrls = false };
    private readonly ListBox sources = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly DataGridView chunks = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Belum ada sumber." };
    private readonly Button saveSource = new() { Text = "Tambahkan teks", AutoSize = true };
    private readonly Button removeSource = new() { Text = "Hapus sumber", AutoSize = true };
    private readonly Button export = new() { Text = "Ekspor paket RAG", AutoSize = true };
    private readonly ToolStripMenuItem buildPackage = new("&Build package");
    private readonly List<TextSource> items = [];
    private string? projectPath;
    private string savedProject = "";
    private string projectLanguage = "Indonesia";
    private bool closingApproved;

    public MainForm()
    {
        Text = "Text RAG Generator";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        Size = new Size(1180, 760);
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        MainMenuStrip = BuildMainMenu();
        Controls.Add(MainMenuStrip);

        var heading = new Label
        {
            Text = "Import Text untuk Vector Database\r\nMasukkan teks, Markdown, atau HTML, periksa chunk, lalu ekspor ZIP RAG.",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        };
        root.Controls.Add(heading);

        var identity = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        identity.Controls.Add(new Label { Text = "Nama asisten", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        identity.Controls.Add(assistant, 1, 0);
        identity.Controls.Add(new Label { Text = "Domain", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 3, 3, 3) }, 2, 0);
        identity.Controls.Add(domain, 3, 0);
        root.Controls.Add(identity);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 520, FixedPanel = FixedPanel.None };
        root.Controls.Add(split);
        split.Panel1.Controls.Add(BuildInputPanel());
        split.Panel2.Controls.Add(BuildPreviewPanel());

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(status, 0, 0);
        footer.Controls.Add(export, 1, 0);
        root.Controls.Add(footer);

        saveSource.Click += (_, _) => AddOrUpdateSource();
        removeSource.Click += (_, _) => RemoveSelectedSource();
        export.Click += async (_, _) => await ExportAsync();
        sources.SelectedIndexChanged += (_, _) => LoadSelectedSource();
        AcceptButton = export;
        RefreshPreview();
        savedProject = TextRagProjectFile.Serialize(CaptureProject());
        FormClosing += async (_, e) =>
        {
            if (!Enabled) { e.Cancel = true; return; }
            if (closingApproved || !HasProjectChanges()) return;
            e.Cancel = true;
            if (await ConfirmProjectChangesAsync())
            {
                closingApproved = true;
                BeginInvoke(new Action(Close));
            }
        };
    }

    private MenuStrip BuildMainMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top };
        var file = new ToolStripMenuItem("&File");
        var loadProject = new ToolStripMenuItem("&Muat proyek…") { ShortcutKeys = Keys.Control | Keys.O };
        var saveProject = new ToolStripMenuItem("&Simpan proyek") { ShortcutKeys = Keys.Control | Keys.S };
        var saveProjectAs = new ToolStripMenuItem("Simpan se&bagai…") { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S };
        var exit = new ToolStripMenuItem("&Keluar");
        loadProject.Click += async (_, _) => await LoadProjectAsync();
        saveProject.Click += async (_, _) => await SaveProjectAsync();
        saveProjectAs.Click += async (_, _) => await SaveProjectAsync(saveAs: true);
        exit.Click += (_, _) => Close();
        file.DropDownItems.AddRange([loadProject, saveProject, saveProjectAs, new ToolStripSeparator(), exit]);
        menu.Items.Add(file);
        var format = new ToolStripMenuItem("F&ormat");
        var htmlToText = new ToolStripMenuItem("HTML ke teks biasa");
        htmlToText.Click += async (_, _) => await ConvertEditorHtmlAsync();
        format.DropDownItems.Add(htmlToText);
        menu.Items.Add(format);
        buildPackage.Click += async (_, _) => await ExportAsync(Path.Combine(AppContext.BaseDirectory, "package"));
        menu.Items.Add(buildPackage);
        return menu;
    }

    private TextRagProject CaptureProject() => new(TextRagProjectFile.Format, 1,
        new(assistant.Text, domain.Text, projectLanguage), items.ToArray(), sourceName.Text, content.Text,
        sources.SelectedIndex);

    private bool HasProjectChanges() => TextRagProjectFile.Serialize(CaptureProject()) != savedProject;

    private async Task<bool> ConfirmProjectChangesAsync()
    {
        if (!HasProjectChanges()) return true;
        var answer = MessageBox.Show(this, "Simpan perubahan proyek sebelum melanjutkan?", "Perubahan belum disimpan",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer == DialogResult.No || (answer == DialogResult.Yes && await SaveProjectAsync());
    }

    private async Task<bool> SaveProjectAsync(bool saveAs = false)
    {
        var path = projectPath;
        if (saveAs || path is null)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "Proyek Text RAG (*.textrag.json)|*.textrag.json",
                DefaultExt = "textrag.json", AddExtension = true,
                FileName = path is null ? "proyek.textrag.json" : Path.GetFileName(path),
                Title = "Simpan proyek Text RAG"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try
        {
            Enabled = false;
            var project = CaptureProject();
            await TextRagProjectFile.SaveAsync(path, project);
            projectPath = path;
            savedProject = TextRagProjectFile.Serialize(project);
            Text = $"Text RAG Generator — {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, error.Message, "Simpan proyek gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally { Enabled = true; }
    }

    private async Task LoadProjectAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Proyek Text RAG (*.textrag.json)|*.textrag.json|File JSON (*.json)|*.json",
            Title = "Muat proyek Text RAG"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        TextRagProject project;
        try
        {
            Enabled = false;
            project = await TextRagProjectFile.LoadAsync(dialog.FileName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, error.Message, "Muat proyek gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally { Enabled = true; }
        if (!await ConfirmProjectChangesAsync()) return;
        items.Clear();
        items.AddRange(project.Sources);
        assistant.Text = project.Options.AssistantName;
        domain.Text = project.Options.Domain;
        projectLanguage = project.Options.Language;
        RefreshPreview();
        sources.SelectedIndex = project.SelectedSourceIndex;
        sourceName.Text = project.EditorName;
        content.Text = project.EditorContent;
        saveSource.Text = project.SelectedSourceIndex < 0 ? "Tambahkan teks" : "Perbarui teks";
        projectPath = dialog.FileName;
        savedProject = TextRagProjectFile.Serialize(CaptureProject());
        Text = $"Text RAG Generator — {Path.GetFileName(projectPath)}";
    }

    private Control BuildInputPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(0, 0, 8, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = "Sumber teks", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        panel.Controls.Add(sourceName);
        panel.Controls.Add(content);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var openFiles = new Button { Text = "Pilih file teks/HTML", AutoSize = true };
        var clearEditor = new Button { Text = "Kosongkan editor", AutoSize = true };
        openFiles.Click += async (_, _) => await OpenFilesAsync();
        clearEditor.Click += (_, _) => { sourceName.Clear(); content.Clear(); sources.ClearSelected(); saveSource.Text = "Tambahkan teks"; };
        actions.Controls.AddRange([openFiles, saveSource, clearEditor]);
        panel.Controls.Add(actions);
        panel.Controls.Add(sources);
        panel.Controls.Add(removeSource);
        return panel;
    }

    private Control BuildPreviewPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8, 0, 0, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = "Preview chunk", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        chunks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Sumber", FillWeight = 30 });
        chunks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Bagian", FillWeight = 28 });
        chunks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Chars", HeaderText = "Karakter", FillWeight = 14 });
        chunks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Preview", HeaderText = "Isi", FillWeight = 65 });
        panel.Controls.Add(chunks);
        panel.Controls.Add(new Label
        {
            Text = "ID dan hash dibuat deterministik dari sumber serta isi. Tidak ada teks yang dipotong diam-diam; paket terlalu besar akan ditolak.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 8, 0, 0)
        });
        return panel;
    }

    private async Task OpenFilesAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Dokumen teks/HTML (*.txt;*.md;*.markdown;*.html;*.htm)|*.txt;*.md;*.markdown;*.html;*.htm|HTML (*.html;*.htm)|*.html;*.htm",
            Multiselect = true,
            Title = "Pilih dokumen teks"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Enabled = false;
            foreach (var file in dialog.FileNames)
            {
                var info = new FileInfo(file);
                if (info.Length > 10_000_000) throw new InvalidOperationException($"File {info.Name} melebihi 10 MB.");
                var text = await File.ReadAllTextAsync(file, Encoding.UTF8);
                if (HtmlTextConverter.IsHtmlFile(file))
                    text = await Task.Run(() => HtmlTextConverter.Convert(text));
                Upsert(new TextSource(info.Name, text));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or InvalidOperationException)
        {
            MessageBox.Show(this, error.Message, "Import teks gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Enabled = true;
            RefreshPreview();
        }
    }

    private async Task ConvertEditorHtmlAsync()
    {
        try
        {
            var html = content.Text;
            Enabled = false;
            var plainText = await Task.Run(() => HtmlTextConverter.Convert(html));
            if (string.IsNullOrWhiteSpace(plainText))
                throw new InvalidOperationException("HTML tidak mengandung teks yang dapat digunakan.");
            content.SelectAll();
            content.SelectedText = plainText;
            content.SelectionStart = 0;
            content.SelectionLength = 0;
        }
        catch (InvalidOperationException error)
        {
            MessageBox.Show(this, error.Message, "Konversi HTML gagal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { Enabled = true; }
    }

    private void AddOrUpdateSource()
    {
        try
        {
            var name = sourceName.Text.Trim();
            if (string.IsNullOrWhiteSpace(Path.GetExtension(name))) name += ".txt";
            Upsert(new TextSource(name, content.Text));
            RefreshPreview();
            sourceName.Clear(); content.Clear(); sources.ClearSelected(); saveSource.Text = "Tambahkan teks";
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, error.Message, "Teks belum valid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Upsert(TextSource source)
    {
        var name = Path.GetFileName(source.Name.Trim());
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Nama sumber wajib diisi.");
        if (string.IsNullOrWhiteSpace(source.Content)) throw new InvalidOperationException("Konten teks wajib diisi.");
        var index = items.FindIndex(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        var value = new TextSource(name, source.Content);
        if (index >= 0) items[index] = value; else items.Add(value);
    }

    private void LoadSelectedSource()
    {
        if (sources.SelectedIndex < 0 || sources.SelectedIndex >= items.Count) return;
        var item = items[sources.SelectedIndex];
        sourceName.Text = item.Name;
        content.Text = item.Content;
        saveSource.Text = "Perbarui teks";
    }

    private void RemoveSelectedSource()
    {
        if (sources.SelectedIndex < 0 || sources.SelectedIndex >= items.Count) return;
        items.RemoveAt(sources.SelectedIndex);
        sourceName.Clear(); content.Clear(); saveSource.Text = "Tambahkan teks";
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        sources.BeginUpdate();
        sources.Items.Clear();
        foreach (var item in items) sources.Items.Add($"{item.Name} · {item.Content.Length:N0} karakter");
        sources.EndUpdate();
        chunks.Rows.Clear();
        try
        {
            var built = items.Count == 0 ? [] : TextChunker.Build(items);
            foreach (var chunk in built)
            {
                var preview = chunk.Text.Replace('\n', ' ');
                if (preview.Length > 140) preview = preview[..140] + "…";
                chunks.Rows.Add(chunk.Source, chunk.Title, chunk.Text.Length.ToString("N0"), preview);
            }
            status.Text = items.Count == 0 ? "Belum ada sumber." : $"{items.Count} sumber · {built.Count} chunk · {items.Sum(item => item.Content.Length):N0} karakter";
            export.Enabled = built.Count > 0;
            buildPackage.Enabled = export.Enabled;
        }
        catch (InvalidOperationException error)
        {
            status.Text = error.Message;
            export.Enabled = false;
            buildPackage.Enabled = false;
        }
    }

    private async Task ExportAsync(string? outputDirectory = null)
    {
        try
        {
            var bundle = TextRagExporter.Build(items, new(assistant.Text, domain.Text, projectLanguage));
            if (outputDirectory is null)
            {
                using var dialog = new FolderBrowserDialog { Description = "Pilih folder output paket RAG", UseDescriptionForTitle = true };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                outputDirectory = dialog.SelectedPath;
            }
            Enabled = false;
            var output = await TextRagExporter.ExportAsync(outputDirectory, bundle);
            Enabled = true;
            var result = MessageBox.Show(this, $"Paket siap:\r\n{output}\r\n\r\nBuka folder hasil?", "Ekspor selesai", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result == DialogResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { output } });
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Enabled = true;
            MessageBox.Show(this, error.Message, "Ekspor gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
