using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using HyperNote.Services;

namespace HyperNote.Controls;

public class BookmarkMargin : AbstractMargin
{
    private readonly string _filePath;

    public BookmarkMargin(string filePath)
    {
        _filePath = filePath;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(16, 0); // 16px wide margin
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView != null)
        {
            oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null)
        {
            newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
        }
    }

    private void TextView_VisualLinesChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid) return;

        // Draw background
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        var bookmarks = BookmarkService.Instance.GetBookmarks(_filePath);
        if (bookmarks.Count == 0) return;

        var brush = new SolidColorBrush(Color.FromRgb(78, 161, 243)); // AppAccent blue
        brush.Freeze();

        foreach (var line in textView.VisualLines)
        {
            int lineNum = line.FirstDocumentLine.LineNumber;
            if (bookmarks.Contains(lineNum))
            {
                double y = line.VisualTop - textView.VerticalOffset;
                double height = line.Height;
                
                // Draw a beautiful blue diamond in the margin
                var center = new Point(RenderSize.Width / 2, y + height / 2);
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(center.X, center.Y - 5), true, true);
                    ctx.LineTo(new Point(center.X + 5, center.Y), true, false);
                    ctx.LineTo(new Point(center.X, center.Y + 5), true, false);
                    ctx.LineTo(new Point(center.X - 5, center.Y), true, false);
                }
                geometry.Freeze();
                drawingContext.DrawGeometry(brush, null, geometry);
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var textView = TextView;
        if (textView == null) return;

        var pos = e.GetPosition(textView);
        var visualLine = textView.GetVisualLineFromVisualTop(pos.Y + textView.VerticalOffset);
        if (visualLine != null)
        {
            int lineNum = visualLine.FirstDocumentLine.LineNumber;
            BookmarkService.Instance.ToggleBookmark(_filePath, lineNum);
            InvalidateVisual();
            e.Handled = true;
        }
    }
}
