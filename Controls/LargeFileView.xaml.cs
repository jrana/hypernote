using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Documents;
using System.Globalization;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using HyperNote.Services;
using TextSearch = HyperNote.Services.TextSearch;

namespace HyperNote.Controls;

public class OffsetLineNumberMargin : AbstractMargin
{
    private readonly TextEditor _editor;
    private readonly Func<int> _startLineOffsetProvider;

    public OffsetLineNumberMargin(TextEditor editor, Func<int> startLineOffsetProvider)
    {
        _editor = editor;
        _startLineOffsetProvider = startLineOffsetProvider;
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView != null)
        {
            oldTextView.VisualLinesChanged -= TextViewVisualLinesChanged;
            oldTextView.ScrollOffsetChanged -= TextViewScrollOffsetChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null)
        {
            newTextView.VisualLinesChanged += TextViewVisualLinesChanged;
            newTextView.ScrollOffsetChanged += TextViewScrollOffsetChanged;
        }
        InvalidateMeasure();
    }

    private void TextViewVisualLinesChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
    }

    private void TextViewScrollOffsetChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (TextView == null) return new Size(0, 0);
        int maxLine = _startLineOffsetProvider() + TextView.Document.LineCount;
        string maxLineStr = maxLine.ToString(CultureInfo.InvariantCulture);
        
        var formattedText = new FormattedText(
            maxLineStr,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(_editor.FontFamily, _editor.FontStyle, _editor.FontWeight, _editor.FontStretch),
            _editor.FontSize,
            (Brush)Application.Current.FindResource("Editor.LineNumbers"),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return new Size(formattedText.Width + 15, 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid) return;

        var foreground = (Brush)Application.Current.FindResource("Editor.LineNumbers");
        var typeface = new Typeface(_editor.FontFamily, _editor.FontStyle, _editor.FontWeight, _editor.FontStretch);
        double fontSize = _editor.FontSize;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int offset = _startLineOffsetProvider();

        foreach (var visualLine in textView.VisualLines)
        {
            int lineNumber = visualLine.FirstDocumentLine.LineNumber + offset;
            string text = lineNumber.ToString(CultureInfo.InvariantCulture);
            
            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                foreground,
                dpi);

            double y = visualLine.VisualTop - textView.VerticalOffset;
            double x = RenderSize.Width - 10 - formattedText.Width;
            drawingContext.DrawText(formattedText, new Point(x, y));
        }
    }
}

public partial class LargeFileView : UserControl, IDisposable
{
    private const int MaxWindowSize = 10000;
    private const int ShiftThreshold = 1000;

    private LargeFileLineProvider? _provider;
    private int _windowStartLine = 0; // 0-based
    private int _windowLineCount = 0;
    private bool _isShifting = false;
    private bool _isUpdatingScrollbar = false;
    private bool _isDirty = false;

    public string FilePath { get; private set; } = "";

    public bool IsDirty => _isDirty;
    public event EventHandler? DocumentChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ZoomChanged;
    public event EventHandler? FileCleared;

    public int LineCount => _provider?.Count ?? 0;
    public int CurrentLine => _windowStartLine + Editor.TextArea.Caret.Line;

    public int SelectedIndex
    {
        get => CurrentLine - 1;
        set => GoToLine(value + 1);
    }

    public double VerticalScrollOffset
    {
        get => _windowStartLine;
        set => LoadWindow((int)value);
    }

    private bool _isInitializingEncoding;

    public double ViewFontSize
    {
        get => Editor.FontSize;
        set
        {
            Editor.FontSize = value;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public LargeFileView()
    {
        InitializeComponent();
        
        Editor.PreviewMouseWheel += Editor_PreviewMouseWheel;
        Editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        Editor.TextArea.SelectionChanged += (s, e) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        Editor.TextChanged += Editor_TextChanged;
        this.Loaded += LargeFileView_Loaded;
    }

    private void LargeFileView_Loaded(object sender, RoutedEventArgs e)
    {
        var sv = GetScrollViewer(Editor);
        if (sv != null)
        {
            sv.ScrollChanged += EditorScrollViewer_ScrollChanged;
        }
    }

    private void EditorScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_provider == null || _isShifting || _isUpdatingScrollbar) return;

        var sv = (ScrollViewer)sender;
        double lineHeight = Editor.TextArea.TextView.DefaultLineHeight;
        if (lineHeight <= 0) lineHeight = 13.0;
        double localScrollLine = sv.VerticalOffset / lineHeight;

        _isUpdatingScrollbar = true;
        try
        {
            VScrollBar.Value = _windowStartLine + localScrollLine;
        }
        finally
        {
            _isUpdatingScrollbar = false;
        }

        int visibleTopLine = _windowStartLine + (int)Math.Floor(localScrollLine);
        int visibleBottomLine = visibleTopLine + (int)Math.Ceiling(sv.ViewportHeight / lineHeight);

        int totalLines = _provider.Count;
        
        bool nearTop = (visibleTopLine - _windowStartLine < ShiftThreshold) && (_windowStartLine > 0);
        bool nearBottom = (_windowStartLine + _windowLineCount - visibleBottomLine < ShiftThreshold) && (_windowStartLine + _windowLineCount < totalLines);

        if (nearTop || nearBottom)
        {
            int newStart = Math.Max(0, visibleTopLine - MaxWindowSize / 2);
            newStart = Math.Clamp(newStart, 0, Math.Max(0, totalLines - MaxWindowSize));

            if (newStart != _windowStartLine)
            {
                ShiftWindow(newStart, visibleTopLine);
            }
        }
    }

    private void ShiftWindow(int newStart, int targetVisibleTopLine)
    {
        if (_provider == null || _isShifting) return;

        int totalLines = _provider.Count;
        SaveCurrentWindowEdits();

        _isShifting = true;
        try
        {
            int countToLoad = Math.Min(MaxWindowSize, totalLines - newStart);
            var linesToLoad = new List<string>(countToLoad);
            for (int i = 0; i < countToLoad; i++)
            {
                linesToLoad.Add(_provider.GetLineText(newStart + i));
            }

            int caretLine = Editor.TextArea.Caret.Line;
            int caretCol = Editor.TextArea.Caret.Column;
            int globalCaretLine = _windowStartLine + (caretLine - 1);

            Editor.Text = string.Join(Environment.NewLine, linesToLoad);
            _windowStartLine = newStart;
            _windowLineCount = countToLoad;

            int newLocalCaretLine = (globalCaretLine - newStart) + 1;
            Editor.TextArea.Caret.Line = Math.Clamp(newLocalCaretLine, 1, countToLoad);
            Editor.TextArea.Caret.Column = caretCol;

            var sv = GetScrollViewer(Editor);
            if (sv != null)
            {
                double lineHeight = Editor.TextArea.TextView.DefaultLineHeight;
                if (lineHeight <= 0) lineHeight = 13.0;
                double localOffset = (targetVisibleTopLine - newStart) * lineHeight;
                sv.ScrollToVerticalOffset(localOffset);
            }

            UpdateVScrollBar();
        }
        finally
        {
            _isShifting = false;
        }
    }

    public TextEditor TextEditorControl => Editor;

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_isShifting) return;
        if (!_isDirty)
        {
            _isDirty = true;
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            double step = 1.0;
            double nextSize = e.Delta > 0 ? Editor.FontSize + step : Editor.FontSize - step;
            double newSize = Math.Clamp(nextSize, 6.0, 60.0);
            if (Math.Abs(Editor.FontSize - newSize) > 0.01)
            {
                Editor.FontSize = newSize;
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveCurrentWindowEdits()
    {
        if (_provider == null || _windowLineCount <= 0) return;

        string text = Editor.Text;
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        
        _provider.ReplaceRange(_windowStartLine, _windowLineCount, lines);
    }

    private void LoadWindow(int startLine, bool forceReload = false)
    {
        if (_provider == null || _isShifting) return;

        int totalLines = _provider.Count;
        if (totalLines == 0)
        {
            _isShifting = true;
            try
            {
                Editor.Text = string.Empty;
                _windowStartLine = 0;
                _windowLineCount = 0;
                UpdateVScrollBar();
            }
            finally
            {
                _isShifting = false;
            }
            return;
        }

        startLine = Math.Clamp(startLine, 0, Math.Max(0, totalLines - MaxWindowSize));

        if (!forceReload && startLine == _windowStartLine && _windowLineCount > 0)
        {
            return;
        }

        SaveCurrentWindowEdits();

        _isShifting = true;
        try
        {
            int countToLoad = Math.Min(MaxWindowSize, totalLines - startLine);
            var linesToLoad = new List<string>(countToLoad);
            for (int i = 0; i < countToLoad; i++)
            {
                linesToLoad.Add(_provider.GetLineText(startLine + i));
            }

            int caretLine = Editor.TextArea.Caret.Line;
            int caretCol = Editor.TextArea.Caret.Column;

            Editor.Text = string.Join(Environment.NewLine, linesToLoad);
            _windowStartLine = startLine;
            _windowLineCount = countToLoad;

            int newCaretLine = Math.Clamp(caretLine, 1, Math.Max(1, countToLoad));
            Editor.TextArea.Caret.Line = newCaretLine;
            Editor.TextArea.Caret.Column = caretCol;

            UpdateVScrollBar();
        }
        finally
        {
            _isShifting = false;
        }
    }

    public void ApplySettings()
    {
        var s = SettingsService.Instance.Settings;
        var family = new FontFamily(s.EditorFontFamily);
        Editor.FontFamily = family;
        Editor.FontSize = s.EditorFontSize;
        Editor.WordWrap = s.WordWrap;
        Editor.Options.ConvertTabsToSpaces = s.ConvertTabsToSpaces;
        Editor.Options.IndentationSize = s.IndentWidth;
        Editor.Options.ShowSpaces = s.ShowWhitespace;
        Editor.Options.ShowTabs = s.ShowWhitespace;
        Editor.Options.ShowEndOfLine = s.ShowWhitespace;
        Editor.Options.HighlightCurrentLine = s.HighlightCurrentLine;

        // Custom Offset Line Margin
        Editor.ShowLineNumbers = false;
        var oldMargin = Editor.TextArea.LeftMargins.OfType<OffsetLineNumberMargin>().FirstOrDefault();
        if (s.ShowLineNumbers)
        {
            if (oldMargin == null)
            {
                var margin = new OffsetLineNumberMargin(Editor, () => _windowStartLine);
                Editor.TextArea.LeftMargins.Insert(0, margin);
            }
        }
        else
        {
            if (oldMargin != null)
            {
                Editor.TextArea.LeftMargins.Remove(oldMargin);
            }
        }

        // Apply theme colors
        Editor.Background = (Brush)Application.Current.FindResource("Editor.Background");
        Editor.Foreground = (Brush)Application.Current.FindResource("Editor.Foreground");
        Editor.LineNumbersForeground = (Brush)Application.Current.FindResource("Editor.LineNumbers");
    }

    public void Open(string path, System.Text.Encoding? encoding = null)
    {
        FilePath = path;
        encoding ??= DetectEncoding(path);

        string ext = Path.GetExtension(path).ToLowerInvariant();
        Editor.SyntaxHighlighting = SyntaxMapper.ForExtension(ext);

        _isInitializingEncoding = true;
        try
        {
            string tag = encoding.WebName switch
            {
                "utf-16" => "utf-16le",
                "utf-16BE" => "utf-16be",
                "windows-1252" => "ansi",
                _ => "utf-8"
            };

            foreach (ComboBoxItem item in EncodingBox.Items)
            {
                if (item.Tag?.ToString() == tag)
                {
                    EncodingBox.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _isInitializingEncoding = false;
        }

        ReloadWithEncoding(encoding);
    }

    private static Encoding DetectEncoding(string path)
    {
        try
        {
            using var stream = FileService.OpenSharedRead(path);
            byte[] bom = new byte[4];
            int read = stream.Read(bom, 0, 4);
            if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                return new UnicodeEncoding(false, true);
            if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
                return new UnicodeEncoding(true, true);
            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return new UTF8Encoding(true);
        }
        catch { }
        return new UTF8Encoding(false);
    }

    private void EncodingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingEncoding || string.IsNullOrEmpty(FilePath)) return;
        if (EncodingBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            Encoding encoding = tag switch
            {
                "utf-16le" => new UnicodeEncoding(false, true),
                "utf-16be" => new UnicodeEncoding(true, true),
                "ansi" => Encoding.GetEncoding(1252),
                _ => new UTF8Encoding(false)
            };
            ReloadWithEncoding(encoding);
        }
    }

    private void ReloadWithEncoding(Encoding encoding)
    {
        _provider?.Dispose();
        _provider = new LargeFileLineProvider(FilePath, encoding, Dispatcher);
        _provider.IndexProgress += OnIndexProgress;

        _windowStartLine = 0;
        _windowLineCount = 0;
        _isDirty = false;

        ApplySettings();

        // Delay slightly to allow the indexer to run initially
        Dispatcher.BeginInvoke(new Action(() =>
        {
            LoadWindow(0, forceReload: true);
        }), DispatcherPriority.Background);
    }

    private void OnIndexProgress(long lines, long bytes, bool done)
    {
        string size = FormatBytes(_provider?.FileLength ?? 0);
        BannerText.Text = done
            ? $"Large file mode (editing) — {Path.GetFileName(FilePath)} • {size} • {lines:N0} lines"
            : $"Large file mode (editing) — indexing… {lines:N0} lines ({FormatBytes(bytes)} of {size} scanned)";
        
        UpdateVScrollBar();
    }

    private void GotoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _provider == null) return;
        if (!int.TryParse(GotoBox.Text.Trim().Replace(",", ""), out int line)) return;
        GoToLine(line);
    }

    public void GoToLine(int line)
    {
        if (_provider == null) return;
        int totalLines = _provider.Count;
        line = Math.Clamp(line, 1, Math.Max(1, totalLines));

        int newStart = Math.Max(0, (line - 1) - MaxWindowSize / 2);
        newStart = Math.Clamp(newStart, 0, Math.Max(0, totalLines - MaxWindowSize));

        LoadWindow(newStart);

        int localLine = (line - 1) - newStart + 1;
        Editor.TextArea.Caret.Line = Math.Clamp(localLine, 1, _windowLineCount);
        Editor.ScrollToLine(localLine);
        Editor.Focus();
    }

    public async Task<bool> FindNextAsync(string term, SearchOptions options)
    {
        if (_provider == null) return false;

        int startLine = CurrentLine - 1;
        int searchStartLine = startLine;

        if (!options.Backward)
            searchStartLine = startLine + 1;
        else
            searchStartLine = startLine - 1;

        var result = await _provider.FindNextAsync(term, searchStartLine, options);
        if (result is { } r)
        {
            int newStart = Math.Max(0, r.LineIndex - MaxWindowSize / 2);
            newStart = Math.Clamp(newStart, 0, Math.Max(0, _provider.Count - MaxWindowSize));

            LoadWindow(newStart);

            int localLine = r.LineIndex - newStart + 1;
            Editor.TextArea.Caret.Line = localLine;

            var lineObj = Editor.Document.GetLineByNumber(localLine);
            int startOffset = lineObj.Offset + r.CharIndex;

            Editor.Select(startOffset, r.Length);
            Editor.ScrollToLine(localLine);
            Editor.Focus();

            return true;
        }
        return false;
    }

    public async Task<int> CountMatchesAsync(string term, bool matchCase, bool wholeWord, bool useRegex = false)
    {
        if (_provider == null) return 0;
        return await _provider.CountMatchesAsync(term, matchCase, wholeWord, useRegex);
    }

    public void ReplaceAll(string term, string replacement, SearchOptions options)
    {
        if (_provider == null) return;

        var regex = TextSearch.BuildRegex(term, options.MatchCase, options.WholeWord, options.UseRegex);
        int totalLines = _provider.Count;
        int replacedCount = 0;

        SaveCurrentWindowEdits();

        for (int i = 0; i < totalLines; i++)
        {
            string lineText = _provider.GetLineText(i);
            if (regex.IsMatch(lineText))
            {
                string newLineText = regex.Replace(lineText, replacement);
                _provider.ReplaceRange(i, 1, new List<string> { newLineText });
                replacedCount++;
            }
        }

        if (replacedCount > 0)
        {
            LoadWindow(_windowStartLine, forceReload: true);
            _isDirty = true;
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            MessageBox.Show(Window.GetWindow(this), $"Replaced {replacedCount} occurrence(s).", "HyperNote", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Window.GetWindow(this), "No occurrences found.", "HyperNote", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void Save(string path)
    {
        if (_provider == null) return;

        SaveCurrentWindowEdits();
        _provider.Save(path);

        _isDirty = false;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void VScrollBar_Scroll(object sender, ScrollEventArgs e)
    {
        if (_provider == null || _isShifting || _isUpdatingScrollbar) return;

        double targetTopLine = VScrollBar.Value;
        int viewportLines = GetViewportLineCount(Editor);

        bool withinWindow = (targetTopLine >= _windowStartLine) && 
                            (targetTopLine + viewportLines <= _windowStartLine + _windowLineCount);

        if (withinWindow)
        {
            var sv = GetScrollViewer(Editor);
            if (sv != null)
            {
                double lineHeight = Editor.TextArea.TextView.DefaultLineHeight;
                if (lineHeight <= 0) lineHeight = 13.0;
                _isUpdatingScrollbar = true;
                try
                {
                    sv.ScrollToVerticalOffset((targetTopLine - _windowStartLine) * lineHeight);
                }
                finally
                {
                    _isUpdatingScrollbar = false;
                }
            }
        }
        else
        {
            LoadWindow((int)targetTopLine);
            var sv = GetScrollViewer(Editor);
            if (sv != null)
            {
                double lineHeight = Editor.TextArea.TextView.DefaultLineHeight;
                if (lineHeight <= 0) lineHeight = 13.0;
                sv.ScrollToVerticalOffset((targetTopLine - _windowStartLine) * lineHeight);
            }
        }
    }

    private void VScrollBar_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVScrollBar();
    }

    private void UpdateVScrollBar()
    {
        if (_provider == null) return;
        _isUpdatingScrollbar = true;
        try
        {
            int totalLines = _provider.Count;
            int viewportLines = GetViewportLineCount(Editor);

            VScrollBar.Minimum = 0;
            VScrollBar.Maximum = Math.Max(0, totalLines - viewportLines);
            VScrollBar.ViewportSize = viewportLines;
            VScrollBar.Value = _windowStartLine;
        }
        finally
        {
            _isUpdatingScrollbar = false;
        }
    }

    private int GetViewportLineCount(TextEditor editor)
    {
        var sv = GetScrollViewer(editor);
        if (sv != null && sv.ViewportHeight > 0)
        {
            double lineHeight = editor.TextArea.TextView.DefaultLineHeight;
            return (int)Math.Max(1, Math.Ceiling(sv.ViewportHeight / lineHeight));
        }
        return 40;
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject d)
    {
        if (d is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private static string FormatBytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{b / (double)(1L << 10):0.#} KB",
        _ => $"{b} B"
    };

    public void Dispose() => _provider?.Dispose();

    private void ClearAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_provider == null || string.IsNullOrEmpty(FilePath)) return;

        var result = MessageBox.Show(Window.GetWindow(this),
            "Are you sure you want to clear all text in this file?\nThis action will immediately overwrite the file on disk.",
            "HyperNote", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var encoding = _provider.Encoding;
                lock (_provider.SyncRoot)
                {
                    _provider.Dispose();
                    File.WriteAllBytes(FilePath, Array.Empty<byte>());
                }
                
                FileCleared?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), $"Failed to clear file:\n{ex.Message}",
                    "HyperNote", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

public static class SearchHighlightBehavior
{
    public static readonly DependencyProperty HighlightableTextProperty =
        DependencyProperty.RegisterAttached(
            "HighlightableText",
            typeof(string),
            typeof(SearchHighlightBehavior),
            new PropertyMetadata(string.Empty, UpdateHighlights));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.RegisterAttached(
            "SearchText",
            typeof(string),
            typeof(SearchHighlightBehavior),
            new PropertyMetadata(string.Empty, UpdateHighlights));

    public static readonly DependencyProperty MatchCaseProperty =
        DependencyProperty.RegisterAttached(
            "MatchCase",
            typeof(bool),
            typeof(SearchHighlightBehavior),
            new PropertyMetadata(false, UpdateHighlights));

    public static readonly DependencyProperty WholeWordProperty =
        DependencyProperty.RegisterAttached(
            "WholeWord",
            typeof(bool),
            typeof(SearchHighlightBehavior),
            new PropertyMetadata(false, UpdateHighlights));

    public static readonly DependencyProperty UseRegexProperty =
        DependencyProperty.RegisterAttached(
            "UseRegex",
            typeof(bool),
            typeof(SearchHighlightBehavior),
            new PropertyMetadata(false, UpdateHighlights));

    public static string GetHighlightableText(DependencyObject obj) => (string)obj.GetValue(HighlightableTextProperty);
    public static void SetHighlightableText(DependencyObject obj, string value) => obj.SetValue(HighlightableTextProperty, value);

    public static string GetSearchText(DependencyObject obj) => (string)obj.GetValue(SearchTextProperty);
    public static void SetSearchText(DependencyObject obj, string value) => obj.SetValue(SearchTextProperty, value);

    public static bool GetMatchCase(DependencyObject obj) => (bool)obj.GetValue(MatchCaseProperty);
    public static void SetMatchCase(DependencyObject obj, bool value) => obj.SetValue(MatchCaseProperty, value);

    public static bool GetWholeWord(DependencyObject obj) => (bool)obj.GetValue(WholeWordProperty);
    public static void SetWholeWord(DependencyObject obj, bool value) => obj.SetValue(WholeWordProperty, value);

    public static bool GetUseRegex(DependencyObject obj) => (bool)obj.GetValue(UseRegexProperty);
    public static void SetUseRegex(DependencyObject obj, bool value) => obj.SetValue(UseRegexProperty, value);

    private static void UpdateHighlights(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;

        string text = GetHighlightableText(textBlock) ?? string.Empty;
        string search = GetSearchText(textBlock) ?? string.Empty;

        textBlock.Inlines.Clear();

        if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(text))
        {
            textBlock.Text = text;
            return;
        }

        bool matchCase = GetMatchCase(textBlock);
        bool wholeWord = GetWholeWord(textBlock);
        bool useRegex = GetUseRegex(textBlock);

        try
        {
            var regex = TextSearch.BuildRegex(search, matchCase, wholeWord, useRegex);
            var matches = regex.Matches(text);

            if (matches.Count == 0)
            {
                textBlock.Text = text;
                return;
            }

            int lastIndex = 0;
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Length == 0) continue;

                if (match.Index > lastIndex)
                {
                    textBlock.Inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
                }

                var run = new Run(match.Value)
                {
                    Background = new SolidColorBrush(Color.FromRgb(255, 255, 0)),
                    Foreground = Brushes.Black
                };
                textBlock.Inlines.Add(run);

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                textBlock.Inlines.Add(new Run(text.Substring(lastIndex)));
            }
        }
        catch
        {
            textBlock.Text = text;
        }
    }
}

