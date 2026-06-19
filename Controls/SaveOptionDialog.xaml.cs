using System.Windows;

namespace HyperNote.Controls
{
    public enum SaveOptionResult
    {
        Overwrite,
        SaveCopy,
        Cancel
    }

    public partial class SaveOptionDialog : Window
    {
        public SaveOptionResult Result { get; private set; } = SaveOptionResult.Cancel;

        public SaveOptionDialog(string fileName)
        {
            InitializeComponent();
            TxtMessage.Text = $"How would you like to save your edits to '{fileName}'?";
        }

        private void BtnOverwrite_Click(object sender, RoutedEventArgs e)
        {
            Result = SaveOptionResult.Overwrite;
            DialogResult = true;
            Close();
        }

        private void BtnSaveCopy_Click(object sender, RoutedEventArgs e)
        {
            Result = SaveOptionResult.SaveCopy;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = SaveOptionResult.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
