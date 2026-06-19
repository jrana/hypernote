using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HyperNote.Services;

namespace HyperNote.Controls;

public partial class HexViewerControl : UserControl
{
    private string _filePath = "";
    private HexFileProvider? _provider;
    private int _bytesPerRow = 16;
    private long _lastFoundOffset = -1;

    public event Action<long, byte>? ByteSelected;

    public HexViewerControl()
    {
        InitializeComponent();
    }

    public void LoadFile(string path)
    {
        _filePath = path;
        _provider?.Dispose();
        _provider = new HexFileProvider(path, _bytesPerRow);
        HexList.ItemsSource = _provider;
    }

    private void BytesPerRowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BytesPerRowCombo == null || _filePath == "") return;
        
        var selectedItem = (ComboBoxItem)BytesPerRowCombo.SelectedItem;
        int val = int.Parse(selectedItem.Content.ToString()!);
        
        _bytesPerRow = val;
        _provider?.Dispose();
        _provider = new HexFileProvider(_filePath, _bytesPerRow);
        HexList.ItemsSource = _provider;
    }

    private void HexText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock tb && tb.DataContext is HexLineItem item && _provider != null)
        {
            var pt = e.GetPosition(tb);
            double charWidth = tb.ActualWidth / tb.Text.Length;
            int charIdx = (int)(pt.X / charWidth);
            charIdx = Math.Clamp(charIdx, 0, tb.Text.Length - 1);

            int byteIdx = charIdx / 3;
            if (byteIdx >= 0 && byteIdx < _bytesPerRow)
            {
                long offset = item.Offset + byteIdx;
                if (offset < _provider.FileLength)
                {
                    byte val = _provider.GetByteAtOffset(offset);
                    ByteSelected?.Invoke(offset, val);
                }
            }
        }
    }

    private void AsciiText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock tb && tb.DataContext is HexLineItem item && _provider != null)
        {
            var pt = e.GetPosition(tb);
            double charWidth = tb.ActualWidth / tb.Text.Length;
            int byteIdx = (int)(pt.X / charWidth);
            byteIdx = Math.Clamp(byteIdx, 0, tb.Text.Length - 1);

            if (byteIdx >= 0 && byteIdx < _bytesPerRow)
            {
                long offset = item.Offset + byteIdx;
                if (offset < _provider.FileLength)
                {
                    byte val = _provider.GetByteAtOffset(offset);
                    ByteSelected?.Invoke(offset, val);
                }
            }
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PerformSearch();
        }
    }

    private void FindButton_Click(object sender, RoutedEventArgs e)
    {
        PerformSearch();
    }

    private async void PerformSearch()
    {
        string term = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(term) || _provider == null || string.IsNullOrEmpty(_filePath)) return;

        byte[]? pattern = ParseHexPattern(term);
        if (pattern == null || pattern.Length == 0)
        {
            pattern = Encoding.ASCII.GetBytes(term);
        }

        long startOffset = _lastFoundOffset + 1;
        if (startOffset >= _provider.FileLength) startOffset = 0;

        long foundOffset = await Task.Run(() => SearchInFile(_filePath, pattern, startOffset));
        if (foundOffset >= 0)
        {
            _lastFoundOffset = foundOffset;
            int rowIndex = (int)(foundOffset / _bytesPerRow);
            
            HexList.SelectedIndex = rowIndex;
            HexList.ScrollIntoView(HexList.SelectedItem);
            
            byte val = _provider.GetByteAtOffset(foundOffset);
            ByteSelected?.Invoke(foundOffset, val);
        }
        else
        {
            if (startOffset > 0)
            {
                foundOffset = await Task.Run(() => SearchInFile(_filePath, pattern, 0));
                if (foundOffset >= 0)
                {
                    _lastFoundOffset = foundOffset;
                    int rowIndex = (int)(foundOffset / _bytesPerRow);
                    HexList.SelectedIndex = rowIndex;
                    HexList.ScrollIntoView(HexList.SelectedItem);
                    
                    byte val = _provider.GetByteAtOffset(foundOffset);
                    ByteSelected?.Invoke(foundOffset, val);
                    return;
                }
            }
            MessageBox.Show(Window.GetWindow(this), "Search pattern not found.", "Hex Search", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private byte[]? ParseHexPattern(string text)
    {
        text = text.Replace(" ", "").Replace("-", "");
        if (text.Length % 2 != 0) return null;
        
        try
        {
            byte[] bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private long SearchInFile(string path, byte[] pattern, long startOffset)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (startOffset >= fs.Length) return -1;
            
            fs.Seek(startOffset, SeekOrigin.Begin);
            
            byte[] buffer = new byte[4096];
            long currentPos = startOffset;
            int read;
            
            int patternIdx = 0;
            
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == pattern[patternIdx])
                    {
                        patternIdx++;
                        if (patternIdx == pattern.Length)
                        {
                            return currentPos + i - pattern.Length + 1;
                        }
                    }
                    else
                    {
                        i -= patternIdx;
                        patternIdx = 0;
                    }
                }
                currentPos += read;
            }
        }
        catch { }
        return -1;
    }
}
