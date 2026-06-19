using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using HyperNote.Services;
using TextSearch = HyperNote.Services.TextSearch;

namespace HyperNote.Controls;

/// <summary>
/// Non-modal Find / Find&amp;Replace tool window. It targets whichever editor tab is
/// active at the moment each action runs (resolved through the supplied callback),
/// so it keeps working as the user switches tabs while it stays open.
/// </summary>
public partial class FindReplaceDialog : Window
{
    private readonly Func<TextEditor?> _getEditor;
    private readonly Func<LargeFileView?> _getLargeFileView;

    /// <summary>Set by the host right before it really wants the window gone (app exit).</summary>
    public bool AllowClose { get; set; }

    public FindReplaceDialog(Func<TextEditor?> getEditor, Func<LargeFileView?> getLargeFileView)
    {
        _getEditor = getEditor;
        _getLargeFileView = getLargeFileView;
        InitializeComponent();
    }

    private TextEditor? Editor => _getEditor();

    private SearchOptions CurrentOptions => new(
        MatchCase: MatchCaseBox.IsChecked == true,
        WholeWord: WholeWordBox.IsChecked == true,
        Backward: UpRadio.IsChecked == true,
        UseRegex: RegexBox.IsChecked == true);

    // ----- shown by the host for Find or Replace -----

    public void ShowFor(bool replace)
    {
        bool showReplace = replace;
        ReplaceLabel.Visibility = ReplaceBox.Visibility =
            ReplaceButton.Visibility = ReplaceAllButton.Visibility =
                showReplace ? Visibility.Visible : Visibility.Collapsed;
        Title = showReplace ? "Find and Replace" : "Find";

        // Seed the search box from a single-line selection, as editors conventionally do.
        var ed = Editor;
        if (ed != null && ed.SelectionLength > 0)
        {
            var sel = ed.SelectedText;
            if (!sel.Contains('\n') && !sel.Contains('\r'))
                FindBox.Text = sel;
        }

        if (!IsVisible) Show();
        Activate();
        FindBox.Focus();
        FindBox.SelectAll();
        StatusLabel.Text = "";
    }

    /// <summary>F3 / Shift+F3 entry point from the main window.</summary>
    public void FindNextExternal(bool backward)
    {
        if (string.IsNullOrEmpty(FindBox.Text)) { ShowFor(false); return; }
        if (!IsVisible) Show();
        DoFindNext(forceBackward: backward);
    }

    // ----- actions -----

    private void FindNext_Click(object sender, RoutedEventArgs e) => DoFindNext();

    private async void DoFindNext(bool? forceBackward = null)
    {
        var ed = Editor;
        var lfv = _getLargeFileView();
        if (ed == null && lfv == null) { StatusLabel.Text = "No active view to search."; return; }

        string term = FindBox.Text;
        if (term.Length == 0) { StatusLabel.Text = "Enter text to find."; return; }

        var opts = CurrentOptions;
        if (forceBackward is bool b) opts = opts with { Backward = b };

        if (lfv != null)
        {
            StatusLabel.Text = "Searching...";
            FindNextButton.IsEnabled = false;
            ReplaceButton.IsEnabled = false;
            ReplaceAllButton.IsEnabled = false;
            CountButton.IsEnabled = false;

            try
            {
                bool found = await lfv.FindNextAsync(term, opts);
                if (found)
                {
                    StatusLabel.Text = "";
                }
                else
                {
                    StatusLabel.Text = $"Cannot find \"{term}\".";
                }
            }
            finally
            {
                FindNextButton.IsEnabled = true;
                ReplaceButton.IsEnabled = true;
                ReplaceAllButton.IsEnabled = true;
                CountButton.IsEnabled = true;
            }
            return;
        }

        // Forward: continue after the current selection. Backward: before it.
        if (ed == null) return;
        int start = opts.Backward ? ed.SelectionStart : ed.SelectionStart + ed.SelectionLength;

        try
        {
            var hit = TextSearch.Find(ed.Text, term, start, opts, wrap: true);
            if (hit is { } h)
            {
                SelectMatch(ed, h.Index, h.Length);
                StatusLabel.Text = "";
            }
            else
            {
                StatusLabel.Text = $"Cannot find \"{term}\".";
            }
        }
        catch (ArgumentException ex)
        {
            StatusLabel.Text = $"Invalid regex: {ex.Message}";
        }
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var ed = Editor;
        if (ed == null) { StatusLabel.Text = "This tab has no editable text."; return; }

        string term = FindBox.Text;
        if (term.Length == 0) { StatusLabel.Text = "Enter text to find."; return; }

        var opts = CurrentOptions;

        // If the current selection is exactly a match, replace it first; then advance.
        if (ed.SelectionLength > 0)
        {
            try
            {
                var re = TextSearch.BuildRegex(term, opts.MatchCase, opts.WholeWord, opts.UseRegex);
                var match = re.Match(ed.Text, ed.SelectionStart);
                if (match.Success && match.Index == ed.SelectionStart && match.Length == ed.SelectionLength)
                {
                    int caret = ed.SelectionStart;
                    string replacement = opts.UseRegex ? match.Result(ReplaceBox.Text) : ReplaceBox.Text;
                    ed.Document.Replace(caret, ed.SelectionLength, replacement);
                    ed.CaretOffset = caret + replacement.Length;
                }
            }
            catch (ArgumentException ex)
            {
                StatusLabel.Text = $"Invalid regex: {ex.Message}";
                return;
            }
        }
        DoFindNext();
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        string term = FindBox.Text;
        if (term.Length == 0) { StatusLabel.Text = "Enter text to find."; return; }

        var lfv = _getLargeFileView();
        if (lfv != null)
        {
            lfv.ReplaceAll(term, ReplaceBox.Text, CurrentOptions);
            return;
        }

        var ed = Editor;
        if (ed == null) { StatusLabel.Text = "This tab has no editable text."; return; }

        var opts = CurrentOptions;
        int replacedCount = 0;

        try
        {
            var re = TextSearch.BuildRegex(term, opts.MatchCase, opts.WholeWord, opts.UseRegex);
            var matches = re.Matches(ed.Text);
            if (matches.Count == 0) { StatusLabel.Text = $"Cannot find \"{term}\"."; return; }

            replacedCount = matches.Count;
            ed.Document.BeginUpdate();
            try
            {
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    var m = matches[i];
                    string replacement = opts.UseRegex ? m.Result(ReplaceBox.Text) : ReplaceBox.Text;
                    ed.Document.Replace(m.Index, m.Length, replacement);
                }
            }
            finally { ed.Document.EndUpdate(); }
        }
        catch (ArgumentException ex)
        {
            StatusLabel.Text = $"Invalid regex: {ex.Message}";
            return;
        }

        StatusLabel.Text = $"Replaced {replacedCount} occurrence(s).";
    }

    private async void Count_Click(object sender, RoutedEventArgs e)
    {
        var ed = Editor;
        var lfv = _getLargeFileView();
        if (ed == null && lfv == null) { StatusLabel.Text = "No active view to search."; return; }
        string term = FindBox.Text;
        if (term.Length == 0) { StatusLabel.Text = "Enter text to find."; return; }

        if (lfv != null)
        {
            StatusLabel.Text = "Counting...";
            CountButton.IsEnabled = false;
            try
            {
                int n = await lfv.CountMatchesAsync(term, MatchCaseBox.IsChecked == true, WholeWordBox.IsChecked == true, CurrentOptions.UseRegex);
                StatusLabel.Text = n == 0 ? $"Cannot find \"{term}\"." : $"{n} occurrence(s).";
            }
            finally
            {
                CountButton.IsEnabled = true;
            }
            return;
        }

        if (ed == null) return;

        try
        {
            int n2 = TextSearch.FindAll(ed.Text, term,
                CurrentOptions.MatchCase, CurrentOptions.WholeWord, CurrentOptions.UseRegex).Count;
            StatusLabel.Text = n2 == 0 ? $"Cannot find \"{term}\"." : $"{n2} occurrence(s).";
        }
        catch (ArgumentException ex)
        {
            StatusLabel.Text = $"Invalid regex: {ex.Message}";
        }
    }

    private static void SelectMatch(TextEditor ed, int index, int length)
    {
        ed.Select(index, length);
        int line = ed.Document.GetLineByOffset(index).LineNumber;
        ed.ScrollToLine(line);
        ed.TextArea.Caret.BringCaretToView();
    }

    // ----- window plumbing -----

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DoFindNext(); e.Handled = true; }
    }

    private void Close_Executed(object sender, ExecutedRoutedEventArgs e) => Hide();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Hide instead of destroy, so search text/options persist between uses.
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
