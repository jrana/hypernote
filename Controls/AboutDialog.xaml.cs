using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace HyperNote.Controls;

public partial class AboutDialog : Window
{
    private bool _isClosing;

    public AboutDialog()
    {
        InitializeComponent();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        Close();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_isClosing) return;
        _isClosing = true;
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { }
        e.Handled = true;
    }
}
