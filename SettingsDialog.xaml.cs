using System.Windows;
using System.Windows.Media;
using HyperNote.Services;

namespace HyperNote;

public partial class SettingsDialog : Window
{
    // ── Monospace fonts shown in the font-family combo ─────────────────────
    private static readonly string[] MonospaceFonts =
    [
        "Cascadia Code",
        "Cascadia Mono",
        "Consolas",
        "Courier New",
        "DejaVu Sans Mono",
        "Fira Code",
        "Fira Mono",
        "Inconsolata",
        "JetBrains Mono",
        "Lucida Console",
        "Menlo",
        "Noto Sans Mono",
        "Source Code Pro",
        "Ubuntu Mono",
    ];

    // Working copy — only written to AppSettings on OK.
    private readonly AppSettings _draft;
    private readonly AppTheme _originalTheme;  // to revert on Cancel

    public SettingsDialog(AppSettings current)
    {
        InitializeComponent();

        _originalTheme = ThemeManager.Current;

        // Clone into draft so Cancel doesn't mutate the live settings.
        _draft = new AppSettings
        {
            DarkTheme             = current.DarkTheme,
            EditorFontFamily      = current.EditorFontFamily,
            EditorFontSize        = current.EditorFontSize,
            WordWrap              = current.WordWrap,
            ConvertTabsToSpaces   = current.ConvertTabsToSpaces,
            IndentWidth           = current.IndentWidth,
            RememberOpenFiles     = current.RememberOpenFiles,
            MaxRecentFiles        = current.MaxRecentFiles,
            LargeFileThresholdBytes = current.LargeFileThresholdBytes,
            
            // Clone new properties into draft
            ShowLineNumbers       = current.ShowLineNumbers,
            ShowMinimap           = current.ShowMinimap,
            ShowWhitespace        = current.ShowWhitespace,
            HighlightCurrentLine  = current.HighlightCurrentLine,
            EnableAutoBraceCompletion = current.EnableAutoBraceCompletion,
            AutoSave              = current.AutoSave,
            AutoSaveIntervalSeconds = current.AutoSaveIntervalSeconds,
            DefaultEncoding       = current.DefaultEncoding,
            DefaultLineEnding     = current.DefaultLineEnding,
            TerminalShellPath     = current.TerminalShellPath,
            TerminalFontFamily    = current.TerminalFontFamily,
            TerminalFontSize      = current.TerminalFontSize,

            // Collections are not editable in this dialog — carry them through.
            RecentFiles           = current.RecentFiles,
            LastOpenFiles         = current.LastOpenFiles,
        };

        PopulateFontFamilyCombos();
        PopulateOtherCombos();
        LoadToUI();
    }

    // ── Populate UI from draft ─────────────────────────────────────────────

    private void PopulateFontFamilyCombos()
    {
        // Seed with known monospace fonts.
        foreach (var name in MonospaceFonts)
        {
            FontFamilyBox.Items.Add(name);
            TerminalFontFamilyBox.Items.Add(name);
        }

        // Always ensure the currently-saved font is in the list.
        if (!FontFamilyBox.Items.Contains(_draft.EditorFontFamily))
            FontFamilyBox.Items.Insert(0, _draft.EditorFontFamily);

        if (!TerminalFontFamilyBox.Items.Contains(_draft.TerminalFontFamily))
            TerminalFontFamilyBox.Items.Insert(0, _draft.TerminalFontFamily);
    }

    private void PopulateOtherCombos()
    {
        // DefaultEncodingBox
        DefaultEncodingBox.Items.Add("UTF-8");
        DefaultEncodingBox.Items.Add("UTF-8 BOM");
        DefaultEncodingBox.Items.Add("UTF-16 LE");
        DefaultEncodingBox.Items.Add("UTF-16 BE");
        DefaultEncodingBox.Items.Add("Windows-1252");
        DefaultEncodingBox.Items.Add("ASCII");

        // DefaultLineEndingBox
        DefaultLineEndingBox.Items.Add("CRLF");
        DefaultLineEndingBox.Items.Add("LF");
        DefaultLineEndingBox.Items.Add("CR");

        // TerminalShellBox
        TerminalShellBox.Items.Add("powershell.exe");
        TerminalShellBox.Items.Add("cmd.exe");
        TerminalShellBox.Items.Add("wsl.exe");
    }

    private void LoadToUI()
    {
        ThemeLight.IsChecked = !_draft.DarkTheme;
        ThemeDark.IsChecked  =  _draft.DarkTheme;

        // Appearance
        LineNumbersBox.IsChecked = _draft.ShowLineNumbers;
        MinimapBox.IsChecked = _draft.ShowMinimap;
        WhitespaceBox.IsChecked = _draft.ShowWhitespace;

        // Editor & Formatting
        FontFamilyBox.Text   = _draft.EditorFontFamily;
        FontSizeBox.Text     = _draft.EditorFontSize.ToString("G");
        WordWrapBox.IsChecked       = _draft.WordWrap;
        HighlightLineBox.IsChecked = _draft.HighlightCurrentLine;
        TabsToSpacesBox.IsChecked   = _draft.ConvertTabsToSpaces;
        IndentWidthBox.Text         = _draft.IndentWidth.ToString();
        AutoBracesBox.IsChecked = _draft.EnableAutoBraceCompletion;

        // Files & Session
        RememberFilesBox.IsChecked      = _draft.RememberOpenFiles;
        MaxRecentBox.Text               = _draft.MaxRecentFiles.ToString();
        LargeFileThresholdBox.Text      = (_draft.LargeFileThresholdBytes / (1024L * 1024L)).ToString();
        AutoSaveBox.IsChecked = _draft.AutoSave;
        AutoSaveIntervalBox.Text = _draft.AutoSaveIntervalSeconds.ToString();
        DefaultEncodingBox.SelectedItem = _draft.DefaultEncoding;
        if (DefaultEncodingBox.SelectedIndex == -1) DefaultEncodingBox.Text = _draft.DefaultEncoding;
        DefaultLineEndingBox.SelectedItem = _draft.DefaultLineEnding;
        if (DefaultLineEndingBox.SelectedIndex == -1) DefaultLineEndingBox.Text = _draft.DefaultLineEnding;

        // Terminal
        TerminalShellBox.Text = _draft.TerminalShellPath;
        TerminalFontFamilyBox.Text = _draft.TerminalFontFamily;
        TerminalFontSizeBox.Text = _draft.TerminalFontSize.ToString("G");
    }

    // ── Category Navigation ────────────────────────────────────────────────

    private void CategoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AppearancePanel == null || EditorPanel == null || FilesPanel == null || TerminalPanel == null)
            return;

        AppearancePanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Collapsed;
        FilesPanel.Visibility = Visibility.Collapsed;
        TerminalPanel.Visibility = Visibility.Collapsed;

        if (CategoryList.SelectedItem is System.Windows.Controls.ListBoxItem selectedItem)
        {
            string category = selectedItem.Content?.ToString() ?? "";
            if (category.Contains("Appearance"))
                AppearancePanel.Visibility = Visibility.Visible;
            else if (category.Contains("Editor"))
                EditorPanel.Visibility = Visibility.Visible;
            else if (category.Contains("Files"))
                FilesPanel.Visibility = Visibility.Visible;
            else if (category.Contains("Terminal"))
                TerminalPanel.Visibility = Visibility.Visible;
        }
    }

    // ── Read UI back into draft ────────────────────────────────────────────

    private bool TrySaveFromUI()
    {
        // Font size
        if (!double.TryParse(FontSizeBox.Text, out double fontSize) || fontSize < 6 || fontSize > 144)
        {
            MessageBox.Show(this, "Font size must be a number between 6 and 144.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            FontSizeBox.Focus();
            return false;
        }

        // Indent width
        if (!int.TryParse(IndentWidthBox.Text, out int indentWidth) || indentWidth < 1 || indentWidth > 16)
        {
            MessageBox.Show(this, "Indent width must be a whole number between 1 and 16.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            IndentWidthBox.Focus();
            return false;
        }

        // Max recent files
        if (!int.TryParse(MaxRecentBox.Text, out int maxRecent) || maxRecent < 1 || maxRecent > 100)
        {
            MessageBox.Show(this, "Max recent files must be a whole number between 1 and 100.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            MaxRecentBox.Focus();
            return false;
        }

        // Large-file threshold
        if (!long.TryParse(LargeFileThresholdBox.Text, out long threshMb) || threshMb < 1 || threshMb > 65536)
        {
            MessageBox.Show(this, "Large-file threshold must be a whole number between 1 and 65536 MB.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            LargeFileThresholdBox.Focus();
            return false;
        }

        // Auto-Save interval
        if (!int.TryParse(AutoSaveIntervalBox.Text, out int autoSaveInterval) || autoSaveInterval < 5 || autoSaveInterval > 3600)
        {
            MessageBox.Show(this, "Auto-Save interval must be a whole number between 5 and 3600 seconds.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            AutoSaveIntervalBox.Focus();
            return false;
        }

        // Terminal font size
        if (!double.TryParse(TerminalFontSizeBox.Text, out double terminalFontSize) || terminalFontSize < 6 || terminalFontSize > 144)
        {
            MessageBox.Show(this, "Terminal font size must be a number between 6 and 144.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            TerminalFontSizeBox.Focus();
            return false;
        }

        // Terminal shell validation
        string shellPath = TerminalShellBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            MessageBox.Show(this, "Terminal shell executable path cannot be empty.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            TerminalShellBox.Focus();
            return false;
        }

        if (shellPath.Contains(System.IO.Path.DirectorySeparatorChar) || shellPath.Contains(System.IO.Path.AltDirectorySeparatorChar))
        {
            if (!System.IO.File.Exists(shellPath))
            {
                MessageBox.Show(this, $"The specified terminal shell executable was not found:\n{shellPath}",
                    "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                TerminalShellBox.Focus();
                return false;
            }
        }

        _draft.DarkTheme           = ThemeDark.IsChecked == true;
        _draft.EditorFontFamily    = string.IsNullOrWhiteSpace(FontFamilyBox.Text) ? "Consolas" : FontFamilyBox.Text.Trim();
        _draft.EditorFontSize      = fontSize;
        _draft.WordWrap            = WordWrapBox.IsChecked == true;
        _draft.ConvertTabsToSpaces = TabsToSpacesBox.IsChecked == true;
        _draft.IndentWidth         = indentWidth;
        _draft.RememberOpenFiles   = RememberFilesBox.IsChecked == true;
        _draft.MaxRecentFiles      = maxRecent;
        _draft.LargeFileThresholdBytes = threshMb * 1024L * 1024L;

        // Save new settings to draft
        _draft.ShowLineNumbers     = LineNumbersBox.IsChecked == true;
        _draft.ShowMinimap         = MinimapBox.IsChecked == true;
        _draft.ShowWhitespace      = WhitespaceBox.IsChecked == true;
        _draft.HighlightCurrentLine = HighlightLineBox.IsChecked == true;
        _draft.EnableAutoBraceCompletion = AutoBracesBox.IsChecked == true;
        _draft.AutoSave            = AutoSaveBox.IsChecked == true;
        _draft.AutoSaveIntervalSeconds = autoSaveInterval;
        _draft.DefaultEncoding     = DefaultEncodingBox.Text;
        _draft.DefaultLineEnding   = DefaultLineEndingBox.Text;
        _draft.TerminalShellPath   = shellPath;
        _draft.TerminalFontFamily  = string.IsNullOrWhiteSpace(TerminalFontFamilyBox.Text) ? "Consolas" : TerminalFontFamilyBox.Text.Trim();
        _draft.TerminalFontSize    = terminalFontSize;

        return true;
    }

    // ── Live theme preview ─────────────────────────────────────────────────

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        // Apply immediately so the dialog (and the rest of the app) updates live.
        // We suppress the handler during LoadToUI by checking IsLoaded.
        if (!IsLoaded) return;
        ThemeManager.Apply(ThemeDark.IsChecked == true ? AppTheme.Dark : AppTheme.Light);
    }

    // ── Button handlers ────────────────────────────────────────────────────

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySaveFromUI()) return;

        // Commit draft → live settings.
        var s = SettingsService.Instance.Settings;
        s.DarkTheme             = _draft.DarkTheme;
        s.EditorFontFamily      = _draft.EditorFontFamily;
        s.EditorFontSize        = _draft.EditorFontSize;
        s.WordWrap              = _draft.WordWrap;
        s.ConvertTabsToSpaces   = _draft.ConvertTabsToSpaces;
        s.IndentWidth           = _draft.IndentWidth;
        s.RememberOpenFiles     = _draft.RememberOpenFiles;
        s.MaxRecentFiles        = _draft.MaxRecentFiles;
        s.LargeFileThresholdBytes = _draft.LargeFileThresholdBytes;

        // Commit new draft properties -> live settings
        s.ShowLineNumbers       = _draft.ShowLineNumbers;
        s.ShowMinimap           = _draft.ShowMinimap;
        s.ShowWhitespace        = _draft.ShowWhitespace;
        s.HighlightCurrentLine  = _draft.HighlightCurrentLine;
        s.EnableAutoBraceCompletion = _draft.EnableAutoBraceCompletion;
        s.AutoSave              = _draft.AutoSave;
        s.AutoSaveIntervalSeconds = _draft.AutoSaveIntervalSeconds;
        s.DefaultEncoding       = _draft.DefaultEncoding;
        s.DefaultLineEnding     = _draft.DefaultLineEnding;
        s.TerminalShellPath     = _draft.TerminalShellPath;
        s.TerminalFontFamily    = _draft.TerminalFontFamily;
        s.TerminalFontSize      = _draft.TerminalFontSize;

        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        // Revert any live theme change the user previewed.
        if (ThemeManager.Current != _originalTheme)
            ThemeManager.Apply(_originalTheme);
        DialogResult = false;
    }
}
