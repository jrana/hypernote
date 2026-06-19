using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace HyperNote.Services;

public class WorkspaceNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public ObservableCollection<WorkspaceNode> Children { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                if (_isExpanded && IsDirectory)
                {
                    // Defer child loading to avoid collection modification during layout/binding passes
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                    {
                        LazyLoadChildren();
                    }));
                }
            }
        }
    }

    public WorkspaceNode(string name, string fullPath, bool isDir)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDir;
        if (IsDirectory)
        {
            // Placeholder child to make the node expandable in TreeView
            Children.Add(new WorkspaceNode("Loading...", "", false));
        }
    }

    public void Refresh()
    {
        if (!IsDirectory) return;
        if (IsExpanded)
        {
            LazyLoadChildrenInternal();
        }
        else
        {
            Children.Clear();
            Children.Add(new WorkspaceNode("Loading...", "", false));
        }
    }

    private void LazyLoadChildren()
    {
        // Check if only the placeholder child is present
        if (Children.Count == 1 && Children[0].FullPath == "")
        {
            LazyLoadChildrenInternal();
        }
    }

    private void LazyLoadChildrenInternal()
    {
        Children.Clear();
        try
        {
            var dirInfo = new DirectoryInfo(FullPath);
            
            // Load folders first
            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                if (WorkspaceService.IsIgnored(dir.Name)) continue;
                Children.Add(new WorkspaceNode(dir.Name, dir.FullName, true));
            }

            // Load files
            foreach (var file in dirInfo.EnumerateFiles())
            {
                Children.Add(new WorkspaceNode(file.Name, file.FullName, false));
            }
        }
        catch (UnauthorizedAccessException)
        {
            Children.Add(new WorkspaceNode("(Access Denied)", "", false));
        }
        catch (Exception ex)
        {
            Children.Add(new WorkspaceNode($"Error: {ex.Message}", "", false));
        }
    }
}

public static class WorkspaceService
{
    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs", ".idea"
    };

    public static bool IsIgnored(string name)
    {
        return IgnoredNames.Contains(name);
    }

    public static ObservableCollection<WorkspaceNode> LoadWorkspace(string rootPath)
    {
        var nodes = new ObservableCollection<WorkspaceNode>();
        try
        {
            var dirInfo = new DirectoryInfo(rootPath);

            // Folders
            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                if (IsIgnored(dir.Name)) continue;
                nodes.Add(new WorkspaceNode(dir.Name, dir.FullName, true));
            }

            // Files
            foreach (var file in dirInfo.EnumerateFiles())
            {
                nodes.Add(new WorkspaceNode(file.Name, file.FullName, false));
            }
        }
        catch { }
        return nodes;
    }
}
