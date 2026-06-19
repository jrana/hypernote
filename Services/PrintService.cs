using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;

namespace HyperNote.Services;

public static class PrintService
{
    /// <summary>
    /// Converts an AvalonEdit TextEditor document to a WPF FlowDocument suitable for printing.
    /// Syntax highlighting colors are extracted per line via the editor's IHighlighter.
    /// </summary>
    public static FlowDocument ConvertTextEditorToFlowDocument(TextEditor editor, bool includeLineNumbers = true)
    {
        var pageWidth = 8.5 * 96;
        var pageHeight = 11 * 96;
        var margin = 48;

        var doc = new FlowDocument
        {
            FontFamily = editor.FontFamily,
            FontSize = editor.FontSize,
            PageHeight = pageHeight,
            PageWidth = pageWidth,
            PagePadding = new Thickness(margin),
            ColumnWidth = pageWidth - (margin * 2),
            Background = Brushes.White
        };

        var paragraph = new Paragraph { TextAlignment = TextAlignment.Left };
        var document = editor.Document;
        var lines = document.Lines;
        var highlighter = editor.TextArea.GetService(typeof(IHighlighter)) as IHighlighter;
        var defaultBrush = GetDefaultForeground(editor);

        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());

            var line = lines[i];
            string lineText = document.GetText(line).TrimEnd('\r', '\n');

            if (includeLineNumbers)
            {
                paragraph.Inlines.Add(new Run($"{i + 1}  ")
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                    FontFamily = editor.FontFamily,
                    FontSize = editor.FontSize
                });
            }

            if (lineText.Length == 0)
                continue;

            // Resolve a foreground brush for every character on the line, then emit
            // grouped runs so consecutive same-colored characters share one Run.
            var charBrushes = ResolveLineColors(highlighter, line, lineText, defaultBrush);
            EmitGroupedRuns(paragraph, lineText, charBrushes, editor);
        }

        doc.Blocks.Add(paragraph);
        return doc;
    }

    /// <summary>
    /// Builds a per-character brush array for a line using the syntax highlighter.
    /// Falls back to <paramref name="defaultBrush"/> for any character without a token color.
    /// </summary>
    private static Brush[] ResolveLineColors(IHighlighter? highlighter, DocumentLine line,
        string lineText, Brush defaultBrush)
    {
        var brushes = new Brush[lineText.Length];
        for (int k = 0; k < brushes.Length; k++)
            brushes[k] = defaultBrush;

        if (highlighter == null)
            return brushes;

        HighlightedLine highlighted;
        try
        {
            highlighted = highlighter.HighlightLine(line.LineNumber);
        }
        catch
        {
            return brushes;
        }

        int lineStart = line.Offset;
        foreach (var section in highlighted.Sections)
        {
            var brush = BrushFromHighlightingColor(section.Color);
            if (brush == null)
                continue;

            int start = section.Offset - lineStart;
            int end = start + section.Length;
            for (int k = System.Math.Max(0, start); k < System.Math.Min(lineText.Length, end); k++)
                brushes[k] = brush;
        }

        return brushes;
    }

    /// <summary>Emits the line as runs, merging adjacent characters that share a brush.</summary>
    private static void EmitGroupedRuns(Paragraph paragraph, string lineText, Brush[] charBrushes,
        TextEditor editor)
    {
        int idx = 0;
        while (idx < lineText.Length)
        {
            int j = idx + 1;
            while (j < lineText.Length && BrushesEqual(charBrushes[j], charBrushes[idx]))
                j++;

            paragraph.Inlines.Add(new Run(lineText.Substring(idx, j - idx))
            {
                Foreground = charBrushes[idx],
                FontFamily = editor.FontFamily,
                FontSize = editor.FontSize
            });
            idx = j;
        }
    }

    /// <summary>Extracts a solid WPF brush from a HighlightingColor's foreground, or null.</summary>
    private static Brush? BrushFromHighlightingColor(HighlightingColor? color)
    {
        var foreground = color?.Foreground;
        if (foreground == null)
            return null;

        // SimpleHighlightingBrush (produced by .xshd definitions) ignores the context,
        // so passing null is safe and yields the actual token color.
        var wpfColor = foreground.GetColor(null);
        if (wpfColor.HasValue)
            return new SolidColorBrush(wpfColor.Value);

        if (foreground.GetBrush(null) is SolidColorBrush scb)
            return new SolidColorBrush(scb.Color);

        return null;
    }

    private static bool BrushesEqual(Brush a, Brush b)
    {
        if (a is SolidColorBrush ca && b is SolidColorBrush cb)
            return ca.Color == cb.Color;
        return ReferenceEquals(a, b);
    }

    private static Brush GetDefaultForeground(TextEditor editor)
    {
        if (editor.Foreground is SolidColorBrush solidBrush)
            return new SolidColorBrush(solidBrush.Color);
        return Brushes.Black;
    }

    /// <summary>
    /// Opens the print dialog directly for a FlowDocument.
    /// </summary>
    public static void PrintFlowDocument(FlowDocument document, string title)
    {
        var printDialog = new System.Windows.Controls.PrintDialog();
        if (printDialog.ShowDialog() != true)
            return;

        printDialog.PrintDocument(
            ((IDocumentPaginatorSource)document).DocumentPaginator,
            title);
    }

    /// <summary>
    /// Converts HTML string to a FlowDocument for printing.
    /// </summary>
    public static FlowDocument ConvertHtmlToFlowDocument(string html)
    {
        var pageWidth = 8.5 * 96;
        var pageHeight = 11 * 96;
        var margin = 48;

        var doc = new FlowDocument
        {
            PageHeight = pageHeight,
            PageWidth = pageWidth,
            PagePadding = new Thickness(margin),
            ColumnWidth = pageWidth - (margin * 2),
            Background = Brushes.White
        };

        var paragraph = new Paragraph(new Run(html))
        {
            Foreground = Brushes.Black
        };

        doc.Blocks.Add(paragraph);
        return doc;
    }
}
