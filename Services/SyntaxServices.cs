using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;

namespace HyperNote.Services;

/// <summary>
/// Fault-tolerant folding for tag-based markup: HTML, XML, XAML, SVG, and any
/// other angle-bracket format with opening/closing tags.
///
/// Why not AvalonEdit's built-in XmlFoldingStrategy? It drives an XmlReader and
/// only succeeds on well-formed XML. Real-world HTML breaks it instantly:
/// void elements (&lt;br&gt;, &lt;img&gt;, &lt;meta&gt;…), optionally-closed
/// elements (&lt;li&gt;, &lt;p&gt;), and &lt;script&gt;/&lt;style&gt; bodies full
/// of bare &lt; and &gt; characters. This strategy tokenizes tags directly and
/// matches them with a forgiving stack, so it folds even malformed markup and
/// keeps working while you type.
///
/// What it produces:
///  - A collapsible region for every multi-line element (matched open/close pair).
///  - Multi-line &lt;!-- comments --&gt; fold too.
///  - &lt;script&gt; / &lt;style&gt; bodies are treated as raw text (their
///    contents are never parsed as tags) and fold as one block.
///  - Self-closing tags (&lt;tag/&gt;) and HTML void elements are never opened.
///  - CDATA, DOCTYPE, and processing instructions are skipped.
/// </summary>
public static class MarkupFoldingStrategy
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr"
    };

    private static readonly HashSet<string> RawTextElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style"
    };

    private struct OpenTag
    {
        public string Name;
        public int ContentStart; // offset just past the opening tag's '>'
    }

    public static void UpdateFoldings(FoldingManager manager, TextDocument document, bool html)
    {
        var foldings = CreateFoldings(document.Text, html);
        manager.UpdateFoldings(foldings, -1);
    }

    public static List<NewFolding> CreateFoldings(string text, bool html)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<OpenTag>();
        var cmp = html ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int i = 0, n = text.Length;

        while (i < n)
        {
            if (text[i] != '<') { i++; continue; }

            // <!-- comment -->
            if (Match(text, i, "<!--"))
            {
                int end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (end < 0) break;
                int close = end + 3;
                AddIfMultiline(foldings, text, i, close, " <!-- … --> ");
                i = close; continue;
            }
            // <![CDATA[ ... ]]>
            if (Match(text, i, "<![CDATA["))
            {
                int end = text.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                if (end < 0) break;
                i = end + 3; continue;
            }
            // <!DOCTYPE …> and other declarations
            if (i + 1 < n && text[i + 1] == '!')
            {
                int end = text.IndexOf('>', i + 2);
                if (end < 0) break;
                i = end + 1; continue;
            }
            // <?xml … ?> processing instruction
            if (i + 1 < n && text[i + 1] == '?')
            {
                int end = text.IndexOf("?>", i + 2, StringComparison.Ordinal);
                if (end < 0) { end = text.IndexOf('>', i + 2); if (end < 0) break; i = end + 1; continue; }
                i = end + 2; continue;
            }
            // </name> closing tag
            if (i + 1 < n && text[i + 1] == '/')
            {
                int j = i + 2;
                string name = ReadName(text, ref j);
                int gt = text.IndexOf('>', j);
                if (gt < 0) break;
                int close = gt + 1;
                if (name.Length > 0) CloseTag(stack, foldings, text, name, close, cmp);
                i = close; continue;
            }
            // <name …> or <name …/> opening tag
            if (i + 1 < n && char.IsLetter(text[i + 1]))
            {
                int j = i + 1;
                string name = ReadName(text, ref j);

                // Scan to the matching '>' while honoring quoted attribute values,
                // so a '>' inside  attr=">"  doesn't end the tag early.
                int k = j;
                char quote = '\0', lastNonSpace = '\0';
                while (k < n)
                {
                    char ch = text[k];
                    if (quote != '\0') { if (ch == quote) quote = '\0'; }
                    else if (ch is '"' or '\'') quote = ch;
                    else if (ch == '>') break;
                    if (!char.IsWhiteSpace(ch)) lastNonSpace = ch;
                    k++;
                }
                if (k >= n) break;

                bool selfClose = lastNonSpace == '/';
                int contentStart = k + 1;

                if (!selfClose && !(html && VoidElements.Contains(name)))
                {
                    if (html && RawTextElements.Contains(name))
                    {
                        // Skip raw body to the matching close; never parse its contents.
                        int closeIdx = IndexOfClose(text, contentStart, name);
                        if (closeIdx < 0) { i = contentStart; continue; }
                        int gt = text.IndexOf('>', closeIdx);
                        int closeEnd = gt < 0 ? closeIdx : gt + 1;
                        AddIfMultiline(foldings, text, contentStart, closeEnd, " … ");
                        i = closeEnd; continue;
                    }
                    stack.Push(new OpenTag { Name = name, ContentStart = contentStart });
                }
                i = contentStart; continue;
            }

            i++; // stray '<'
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    private static void CloseTag(Stack<OpenTag> stack, List<NewFolding> foldings,
                                 string text, string name, int closeEnd, StringComparison cmp)
    {
        // Tolerate unclosed tags: if the close matches something deeper in the
        // stack, discard the unclosed tags sitting above it, then fold the match.
        bool hasMatch = false;
        foreach (var t in stack)
            if (string.Equals(t.Name, name, cmp)) { hasMatch = true; break; }
        if (!hasMatch) return; // stray close tag — ignore

        while (stack.Count > 0)
        {
            var top = stack.Pop();
            if (string.Equals(top.Name, name, cmp))
            {
                AddIfMultiline(foldings, text, top.ContentStart, closeEnd, " … ");
                break;
            }
        }
    }

    private static int IndexOfClose(string text, int from, string name)
    {
        int p = from;
        while (true)
        {
            int lt = text.IndexOf("</", p, StringComparison.Ordinal);
            if (lt < 0) return -1;
            int q = lt + 2;
            string nm = ReadName(text, ref q);
            if (string.Equals(nm, name, StringComparison.OrdinalIgnoreCase)) return lt;
            p = lt + 2;
        }
    }

    private static string ReadName(string text, ref int i)
    {
        int start = i;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or ':' or '.') i++;
            else break;
        }
        return text.Substring(start, i - start);
    }

    private static bool Match(string text, int i, string s) =>
        i + s.Length <= text.Length && string.CompareOrdinal(text, i, s, 0, s.Length) == 0;

    private static void AddIfMultiline(List<NewFolding> foldings, string text,
                                       int start, int end, string name)
    {
        if (start < 0 || end > text.Length || start >= end) return;
        if (text.IndexOf('\n', start, end - start) < 0) return; // single line — don't fold
        foldings.Add(new NewFolding(start, end) { Name = name });
    }
}

/// <summary>
/// Brace/bracket folding for JSON (and similar C-like structures): every
/// multi-line {...} or [...] block becomes a collapsible fold. String literals
/// and escapes are respected so braces inside strings are ignored.
/// </summary>
public static class JsonFoldingStrategy
{
    public static void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateFoldings(document);
        manager.UpdateFoldings(foldings, -1);
    }

    public static List<NewFolding> CreateFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack = new Stack<int>();
        bool inString = false, escape = false;

        for (int i = 0; i < document.TextLength; i++)
        {
            char c = document.GetCharAt(i);

            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{':
                case '[': stack.Push(i); break;
                case '}':
                case ']':
                    if (stack.Count > 0)
                    {
                        int start = stack.Pop();
                        if (document.GetLineByOffset(start).LineNumber !=
                            document.GetLineByOffset(i).LineNumber)
                            foldings.Add(new NewFolding(start + 1, i) { Name = " … " });
                    }
                    break;
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }
}

/// <summary>Maps file extensions to AvalonEdit highlighting definitions and a fold kind.</summary>
public static class SyntaxMapper
{
    private static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "JavaScript", [".jsonc"] = "JavaScript", [".js"] = "JavaScript", [".ts"] = "JavaScript",
        [".md"] = "MarkDown", [".markdown"] = "MarkDown",
        // XML family
        [".xml"] = "XML", [".xaml"] = "XML", [".csproj"] = "XML", [".vbproj"] = "XML", [".fsproj"] = "XML",
        [".config"] = "XML", [".svg"] = "XML", [".plist"] = "XML", [".resx"] = "XML", [".props"] = "XML",
        [".targets"] = "XML", [".xsd"] = "XML", [".xsl"] = "XML", [".xslt"] = "XML", [".wsdl"] = "XML",
        [".rss"] = "XML", [".atom"] = "XML", [".storyboard"] = "XML", [".axml"] = "XML", [".nuspec"] = "XML",
        // HTML family
        [".html"] = "HTML", [".htm"] = "HTML", [".xhtml"] = "HTML", [".cshtml"] = "HTML", [".vue"] = "HTML",
        // other languages
        [".cs"] = "C#", [".c"] = "C++", [".h"] = "C++", [".cpp"] = "C++", [".hpp"] = "C++",
        [".java"] = "Java", [".py"] = "Python", [".php"] = "PHP", [".css"] = "CSS",
        [".ps1"] = "PowerShell", [".sql"] = "TSQL", [".vb"] = "VB", [".patch"] = "Patch",
        [".diff"] = "Patch", [".tex"] = "TeX",
    };

    public static IHighlightingDefinition? ForExtension(string ext)
    {
        if (ByName.TryGetValue(ext, out var name))
        {
            var def = HighlightingManager.Instance.GetDefinition(name);
            if (def != null) return def;
        }
        return HighlightingManager.Instance.GetDefinitionByExtension(ext);
    }

    /// <summary>Folding behavior for a file: "html", "xml", "json", or null.</summary>
    public static string? FoldKindForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".html" or ".htm" or ".xhtml" or ".cshtml" or ".vue" => "html",
        ".xml" or ".xaml" or ".csproj" or ".vbproj" or ".fsproj" or ".config" or ".svg" or ".plist"
            or ".resx" or ".props" or ".targets" or ".xsd" or ".xsl" or ".xslt" or ".wsdl"
            or ".rss" or ".atom" or ".storyboard" or ".axml" or ".nuspec" => "xml",
        ".json" or ".jsonc" or ".js" or ".ts" or ".cs" or ".cpp" or ".c" or ".h" or ".java" or ".css" => "json",
        _ => null
    };
}
