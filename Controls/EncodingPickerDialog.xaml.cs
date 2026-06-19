using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HyperNote.Controls;

public class EncodingItem
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Encoding Encoding { get; set; } = Encoding.UTF8;
}

public partial class EncodingPickerDialog : Window
{
    private readonly List<EncodingItem> _encodings = new()
    {
        new() { Name = "utf-8", DisplayName = "UTF-8 (No BOM)", Encoding = new UTF8Encoding(false) },
        new() { Name = "utf-8-bom", DisplayName = "UTF-8 with BOM", Encoding = new UTF8Encoding(true) },
        new() { Name = "utf-16", DisplayName = "UTF-16 LE (Unicode)", Encoding = new UnicodeEncoding(false, true) },
        new() { Name = "utf-16-be", DisplayName = "UTF-16 BE (Unicode Big Endian)", Encoding = new UnicodeEncoding(true, true) },
        new() { Name = "windows-1252", DisplayName = "Windows-1252 (ANSI)", Encoding = Encoding.GetEncoding(1252) },
        new() { Name = "us-ascii", DisplayName = "US-ASCII", Encoding = Encoding.ASCII }
    };

    private bool _isClosing;

    public EncodingItem? SelectedItem { get; private set; }
    public string ActionType { get; private set; } = ""; // "reopen" or "save"

    public EncodingPickerDialog()
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
        FilterEncodings("");
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_isClosing)
        {
            Close();
        }
    }

    private void FilterEncodings(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            EncodingResults.ItemsSource = _encodings;
            if (EncodingResults.Items.Count > 0) EncodingResults.SelectedIndex = 0;
            return;
        }

        query = query.Trim();
        var matches = _encodings
            .Where(e => e.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        EncodingResults.ItemsSource = matches;
        if (EncodingResults.Items.Count > 0) EncodingResults.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterEncodings(SearchBox.Text);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        int index = EncodingResults.SelectedIndex;
        int count = EncodingResults.Items.Count;

        switch (e.Key)
        {
            case Key.Down:
                if (count > 0)
                {
                    EncodingResults.SelectedIndex = (index + 1) % count;
                    EncodingResults.ScrollIntoView(EncodingResults.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (count > 0)
                {
                    EncodingResults.SelectedIndex = (index - 1 + count) % count;
                    EncodingResults.ScrollIntoView(EncodingResults.SelectedItem);
                }
                e.Handled = true;
                break;
            case Key.Enter:
                ConfirmSelection("reopen");
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void EncodingResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection("reopen");
    }

    private void ReopenBtn_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection("reopen");
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection("save");
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ConfirmSelection(string actionType)
    {
        if (EncodingResults.SelectedItem is EncodingItem item)
        {
            SelectedItem = item;
            ActionType = actionType;
            DialogResult = true;
        }
        else
        {
            Close();
        }
    }
}
