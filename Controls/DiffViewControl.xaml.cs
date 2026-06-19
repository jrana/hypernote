using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;
using HyperNote.Services;
using HyperNote;

namespace HyperNote.Controls;

public partial class DiffViewControl : UserControl
{
    private string? _leftPath;
    private string? _rightPath;
    private readonly DiffBackgroundRenderer _leftRenderer;
    private readonly DiffBackgroundRenderer _rightRenderer;
    private bool _scrollSyncInitialized;
    private readonly MainWindow _mainWin;

    public DiffViewControl(MainWindow mainWin)
    {
        _mainWin = mainWin;
        InitializeComponent();

        _leftRenderer = new DiffBackgroundRenderer(LeftEditor);
        _rightRenderer = new DiffBackgroundRenderer(RightEditor);

        LeftEditor.TextArea.TextView.BackgroundRenderers.Add(_leftRenderer);
        RightEditor.TextArea.TextView.BackgroundRenderers.Add(_rightRenderer);

        // Apply dynamic resources to editors so they match theme automatically
        LeftEditor.SetResourceReference(TextEditor.BackgroundProperty, "Editor.Background");
        LeftEditor.SetResourceReference(TextEditor.ForegroundProperty, "Editor.Foreground");
        LeftEditor.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "Editor.LineNumbers");

        RightEditor.SetResourceReference(TextEditor.BackgroundProperty, "Editor.Background");
        RightEditor.SetResourceReference(TextEditor.ForegroundProperty, "Editor.Foreground");
        RightEditor.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "Editor.LineNumbers");

        PopulateFileCombos(mainWin);

        Loaded += (_, _) => SetupScrollSync();
    }

    private void PopulateFileCombos(MainWindow mainWin)
    {
        var openFiles = new List<FileComboItem>();
        foreach (TabItem tab in mainWin.Tabs.Items)
        {
            if (tab.Tag is MainWindow.TabContext ctx && !string.IsNullOrEmpty(ctx.FilePath))
            {
                openFiles.Add(new FileComboItem { 
                    DisplayName = Path.GetFileName(ctx.FilePath), 
                    FullPath = ctx.FilePath 
                });
            }
        }

        foreach (var item in openFiles)
        {
            LeftFileCombo.Items.Add(item);
            RightFileCombo.Items.Add(item);
        }

        LeftFileCombo.SelectionChanged += (s, e) => {
            if (LeftFileCombo.SelectedItem is FileComboItem item)
            {
                _leftPath = item.FullPath;
                LeftFileCombo.ToolTip = _leftPath;
                LeftPathText.Text = _leftPath;
                LoadLeftFile();
            }
        };

        RightFileCombo.SelectionChanged += (s, e) => {
            if (RightFileCombo.SelectedItem is FileComboItem item)
            {
                _rightPath = item.FullPath;
                RightFileCombo.ToolTip = _rightPath;
                RightPathText.Text = _rightPath;
                LoadRightFile();
            }
        };
    }

    private void LeftBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog();
        if (dlg.ShowDialog() == true)
        {
            _leftPath = dlg.FileName;
            
            FileComboItem? existingItem = null;
            foreach (FileComboItem item in LeftFileCombo.Items)
            {
                if (string.Equals(item.FullPath, dlg.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    existingItem = item;
                    break;
                }
            }

            if (existingItem == null)
            {
                existingItem = new FileComboItem { 
                    DisplayName = Path.GetFileName(dlg.FileName), 
                    FullPath = dlg.FileName 
                };
                LeftFileCombo.Items.Add(existingItem);
                
                RightFileCombo.Items.Add(new FileComboItem { 
                    DisplayName = Path.GetFileName(dlg.FileName), 
                    FullPath = dlg.FileName 
                });
            }

            LeftFileCombo.SelectedItem = existingItem;
        }
    }

    private void RightBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog();
        if (dlg.ShowDialog() == true)
        {
            _rightPath = dlg.FileName;
            
            FileComboItem? existingItem = null;
            foreach (FileComboItem item in RightFileCombo.Items)
            {
                if (string.Equals(item.FullPath, dlg.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    existingItem = item;
                    break;
                }
            }

            if (existingItem == null)
            {
                existingItem = new FileComboItem { 
                    DisplayName = Path.GetFileName(dlg.FileName), 
                    FullPath = dlg.FileName 
                };
                RightFileCombo.Items.Add(existingItem);
                
                LeftFileCombo.Items.Add(new FileComboItem { 
                    DisplayName = Path.GetFileName(dlg.FileName), 
                    FullPath = dlg.FileName 
                });
            }

            RightFileCombo.SelectedItem = existingItem;
        }
    }

    private string GetFileText(string path)
    {
        foreach (TabItem tab in _mainWin.Tabs.Items)
        {
            if (tab.Tag is MainWindow.TabContext ctx && 
                string.Equals(ctx.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (ctx.Editor != null)
                {
                    return ctx.Editor.Text;
                }
            }
        }
        return File.ReadAllText(path);
    }

    private void LoadLeftFile()
    {
        if (string.IsNullOrEmpty(_leftPath) || !File.Exists(_leftPath)) return;
        try
        {
            LeftEditor.Text = GetFileText(_leftPath);
            LeftEditor.SyntaxHighlighting = SyntaxMapper.ForExtension(Path.GetExtension(_leftPath));
            _leftRenderer.Clear();
            LeftEditor.TextArea.TextView.InvalidateVisual();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading left file:\n{ex.Message}", "File Compare", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadRightFile()
    {
        if (string.IsNullOrEmpty(_rightPath) || !File.Exists(_rightPath)) return;
        try
        {
            RightEditor.Text = GetFileText(_rightPath);
            RightEditor.SyntaxHighlighting = SyntaxMapper.ForExtension(Path.GetExtension(_rightPath));
            _rightRenderer.Clear();
            RightEditor.TextArea.TextView.InvalidateVisual();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading right file:\n{ex.Message}", "File Compare", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_leftPath) || string.IsNullOrEmpty(_rightPath))
        {
            MessageBox.Show("Please select both files before comparing.", "File Compare", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            string leftText = GetFileText(_leftPath);
            string rightText = GetFileText(_rightPath);

            string[] leftLines = leftText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string[] rightLines = rightText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            var (leftDiff, rightDiff) = DiffService.ComputeDiff(
                leftLines, 
                rightLines, 
                IgnoreWhitespaceCheck.IsChecked == true, 
                IgnoreCaseCheck.IsChecked == true);

            // Populate text
            LeftEditor.Text = string.Join(Environment.NewLine, leftDiff.ConvertAll(i => i.Text));
            RightEditor.Text = string.Join(Environment.NewLine, rightDiff.ConvertAll(i => i.Text));

            // Set highlighting
            LeftEditor.SyntaxHighlighting = SyntaxMapper.ForExtension(Path.GetExtension(_leftPath));
            RightEditor.SyntaxHighlighting = SyntaxMapper.ForExtension(Path.GetExtension(_rightPath));

            // Populate line backgrounds
            _leftRenderer.Clear();
            _rightRenderer.Clear();

            for (int i = 0; i < leftDiff.Count; i++)
            {
                int lineNum = i + 1;
                if (leftDiff[i].Type == ChangeType.Deleted)
                {
                    _leftRenderer.SetLineColor(lineNum, Color.FromArgb(35, 239, 83, 80)); // Red deletion
                }
                else if (leftDiff[i].LineNumber == null)
                {
                    _leftRenderer.SetLineColor(lineNum, Color.FromArgb(20, 128, 128, 128)); // Grey placeholder
                }
            }

            for (int i = 0; i < rightDiff.Count; i++)
            {
                int lineNum = i + 1;
                if (rightDiff[i].Type == ChangeType.Inserted)
                {
                    _rightRenderer.SetLineColor(lineNum, Color.FromArgb(35, 102, 187, 106)); // Green insertion
                }
                else if (rightDiff[i].LineNumber == null)
                {
                    _rightRenderer.SetLineColor(lineNum, Color.FromArgb(20, 128, 128, 128)); // Grey placeholder
                }
            }

            LeftEditor.TextArea.TextView.InvalidateVisual();
            RightEditor.TextArea.TextView.InvalidateVisual();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during comparison: {ex.Message}", "File Compare", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetupScrollSync()
    {
        if (_scrollSyncInitialized) return;

        var leftScroll = GetScrollViewer(LeftEditor);
        var rightScroll = GetScrollViewer(RightEditor);

        if (leftScroll != null && rightScroll != null)
        {
            leftScroll.ScrollChanged += (s, e) =>
            {
                if (rightScroll.VerticalOffset != leftScroll.VerticalOffset)
                    rightScroll.ScrollToVerticalOffset(leftScroll.VerticalOffset);
                if (rightScroll.HorizontalOffset != leftScroll.HorizontalOffset)
                    rightScroll.ScrollToHorizontalOffset(leftScroll.HorizontalOffset);
            };

            rightScroll.ScrollChanged += (s, e) =>
            {
                if (leftScroll.VerticalOffset != rightScroll.VerticalOffset)
                    leftScroll.ScrollToVerticalOffset(rightScroll.VerticalOffset);
                if (leftScroll.HorizontalOffset != rightScroll.HorizontalOffset)
                    leftScroll.ScrollToHorizontalOffset(rightScroll.HorizontalOffset);
            };

            _scrollSyncInitialized = true;
        }
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

public class DiffBackgroundRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly Dictionary<int, Color> _lineColors = new();

    public DiffBackgroundRenderer(TextEditor editor)
    {
        _editor = editor;
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void SetLineColor(int lineNumber, Color color)
    {
        _lineColors[lineNumber] = color;
    }

    public void Clear()
    {
        _lineColors.Clear();
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_editor.Document == null) return;
        
        textView.EnsureVisualLines();
        foreach (var visualLine in textView.VisualLines)
        {
            int lineNum = visualLine.FirstDocumentLine.LineNumber;
            if (_lineColors.TryGetValue(lineNum, out var color))
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, visualLine.FirstDocumentLine))
                {
                    drawingContext.DrawRectangle(brush, null, new Rect(
                        0,
                        rect.Top,
                        textView.ActualWidth,
                        rect.Height
                    ));
                }
            }
        }
    }
}

public class FileComboItem
{
    public string DisplayName { get; set; } = "";
    public string FullPath { get; set; } = "";

    public override string ToString() => DisplayName;
}
