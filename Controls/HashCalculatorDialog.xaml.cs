using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace HyperNote.Controls;

public partial class HashCalculatorDialog : Window
{
    private readonly string? _activeFilePath;
    private readonly string? _editorText;
    private readonly string? _selectedText;

    private string _rawMd5 = "";
    private string _rawSha1 = "";
    private string _rawSha256 = "";
    private string _rawSha512 = "";

    private string? _chosenExternalFilePath;

    public HashCalculatorDialog(string? activeFilePath, string? editorText, string? selectedText)
    {
        InitializeComponent();

        _activeFilePath = activeFilePath;
        _editorText = editorText;
        _selectedText = selectedText;

        InitializeSources();
    }

    private void InitializeSources()
    {
        // Disable "Selected Text" option if nothing is selected
        if (string.IsNullOrEmpty(_selectedText))
        {
            SelectedTextComboItem.IsEnabled = false;
        }

        // Default selection: Active Tab if available, otherwise External File
        if (_editorText != null)
        {
            SourceCombo.SelectedIndex = 0; // Active Tab
        }
        else
        {
            SourceCombo.SelectedIndex = 2; // External File
        }
    }

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceCombo == null) return;

        var selectedItem = (ComboBoxItem)SourceCombo.SelectedItem;
        string tag = selectedItem.Tag.ToString()!;

        // Show/hide browse section
        if (tag == "ExternalFile")
        {
            BrowseGrid.Visibility = Visibility.Visible;
            if (string.IsNullOrEmpty(_chosenExternalFilePath))
            {
                SetBoxesText("");
            }
            else
            {
                TriggerHashCalculation();
            }
        }
        else
        {
            BrowseGrid.Visibility = Visibility.Collapsed;
            TriggerHashCalculation();
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select File for Hash Calculation",
            Filter = "All Files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog(this) == true)
        {
            _chosenExternalFilePath = openFileDialog.FileName;
            FilePathBox.Text = _chosenExternalFilePath;
            TriggerHashCalculation();
        }
    }

    private void TriggerHashCalculation()
    {
        var selectedItem = (ComboBoxItem)SourceCombo.SelectedItem;
        string tag = selectedItem.Tag.ToString()!;

        Func<Stream>? streamProvider = null;

        if (tag == "TabEntire")
        {
            if (_editorText != null)
            {
                streamProvider = () => new MemoryStream(Encoding.UTF8.GetBytes(_editorText));
            }
            else if (!string.IsNullOrEmpty(_activeFilePath) && File.Exists(_activeFilePath))
            {
                // Fallback for hex/binary views of a file
                string path = _activeFilePath;
                streamProvider = () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
        }
        else if (tag == "TabSelection" && !string.IsNullOrEmpty(_selectedText))
        {
            streamProvider = () => new MemoryStream(Encoding.UTF8.GetBytes(_selectedText));
        }
        else if (tag == "ExternalFile" && !string.IsNullOrEmpty(_chosenExternalFilePath) && File.Exists(_chosenExternalFilePath))
        {
            string path = _chosenExternalFilePath;
            streamProvider = () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        if (streamProvider != null)
        {
            CalculateHashesFromStream(streamProvider);
        }
        else
        {
            SetBoxesText("");
        }
    }

    private async void CalculateHashesFromStream(Func<Stream> streamProvider)
    {
        SetBoxesText("Calculating...");
        
        try
        {
            string md5 = "";
            string sha1 = "";
            string sha256 = "";
            string sha512 = "";

            await Task.Run(() =>
            {
                // MD5
                using (var stream = streamProvider())
                using (var alg = MD5.Create())
                {
                    md5 = ToHex(alg.ComputeHash(stream));
                }
                // SHA-1
                using (var stream = streamProvider())
                using (var alg = SHA1.Create())
                {
                    sha1 = ToHex(alg.ComputeHash(stream));
                }
                // SHA-256
                using (var stream = streamProvider())
                using (var alg = SHA256.Create())
                {
                    sha256 = ToHex(alg.ComputeHash(stream));
                }
                // SHA-512
                using (var stream = streamProvider())
                using (var alg = SHA512.Create())
                {
                    sha512 = ToHex(alg.ComputeHash(stream));
                }
            });

            _rawMd5 = md5;
            _rawSha1 = sha1;
            _rawSha256 = sha256;
            _rawSha512 = sha512;

            UpdateDisplayHashes();
        }
        catch (Exception ex)
        {
            SetBoxesText($"Error: {ex.Message}");
        }
    }

    private void SetBoxesText(string text)
    {
        Md5Box.Text = text;
        Sha1Box.Text = text;
        Sha256Box.Text = text;
        Sha512Box.Text = text;
    }

    private string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private void UpdateDisplayHashes()
    {
        bool uppercase = UppercaseCheck.IsChecked == true;
        
        Md5Box.Text = uppercase ? _rawMd5.ToUpperInvariant() : _rawMd5.ToLowerInvariant();
        Sha1Box.Text = uppercase ? _rawSha1.ToUpperInvariant() : _rawSha1.ToLowerInvariant();
        Sha256Box.Text = uppercase ? _rawSha256.ToUpperInvariant() : _rawSha256.ToLowerInvariant();
        Sha512Box.Text = uppercase ? _rawSha512.ToUpperInvariant() : _rawSha512.ToLowerInvariant();
    }

    private void CaseCheck_Changed(object sender, RoutedEventArgs e)
    {
        UpdateDisplayHashes();
    }

    private void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.SelectAll();
        }
    }

    private void CopyMd5_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(Md5Box.Text);
    }

    private void CopySha1_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(Sha1Box.Text);
    }

    private void CopySha256_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(Sha256Box.Text);
    }

    private void CopySha512_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(Sha512Box.Text);
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"MD5: {Md5Box.Text}");
        sb.AppendLine($"SHA-1: {Sha1Box.Text}");
        sb.AppendLine($"SHA-256: {Sha256Box.Text}");
        sb.AppendLine($"SHA-512: {Sha512Box.Text}");

        CopyToClipboard(sb.ToString());
    }

    private void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text) || text.StartsWith("Calculating") || text.StartsWith("Error")) return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to copy to clipboard:\n{ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
