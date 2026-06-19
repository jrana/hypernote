using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace HyperNote.Controls;

public class MinimapControl : FrameworkElement
{
    private readonly TextEditor _editor;
    private ScrollViewer? _scrollViewer;
    private List<LineMeta> _lineCache = new();
    private bool _scrollEventsHooked;

    private struct LineMeta
    {
        public int Indent;
        public int Length;
    }

    public MinimapControl(TextEditor editor)
    {
        _editor = editor;
        Width = 100;
        Cursor = Cursors.Hand;

        _editor.TextChanged += Editor_TextChanged;
        _editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        _editor.SizeChanged += Editor_SizeChanged;

        Loaded += MinimapControl_Loaded;
        Unloaded += MinimapControl_Unloaded;

        UpdateCache();
    }

    private void MinimapControl_Loaded(object sender, RoutedEventArgs e)
    {
        HookScrollEvents();
    }

    private void MinimapControl_Unloaded(object sender, RoutedEventArgs e)
    {
        UnhookScrollEvents();
        _editor.TextChanged -= Editor_TextChanged;
        _editor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        _editor.SizeChanged -= Editor_SizeChanged;
    }

    private void HookScrollEvents()
    {
        if (_scrollEventsHooked) return;
        _scrollViewer = GetScrollViewer(_editor);
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            _scrollEventsHooked = true;
        }
    }

    private void UnhookScrollEvents()
    {
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            _scrollEventsHooked = false;
        }
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        UpdateCache();
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private void Editor_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        HookScrollEvents();
        InvalidateVisual();
    }

    private void UpdateCache()
    {
        string text = _editor.Text;
        var cache = new List<LineMeta>();
        int len = text.Length;
        int lineStart = 0;
        int indent = 0;
        bool countingIndent = true;

        for (int i = 0; i < len; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                int lineLen = i - lineStart;
                if (lineLen > 0 && text[i - 1] == '\r') lineLen--; // CRLF
                cache.Add(new LineMeta { Indent = indent, Length = Math.Max(0, lineLen - indent) });

                lineStart = i + 1;
                indent = 0;
                countingIndent = true;
            }
            else if (c == '\r')
            {
                if (i + 1 < len && text[i + 1] != '\n') // Standalone CR
                {
                    int lineLen = i - lineStart;
                    cache.Add(new LineMeta { Indent = indent, Length = Math.Max(0, lineLen - indent) });
                    lineStart = i + 1;
                    indent = 0;
                    countingIndent = true;
                }
            }
            else
            {
                if (countingIndent)
                {
                    if (c == ' ' || c == '\t')
                        indent++;
                    else
                        countingIndent = false;
                }
            }
        }

        int finalLen = len - lineStart;
        cache.Add(new LineMeta { Indent = indent, Length = Math.Max(0, finalLen - indent) });

        _lineCache = cache;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        CaptureMouse();
        ScrollToMouse(e.GetPosition(this).Y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
        {
            ScrollToMouse(e.GetPosition(this).Y);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        ReleaseMouseCapture();
    }

    private void ScrollToMouse(double y)
    {
        if (_scrollViewer == null || ActualHeight == 0) return;

        double ratio = y / ActualHeight;
        double targetOffset = ratio * _scrollViewer.ExtentHeight - (_scrollViewer.ViewportHeight / 2);
        _scrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0, _scrollViewer.ScrollableHeight));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // 1. Draw subtle background
        var bgBrush = (Brush)Application.Current.FindResource("App.ChromeBackground");
        dc.DrawRectangle(bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        int lineCount = _lineCache.Count;
        if (lineCount == 0 || ActualHeight == 0) return;

        double scaleY = ActualHeight / lineCount;
        double scaleX = 1.0; // horizontal scale: 1px per char

        // 2. Draw lines representation
        var foreBrush = (Brush)Application.Current.FindResource("Editor.Foreground");
        Color textCol = Colors.Gray;
        if (foreBrush is SolidColorBrush scb) textCol = scb.Color;
        
        var textPenBrush = new SolidColorBrush(Color.FromArgb(55, textCol.R, textCol.G, textCol.B));
        textPenBrush.Freeze();

        double lineHeight = Math.Max(1.0, scaleY - 0.5);

        for (int i = 0; i < lineCount; i++)
        {
            var line = _lineCache[i];
            if (line.Length == 0) continue;

            double y = i * scaleY;
            double x = line.Indent * scaleX;
            double w = line.Length * scaleX;

            dc.DrawRectangle(textPenBrush, null, new Rect(x, y, w, lineHeight));
        }

        // 3. Draw viewport overlay
        if (_scrollViewer != null && _scrollViewer.ExtentHeight > 0)
        {
            double vOffset = _scrollViewer.VerticalOffset;
            double vHeight = _scrollViewer.ViewportHeight;
            double extHeight = _scrollViewer.ExtentHeight;

            double overlayTop = (vOffset / extHeight) * ActualHeight;
            double overlayHeight = (vHeight / extHeight) * ActualHeight;

            var overlayBrush = new SolidColorBrush(Color.FromArgb(22, 128, 128, 128));
            overlayBrush.Freeze();
            var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), 1.0);
            borderPen.Freeze();

            dc.DrawRectangle(overlayBrush, borderPen, new Rect(0, overlayTop, ActualWidth, overlayHeight));
        }

        // 4. Draw caret position indicator
        int caretLine = _editor.TextArea.Caret.Line;
        double caretY = (caretLine - 1) * scaleY;
        var accentBrush = (Brush)Application.Current.FindResource("App.Accent");

        dc.DrawRectangle(accentBrush, null, new Rect(0, caretY, ActualWidth, 1.5));
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
