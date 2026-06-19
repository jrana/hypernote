using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HyperNote.Services;

namespace HyperNote.Controls;

public class FlatSymbolItem
{
    public string Label { get; set; } = "";
    public int LineNumber { get; set; }
    public int Level { get; set; }

    public string DisplayName => new string(' ', Level * 2) + Label;
    public string LineHint => $"line {LineNumber}";
}

public partial class GoToSymbolDialog : Window
{
    private readonly List<FlatSymbolItem> _allSymbols = new();
    
    private bool _isClosing;

    public int SelectedLineNumber { get; private set; } = -1;

    public GoToSymbolDialog(List<OutlineNode> outlineRoot)
    {
        InitializeComponent();
        FlattenOutline(outlineRoot, 0);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        FilterSymbols("");
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_isClosing)
        {
            Close();
        }
    }

    private void FlattenOutline(List<OutlineNode> nodes, int level)
    {
        if (nodes == null) return;
        foreach (var node in nodes)
        {
            _allSymbols.Add(new FlatSymbolItem
            {
                Label = node.Label,
                LineNumber = node.LineNumber,
                Level = level
            });
            FlattenOutline(node.Children.ToList(), level + 1);
        }
    }

    private void FilterSymbols(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            SymbolResults.ItemsSource = _allSymbols;
            if (SymbolResults.Items.Count > 0) SymbolResults.SelectedIndex = 0;
            return;
        }

        query = query.Trim();
        var matches = _allSymbols
            .Where(s => s.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s =>
            {
                bool starts = s.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase);
                if (starts) return 0;
                return 1;
            })
            .ThenBy(s => s.LineNumber)
            .ToList();

        SymbolResults.ItemsSource = matches;
        if (SymbolResults.Items.Count > 0) SymbolResults.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterSymbols(SearchBox.Text);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int index = SymbolResults.SelectedIndex;
        int count = SymbolResults.Items.Count;

        switch (e.Key)
        {
            case Key.Down:
                if (count > 0)
                {
                    SymbolResults.SelectedIndex = (index + 1) % count;
                    SymbolResults.ScrollIntoView(SymbolResults.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (count > 0)
                {
                    SymbolResults.SelectedIndex = (index - 1 + count) % count;
                    SymbolResults.ScrollIntoView(SymbolResults.SelectedItem);
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

    private void SymbolResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (SymbolResults.SelectedItem is FlatSymbolItem symbol)
        {
            SelectedLineNumber = symbol.LineNumber;
            DialogResult = true;
        }
        else
        {
            Close();
        }
    }
}
