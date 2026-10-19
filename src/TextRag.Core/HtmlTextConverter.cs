using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace TextRag.Core;

public static class HtmlTextConverter
{
    public const int MaximumInputCharacters = 10_000_000;

    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "head", "script", "style", "template", "iframe", "object", "embed"
    };

    private static readonly HashSet<string> Blocks = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "caption", "dd", "details", "div", "dl", "dt",
        "fieldset", "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6",
        "header", "hr", "li", "main", "nav", "ol", "p", "pre", "section", "summary", "table", "tr", "ul"
    };

    public static bool IsHtmlFile(string path) =>
        Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".htm", StringComparison.OrdinalIgnoreCase);

    public static string Convert(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (html.Length > MaximumInputCharacters)
            throw new InvalidOperationException("HTML maksimal 10 juta karakter. Pecah menjadi beberapa dokumen.");

        // Parse local markup only: no resource loader, JavaScript engine, or network requests.
        using var document = new HtmlParser().ParseDocument(html);
        var output = new PlainTextWriter();
        var pending = new Stack<(INode Node, bool Exit, bool Preformatted)>();
        var listNumbers = new Dictionary<IElement, (long Number, int Step)>();
        pending.Push((document.Body ?? (INode)document.DocumentElement, false, false));
        while (pending.TryPop(out var step))
        {
            if (step.Node is IText text)
            {
                output.Append(text.Data, step.Preformatted);
                continue;
            }
            if (step.Node is not IElement element) continue;
            var tag = element.LocalName;
            if (Ignored.Contains(tag) || element.HasAttribute("hidden")) continue;
            if (step.Exit)
            {
                if (Blocks.Contains(tag)) output.Break();
                if (tag is "td" or "th") output.CellSeparator();
                continue;
            }
            if (Blocks.Contains(tag) || tag == "br") output.Break();
            if (tag == "ol")
            {
                var reversed = element.HasAttribute("reversed");
                var start = int.TryParse(element.GetAttribute("start"), out var specifiedStart)
                    ? specifiedStart : reversed ? element.Children.Count(child => child.LocalName == "li") : 1;
                listNumbers[element] = (start, reversed ? -1 : 1);
            }
            if (tag == "li")
            {
                var parent = element.ParentElement;
                if (parent is not null && listNumbers.TryGetValue(parent, out var counter))
                {
                    var number = int.TryParse(element.GetAttribute("value"), out var value) ? value : counter.Number;
                    output.Append($"{number}. ", false);
                    listNumbers[parent] = (number + counter.Step, counter.Step);
                }
                else output.Append("• ", false);
            }
            if (tag == "img") output.Append(element.GetAttribute("alt") ?? "", false);
            var preformatted = step.Preformatted || tag == "pre";
            pending.Push((element, true, preformatted));
            for (var child = element.LastChild; child is not null; child = child.PreviousSibling)
                pending.Push((child, false, preformatted));
        }
        return output.ToString();
    }

    private sealed class PlainTextWriter
    {
        private readonly StringBuilder text = new();
        private bool pendingSpace;

        public void Append(string value, bool preformatted)
        {
            if (preformatted)
            {
                text.Append(value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
                pendingSpace = false;
                return;
            }
            foreach (var character in value)
            {
                if (char.IsWhiteSpace(character)) { pendingSpace = true; continue; }
                if (pendingSpace && text.Length > 0 && !char.IsWhiteSpace(text[^1])) text.Append(' ');
                text.Append(character);
                pendingSpace = false;
            }
        }

        public void Break()
        {
            pendingSpace = false;
            while (text.Length > 0 && text[^1] is ' ' or '\t') text.Length--;
            if (text.Length > 0 && text[^1] != '\n') text.Append('\n');
        }

        public void CellSeparator()
        {
            pendingSpace = false;
            text.Append('\t');
        }

        public override string ToString() => text.ToString().Trim('\n');
    }
}
