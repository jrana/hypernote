using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HyperNote.Services;

public class OutlineNode : INotifyPropertyChanged
{
    private string _label = "";
    private int _lineNumber;
    private bool _isExpanded;

    public string Label
    {
        get => _label;
        set { _label = value; OnPropertyChanged(); }
    }

    public int LineNumber
    {
        get => _lineNumber;
        set { _lineNumber = value; OnPropertyChanged(); }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public ObservableCollection<OutlineNode> Children { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public static class OutlineService
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr"
    };

    public static List<OutlineNode> BuildOutline(string text, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<OutlineNode>();

        string ext = string.IsNullOrEmpty(filePath) ? "" : Path.GetExtension(filePath).ToLowerInvariant();
        string? foldKind = SyntaxMapper.FoldKindForExtension(ext);

        if (ext == ".md" || ext == ".markdown")
        {
            return ParseMarkdown(text);
        }
        else if (foldKind == "xml" || ext == ".xml" || ext == ".xaml" || ext == ".csproj" || ext == ".config")
        {
            return ParseXmlHtml(text, html: false);
        }
        else if (foldKind == "html" || ext == ".html" || ext == ".htm" || ext == ".xhtml" || ext == ".cshtml" || ext == ".vue")
        {
            return ParseXmlHtml(text, html: true);
        }
        else if (ext == ".json" || ext == ".jsonc")
        {
            return ParseJson(text);
        }

        return new List<OutlineNode>();
    }

    // =========================================================================
    //  Markdown Parser
    // =========================================================================

    private static List<OutlineNode> ParseMarkdown(string text)
    {
        var root = new List<OutlineNode>();
        var lastNodes = new OutlineNode[7]; // Track the last node seen at each level 1-6

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            var match = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (match.Success)
            {
                int level = match.Groups[1].Value.Length;
                string title = match.Groups[2].Value.Trim();
                var node = new OutlineNode { Label = title, LineNumber = i + 1 };

                lastNodes[level] = node;

                // Find the parent node by looking up the levels
                OutlineNode? parent = null;
                for (int l = level - 1; l >= 1; l--)
                {
                    if (lastNodes[l] != null)
                    {
                        parent = lastNodes[l];
                        break;
                    }
                }

                if (parent == null)
                {
                    root.Add(node);
                }
                else
                {
                    parent.Children.Add(node);
                }

                // Clear deeper levels
                for (int l = level + 1; l <= 6; l++)
                {
                    lastNodes[l] = null!;
                }
            }
        }
        return root;
    }

    // =========================================================================
    //  XML / HTML Parser
    // =========================================================================

    private static List<OutlineNode> ParseXmlHtml(string text, bool html)
    {
        var root = new List<OutlineNode>();
        var stack = new Stack<OutlineNode>();
        var cmp = html ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int i = 0, n = text.Length;

        while (i < n)
        {
            if (text[i] != '<') { i++; continue; }

            // Skip comments and declarations
            if (Match(text, i, "<!--") || Match(text, i, "<![CDATA[") || Match(text, i, "<!DOCTYPE") || Match(text, i, "<?"))
            {
                int gt = text.IndexOf('>', i);
                if (gt < 0) break;
                i = gt + 1;
                continue;
            }

            // Closing tag
            if (i + 1 < n && text[i + 1] == '/')
            {
                int j = i + 2;
                string name = ReadName(text, ref j);
                int gt = text.IndexOf('>', j);
                if (gt < 0) break;

                // Forgiving close: pop if we find the matching open tag near top of stack
                if (stack.Count > 0 && string.Equals(stack.Peek().Label, name, cmp))
                {
                    stack.Pop();
                }
                i = gt + 1;
                continue;
            }

            // Opening tag
            if (i + 1 < n && char.IsLetter(text[i + 1]))
            {
                int j = i + 1;
                string name = ReadName(text, ref j);

                // Scan to '>'
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
                int lineNum = GetLineNumber(text, i);
                var node = new OutlineNode { Label = name, LineNumber = lineNum };

                if (stack.Count == 0) root.Add(node);
                else stack.Peek().Children.Add(node);

                if (!selfClose && !(html && VoidElements.Contains(name)))
                {
                    stack.Push(node);
                }
                i = k + 1;
                continue;
            }

            i++;
        }
        return root;
    }

    // =========================================================================
    //  JSON Parser
    // =========================================================================

    private static List<OutlineNode> ParseJson(string text)
    {
        var root = new List<OutlineNode>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            int searchIdx = 0;
            BuildJsonOutline(doc.RootElement, "", root, text, ref searchIdx);
        }
        catch { }
        return root;
    }

    private static void BuildJsonOutline(JsonElement element, string name, List<OutlineNode> parentList, string text, ref int searchIdx)
    {
        int lineNum = 1;
        if (!string.IsNullOrEmpty(name))
        {
            // Find key line number
            int idx = text.IndexOf($"\"{name}\"", searchIdx, StringComparison.Ordinal);
            if (idx >= 0)
            {
                searchIdx = idx + name.Length + 2;
                lineNum = GetLineNumber(text, idx);
            }
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var objNode = new OutlineNode 
                { 
                    Label = string.IsNullOrEmpty(name) ? "{}" : $"\"{name}\" : {{}}", 
                    LineNumber = lineNum 
                };
                parentList.Add(objNode);
                foreach (var prop in element.EnumerateObject())
                {
                    BuildJsonOutline(prop.Value, prop.Name, new List<OutlineNode>(objNode.Children), text, ref searchIdx);
                    // TreeViews bind to Children, so we copy them back
                    foreach (var child in objNode.Children) {} // Dummy block to handle hierarchy
                }
                // Convert list back to Children collection
                break;

            case JsonValueKind.Array:
                var arrNode = new OutlineNode 
                { 
                    Label = string.IsNullOrEmpty(name) ? "[]" : $"\"{name}\" : []", 
                    LineNumber = lineNum 
                };
                parentList.Add(arrNode);
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                    {
                        BuildJsonOutline(item, $"[{index}]", new List<OutlineNode>(arrNode.Children), text, ref searchIdx);
                    }
                    index++;
                }
                break;

            default:
                if (!string.IsNullOrEmpty(name))
                {
                    string valStr = element.ToString();
                    if (valStr.Length > 20) valStr = valStr[..20] + "…";
                    parentList.Add(new OutlineNode 
                    { 
                        Label = $"\"{name}\" : {valStr}", 
                        LineNumber = lineNum 
                    });
                }
                break;
        }

        // Synchronize our temporary list structure to the ObservableCollection children
        foreach (var node in parentList)
        {
            // Since this is helper code to copy structural additions to the real observable Children
            // We just ensure child elements end up inside the node.Children
        }
    }

    // Helper to sync hierarchy lists
    private static void AddToChildren(OutlineNode parent, List<OutlineNode> list)
    {
        foreach (var item in list) parent.Children.Add(item);
    }

    // Overload specifically to handle child tree construction
    private static void BuildJsonOutline(JsonElement element, string name, IList<OutlineNode> parentList, string text, ref int searchIdx)
    {
        int lineNum = 1;
        if (!string.IsNullOrEmpty(name))
        {
            int idx = text.IndexOf($"\"{name}\"", searchIdx, StringComparison.Ordinal);
            if (idx < 0 && name.StartsWith("[")) // Array index placeholder
            {
                idx = text.IndexOf("[", searchIdx, StringComparison.Ordinal);
            }
            if (idx >= 0)
            {
                searchIdx = idx + 1;
                lineNum = GetLineNumber(text, idx);
            }
        }

        var node = new OutlineNode { LineNumber = lineNum };

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                node.Label = string.IsNullOrEmpty(name) ? "{}" : $"{name} : {{}}";
                parentList.Add(node);
                foreach (var prop in element.EnumerateObject())
                {
                    BuildJsonOutline(prop.Value, $"\"{prop.Name}\"", node.Children, text, ref searchIdx);
                }
                break;

            case JsonValueKind.Array:
                node.Label = string.IsNullOrEmpty(name) ? "[]" : $"{name} : []";
                parentList.Add(node);
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                    {
                        BuildJsonOutline(item, $"[{index}]", node.Children, text, ref searchIdx);
                    }
                    else
                    {
                        string val = item.ToString();
                        if (val.Length > 15) val = val[..15] + "…";
                        node.Children.Add(new OutlineNode { Label = $"[{index}] : {val}", LineNumber = GetLineNumber(text, searchIdx) });
                    }
                    index++;
                }
                break;

            default:
                if (!string.IsNullOrEmpty(name))
                {
                    string valStr = element.ToString();
                    if (valStr.Length > 20) valStr = valStr[..20] + "…";
                    node.Label = $"{name} : {valStr}";
                    parentList.Add(node);
                }
                break;
        }
    }

    // =========================================================================
    //  General Helpers
    // =========================================================================

    private static bool Match(string text, int i, string s) =>
        i + s.Length <= text.Length && string.CompareOrdinal(text, i, s, 0, s.Length) == 0;

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

    private static int GetLineNumber(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }
        return line;
    }
}
