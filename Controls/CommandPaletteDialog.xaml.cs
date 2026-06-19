using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HyperNote.Controls;

public class CommandItem
{
    public string Name { get; set; } = "";
    public string Shortcut { get; set; } = "";
    public string Id { get; set; } = "";
}

public partial class CommandPaletteDialog : Window
{
    private readonly List<CommandItem> _commands = new()
    {
        // File
        new() { Name = "File: New Tab", Shortcut = "Ctrl+N", Id = "File.New" },
        new() { Name = "File: New Window", Shortcut = "Ctrl+Shift+N", Id = "File.NewWindow" },
        new() { Name = "File: Open File...", Shortcut = "Ctrl+O", Id = "File.Open" },
        new() { Name = "File: Open Folder...", Id = "File.OpenFolder" },
        new() { Name = "File: Save", Shortcut = "Ctrl+S", Id = "File.Save" },
        new() { Name = "File: Save As...", Shortcut = "Ctrl+Shift+S", Id = "File.SaveAs" },
        new() { Name = "File: Close Tab", Shortcut = "Ctrl+W", Id = "File.CloseTab" },
        new() { Name = "File: Exit", Id = "File.Exit" },
        
        // Edit
        new() { Name = "Edit: Undo", Shortcut = "Ctrl+Z", Id = "Edit.Undo" },
        new() { Name = "Edit: Redo", Shortcut = "Ctrl+Y", Id = "Edit.Redo" },
        new() { Name = "Edit: Cut", Shortcut = "Ctrl+X", Id = "Edit.Cut" },
        new() { Name = "Edit: Copy", Shortcut = "Ctrl+C", Id = "Edit.Copy" },
        new() { Name = "Edit: Paste", Shortcut = "Ctrl+V", Id = "Edit.Paste" },
        new() { Name = "Edit: Delete", Shortcut = "Del", Id = "Edit.Delete" },
        new() { Name = "Edit: Select All", Shortcut = "Ctrl+A", Id = "Edit.SelectAll" },
        new() { Name = "Edit: Insert Date/Time", Shortcut = "F5", Id = "Edit.TimeDate" },
        new() { Name = "Edit: Find...", Shortcut = "Ctrl+F", Id = "Edit.Find" },
        new() { Name = "Edit: Replace...", Shortcut = "Ctrl+H", Id = "Edit.Replace" },
        new() { Name = "Edit: Find in Files...", Shortcut = "Ctrl+Shift+F", Id = "Edit.FindInFiles" },
        new() { Name = "Edit: Go to Line...", Shortcut = "Ctrl+G", Id = "Edit.GoToLine" },
        new() { Name = "Edit: Go to Symbol...", Shortcut = "Ctrl+Shift+O", Id = "Edit.GoToSymbol" },
        new() { Name = "Edit: Format Document", Shortcut = "Ctrl+Shift+I", Id = "Edit.FormatDocument" },
        
        // View
        new() { Name = "View: Toggle Dark Theme", Id = "View.ToggleDarkTheme" },
        new() { Name = "View: Toggle Sidebar", Shortcut = "Ctrl+B", Id = "View.ToggleSidebar" },
        new() { Name = "View: Toggle Terminal", Shortcut = "Ctrl+`", Id = "View.ToggleTerminal" },
        new() { Name = "View: Toggle Preview", Shortcut = "Ctrl+Shift+V", Id = "View.TogglePreview" },
        new() { Name = "View: Zoom In", Shortcut = "Ctrl+Plus", Id = "View.ZoomIn" },
        new() { Name = "View: Zoom Out", Shortcut = "Ctrl+Minus", Id = "View.ZoomOut" },
        new() { Name = "View: Reset Zoom", Shortcut = "Ctrl+0", Id = "View.ResetZoom" },
        new() { Name = "View: Toggle Word Wrap", Id = "View.ToggleWordWrap" },
        new() { Name = "View: Toggle Line Numbers", Id = "View.ToggleLineNumbers" },
        new() { Name = "View: Toggle Scroll Minimap", Id = "View.ToggleMinimap" },
        new() { Name = "View: Toggle Whitespace Characters", Id = "View.ToggleWhitespace" },
        new() { Name = "View: Toggle Highlight Current Line", Id = "View.ToggleHighlightLine" },
        new() { Name = "View: Toggle Status Bar", Id = "View.ToggleStatusBar" },
        
        // Tools
        new() { Name = "Tools: Settings...", Shortcut = "Ctrl+,", Id = "Tools.Settings" },
        new() { Name = "Tools: Compare Files...", Id = "Tools.CompareFiles" },
        
        // Help
        new() { Name = "Help: View Help", Id = "Help.ViewHelp" },
        new() { Name = "Help: About HyperNote", Id = "Help.About" },
        
        // Transform Case
        new() { Name = "Transform: UPPERCASE", Id = "Transform.Uppercase" },
        new() { Name = "Transform: lowercase", Id = "Transform.Lowercase" },
        new() { Name = "Transform: Title Case", Id = "Transform.TitleCase" },
        new() { Name = "Transform: Sentence Case", Id = "Transform.SentenceCase" },
        new() { Name = "Transform: camelCase", Id = "Transform.CamelCase" },
        new() { Name = "Transform: snake_case", Id = "Transform.SnakeCase" },
        
        // Transform Lines
        new() { Name = "Transform: Sort Lines Ascending", Id = "Transform.SortAsc" },
        new() { Name = "Transform: Sort Lines Descending", Id = "Transform.SortDesc" },
        new() { Name = "Transform: Remove Duplicate Lines", Id = "Transform.RemoveDuplicates" },
        new() { Name = "Transform: Trim Whitespace", Id = "Transform.TrimBoth" },
        
        // Transform Encode/Decode
        new() { Name = "Transform: URL Encode", Id = "Transform.UrlEncode" },
        new() { Name = "Transform: URL Decode", Id = "Transform.UrlDecode" },
        new() { Name = "Transform: Base64 Encode", Id = "Transform.Base64Encode" },
        new() { Name = "Transform: Base64 Decode", Id = "Transform.Base64Decode" },
        new() { Name = "Transform: HTML Encode", Id = "Transform.HtmlEncode" },
        new() { Name = "Transform: HTML Decode", Id = "Transform.HtmlDecode" },
        new() { Name = "Transform: Minify JSON/XML", Id = "Transform.MinifyJsonXml" }
    };

    private bool _isClosing;

    public CommandItem? SelectedCommand { get; private set; }

    public CommandPaletteDialog()
    {
        InitializeComponent();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        FilterCommands("");
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_isClosing)
        {
            Close();
        }
    }

    private void FilterCommands(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            CommandResults.ItemsSource = _commands;
            if (CommandResults.Items.Count > 0) CommandResults.SelectedIndex = 0;
            return;
        }

        query = query.Trim();
        var matches = _commands
            .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c =>
            {
                bool starts = c.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase);
                int colonIdx = c.Name.IndexOf(':');
                bool opStarts = colonIdx >= 0 && c.Name.Substring(colonIdx + 1).Trim().StartsWith(query, StringComparison.OrdinalIgnoreCase);
                
                if (starts) return 0;
                if (opStarts) return 1;
                return 2;
            })
            .ThenBy(c => c.Name)
            .ToList();

        CommandResults.ItemsSource = matches;
        if (CommandResults.Items.Count > 0) CommandResults.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterCommands(SearchBox.Text);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int index = CommandResults.SelectedIndex;
        int count = CommandResults.Items.Count;

        switch (e.Key)
        {
            case Key.Down:
                if (count > 0)
                {
                    CommandResults.SelectedIndex = (index + 1) % count;
                    CommandResults.ScrollIntoView(CommandResults.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (count > 0)
                {
                    CommandResults.SelectedIndex = (index - 1 + count) % count;
                    CommandResults.ScrollIntoView(CommandResults.SelectedItem);
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

    private void CommandResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (CommandResults.SelectedItem is CommandItem command)
        {
            SelectedCommand = command;
            DialogResult = true;
        }
        else
        {
            Close();
        }
    }
}
