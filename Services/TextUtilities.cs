using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml;

namespace HyperNote.Services;

public static class TextUtilities
{
    // === Case Transformations ===
    public static string ToUppercase(string text) => text.ToUpper();
    public static string ToLowercase(string text) => text.ToLower();
    
    public static string ToTitleCase(string text)
    {
        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower());
    }

    public static string ToSentenceCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        // Capitalize the first letter of each sentence
        var rx = new Regex(@"(^\s*|[.!?]\s+)(\p{L})", RegexOptions.Multiline);
        return rx.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value.ToUpper());
    }

    public static string ToCamelCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var words = text.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return text;
        
        var sb = new StringBuilder();
        sb.Append(words[0].ToLower());
        for (int i = 1; i < words.Length; i++)
        {
            string word = words[i];
            if (word.Length > 0)
            {
                sb.Append(char.ToUpper(word[0]));
                sb.Append(word.Substring(1).ToLower());
            }
        }
        return sb.ToString();
    }

    public static string ToSnakeCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var intermediate = Regex.Replace(text, @"(\p{Ll})(\p{Lu})", "$1_$2");
        var words = intermediate.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("_", words.Select(w => w.ToLower()));
    }

    // === Line-based Operations ===
    public static string SortLines(string text, bool desc, bool ignoreCase)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var sorted = desc ? lines.OrderByDescending(l => l, comparer) : lines.OrderBy(l => l, comparer);
        string separator = text.Contains("\r\n") ? "\r\n" : "\n";
        return string.Join(separator, sorted);
    }

    public static string RemoveDuplicateLines(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var unique = lines.Distinct(StringComparer.Ordinal);
        string separator = text.Contains("\r\n") ? "\r\n" : "\n";
        return string.Join(separator, unique);
    }

    // === Trim Operations ===
    public static string TrimLeading(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string separator = text.Contains("\r\n") ? "\r\n" : "\n";
        return string.Join(separator, lines.Select(l => l.TrimStart()));
    }

    public static string TrimTrailing(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string separator = text.Contains("\r\n") ? "\r\n" : "\n";
        return string.Join(separator, lines.Select(l => l.TrimEnd()));
    }

    public static string TrimBoth(string text)
    {
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        string separator = text.Contains("\r\n") ? "\r\n" : "\n";
        return string.Join(separator, lines.Select(l => l.Trim()));
    }

    // === Encodings ===
    public static string UrlEncode(string text) => System.Net.WebUtility.UrlEncode(text);
    public static string UrlDecode(string text) => System.Net.WebUtility.UrlDecode(text);

    public static string Base64Encode(string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }

    public static string Base64Decode(string text)
    {
        byte[] bytes = Convert.FromBase64String(text.Trim());
        return Encoding.UTF8.GetString(bytes);
    }

    public static string HtmlEncode(string text) => System.Net.WebUtility.HtmlEncode(text);
    public static string HtmlDecode(string text) => System.Net.WebUtility.HtmlDecode(text);

    // === Minifiers ===
    public static string MinifyJson(string text)
    {
        using var doc = JsonDocument.Parse(text);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            doc.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static string MinifyXml(string text)
    {
        var doc = XDocument.Parse(text);
        var settings = new XmlWriterSettings
        {
            Indent = false,
            NewLineOnAttributes = false,
            OmitXmlDeclaration = !text.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
        };
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            doc.WriteTo(writer);
        }
        return sb.ToString();
    }

    // === Stats Helper ===
    public static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        int count = 0;
        bool inWord = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }

    public static async System.Threading.Tasks.Task<int> CountWordsAsync(
        ICSharpCode.AvalonEdit.Document.ITextSource snapshot,
        System.Threading.CancellationToken token)
    {
        return await System.Threading.Tasks.Task.Run(() =>
        {
            int count = 0;
            bool inWord = false;
            int len = snapshot.TextLength;
            int offset = 0;

            while (offset < len)
            {
                token.ThrowIfCancellationRequested();
                int chunk = Math.Min(4096, len - offset);
                string text = snapshot.GetText(offset, chunk);
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                    {
                        inWord = false;
                    }
                    else if (!inWord)
                    {
                        inWord = true;
                        count++;
                    }
                }
                offset += chunk;
            }
            return count;
        }, token);
    }
}

