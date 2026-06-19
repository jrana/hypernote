using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace HyperNote.Controls;

public partial class SessionPickerDialog : Window
{
    public string? SelectedSession { get; private set; }

    public SessionPickerDialog(IEnumerable<string> sessions, string actionName, string title)
    {
        InitializeComponent();

        TxtHeader.Text = title;
        Title = title;
        BtnAction.Content = actionName;

        LstSessions.ItemsSource = sessions;
        if (LstSessions.Items.Count > 0)
        {
            LstSessions.SelectedIndex = 0;
        }
    }

    private void BtnAction_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void LstSessions_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (LstSessions.SelectedItem is string session)
        {
            SelectedSession = session;
            DialogResult = true;
            Close();
        }
    }
}
