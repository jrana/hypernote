using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HyperNote.Services;

namespace HyperNote.Controls;

public class FileSearchResult
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string FullPath { get; set; } = "";
}

public partial class FuzzySwitcherDialog : Window
{
    private readonly string _workspaceRoot;
    private readonly List<FileSearchResult> _allFiles = new();
    private bool _isClosing;
    
    public string? SelectedFilePath { get; private set; }

    public FuzzySwitcherDialog(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
        InitializeComponent();
        LoadFiles();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        FilterFiles("");
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_isClosing)
        {
            Close();
        }
    }

    private void LoadFiles()
    {
        if (string.IsNullOrEmpty(_workspaceRoot) || !Directory.Exists(_workspaceRoot)) return;
        try
        {
            var rootDir = new DirectoryInfo(_workspaceRoot);
            EnumerateFilesRecursive(rootDir);
        }
        catch { }
    }

    private void EnumerateFilesRecursive(DirectoryInfo dir)
    {
        if (WorkspaceService.IsIgnored(dir.Name)) return;

        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                string rel = Path.GetRelativePath(_workspaceRoot, file.FullName);
                _allFiles.Add(new FileSearchResult
                {
                    Name = file.Name,
                    RelativePath = rel,
                    FullPath = file.FullName
                });
            }

            foreach (var sub in dir.EnumerateDirectories())
            {
                EnumerateFilesRecursive(sub);
            }
        }
        catch { /* skip restricted folders */ }
    }

    private void FilterFiles(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            FileResults.ItemsSource = _allFiles.Take(20).ToList();
            if (FileResults.Items.Count > 0) FileResults.SelectedIndex = 0;
            return;
        }

        query = query.Trim();
        var matches = _allFiles
            .Where(f => f.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f =>
            {
                bool nameMatch = f.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
                bool nameStart = f.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase);
                if (nameStart) return 0;
                if (nameMatch) return 1;
                return 2;
            })
            .ThenBy(f => f.Name)
            .Take(30)
            .ToList();

        FileResults.ItemsSource = matches;
        if (FileResults.Items.Count > 0) FileResults.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterFiles(SearchBox.Text);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int index = FileResults.SelectedIndex;
        int count = FileResults.Items.Count;

        switch (e.Key)
        {
            case Key.Down:
                if (count > 0)
                {
                    FileResults.SelectedIndex = (index + 1) % count;
                    FileResults.ScrollIntoView(FileResults.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (count > 0)
                {
                    FileResults.SelectedIndex = (index - 1 + count) % count;
                    FileResults.ScrollIntoView(FileResults.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Enter:
                ConfirmSelection();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void FileResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (FileResults.SelectedItem is FileSearchResult result)
        {
            SelectedFilePath = result.FullPath;
            DialogResult = true;
        }
        else
        {
            Close();
        }
    }
}
