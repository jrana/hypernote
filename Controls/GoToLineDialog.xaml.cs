using System;
using System.Windows;
using System.Windows.Input;

namespace HyperNote.Controls;

public partial class GoToLineDialog : Window
{
    public int LineNumber { get; private set; } = -1;
    private readonly int _maxLine;

    private bool _isClosing;

    public GoToLineDialog(int maxLine, int currentLine)
    {
        InitializeComponent();
        _maxLine = maxLine;
        PromptText.Text = $"Go to line (1 - {maxLine:N0}):";
        LineBox.Text = currentLine.ToString();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LineBox.Focus();
        LineBox.SelectAll();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!_isClosing)
        {
            _isClosing = true;
            Close();
        }
    }

    private void LineBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _isClosing = true;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            if (int.TryParse(LineBox.Text.Trim().Replace(",", ""), out int line) && line >= 1 && line <= _maxLine)
            {
                LineNumber = line;
                _isClosing = true;
                DialogResult = true;
            }
            else
            {
                _isClosing = true; // prevent deactivation closing when showing MessageBox
                MessageBox.Show(this, $"Please enter a valid line number between 1 and {_maxLine:N0}.", 
                    "Go to Line", MessageBoxButton.OK, MessageBoxImage.Warning);
                _isClosing = false;
                LineBox.Focus();
                LineBox.SelectAll();
            }
        }
    }
}
