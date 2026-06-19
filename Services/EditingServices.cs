using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace HyperNote.Services;

/// <summary>
/// Provides editing services including bracket/quote auto-closing
/// and document formatting for JSON and XML files.
/// </summary>
public static class EditingServices
{
    // =========================================================================
    //  Bracket & Quote Auto-Closing
    // =========================================================================

    public sealed class BracketAutoCloser
    {
        private readonly TextEditor _editor;

        public BracketAutoCloser(TextEditor editor)
        {
            _editor = editor;
            _editor.TextArea.TextEntering += TextArea_TextEntering;
            _editor.TextArea.TextEntered += TextArea_TextEntered;
            _editor.PreviewKeyDown += TextArea_PreviewKeyDown;
        }

        private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (!SettingsService.Instance.Settings.EnableAutoBraceCompletion) return;
            if (string.IsNullOrEmpty(e.Text) || _editor.Document == null) return;
            char c = e.Text[0];

            // 1. If text is selected and user types open bracket/quote, wrap selection.
            if (c == '(' || c == '[' || c == '{' || c == '"' || c == '\'')
            {
                if (_editor.SelectionLength > 0)
                {
                    WrapSelection(c);
                    e.Handled = true;
                    return;
                }
            }

            // 2. If user types a closing char and it matches the character immediately
            // after the caret, skip over it instead of inserting a duplicate.
            if (c == ')' || c == ']' || c == '}' || c == '"' || c == '\'')
            {
                int offset = _editor.CaretOffset;
                if (offset < _editor.Document.TextLength && _editor.Document.GetCharAt(offset) == c)
                {
                    _editor.CaretOffset = offset + 1;
                    e.Handled = true;
                }
            }
        }

        private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (!SettingsService.Instance.Settings.EnableAutoBraceCompletion) return;
            if (string.IsNullOrEmpty(e.Text) || _editor.Document == null) return;
            char c = e.Text[0];
            char closing = GetClosingChar(c);

            if (closing != '\0')
            {
                // Heuristic for quotes: only auto-close if character after cursor is whitespace,
                // punctuation/brackets, or end-of-document. This avoids closing inside words.
                if (c == '"' || c == '\'')
                {
                    int offset = _editor.CaretOffset;
                    if (offset < _editor.Document.TextLength)
                    {
                        char next = _editor.Document.GetCharAt(offset);
                        if (!char.IsWhiteSpace(next) && next != ')' && next != ']' && next != '}' && next != ',' && next != ';')
                        {
                            return; // Skip auto-closing
                        }
                    }
                }

                int caret = _editor.CaretOffset;
                _editor.Document.Insert(caret, closing.ToString());
                _editor.CaretOffset = caret; // Keep cursor inside matching pair
            }
        }

        private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!SettingsService.Instance.Settings.EnableAutoBraceCompletion) return;
            // Backspace deletes both matching symbols if cursor is between them
            if (e.Key == Key.Back && _editor.SelectionLength == 0)
            {
                int caret = _editor.CaretOffset;
                if (caret > 0 && caret < _editor.Document.TextLength)
                {
                    char prev = _editor.Document.GetCharAt(caret - 1);
                    char next = _editor.Document.GetCharAt(caret);
                    if (IsMatchingPair(prev, next))
                    {
                        _editor.Document.Remove(caret - 1, 2);
                        e.Handled = true;
                    }
                }
            }
        }

        private void WrapSelection(char c)
        {
            char close = GetClosingChar(c);
            if (close == '\0') return;

            int start = _editor.SelectionStart;
            int len = _editor.SelectionLength;
            string text = _editor.SelectedText;

            _editor.Document.BeginUpdate();
            try
            {
                _editor.Document.Replace(start, len, c + text + close);
                _editor.Select(start + 1, len);
            }
            finally
            {
                _editor.Document.EndUpdate();
            }
        }

        private static char GetClosingChar(char c) => c switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '"' => '"',
            '\'' => '\'',
            _ => '\0'
        };

        private static bool IsMatchingPair(char open, char close) =>
            (open == '(' && close == ')') ||
            (open == '[' && close == ']') ||
            (open == '{' && close == '}') ||
            (open == '"' && close == '"') ||
            (open == '\'' && close == '\'');
    }

    public static void InstallAutoCloser(TextEditor editor)
    {
        _ = new BracketAutoCloser(editor);
    }

    // =========================================================================
    //  Document Formatting
    // =========================================================================

    public static bool Format(TextEditor editor, string? filePath)
    {
        if (editor == null || editor.Document == null) return false;

        string text = editor.Text;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string ext = string.IsNullOrEmpty(filePath) ? "" : Path.GetExtension(filePath).ToLowerInvariant();
        string? foldKind = SyntaxMapper.FoldKindForExtension(ext);

        // Determine format strategy
        if (ext == ".json" || ext == ".jsonc" || (string.IsNullOrEmpty(ext) && text.TrimStart().StartsWith("{")))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    doc.WriteTo(writer);
                }
                editor.Text = Encoding.UTF8.GetString(ms.ToArray());
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Malformed JSON:\n{ex.Message}", ex);
            }
        }
        else if (foldKind == "xml" || ext == ".xml" || ext == ".xaml" || ext == ".csproj" || ext == ".config" || (string.IsNullOrEmpty(ext) && text.TrimStart().StartsWith("<")))
        {
            try
            {
                var doc = XDocument.Parse(text);
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = new string(' ', editor.Options.IndentationSize),
                    NewLineOnAttributes = false,
                    OmitXmlDeclaration = !text.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                };
                var sb = new StringBuilder();
                using (var writer = XmlWriter.Create(sb, settings))
                {
                    doc.WriteTo(writer);
                }
                editor.Text = sb.ToString();
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Malformed XML:\n{ex.Message}", ex);
            }
        }

        throw new NotSupportedException("Formatting is only supported for JSON and XML/XAML files.");
    }
}
