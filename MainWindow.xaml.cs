using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Media.Animation;
using TextSearch = HyperNote.Services.TextSearch;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using HyperNote.Controls;
using HyperNote.Services;
using Wpf.Ui.Appearance;
// Import only what we need from Wpf.Ui.Controls to avoid namespace collisions
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;
// Explicit aliases to resolve ambiguity with Wpf.Ui.Controls types
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace HyperNote;

public partial class MainWindow : FluentWindow
{
    public static readonly RoutedUICommand TogglePreviewCommand =
        new("Toggle Preview", "TogglePreview", typeof(MainWindow));

    public static readonly RoutedUICommand FindNextCommand =
        new("Find Next", "FindNext", typeof(MainWindow));

    public static readonly RoutedUICommand FindPreviousCommand =
        new("Find Previous", "FindPrevious", typeof(MainWindow));

    public static readonly RoutedUICommand SettingsCommand =
        new("Settings", "Settings", typeof(MainWindow));

    public static readonly RoutedUICommand FormatDocumentCommand =
        new("Format Document", "FormatDocument", typeof(MainWindow));

    public static readonly RoutedUICommand ToggleTerminalCommand =
        new("Toggle Terminal", "ToggleTerminal", typeof(MainWindow));

    public static readonly RoutedUICommand ToggleSidebarCommand =
        new("Toggle Sidebar", "ToggleSidebar", typeof(MainWindow));

    public static readonly RoutedUICommand FuzzySearchCommand =
        new("Go to File", "FuzzySearch", typeof(MainWindow));

    public static readonly RoutedUICommand GoToLineCommand =
        new("Go to Line", "GoToLine", typeof(MainWindow));

    public static readonly RoutedUICommand FindInFilesCommand =
        new("Find in Files", "FindInFiles", typeof(MainWindow));

    public static readonly RoutedUICommand TransformTextCommand =
        new("Transform Text", "TransformText", typeof(MainWindow));

    public static readonly RoutedUICommand CommandPaletteCommand =
        new("Command Palette", "CommandPalette", typeof(MainWindow));

    public static readonly RoutedUICommand GoToSymbolCommand =
        new("Go to Symbol", "GoToSymbol", typeof(MainWindow));

    public static readonly RoutedUICommand NewWindowCommand =
        new("New Window", "NewWindow", typeof(MainWindow));

    public static readonly RoutedUICommand TimeDateCommand =
        new("Time/Date", "TimeDate", typeof(MainWindow));

    public static readonly RoutedUICommand ZoomInCommand =
        new("Zoom In", "ZoomIn", typeof(MainWindow));

    public static readonly RoutedUICommand ZoomOutCommand =
        new("Zoom Out", "ZoomOut", typeof(MainWindow));

    public static readonly RoutedUICommand ResetZoomCommand =
        new("Reset Zoom", "ResetZoom", typeof(MainWindow));

    public static readonly RoutedUICommand ToggleBookmarkCommand =
        new("Toggle Bookmark", "ToggleBookmark", typeof(MainWindow));

    public static readonly RoutedUICommand NextBookmarkCommand =
        new("Next Bookmark", "NextBookmark", typeof(MainWindow));

    public static readonly RoutedUICommand PrevBookmarkCommand =
        new("Previous Bookmark", "PrevBookmark", typeof(MainWindow));

    public static readonly RoutedUICommand ClearBookmarksCommand =
        new("Clear Bookmarks", "ClearBookmarks", typeof(MainWindow));



    private static readonly MarkdownPipeline MdPipeline =

        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private static readonly string WebViewDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HyperNote", "WebView2");

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tiff", ".tif", ".wdp", ".hdp"
    };

    private static bool IsImageExtension(string ext) => ImageExtensions.Contains(ext);

    // Live preview is served from a stable virtual origin. A stable origin lets
    // relative resources (images, CSS) resolve against the file's folder and lets
    // sessionStorage survive reloads so scroll position is preserved while typing.
    private const string PreviewLiveUrl = "https://qnp.preview/__live__";
    private const string PreviewFilter  = "https://qnp.preview/*";

    // Injected so the preview remembers scroll position across live reloads.
    private const string ScrollScript =
        "<script>(function(){try{var k='qnp_scroll';var y=sessionStorage.getItem(k);" +
        "if(y!==null)window.scrollTo(0,parseFloat(y));" +
        "window.addEventListener('scroll',function(){sessionStorage.setItem(k,window.scrollY);}," +
        "{passive:true});}catch(e){}})();</script>";

    private int _untitledCounter;
    private string? _currentWorkspaceFolder;
    private CancellationTokenSource? _searchCts;
    private System.Threading.CancellationTokenSource? _statsCts;
    private DispatcherTimer? _statsDebounceTimer;

    public MainWindow()
    {
        InitializeComponent();

        // Force TitleBar template to apply early to prevent NullReferenceException in HwndSourceHook
        AppTitleBar.ApplyTemplate();

        // ── Mica / Acrylic / Solid backdrop cascade ───────────────────────
        // Windows 11 (build >= 22000): Mica   Windows 10: Acrylic  Older: solid
        try
        {
            var ver = Environment.OSVersion.Version;
            if (ver.Major >= 10 && ver.Build >= 22000)
                WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
            else if (ver.Major >= 10)
                WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Acrylic;
            // else: no backdrop — solid App.ChromeBackground used automatically
        }
        catch { /* backdrop is cosmetic — never crash */ }

        CommandBindings.Add(new CommandBinding(TogglePreviewCommand, (_, _) => TogglePreview()));
        CommandBindings.Add(new CommandBinding(GoToLineCommand, GoToLine_Executed, GoToLine_CanExecute));
        CommandBindings.Add(new CommandBinding(CommandPaletteCommand, CommandPalette_Executed));
        CommandBindings.Add(new CommandBinding(GoToSymbolCommand, GoToSymbol_Executed, GoToSymbol_CanExecute));
        CommandBindings.Add(new CommandBinding(NewWindowCommand, NewWindow_Executed));
        CommandBindings.Add(new CommandBinding(TimeDateCommand, TimeDate_Executed, (s, e) => e.CanExecute = CurrentContext is { IsReadOnlyView: false, Editor: not null }));
        CommandBindings.Add(new CommandBinding(ZoomInCommand, ZoomIn_Executed, Zoom_CanExecute));
        CommandBindings.Add(new CommandBinding(ZoomOutCommand, ZoomOut_Executed, Zoom_CanExecute));
        CommandBindings.Add(new CommandBinding(ResetZoomCommand, ResetZoom_Executed, Zoom_CanExecute));

        DarkThemeMenu.IsChecked = ThemeManager.Current == AppTheme.Dark;
        ThemeManager.ThemeChanged += OnThemeChanged;

        RebuildRecentMenu();

        var args = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToList();
        if (args.Count > 0)
        {
            foreach (var a in args) OpenFile(a);
        }
        else if (SettingsService.Instance.Settings.RememberOpenFiles)
        {
            if (!RestoreSession())
                NewTab();
        }
        else
        {
            NewTab();
        }

        ApplyAutoSaveSettings();
        UpdateViewMenuCheckmarks();
        CheckForOrphanedBackups();

        BookmarkService.Instance.BookmarksChanged += (filePath) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (TabItem tab in Tabs.Items)
                {
                    if (tab.DataContext is TabContext tc && string.Equals(tc.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        tc.BookmarkMargin?.InvalidateVisual();
                    }
                }
                RefreshBookmarksList();
            }));
        };

    }

    // =====================================================================
    //  Tab plumbing
    // =====================================================================

    public enum PreviewKind { None, Markdown, Html }

    /// <summary>Per-tab state bag, stored in TabItem.Tag.</summary>
    public sealed class TabContext
    {
        public required TabItem Tab;
        public required TextBlock HeaderText;
        public string? FilePath;
        public TextEditor? Editor;
        public Encoding Encoding = new UTF8Encoding(false);
        public bool IsDirty;
        public bool IsReadOnlyView;          // large-file, PDF, or image tab
        public LargeFileView? LargeView;
        public Controls.ImageViewerControl? ImageViewer;
        public Controls.HexViewerControl? HexViewer;
        public FoldingManager? Folding;
        public string? FoldKind;             // "html" | "xml" | "json" | null
        public DispatcherTimer? FoldTimer;
        public DispatcherTimer? OutlineTimer;
        public DispatcherTimer? BackupTimer;
        public string? BackupPath;
        public MinimapControl? Minimap;

        // File Watcher
        public FileWatcherService? FileWatcher;
        public Border? FileChangedBar;
        public Button? FileChangedReloadBtn;
        public Controls.BookmarkMargin? BookmarkMargin;





        // Live preview (Markdown or HTML)
        public PreviewKind PreviewKind = PreviewKind.None;
        public Border? BarBorder;            // Top bar border control
        public Grid? PreviewGrid;            // inner grid: editor | splitter | preview
        public ColumnDefinition? PreviewColumn;
        public ToggleButton? PreviewToggle;
        public WebView2? Preview;
        public DispatcherTimer? PreviewTimer;
        public string? LiveHtml;             // content served to the preview on demand
        public bool PreviewNavigated;

        // Outline pane controls
        public ColumnDefinition? OutlineColumn;
        public ToggleButton? OutlineToggle;
        public TreeView? OutlineTree;
        public GridSplitter? OutlineSplitter;
        public GridSplitter? PreviewSplitter;

        public string DisplayName =>
            FilePath != null ? Path.GetFileName(FilePath) : HeaderText.Text.TrimEnd('*', ' ');

        public void UpdateHeader() =>
            HeaderText.Text = (FilePath != null ? Path.GetFileName(FilePath) : DisplayName)
                              + (IsDirty ? " *" : "");
    }

    private TabContext? CurrentContext => Tabs == null ? null : (Tabs.SelectedItem as TabItem)?.Tag as TabContext;

    private TabContext CreateTab(string title, object content)
    {
        var headerText = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        var closeBtn = new Button
        {
            Content = "✕", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(3, 0, 3, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = (Brush)FindResource("App.Foreground"), Cursor = Cursors.Hand
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(headerText);
        header.Children.Add(closeBtn);

        var tab = new TabItem { Header = header, Content = content };
        var ctx = new TabContext { Tab = tab, HeaderText = headerText };
        tab.Tag = ctx;
        closeBtn.Click += (_, _) => CloseTab(ctx);

        Tabs.Items.Add(tab);
        Tabs.SelectedItem = tab;
        return ctx;
    }

    private void CloseTab(TabContext ctx)
    {
        if (ctx.IsDirty && !ConfirmDiscard(ctx)) return;
        StopFileWatcher(ctx);
        ctx.FoldTimer?.Stop();

        ctx.PreviewTimer?.Stop();
        ctx.OutlineTimer?.Stop();
        ctx.BackupTimer?.Stop();

        if (!string.IsNullOrEmpty(ctx.BackupPath) && File.Exists(ctx.BackupPath))
        {
            try { File.Delete(ctx.BackupPath); } catch { }
        }

        ctx.LargeView?.Dispose();
        ctx.Preview?.Dispose();
        Tabs.Items.Remove(ctx.Tab);
        if (Tabs.Items.Count == 0) NewTab();

        SaveSessionState();
    }

    private bool ConfirmDiscard(TabContext ctx)
    {
        var r = MessageBox.Show(this, $"Save changes to {ctx.DisplayName}?",
            "HyperNote", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return SaveTab(ctx);
        return true;
    }

    // =====================================================================
    //  Creating editors / viewers
    // =====================================================================

    private TabContext NewTab()
    {
        var editor = CreateEditor();
        var built = BuildEditorLayout(editor);
        var ctx = CreateTab($"Untitled-{++_untitledCounter}", built.Container);
        ctx.Encoding = ParseEncoding(SettingsService.Instance.Settings.DefaultEncoding);
        ctx.Editor = editor;
        ApplyLayoutResult(ctx, built);


        built.PreviewToggle.Click += (_, _) => SetPreviewVisible(ctx, built.PreviewToggle.IsChecked == true);
        built.OutlineToggle.Click += (_, _) => SetOutlineVisible(ctx, built.OutlineToggle.IsChecked == true);

        built.OutlineTree.SelectedItemChanged += (s, e) => {
            if (built.OutlineTree.SelectedItem is OutlineNode node && ctx.Editor != null)
            {
                var ed = ctx.Editor;
                int line = Math.Clamp(node.LineNumber, 1, ed.Document.LineCount);
                ed.ScrollToLine(line);
                var lineSeg = ed.Document.GetLineByNumber(line);
                ed.Select(lineSeg.Offset, 0);
                ed.TextArea.Caret.BringCaretToView();
            }
        };

        WireEditor(ctx);
        UpdateEditorLayoutCapabilities(ctx);
        return ctx;
    }

    public void OpenFile(string path)
    {
        path = Path.GetFullPath(path);

        foreach (TabItem t in Tabs.Items)
            if (t.Tag is TabContext c &&
                string.Equals(c.FilePath, path, StringComparison.OrdinalIgnoreCase))
            { Tabs.SelectedItem = t; return; }

        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            long size = new FileInfo(path).Length;

            if (ext == ".pdf")
            {
                OpenPdfTab(path);
            }
            else if (IsImageExtension(ext))
            {
                OpenImageTab(path);
            }
            else if (IsBinaryFile(path))
            {
                OpenHexTab(path);
            }
            else if (size >= SettingsService.Instance.Settings.LargeFileThresholdBytes)
            {
                var view = new LargeFileView();
                var built = BuildEditorLayout(view);
                var ctx = CreateTab(Path.GetFileName(path), built.Container);
                ctx.FilePath = path;
                ctx.IsReadOnlyView = false;
                ctx.LargeView = view;

                view.DocumentChanged += (s, e) => {
                    ctx.IsDirty = view.IsDirty;
                    ctx.UpdateHeader();
                };
                view.FileCleared += (s, e) => ConvertLargeTabToNormalTab(ctx);

                ApplyLayoutResult(ctx, built);
                StartFileWatcher(ctx);


                built.PreviewToggle.Click += (_, _) => SetPreviewVisible(ctx, built.PreviewToggle.IsChecked == true);
                built.OutlineToggle.Click += (_, _) => SetOutlineVisible(ctx, built.OutlineToggle.IsChecked == true);

                view.SelectionChanged += (_, _) => {
                    if (CurrentContext?.LargeView == view)
                        UpdateStatusBar();
                };

                view.ZoomChanged += (_, _) => {
                    if (CurrentContext?.LargeView == view)
                        UpdateStatusBar();
                };

                view.Open(path);
                UpdateEditorLayoutCapabilities(ctx);
            }
            else
            {
                var loaded = FileService.ReadAllTextShared(path);
                var editor = CreateEditor();
                editor.Text = loaded.Text;
                editor.SyntaxHighlighting = SyntaxMapper.ForExtension(ext);

                var built = BuildEditorLayout(editor);
                var ctx = CreateTab(Path.GetFileName(path), built.Container);
                ctx.FilePath = path;
                ctx.Editor = editor;
                ctx.Encoding = loaded.Encoding;
                ApplyLayoutResult(ctx, built);
                StartFileWatcher(ctx);


                built.PreviewToggle.Click += (_, _) => SetPreviewVisible(ctx, built.PreviewToggle.IsChecked == true);
                built.OutlineToggle.Click += (_, _) => SetOutlineVisible(ctx, built.OutlineToggle.IsChecked == true);

                built.OutlineTree.SelectedItemChanged += (s, e) => {
                    if (built.OutlineTree.SelectedItem is OutlineNode node && ctx.Editor != null)
                    {
                        var ed = ctx.Editor;
                        int line = Math.Clamp(node.LineNumber, 1, ed.Document.LineCount);
                        ed.ScrollToLine(line);
                        var lineSeg = ed.Document.GetLineByNumber(line);
                        ed.Select(lineSeg.Offset, 0);
                        ed.TextArea.Caret.BringCaretToView();
                    }
                };

                ctx.FoldKind = SyntaxMapper.FoldKindForExtension(ext);
                WireEditor(ctx);
                UpdateEditorLayoutCapabilities(ctx);
            }

            SettingsService.Instance.AddRecentFile(path);
            RebuildRecentMenu();
            StatusText.Text = $"Opened {path}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open file:\n{path}\n\n{ex.Message}",
                "HyperNote", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }



    private void OpenHexTab(string path)
    {
        var hexView = new Controls.HexViewerControl();
        var ctx = CreateTab(Path.GetFileName(path), hexView);
        ctx.FilePath = path;
        ctx.IsReadOnlyView = true;
        ctx.HexViewer = hexView;

        hexView.ByteSelected += (offset, val) =>
        {
            char asciiChar = (val >= 32 && val <= 126) ? (char)val : '.';
            StatusText.Text = $"Offset: 0x{offset:X8} ({offset})   Value: 0x{val:X2} ({val} '{asciiChar}')";
        };

        hexView.LoadFile(path);
        StartFileWatcher(ctx);
    }

    private static PreviewKind PreviewKindForExtension(string ext) => ext switch
    {
        ".md" or ".markdown" => PreviewKind.Markdown,
        ".html" or ".htm" or ".xhtml" => PreviewKind.Html,
        _ => PreviewKind.None
    };

    private static TextEditor CreateEditor()
    {
        var s = SettingsService.Instance.Settings;
        string newLineStr = s.DefaultLineEnding switch
        {
            "LF" => "\n",
            "CR" => "\r",
            _ => "\r\n"
        };
        var editor = new TextEditor
        {
            ShowLineNumbers = s.ShowLineNumbers,
            FontFamily = new FontFamily(s.EditorFontFamily),
            FontSize   = s.EditorFontSize,
            WordWrap   = s.WordWrap,
            HorizontalScrollBarVisibility = s.WordWrap
                ? ScrollBarVisibility.Disabled
                : ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        editor.Options.EnableHyperlinks          = false;
        editor.Options.ConvertTabsToSpaces       = s.ConvertTabsToSpaces;
        editor.Options.IndentationSize           = s.IndentWidth;
        editor.Options.EnableRectangularSelection = true;
        editor.Options.EnableTextDragDrop        = true;
        editor.Options.ShowSpaces                = s.ShowWhitespace;
        editor.Options.ShowTabs                  = s.ShowWhitespace;
        editor.Options.ShowEndOfLine             = s.ShowWhitespace;
        editor.Options.HighlightCurrentLine      = s.HighlightCurrentLine;
        ApplyEditorTheme(editor);
        return editor;
    }

    private void WireEditor(TabContext ctx)
    {
        var editor = ctx.Editor!;
        EditingServices.InstallAutoCloser(editor);






        if (!string.IsNullOrEmpty(ctx.FilePath))
        {
            ctx.BookmarkMargin = new Controls.BookmarkMargin(ctx.FilePath);
            editor.TextArea.LeftMargins.Insert(0, ctx.BookmarkMargin);
        }


        // Setup Outline Timer
        ctx.OutlineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        ctx.OutlineTimer.Tick += (_, _) => { ctx.OutlineTimer!.Stop(); UpdateOutlineForActiveTab(); };

        // Always setup Preview Timer so it is ready if capability changes later
        ctx.PreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        ctx.PreviewTimer.Tick += (_, _) =>
        {
            ctx.PreviewTimer!.Stop();
            if (ctx.PreviewColumn != null && ctx.PreviewColumn.Width.Value > 0)
                RenderPreview(ctx);
        };

        // Setup Backup Timer for debounced session auto-saves
        ctx.BackupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        ctx.BackupTimer.Tick += (_, _) => { ctx.BackupTimer!.Stop(); SaveBackup(ctx); };

        editor.Document.TextChanged += (_, _) =>
        {
            if (!ctx.IsDirty) { ctx.IsDirty = true; ctx.UpdateHeader(); }
            if (ctx.PreviewTimer != null) { ctx.PreviewTimer.Stop(); ctx.PreviewTimer.Start(); }
            if (ctx.FoldTimer != null) { ctx.FoldTimer.Stop(); ctx.FoldTimer.Start(); }
            ctx.OutlineTimer.Stop();
            ctx.OutlineTimer.Start();
            ctx.BackupTimer?.Stop();
            ctx.BackupTimer?.Start();
            if (CurrentContext == ctx) UpdateStatusBar();
        };

        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (CurrentContext == ctx)
                UpdateStatusBar();
        };

        editor.TextArea.SelectionChanged += (_, _) =>
        {
            if (CurrentContext == ctx)
                UpdateStatusBar();
        };

        editor.PreviewMouseWheel += Editor_PreviewMouseWheel;

        if (ctx.FoldKind != null)
        {
            ctx.Folding = FoldingManager.Install(editor.TextArea);
            UpdateFoldings(ctx);
            ctx.FoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            ctx.FoldTimer.Tick += (_, _) => { ctx.FoldTimer!.Stop(); UpdateFoldings(ctx); };
        }
    }

    private void ConvertLargeTabToNormalTab(TabContext ctx)
    {
        if (ctx.LargeView == null || ctx.FilePath == null) return;

        ctx.LargeView.Dispose();
        ctx.LargeView = null;

        var editor = CreateEditor();
        editor.Text = string.Empty;
        ctx.Editor = editor;

        var built = BuildEditorLayout(editor);
        ctx.Tab.Content = built.Container;

        ApplyLayoutResult(ctx, built);

        built.PreviewToggle.Click += (_, _) => SetPreviewVisible(ctx, built.PreviewToggle.IsChecked == true);
        built.OutlineToggle.Click += (_, _) => SetOutlineVisible(ctx, built.OutlineToggle.IsChecked == true);

        built.OutlineTree.SelectedItemChanged += (s, e) => {
            if (built.OutlineTree.SelectedItem is OutlineNode node && ctx.Editor != null)
            {
                var ed = ctx.Editor;
                int line = Math.Clamp(node.LineNumber, 1, ed.Document.LineCount);
                ed.ScrollToLine(line);
                var lineSeg = ed.Document.GetLineByNumber(line);
                ed.Select(lineSeg.Offset, 0);
                ed.TextArea.Caret.BringCaretToView();
            }
        };

        string ext = Path.GetExtension(ctx.FilePath).ToLowerInvariant();
        ctx.FoldKind = SyntaxMapper.FoldKindForExtension(ext);
        
        WireEditor(ctx);
        UpdateEditorLayoutCapabilities(ctx);

        ctx.IsDirty = false;
        ctx.IsReadOnlyView = false;
        ctx.UpdateHeader();

        editor.Focus();
        UpdateStatusBar();
    }

    private static void UpdateFoldings(TabContext ctx)
    {
        if (ctx.Folding == null || ctx.Editor == null) return;
        try
        {
            var doc = ctx.Editor.Document;
            switch (ctx.FoldKind)
            {
                case "html": MarkupFoldingStrategy.UpdateFoldings(ctx.Folding, doc, html: true); break;
                case "xml":  MarkupFoldingStrategy.UpdateFoldings(ctx.Folding, doc, html: false); break;
                default:     JsonFoldingStrategy.UpdateFoldings(ctx.Folding, doc); break;
            }
        }
        catch { /* malformed mid-edit content; try again on next tick */ }
    }

    // =====================================================================
    //  Live preview (Markdown + HTML)
    // =====================================================================

    private static bool HasOutlineCapability(string ext) => ext switch
    {
        ".md" or ".markdown" or ".json" or ".jsonc" or ".xml" or ".xaml" or ".csproj"
        or ".config" or ".html" or ".htm" or ".xhtml" or ".cshtml" or ".vue" => true,
        _ => false
    };

    private struct EditorLayoutResult
    {
        public FrameworkElement Container;
        public Grid Grid;
        public ColumnDefinition PreviewCol;
        public ToggleButton PreviewToggle;
        public ColumnDefinition OutlineCol;
        public ToggleButton OutlineToggle;
        public TreeView OutlineTree;
        public GridSplitter OutlineSplitter;
        public GridSplitter PreviewSplitter;
        public Border BarBorder;
        public MinimapControl? Minimap;
        public Border FileChangedBar;
        public Button ReloadButton;
        public Button DismissButton;


    }

    private EditorLayoutResult BuildEditorLayout(FrameworkElement content)
    {
        FrameworkElement finalContent = content;
        MinimapControl? minimap = null;

        if (content is TextEditor editor)
        {
            var editorGrid = new Grid();
            editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Minimap

            minimap = new MinimapControl(editor);
            minimap.Visibility = SettingsService.Instance.Settings.ShowMinimap ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(editor, 0);
            Grid.SetColumn(minimap, 1);
            editorGrid.Children.Add(editor);
            editorGrid.Children.Add(minimap);

            finalContent = editorGrid;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) }); // Column 0: Outline
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });    // Column 1: Splitter
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Column 2: Editor
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });    // Column 3: Splitter
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) }); // Column 4: Preview

        Grid.SetColumn(finalContent, 2);
        grid.Children.Add(finalContent);

        var outlineSplitter = new GridSplitter
        {
            Width = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.CurrentAndNext,
            ResizeDirection = GridResizeDirection.Columns,
            Visibility = Visibility.Collapsed
        };
        outlineSplitter.SetResourceReference(GridSplitter.BackgroundProperty, "App.Border");
        Grid.SetColumn(outlineSplitter, 1);
        grid.Children.Add(outlineSplitter);

        var outlinePanel = new Grid { Visibility = Visibility.Collapsed };
        outlinePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outlinePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            Margin = new Thickness(4, 2, 4, 4),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var btnStyle = new Style(typeof(Button));
        var btnTemplate = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Bd";
        borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        borderFactory.SetValue(Border.PaddingProperty, new System.Windows.TemplateBindingExtension(Button.PaddingProperty));
        
        var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(presenterFactory);
        btnTemplate.VisualTree = borderFactory;
        
        var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, System.Windows.Application.Current.FindResource("Btn.BackgroundHover"), "Bd"));
        btnTemplate.Triggers.Add(hoverTrigger);
        
        btnStyle.Setters.Add(new Setter(Button.TemplateProperty, btnTemplate));
        btnStyle.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        btnStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
        btnStyle.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));

        var expandButton = new Button
        {
            Content = "Expand All",
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(8, 3, 8, 3),
            Style = btnStyle,
            FontSize = 11.0
        };
        expandButton.SetResourceReference(Button.ForegroundProperty, "App.Foreground");

        var collapseButton = new Button
        {
            Content = "Collapse All",
            Padding = new Thickness(8, 3, 8, 3),
            Style = btnStyle,
            FontSize = 11.0
        };
        collapseButton.SetResourceReference(Button.ForegroundProperty, "App.Foreground");

        toolbar.Children.Add(expandButton);
        toolbar.Children.Add(collapseButton);
        Grid.SetRow(toolbar, 0);
        outlinePanel.Children.Add(toolbar);

        var outlineTree = new TreeView
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        outlineTree.SetResourceReference(TreeView.ForegroundProperty, "App.Foreground");
        
        var template = new HierarchicalDataTemplate(typeof(OutlineNode));
        template.ItemsSource = new System.Windows.Data.Binding("Children");
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Label"));
        factory.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 2));
        template.VisualTree = factory;
        outlineTree.ItemTemplate = template;
        
        var itemStyle = new Style(typeof(TreeViewItem));
        var foreBinding = new System.Windows.Data.Binding { Source = outlineTree, Path = new PropertyPath("Foreground") };
        itemStyle.Setters.Add(new Setter(TreeViewItem.ForegroundProperty, foreBinding));
        
        var expandedBinding = new System.Windows.Data.Binding("IsExpanded") { Mode = System.Windows.Data.BindingMode.TwoWay };
        itemStyle.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, expandedBinding));
        
        outlineTree.Resources.Add(typeof(TreeViewItem), itemStyle);

        expandButton.Click += (s, e) =>
        {
            var nodes = outlineTree.ItemsSource as System.Collections.IEnumerable;
            SetExpansionAll(nodes, true);
        };
        collapseButton.Click += (s, e) =>
        {
            var nodes = outlineTree.ItemsSource as System.Collections.IEnumerable;
            SetExpansionAll(nodes, false);
        };

        Grid.SetRow(outlineTree, 1);
        outlinePanel.Children.Add(outlineTree);

        Grid.SetColumn(outlinePanel, 0);
        grid.Children.Add(outlinePanel);

        var previewSplitter = new GridSplitter
        {
            Width = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = GridResizeDirection.Columns,
            Visibility = Visibility.Collapsed
        };
        previewSplitter.SetResourceReference(GridSplitter.BackgroundProperty, "App.Border");
        Grid.SetColumn(previewSplitter, 3);
        grid.Children.Add(previewSplitter);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var outlineToggle = new ToggleButton
        {
            Content = "Outline",
            Padding = new Thickness(12, 2, 12, 2),
            Cursor = Cursors.Hand,
            ToolTip = "Show/hide document outline",
            Margin = new Thickness(0, 0, 6, 0)
        };
        bar.Children.Add(outlineToggle);

        var previewToggle = new ToggleButton
        {
            Content = "Preview",
            Padding = new Thickness(12, 2, 12, 2),
            Cursor = Cursors.Hand,
            ToolTip = "Show/hide preview (Ctrl+Shift+V)"
        };
        bar.Children.Add(previewToggle);

        var barBorder = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 3, 6, 3),
            Child = bar,
            Visibility = Visibility.Collapsed
        };
        barBorder.SetResourceReference(Border.BackgroundProperty, "App.ChromeBackground");
        barBorder.SetResourceReference(Border.BorderBrushProperty, "App.Border");
        DockPanel.SetDock(barBorder, Dock.Top);

        var fileChangedBar = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 6, 12, 6),
            Visibility = Visibility.Collapsed
        };
        fileChangedBar.SetResourceReference(Border.BackgroundProperty, "Banner.Background");
        fileChangedBar.SetResourceReference(Border.BorderBrushProperty, "App.Border");
        DockPanel.SetDock(fileChangedBar, Dock.Top);

        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var barMsg = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Text = "📄 This file has been modified externally."
        };
        barMsg.SetResourceReference(TextBlock.ForegroundProperty, "Banner.Foreground");
        Grid.SetColumn(barMsg, 0);
        barGrid.Children.Add(barMsg);

        var actionPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var reloadBtn = new Button
        {
            Content = "Reload",
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        var dismissBtn = new Button
        {
            Content = "Dismiss",
            Padding = new Thickness(10, 2, 10, 2),
            Cursor = Cursors.Hand
        };
        actionPanel.Children.Add(reloadBtn);
        actionPanel.Children.Add(dismissBtn);
        Grid.SetColumn(actionPanel, 1);
        barGrid.Children.Add(actionPanel);

        fileChangedBar.Child = barGrid;

        var root = new DockPanel();
        root.Children.Add(barBorder);
        root.Children.Add(fileChangedBar);
        root.Children.Add(grid);

        return new EditorLayoutResult
        {
            Container = root,
            Grid = grid,
            PreviewCol = grid.ColumnDefinitions[4],
            PreviewToggle = previewToggle,
            OutlineCol = grid.ColumnDefinitions[0],
            OutlineToggle = outlineToggle,
            OutlineTree = outlineTree,
            OutlineSplitter = outlineSplitter,
            PreviewSplitter = previewSplitter,
            BarBorder = barBorder,
            Minimap = minimap,
            FileChangedBar = fileChangedBar,
            ReloadButton = reloadBtn,
            DismissButton = dismissBtn
        };

    }

    private void ApplyLayoutResult(TabContext ctx, EditorLayoutResult built)
    {
        ctx.Minimap = built.Minimap;
        ctx.BarBorder = built.BarBorder;
        ctx.OutlineColumn = built.OutlineCol;
        ctx.OutlineToggle = built.OutlineToggle;
        ctx.OutlineTree = built.OutlineTree;
        ctx.OutlineSplitter = built.OutlineSplitter;
        ctx.PreviewGrid = built.Grid;
        ctx.PreviewColumn = built.PreviewCol;
        ctx.PreviewToggle = built.PreviewToggle;
        ctx.PreviewSplitter = built.PreviewSplitter;
        SetupFileChangedBar(ctx, built);
    }

    /// <summary>Menu / keyboard entry point — flips the current tab's preview.</summary>
    private void TogglePreview()
    {
        var ctx = CurrentContext;
        if (ctx == null) return;
        if (ctx.PreviewKind == PreviewKind.None)
        {
            StatusText.Text = "Preview is available for Markdown (.md) and HTML files.";
            return;
        }
        bool visible = ctx.PreviewColumn != null && ctx.PreviewColumn.Width.Value > 0;
        SetPreviewVisible(ctx, !visible);
    }

    private async void SetPreviewVisible(TabContext ctx, bool show)
    {
        if (ctx.PreviewKind == PreviewKind.None || ctx.Editor == null || ctx.PreviewColumn == null || ctx.PreviewGrid == null)
        {
            if (ctx.PreviewToggle != null) ctx.PreviewToggle.IsChecked = false;
            return;
        }

        if (show)
        {
            bool justNavigated = await EnsurePreviewAsync(ctx);
            ctx.PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            if (ctx.PreviewSplitter != null) ctx.PreviewSplitter.Visibility = Visibility.Visible;

            if (!justNavigated) RenderPreview(ctx); // refresh to current text
        }
        else
        {
            ctx.PreviewColumn.Width = new GridLength(0);
            if (ctx.PreviewSplitter != null) ctx.PreviewSplitter.Visibility = Visibility.Collapsed;
        }

        if (ctx.PreviewToggle != null) ctx.PreviewToggle.IsChecked = show;
    }

    /// <summary>Creates the WebView2 on first use; returns true if it just performed the initial navigation.</summary>
    private async Task<bool> EnsurePreviewAsync(TabContext ctx)
    {
        if (ctx.Preview != null && ctx.PreviewNavigated) return false;

        if (ctx.Preview == null)
        {
            ctx.Preview = new WebView2
            {
                CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = WebViewDataDir }
            };
            Grid.SetColumn(ctx.Preview, 4); // Place in Column 4 (Preview Column)
            ctx.PreviewGrid!.Children.Add(ctx.Preview);
            await ctx.Preview.EnsureCoreWebView2Async();

            var core = ctx.Preview.CoreWebView2;

            // Serve BOTH the live document and any relative assets ourselves, from a
            // single synthetic origin. We deliberately do NOT use a virtual-host folder
            // mapping: a folder mapping resolves every path against disk first, which
            // turns the live document URL (/__live__, a path with no file on disk) into a
            // "file not found" error before WebResourceRequested can run. Handling every
            // request here keeps the document and its relative assets on one origin — so
            // relative paths and scroll-preserving sessionStorage both work — with no
            // path collisions and no temp files written to the user's folder.
            core.AddWebResourceRequestedFilter(PreviewFilter, CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => ServePreviewRequest(ctx, core, e);
        }

        ctx.LiveHtml = BuildPreviewHtml(ctx);
        ctx.Preview.CoreWebView2.Navigate(PreviewLiveUrl);
        ctx.PreviewNavigated = true;
        return true;
    }

    private static void RenderPreview(TabContext ctx)
    {
        if (ctx.Preview?.CoreWebView2 == null || !ctx.PreviewNavigated) return;
        ctx.LiveHtml = BuildPreviewHtml(ctx);
        ctx.Preview.CoreWebView2.Reload(); // re-requests the live URL -> fresh content, stable origin
    }

    /// <summary>
    /// Serves preview requests for the synthetic origin:
    ///  - /__live__              -> the current editor content (the document itself)
    ///  - any other path         -> a file of that name read from the document's folder
    ///                              (so relative images/CSS/JS resolve), with traversal
    ///                              outside the folder rejected.
    /// </summary>
    private static void ServePreviewRequest(TabContext ctx, CoreWebView2 core,
                                            CoreWebView2WebResourceRequestedEventArgs e)
    {
        Uri uri;
        try { uri = new Uri(e.Request.Uri); }
        catch { return; }

        string path = Uri.UnescapeDataString(uri.AbsolutePath);

        // The live document.
        if (path is "/__live__" or "/")
        {
            var html = Encoding.UTF8.GetBytes(ctx.LiveHtml ?? "");
            e.Response = core.Environment.CreateWebResourceResponse(
                new MemoryStream(html), 200, "OK", "Content-Type: text/html; charset=utf-8");
            return;
        }

        // A relative asset, resolved against the document's folder.
        string? dir = ctx.FilePath != null ? Path.GetDirectoryName(ctx.FilePath) : null;
        if (!string.IsNullOrEmpty(dir) && TryResolveInside(dir, path, out string full))
        {
            try
            {
                byte[] bytes;
                using (var fs = FileService.OpenSharedRead(full))
                using (var ms = new MemoryStream())
                { fs.CopyTo(ms); bytes = ms.ToArray(); }

                e.Response = core.Environment.CreateWebResourceResponse(
                    new MemoryStream(bytes), 200, "OK", $"Content-Type: {GuessContentType(full)}");
                return;
            }
            catch { /* fall through to 404 */ }
        }

        e.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(), 404, "Not Found", "Content-Type: text/plain");
    }

    /// <summary>Maps a request path to a real file under <paramref name="dir"/>, rejecting traversal.</summary>
    private static bool TryResolveInside(string dir, string requestPath, out string full)
    {
        full = "";
        string rel = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        if (rel.Length == 0) return false;
        try
        {
            string candidate = Path.GetFullPath(Path.Combine(dir, rel));
            string root = Path.GetFullPath(dir);
            if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;
            if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
            {
                full = candidate;
                return true;
            }
        }
        catch { /* malformed path */ }
        return false;
    }

    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".txt" or ".md" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };

    private static string BuildPreviewHtml(TabContext ctx)
    {
        string text = ctx.Editor?.Text ?? "";

        if (ctx.PreviewKind == PreviewKind.Html)
            return InjectScrollScript(text); // render the user's own document as-is

        // Markdown -> themed HTML.
        string body = Markdown.ToHtml(text, MdPipeline);
        bool dark = ThemeManager.Current == AppTheme.Dark;
        string doc = $$"""
            <!doctype html><html><head><meta charset="utf-8">
            <style>
              body { font-family: 'Segoe UI', sans-serif; max-width: 860px; margin: 0 auto;
                     padding: 24px; line-height: 1.55;
                     background: {{(dark ? "#1e1e1e" : "#ffffff")}};
                     color: {{(dark ? "#dcdcdc" : "#1e1e1e")}}; }
              pre  { background: {{(dark ? "#2d2d30" : "#f5f5f5")}}; padding: 12px;
                     border-radius: 6px; overflow-x: auto; }
              code { font-family: Consolas, monospace; }
              a    { color: {{(dark ? "#4ea1f3" : "#0a66c2")}}; }
              table { border-collapse: collapse; }
              th, td { border: 1px solid {{(dark ? "#3f3f46" : "#d4d4d4")}}; padding: 6px 10px; }
              blockquote { border-left: 4px solid {{(dark ? "#3f3f46" : "#d4d4d4")}};
                           margin-left: 0; padding-left: 14px; opacity: .85; }
              img { max-width: 100%; }
            </style>
            <script>
              window.MathJax = {
                tex: {
                  inlineMath: [['$', '$'], ['\\(', '\\)']],
                  displayMath: [['$$', '$$'], ['\\[', '\\]']]
                }
              };
            </script>
            <script id="MathJax-script" async src="https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-mml-chtml.js"></script>
            <script src="https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js"></script>
            <script>
              document.addEventListener("DOMContentLoaded", function() {
                // Post-process Markdig output for Mermaid
                const blocks = document.querySelectorAll("pre code.language-mermaid");
                blocks.forEach(block => {
                  const pre = block.parentElement;
                  const div = document.createElement("div");
                  div.className = "mermaid";
                  div.textContent = block.textContent;
                  pre.replaceWith(div);
                });
                
                try {
                  mermaid.initialize({
                    startOnLoad: false,
                    theme: '{{(dark ? "dark" : "default")}}'
                  });
                  mermaid.run();
                } catch (e) {
                  console.error("Mermaid initialization failed: ", e);
                }
              });
            </script>
            </head><body>{{body}}</body></html>
            """;
        return InjectScrollScript(doc);
    }

    private static string InjectScrollScript(string html)
    {
        int idx = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? html.Insert(idx, ScrollScript) : html + ScrollScript;
    }

    // =====================================================================
    //  PDF viewing (WebView2 / Edge built-in PDF renderer)
    // =====================================================================

    private async void OpenPdfTab(string path)
    {
        var web = new WebView2
        {
            CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = WebViewDataDir }
        };
        var ctx = CreateTab(Path.GetFileName(path), web);
        ctx.FilePath = path;
        ctx.IsReadOnlyView = true;
        await web.EnsureCoreWebView2Async();
        web.Source = new Uri(path);
    }

    private void OpenImageTab(string path)
    {
        var view = new Controls.ImageViewerControl();
        var ctx = CreateTab(Path.GetFileName(path), view);
        ctx.FilePath = path;
        ctx.IsReadOnlyView = true;
        ctx.ImageViewer = view;

        view.ZoomChanged += (s, e) => {
            if (CurrentContext?.ImageViewer == view)
                UpdateStatusBar();
        };

        view.IsDirtyChanged += (s, e) => {
            if (CurrentContext?.ImageViewer == view)
            {
                ctx.IsDirty = view.IsDirty;
                ctx.UpdateHeader();
            }
        };

        view.Open(path);
        UpdateEditorLayoutCapabilities(ctx);
    }

    // =====================================================================
    //  Commands
    // =====================================================================

    private void New_Executed(object s, ExecutedRoutedEventArgs e) => NewTab();

    private void Open_Executed(object s, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "All files (*.*)|*.*|Text files (*.txt;*.log;*.md)|*.txt;*.log;*.md|PDF files (*.pdf)|*.pdf"
        };
        if (dlg.ShowDialog(this) == true)
            foreach (var f in dlg.FileNames) OpenFile(f);
    }

    private void Save_CanExecute(object s, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = CurrentContext != null && 
                      ((!CurrentContext.IsReadOnlyView && CurrentContext.Editor != null) || 
                       CurrentContext.LargeView != null ||
                       (CurrentContext.ImageViewer != null && CurrentContext.IsDirty));

    private void Print_CanExecute(object s, CanExecuteRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx == null) { e.CanExecute = false; return; }
        if (ctx.LargeView != null) { e.CanExecute = false; return; }
        if (ctx.Editor == null && ctx.Preview == null) { e.CanExecute = false; return; }
        e.CanExecute = true;
    }

    private void Save_Executed(object s, ExecutedRoutedEventArgs e)
    {
        if (CurrentContext != null) SaveTab(CurrentContext);
    }

    private void SaveAs_Executed(object s, ExecutedRoutedEventArgs e)
    {
        if (CurrentContext != null) SaveTabAs(CurrentContext);
    }

    private void CloseTab_Executed(object s, ExecutedRoutedEventArgs e)
    {
        if (CurrentContext != null) CloseTab(CurrentContext);
    }

    private void Print_Executed(object s, ExecutedRoutedEventArgs e)
    {
        if (CurrentContext == null) return;
        var ctx = CurrentContext;

        if (ctx.FilePath != null && Path.GetExtension(ctx.FilePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            PrintPdf(ctx);
        }
        else if (ctx.Editor != null)
        {
            PrintTextDocument(ctx);
        }
        else if (ctx.Preview != null && ctx.PreviewKind == PreviewKind.Markdown)
        {
            PrintMarkdownPreview(ctx);
        }
    }

    private void PrintTextDocument(TabContext ctx)
    {
        if (ctx.Editor == null) return;
        var doc = PrintService.ConvertTextEditorToFlowDocument(ctx.Editor, includeLineNumbers: true);
        PrintService.PrintFlowDocument(doc, ctx.DisplayName);
    }

    private void PrintMarkdownPreview(TabContext ctx)
    {
        if (ctx.Preview == null || ctx.LiveHtml == null) return;
        var doc = PrintService.ConvertHtmlToFlowDocument(ctx.LiveHtml);
        PrintService.PrintFlowDocument(doc, ctx.DisplayName);
    }

    private async void PrintPdf(TabContext ctx)
    {
        if (ctx.Preview?.CoreWebView2 == null) return;
        try
        {
            await ctx.Preview.CoreWebView2.PrintAsync(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Print failed:\n{ex.Message}", "HyperNote", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool SaveTab(TabContext ctx)
    {
        try
        {
            ctx.FileWatcher?.Suspend();

            if (ctx.LargeView != null)
            {
                if (ctx.FilePath == null) return SaveTabAs(ctx);
                try
                {
                    ctx.LargeView.Save(ctx.FilePath);
                    ctx.IsDirty = false;
                    ctx.UpdateHeader();
                    StatusText.Text = $"Saved {ctx.FilePath}";
                    if (!string.IsNullOrEmpty(ctx.BackupPath) && File.Exists(ctx.BackupPath))
                    {
                        try { File.Delete(ctx.BackupPath); } catch { }
                        ctx.BackupPath = null;
                    }
                    SaveSessionState();
                    UpdateStatusBar();
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "HyperNote", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            if (ctx.ImageViewer != null)
            {
                if (!ctx.ImageViewer.IsDirty) return true;
                if (ctx.FilePath == null) return SaveTabAs(ctx);

                var dialog = new Controls.SaveOptionDialog(Path.GetFileName(ctx.FilePath)) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    if (dialog.Result == Controls.SaveOptionResult.Overwrite)
                    {
                        try
                        {
                            ctx.ImageViewer.Save(ctx.FilePath);
                            ctx.IsDirty = false;
                            ctx.UpdateHeader();
                            StatusText.Text = $"Saved {ctx.FilePath}";
                            SaveSessionState();
                            UpdateStatusBar();
                            return true;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, ex.Message, "HyperNote", MessageBoxButton.OK, MessageBoxImage.Error);
                            return false;
                        }
                    }
                    else if (dialog.Result == Controls.SaveOptionResult.SaveCopy)
                    {
                        return SaveTabAs(ctx);
                    }
                }
                return false;
            }

            if (ctx.Editor == null || ctx.IsReadOnlyView) return true;
            if (ctx.FilePath == null) return SaveTabAs(ctx);
            try
            {
                FileService.WriteAllTextSafe(ctx.FilePath, ctx.Editor.Text, ctx.Encoding, SettingsService.Instance.Settings.DefaultLineEnding);
                ctx.IsDirty = false;
                ctx.UpdateHeader();
                StatusText.Text = $"Saved {ctx.FilePath}";
                
                if (!string.IsNullOrEmpty(ctx.BackupPath) && File.Exists(ctx.BackupPath))
                {
                    try { File.Delete(ctx.BackupPath); } catch { }
                    ctx.BackupPath = null;
                }
                SaveSessionState();

                UpdateStatusBar();
                return true;
            }
            catch (IOException)
            {
                if (MessageBox.Show(this,
                    "The file is locked for writing by another application.\nSave a copy instead?",
                    "HyperNote", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    return SaveTabAs(ctx);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "HyperNote", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        finally
        {
            ctx.FileWatcher?.Resume();
        }
    }


    private bool SaveTabAs(TabContext ctx)
    {
        string filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*";
        if (ctx.ImageViewer != null)
        {
            filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg|GIF Image (*.gif)|*.gif|Bitmap Image (*.bmp)|*.bmp|TIFF Image (*.tiff;*.tif)|*.tiff;*.tif|All files (*.*)|*.*";
        }

        var dlg = new SaveFileDialog
        {
            FileName = ctx.DisplayName,
            Filter = filter
        };
        if (dlg.ShowDialog(this) != true) return false;

        if (ctx.ImageViewer != null)
        {
            try
            {
                ctx.ImageViewer.Save(dlg.FileName);
                ctx.FilePath = dlg.FileName;
                StartFileWatcher(ctx);
                ctx.IsDirty = false;

                ctx.UpdateHeader();
                StatusText.Text = $"Saved {dlg.FileName}";
                
                SettingsService.Instance.AddRecentFile(dlg.FileName);
                RebuildRecentMenu();
                UpdateStatusBar();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "HyperNote", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        ctx.FilePath = dlg.FileName;
        StartFileWatcher(ctx);
        if (ctx.BookmarkMargin == null && ctx.Editor != null)
        {
            ctx.BookmarkMargin = new Controls.BookmarkMargin(ctx.FilePath);
            ctx.Editor.TextArea.LeftMargins.Insert(0, ctx.BookmarkMargin);
        }
        var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();


        if (ctx.Editor != null)
        {
            ctx.Editor.SyntaxHighlighting = SyntaxMapper.ForExtension(ext);
        }
        else if (ctx.LargeView != null)
        {
            ctx.LargeView.TextEditorControl.SyntaxHighlighting = SyntaxMapper.ForExtension(ext);
        }
        bool ok = SaveTab(ctx);
        if (ok)
        {
            SettingsService.Instance.AddRecentFile(dlg.FileName);
            RebuildRecentMenu();
            UpdateEditorLayoutCapabilities(ctx);
        }
        return ok;
    }

    // =====================================================================
    //  Recent files, theme, misc UI
    // =====================================================================

    private void RebuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        var recent = SettingsService.Instance.Settings.RecentFiles;
        if (recent.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }
        foreach (var path in recent)
        {
            var item = new MenuItem { Header = path };
            item.Click += (_, _) => OpenFile(path);
            RecentMenu.Items.Add(item);
        }
    }

    private void ToggleTheme_Click(object s, RoutedEventArgs e) => ThemeManager.Toggle();

    private void OnThemeChanged(AppTheme theme)
    {
        DarkThemeMenu.IsChecked = theme == AppTheme.Dark;
        foreach (TabItem t in Tabs.Items)
        {
            if (t.Tag is not TabContext ctx) continue;
            if (ctx.Editor != null) ApplyEditorTheme(ctx.Editor);
            // Re-theme an open Markdown preview (HTML previews keep their own styling).
            if (ctx.PreviewKind == PreviewKind.Markdown &&
                ctx.PreviewColumn?.Width.Value > 0)
                RenderPreview(ctx);
        }
    }

    private DispatcherTimer? _autoSaveTimer;

    private void ApplyAutoSaveSettings()
    {
        if (_autoSaveTimer != null)
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer = null;
        }

        var s = SettingsService.Instance.Settings;
        if (s.AutoSave && s.AutoSaveIntervalSeconds > 0)
        {
            _autoSaveTimer = new DispatcherTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(s.AutoSaveIntervalSeconds);
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();
        }
    }

    private void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        foreach (TabItem t in Tabs.Items)
        {
            if (t.Tag is TabContext ctx && ctx.IsDirty && ctx.FilePath != null && !ctx.IsReadOnlyView && ctx.Editor != null)
            {
                try
                {
                    FileService.WriteAllTextSafe(ctx.FilePath, ctx.Editor.Text, ctx.Encoding, SettingsService.Instance.Settings.DefaultLineEnding);
                    ctx.IsDirty = false;
                    ctx.UpdateHeader();
                    if (!string.IsNullOrEmpty(ctx.BackupPath) && File.Exists(ctx.BackupPath))
                    {
                        try { File.Delete(ctx.BackupPath); } catch { }
                        ctx.BackupPath = null;
                    }
                }
                catch
                {
                    // Ignore background auto-save errors to prevent disrupting user.
                }
            }
        }
        SaveSessionState();
        UpdateStatusBar();
    }

    private static Encoding ParseEncoding(string name)
    {
        return name switch
        {
            "UTF-8" => new UTF8Encoding(false),
            "UTF-8 BOM" => new UTF8Encoding(true),
            "UTF-16 LE" => new UnicodeEncoding(false, true),
            "UTF-16 BE" => new UnicodeEncoding(true, true),
            "ASCII" => Encoding.ASCII,
            "Windows-1252" => Encoding.GetEncoding(1252),
            _ => new UTF8Encoding(false)
        };
    }

    /// <summary>Applies current AppSettings font/wrap/tab options to every open editor.</summary>
    private void ApplySettingsToAllEditors()
    {
        var s = SettingsService.Instance.Settings;
        var family = new FontFamily(s.EditorFontFamily);
        string newLineStr = s.DefaultLineEnding switch
        {
            "LF" => "\n",
            "CR" => "\r",
            _ => "\r\n"
        };
        foreach (TabItem t in Tabs.Items)
        {
            if (t.Tag is not TabContext ctx) continue;
            if (ctx.Editor != null)
            {
                ctx.Editor.FontFamily  = family;
                ctx.Editor.FontSize    = s.EditorFontSize;
                ctx.Editor.WordWrap    = s.WordWrap;
                ctx.Editor.HorizontalScrollBarVisibility = s.WordWrap
                    ? ScrollBarVisibility.Disabled
                    : ScrollBarVisibility.Auto;
                ctx.Editor.Options.ConvertTabsToSpaces = s.ConvertTabsToSpaces;
                ctx.Editor.Options.IndentationSize     = s.IndentWidth;
                ctx.Editor.ShowLineNumbers             = s.ShowLineNumbers;
                ctx.Editor.Options.ShowSpaces          = s.ShowWhitespace;
                ctx.Editor.Options.ShowTabs            = s.ShowWhitespace;
                ctx.Editor.Options.ShowEndOfLine       = s.ShowWhitespace;
                ctx.Editor.Options.HighlightCurrentLine = s.HighlightCurrentLine;
            }


            if (ctx.Minimap != null)
                ctx.Minimap.Visibility = s.ShowMinimap ? Visibility.Visible : Visibility.Collapsed;
            if (ctx.LargeView != null)
            {
                ctx.LargeView.ApplySettings();
            }
        }

        // Apply settings to terminal
        Terminal.ApplySettings();

        // Refresh/restart auto-save background timer
        ApplyAutoSaveSettings();
    }

    private static void ApplyEditorTheme(TextEditor editor)
    {
        editor.Background = (Brush)Application.Current.FindResource("Editor.Background");
        editor.Foreground = (Brush)Application.Current.FindResource("Editor.Foreground");
        editor.LineNumbersForeground = (Brush)Application.Current.FindResource("Editor.LineNumbers");
    }

    private void TogglePreview_Click(object s, RoutedEventArgs e) => TogglePreview();

    private void CompareFiles_Click(object sender, RoutedEventArgs e)
    {
        var control = new HyperNote.Controls.DiffViewControl(this);
        var ctx = CreateTab("File Compare", control);
        ctx.IsReadOnlyView = true;
    }

    private void ComputeHash_Click(object sender, RoutedEventArgs e)
    {
        var ctx = CurrentContext;
        string? activeFilePath = ctx?.FilePath;
        string? editorText = ctx?.Editor?.Text;
        string? selectedText = ctx?.Editor?.SelectedText;

        var dialog = new HyperNote.Controls.HashCalculatorDialog(activeFilePath, editorText, selectedText) { Owner = this };
        dialog.ShowDialog();
    }

    private void Tabs_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx?.FilePath != null) StatusText.Text = ctx.FilePath;
        UpdateOutlineForActiveTab();
        UpdateStatusBar();

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is TabItem selectedTab && selectedTab.Content is UIElement element)
        {
            var anim = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromSeconds(0.15)));
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }

    private void Window_Drop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            foreach (var f in files.Where(File.Exists)) OpenFile(f);
    }

    private void Window_Closing(object? s, System.ComponentModel.CancelEventArgs e)
    {
        var settings = SettingsService.Instance.Settings;
        if (!settings.RememberOpenFiles)
        {
            foreach (TabItem t in Tabs.Items.Cast<TabItem>().ToList())
            {
                if (t.Tag is TabContext { IsDirty: true } ctx && !ConfirmDiscard(ctx))
                {
                    e.Cancel = true;
                    return;
                }
            }
            DeleteSessionAndBackups();
        }
        else
        {
            SaveSessionState();
        }

        foreach (TabItem t in Tabs.Items)
            if (t.Tag is TabContext c) { c.LargeView?.Dispose(); c.Preview?.Dispose(); }

        Terminal.Dispose();

        if (_findDialog != null) { _findDialog.AllowClose = true; _findDialog.Close(); }
    }

    // =====================================================================
    //  Find / Replace
    // =====================================================================

    private FindReplaceDialog? _findDialog;

    private FindReplaceDialog FindDialog()
    {
        if (_findDialog == null)
            _findDialog = new FindReplaceDialog(() => CurrentContext?.Editor ?? CurrentContext?.LargeView?.TextEditorControl, () => CurrentContext?.LargeView) { Owner = this };
        return _findDialog;
    }

    private void Find_CanExecute(object s, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = CurrentContext != null && (CurrentContext.Editor != null || CurrentContext.LargeView != null);

    private void Find_Executed(object s, ExecutedRoutedEventArgs e) => FindDialog().ShowFor(replace: false);
    private void Replace_Executed(object s, ExecutedRoutedEventArgs e) => FindDialog().ShowFor(replace: true);
    private void FindNext_Executed(object s, ExecutedRoutedEventArgs e) => FindDialog().FindNextExternal(backward: false);
    private void FindPrevious_Executed(object s, ExecutedRoutedEventArgs e) => FindDialog().FindNextExternal(backward: true);

    private void FormatDocument_CanExecute(object s, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = CurrentContext is { IsReadOnlyView: false, Editor: not null };

    private void FormatDocument_Executed(object s, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx?.Editor == null) return;
        try
        {
            if (EditingServices.Format(ctx.Editor, ctx.FilePath))
            {
                StatusText.Text = "Document formatted.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Format Document", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TransformText_CanExecute(object s, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CurrentContext is { IsReadOnlyView: false, Editor: not null };
    }

    private void TransformText_Executed(object s, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx?.Editor == null || e.Parameter is not string operation) return;

        var editor = ctx.Editor;
        var doc = editor.Document;

        // Check if line-based operations need selection expansion
        bool isLineBased = operation is "SortAsc" or "SortDesc" or "SortAscIgnoreCase" or "SortDescIgnoreCase" or "RemoveDuplicates" or "TrimLeading" or "TrimTrailing" or "TrimBoth";

        int startOffset = editor.SelectionStart;
        int length = editor.SelectionLength;

        int replaceStart, replaceLength;
        string originalText;

        if (length > 0)
        {
            if (isLineBased)
            {
                // Expand selection to cover full lines
                var startLine = doc.GetLineByOffset(startOffset);
                var endLine = doc.GetLineByOffset(startOffset + length);
                replaceStart = startLine.Offset;
                replaceLength = endLine.EndOffset - startLine.Offset;
            }
            else
            {
                replaceStart = startOffset;
                replaceLength = length;
            }
            originalText = doc.GetText(replaceStart, replaceLength);
        }
        else
        {
            // No selection: apply to entire document
            replaceStart = 0;
            replaceLength = doc.TextLength;
            originalText = doc.Text;
        }

        if (string.IsNullOrEmpty(originalText)) return;

        try
        {
            string transformedText = operation switch
            {
                "Uppercase" => TextUtilities.ToUppercase(originalText),
                "Lowercase" => TextUtilities.ToLowercase(originalText),
                "TitleCase" => TextUtilities.ToTitleCase(originalText),
                "SentenceCase" => TextUtilities.ToSentenceCase(originalText),
                "CamelCase" => TextUtilities.ToCamelCase(originalText),
                "SnakeCase" => TextUtilities.ToSnakeCase(originalText),
                "SortAsc" => TextUtilities.SortLines(originalText, desc: false, ignoreCase: false),
                "SortDesc" => TextUtilities.SortLines(originalText, desc: true, ignoreCase: false),
                "SortAscIgnoreCase" => TextUtilities.SortLines(originalText, desc: false, ignoreCase: true),
                "SortDescIgnoreCase" => TextUtilities.SortLines(originalText, desc: true, ignoreCase: true),
                "RemoveDuplicates" => TextUtilities.RemoveDuplicateLines(originalText),
                "TrimLeading" => TextUtilities.TrimLeading(originalText),
                "TrimTrailing" => TextUtilities.TrimTrailing(originalText),
                "TrimBoth" => TextUtilities.TrimBoth(originalText),
                "UrlEncode" => TextUtilities.UrlEncode(originalText),
                "UrlDecode" => TextUtilities.UrlDecode(originalText),
                "Base64Encode" => TextUtilities.Base64Encode(originalText),
                "Base64Decode" => TextUtilities.Base64Decode(originalText),
                "HtmlEncode" => TextUtilities.HtmlEncode(originalText),
                "HtmlDecode" => TextUtilities.HtmlDecode(originalText),
                "MinifyJsonXml" => MinifyJsonOrXml(originalText, ctx.FilePath),
                _ => throw new NotSupportedException($"Unknown operation: {operation}")
            };

            doc.Replace(replaceStart, replaceLength, transformedText);
            StatusText.Text = $"Transformed: {operation}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Error executing '{operation}':\n{ex.Message}", "Transform Text", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string MinifyJsonOrXml(string text, string? filePath)
    {
        string ext = string.IsNullOrEmpty(filePath) ? "" : Path.GetExtension(filePath).ToLowerInvariant();
        string? foldKind = SyntaxMapper.FoldKindForExtension(ext);

        if (ext == ".json" || ext == ".jsonc" || (string.IsNullOrEmpty(ext) && text.TrimStart().StartsWith("{")))
        {
            return TextUtilities.MinifyJson(text);
        }
        else if (foldKind == "xml" || ext == ".xml" || ext == ".xaml" || ext == ".csproj" || ext == ".config" || (string.IsNullOrEmpty(ext) && text.TrimStart().StartsWith("<")))
        {
            return TextUtilities.MinifyXml(text);
        }
        throw new NotSupportedException("Minification is only supported for JSON and XML/XAML content.");
    }

    private void ToggleTerminal_Executed(object s, ExecutedRoutedEventArgs e)
    {
        ToggleTerminal();
    }

    private void ToggleTerminal()
    {
        bool isVisible = Terminal.Visibility == Visibility.Visible;
        if (isVisible)
        {
            Terminal.Visibility = Visibility.Collapsed;
            TerminalSplitter.Visibility = Visibility.Collapsed;
            TerminalRow.Height = new GridLength(0);
        }
        else
        {
            Terminal.Visibility = Visibility.Visible;
            TerminalSplitter.Visibility = Visibility.Visible;
            TerminalRow.Height = new GridLength(180);
            Terminal.FocusInput();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (dlg.ShowDialog(this) == true)
        {
            OpenWorkspace(dlg.FolderName);
        }
    }

    private void OpenWorkspace(string folder)
    {
        try
        {
            _currentWorkspaceFolder = folder;
            ExplorerTree.ItemsSource = WorkspaceService.LoadWorkspace(folder);
            
            // Ensure sidebar is visible
            if (SidebarPanel.Visibility != Visibility.Visible)
            {
                ToggleSidebar();
            }
            
            StatusText.Text = $"Opened folder: {folder}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Error displaying folder:\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Open Workspace Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExplorerTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ExplorerTree.SelectedItem is WorkspaceNode node && !node.IsDirectory)
        {
            OpenFile(node.FullPath);
        }
    }

    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void SetOutlineVisible(TabContext ctx, bool show)
    {
        if (ctx.OutlineTree == null || ctx.OutlineColumn == null || ctx.OutlineSplitter == null)
        {
            if (ctx.OutlineToggle != null) ctx.OutlineToggle.IsChecked = false;
            return;
        }

        var panel = ctx.OutlineTree.Parent as FrameworkElement;
        if (panel == null) return;

        if (show)
        {
            ctx.OutlineColumn.Width = new GridLength(200);
            panel.Visibility = Visibility.Visible;
            ctx.OutlineSplitter.Visibility = Visibility.Visible;
            UpdateOutlineForActiveTab();
        }
        else
        {
            ctx.OutlineColumn.Width = new GridLength(0);
            panel.Visibility = Visibility.Collapsed;
            ctx.OutlineSplitter.Visibility = Visibility.Collapsed;
        }

        if (ctx.OutlineToggle != null) ctx.OutlineToggle.IsChecked = show;
    }

    private void SetExpansionAll(System.Collections.IEnumerable? nodes, bool expand)
    {
        if (nodes == null) return;
        foreach (var item in nodes)
        {
            if (item is OutlineNode node)
            {
                node.IsExpanded = expand;
                SetExpansionAll(node.Children, expand);
            }
        }
    }

    private void UpdateEditorLayoutCapabilities(TabContext ctx)
    {
        if (ctx.Editor == null) return;

        string ext = string.IsNullOrEmpty(ctx.FilePath) ? "" : Path.GetExtension(ctx.FilePath).ToLowerInvariant();

        // Map manually overridden syntax highlighting back to a representative extension
        string? activeSyntax = ctx.Editor.SyntaxHighlighting?.Name;
        if (!string.IsNullOrEmpty(activeSyntax))
        {
            if (activeSyntax.Equals("XML", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".xml";
            }
            else if (activeSyntax.Equals("HTML", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".html";
            }
            else if (activeSyntax.Equals("JSON", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".json";
            }
            else if (activeSyntax.Equals("MarkDown", StringComparison.OrdinalIgnoreCase) || 
                     activeSyntax.Equals("Markdown", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".md";
            }
        }

        ctx.PreviewKind = PreviewKindForExtension(ext);
        bool hasOutline = HasOutlineCapability(ext);

        // Update folding manager dynamically if needed
        var newFoldKind = SyntaxMapper.FoldKindForExtension(ext);
        if (newFoldKind != ctx.FoldKind)
        {
            if (ctx.Folding != null)
            {
                FoldingManager.Uninstall(ctx.Folding);
                ctx.Folding = null;
                ctx.FoldTimer = null;
            }

            ctx.FoldKind = newFoldKind;

            if (ctx.FoldKind != null)
            {
                ctx.Folding = FoldingManager.Install(ctx.Editor.TextArea);
                UpdateFoldings(ctx);
                ctx.FoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                ctx.FoldTimer.Tick += (_, _) => { ctx.FoldTimer!.Stop(); UpdateFoldings(ctx); };
            }
        }

        if (ctx.OutlineToggle != null)
        {
            ctx.OutlineToggle.Visibility = hasOutline ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ctx.PreviewToggle != null)
        {
            ctx.PreviewToggle.Visibility = ctx.PreviewKind != PreviewKind.None ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ctx.BarBorder != null)
        {
            ctx.BarBorder.Visibility = (hasOutline || ctx.PreviewKind != PreviewKind.None)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (!hasOutline)
        {
            SetOutlineVisible(ctx, false);
        }

        if (ctx.PreviewKind == PreviewKind.None)
        {
            SetPreviewVisible(ctx, false);
        }
    }

    private void UpdateOutlineForActiveTab()
    {
        var ctx = CurrentContext;
        if (ctx?.Editor != null)
        {
            bool outlineVisible = ctx.OutlineColumn != null && ctx.OutlineColumn.Width.Value > 0;
            
            if (outlineVisible)
            {
                var outline = OutlineService.BuildOutline(ctx.Editor.Text, ctx.FilePath);
                if (ctx.OutlineTree != null)
                {
                    ctx.OutlineTree.ItemsSource = outline;
                }
            }
        }
    }

    private void ToggleSidebar_Executed(object s, ExecutedRoutedEventArgs e)
    {
        ToggleSidebar();
    }

    private void ToggleSidebar()
    {
        bool isVisible = SidebarPanel.Visibility == Visibility.Visible;
        if (isVisible)
        {
            // Fade out, then collapse
            var sb = (Storyboard)Resources["SidebarCloseStoryboard"];
            sb.Completed += (_, _) =>
            {
                SidebarPanel.Visibility = Visibility.Collapsed;
                SidebarSplitter.Visibility = Visibility.Collapsed;
                SidebarColumn.Width = new GridLength(0);
            };
            sb.Begin();
        }
        else
        {
            SidebarPanel.Visibility = Visibility.Visible;
            SidebarSplitter.Visibility = Visibility.Visible;
            SidebarColumn.Width = new GridLength(240);
            // Fade in
            var sb = (Storyboard)Resources["SidebarOpenStoryboard"];
            sb.Begin();
        }
    }

    private void CloseSidebar_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidebar();
    }

    // Drag-to-resize for the 1px Fluent sidebar splitter Border
    private bool _splitterDragging;
    private double _splitterStartX;
    private double _splitterStartWidth;

    private void SidebarSplitter_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border border) return;
        _splitterDragging = true;
        _splitterStartX = e.GetPosition(this).X;
        _splitterStartWidth = SidebarColumn.Width.Value;
        border.CaptureMouse();
        border.MouseMove += SidebarSplitter_MouseMove;
        border.MouseLeftButtonUp += SidebarSplitter_MouseLeftButtonUp;
    }

    private void SidebarSplitter_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_splitterDragging) return;
        double delta = e.GetPosition(this).X - _splitterStartX;
        double newWidth = Math.Max(150, Math.Min(600, _splitterStartWidth + delta));
        SidebarColumn.Width = new GridLength(newWidth);
    }

    private void SidebarSplitter_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _splitterDragging = false;
        if (sender is System.Windows.Controls.Border border)
        {
            border.ReleaseMouseCapture();
            border.MouseMove -= SidebarSplitter_MouseMove;
            border.MouseLeftButtonUp -= SidebarSplitter_MouseLeftButtonUp;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
    }

    private void UpdateStatusBar()
    {
        var ctx = CurrentContext;
        if (ctx == null)
        {
            CaretStatusText.Text = "";
            FileInfoText.Text = "";
            TextStatsText.Text = "";
            LanguageSelectorText.Text = "Plain Text";
            LanguageSelectorBtn.IsEnabled = false;
            ZoomStatusText.Text = "100%";
            return;
        }

        // 1. Caret position status
        if (ctx.Editor != null)
        {
            CaretStatusText.Text = $"Ln {ctx.Editor.TextArea.Caret.Line}, Col {ctx.Editor.TextArea.Caret.Column}";
        }
        else if (ctx.LargeView != null)
        {
            int selectedLine = ctx.LargeView.CurrentLine;
            CaretStatusText.Text = selectedLine > 0 ? $"Ln {selectedLine}" : "";
        }
        else
        {
            CaretStatusText.Text = "";
        }

        // 2. File size and encoding status
        long sizeInBytes = 0;
        string encodingName = "";

        if (ctx.FilePath != null)
        {
            if (File.Exists(ctx.FilePath))
            {
                sizeInBytes = new FileInfo(ctx.FilePath).Length;
            }
            
            if (ctx.Editor != null && ctx.IsDirty)
            {
                try
                {
                    sizeInBytes = ctx.Encoding.GetByteCount(ctx.Editor.Text);
                }
                catch { }
            }

            encodingName = ctx.Encoding.WebName.ToUpperInvariant();
        }
        else if (ctx.Editor != null)
        {
            // Untitled tab
            try
            {
                sizeInBytes = ctx.Encoding.GetByteCount(ctx.Editor.Text);
            }
            catch { }
            encodingName = ctx.Encoding.WebName.ToUpperInvariant();
        }

        if (ctx.ImageViewer != null)
        {
            string sizeStr = sizeInBytes > 0 ? FormatSize(sizeInBytes) : "0 B";
            FileInfoText.Text = $"{sizeStr}   |   {ctx.ImageViewer.ImageFormat}   |   {ctx.ImageViewer.ImageWidth} × {ctx.ImageViewer.ImageHeight}";
        }
        else if (sizeInBytes > 0 || !string.IsNullOrEmpty(encodingName))
        {
            string sizeStr = sizeInBytes > 0 ? FormatSize(sizeInBytes) : "0 B";
            FileInfoText.Text = !string.IsNullOrEmpty(encodingName) 
                ? $"{sizeStr}   |   {encodingName}" 
                : sizeStr;
        }
        else
        {
            FileInfoText.Text = "";
        }

        // 3. Interactive Syntax Highlighting Language Button
        if (ctx.Editor != null)
        {
            LanguageSelectorText.Text = ctx.Editor.SyntaxHighlighting?.Name ?? "Plain Text";
            LanguageSelectorBtn.IsEnabled = true;
        }
        else
        {
            LanguageSelectorText.Text = "Plain Text";
            LanguageSelectorBtn.IsEnabled = false;
        }

        // 4. Zoom Percentage
        if (ctx.ImageViewer != null)
        {
            int zoomPercent = (int)Math.Round(ctx.ImageViewer.ZoomFactor * 100);
            ZoomStatusText.Text = $"{zoomPercent}%";
        }
        else
        {
            double defaultFontSize = SettingsService.Instance.Settings.EditorFontSize;
            double currentFontSize = defaultFontSize;
            if (ctx.Editor != null)
            {
                currentFontSize = ctx.Editor.FontSize;
            }
            else if (ctx.LargeView != null)
            {
                currentFontSize = ctx.LargeView.ViewFontSize;
            }
            int zoomPercent = (int)Math.Round((currentFontSize / defaultFontSize) * 100);
            ZoomStatusText.Text = $"{zoomPercent}%";
        }

        // 5. Text Statistics & Selection Details
        if (ctx.Editor != null)
        {
            int selLength = ctx.Editor.SelectionLength;
            if (selLength > 0)
            {
                // Cancel any pending async stats update since selection stats are active
                _statsCts?.Cancel();
                _statsDebounceTimer?.Stop();

                var editor = ctx.Editor;
                int startLine = editor.Document.GetLineByOffset(editor.SelectionStart).LineNumber;
                int endLine = editor.Document.GetLineByOffset(editor.SelectionStart + selLength).LineNumber;
                int selLines = endLine - startLine + 1;

                if (selLength < 100000)
                {
                    int selWords = TextUtilities.CountWords(editor.SelectedText);
                    TextStatsText.Text = $"Sel: {selLength} chars, {selLines} lines, {selWords} words";
                }
                else
                {
                    TextStatsText.Text = $"Sel: {selLength} chars, {selLines} lines, counting words...";
                    var selStart = editor.SelectionStart;
                    var selectedText = editor.SelectedText;
                    _ = Task.Run(() =>
                    {
                        int selWords = TextUtilities.CountWords(selectedText);
                        Dispatcher.Invoke(() =>
                        {
                            if (CurrentContext == ctx && editor.SelectionLength == selLength && editor.SelectionStart == selStart)
                            {
                                TextStatsText.Text = $"Sel: {selLength} chars, {selLines} lines, {selWords} words";
                            }
                        });
                    });
                }
            }
            else
            {
                int charCount = ctx.Editor.Document.TextLength;
                int lineCount = ctx.Editor.Document.LineCount;
                
                TextStatsText.Text = $"{charCount} chars, {lineCount} lines, calculating words...";
                TriggerStatsUpdateAsync(ctx);
            }
        }
        else if (ctx.LargeView != null)
        {
            _statsCts?.Cancel();
            _statsDebounceTimer?.Stop();
            TextStatsText.Text = $"{ctx.LargeView.LineCount} lines";
        }
        else
        {
            _statsCts?.Cancel();
            _statsDebounceTimer?.Stop();
            TextStatsText.Text = "";
        }
    }

    private void TriggerStatsUpdateAsync(TabContext ctx)
    {
        _statsCts?.Cancel();
        _statsCts = new System.Threading.CancellationTokenSource();
        var token = _statsCts.Token;

        var editor = ctx.Editor;
        if (editor == null) return;

        bool isLarge = editor.Document.TextLength >= 100000;

        _statsDebounceTimer?.Stop();

        if (isLarge)
        {
            _statsDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _statsDebounceTimer.Tick += async (s, e) =>
            {
                _statsDebounceTimer.Stop();
                await RunStatsCalcAsync(ctx, token);
            };
            _statsDebounceTimer.Start();
        }
        else
        {
            _ = RunStatsCalcAsync(ctx, token);
        }
    }

    private async System.Threading.Tasks.Task RunStatsCalcAsync(TabContext ctx, System.Threading.CancellationToken token)
    {
        var editor = ctx.Editor;
        if (editor == null) return;

        var snapshot = editor.Document.CreateSnapshot();
        int charCount = snapshot.TextLength;
        int lineCount = editor.Document.LineCount;

        try
        {
            int wordCount = await TextUtilities.CountWordsAsync(snapshot, token);

            if (!token.IsCancellationRequested && CurrentContext == ctx)
            {
                if (editor.SelectionLength == 0)
                {
                    TextStatsText.Text = $"{charCount} chars, {lineCount} lines, {wordCount} words";
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CommandPalette_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dlg = new CommandPaletteDialog { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedCommand != null)
        {
            ExecuteCommandPaletteAction(dlg.SelectedCommand.Id);
        }
    }

    private void ExecuteCommandPaletteAction(string commandId)
    {
        switch (commandId)
        {
            case "File.New":
                ApplicationCommands.New.Execute(null, this);
                break;
            case "File.NewWindow":
                NewWindowCommand.Execute(null, this);
                break;
            case "File.Open":
                ApplicationCommands.Open.Execute(null, this);
                break;
            case "File.OpenFolder":
                OpenFolder_Click(this, new RoutedEventArgs());
                break;
            case "File.Save":
                if (ApplicationCommands.Save.CanExecute(null, this))
                    ApplicationCommands.Save.Execute(null, this);
                break;
            case "File.SaveAs":
                if (ApplicationCommands.SaveAs.CanExecute(null, this))
                    ApplicationCommands.SaveAs.Execute(null, this);
                break;
            case "File.CloseTab":
                ApplicationCommands.Close.Execute(null, this);
                break;
            case "File.Exit":
                Exit_Click(this, new RoutedEventArgs());
                break;

            case "Edit.Undo":
                ApplicationCommands.Undo.Execute(null, this);
                break;
            case "Edit.Redo":
                ApplicationCommands.Redo.Execute(null, this);
                break;
            case "Edit.Cut":
                ApplicationCommands.Cut.Execute(null, this);
                break;
            case "Edit.Copy":
                ApplicationCommands.Copy.Execute(null, this);
                break;
            case "Edit.Paste":
                ApplicationCommands.Paste.Execute(null, this);
                break;
            case "Edit.Delete":
                ApplicationCommands.Delete.Execute(null, this);
                break;
            case "Edit.SelectAll":
                ApplicationCommands.SelectAll.Execute(null, this);
                break;
            case "Edit.TimeDate":
                if (TimeDateCommand.CanExecute(null, this))
                    TimeDateCommand.Execute(null, this);
                break;
            case "Edit.Find":
                if (ApplicationCommands.Find.CanExecute(null, this))
                    ApplicationCommands.Find.Execute(null, this);
                break;
            case "Edit.Replace":
                if (ApplicationCommands.Replace.CanExecute(null, this))
                    ApplicationCommands.Replace.Execute(null, this);
                break;
            case "Edit.FindInFiles":
                if (FindInFilesCommand.CanExecute(null, this))
                    FindInFilesCommand.Execute(null, this);
                break;
            case "Edit.GoToLine":
                if (GoToLineCommand.CanExecute(null, this))
                    GoToLineCommand.Execute(null, this);
                break;
            case "Edit.GoToSymbol":
                if (GoToSymbolCommand.CanExecute(null, this))
                    GoToSymbolCommand.Execute(null, this);
                break;
            case "Edit.FormatDocument":
                if (FormatDocumentCommand.CanExecute(null, this))
                    FormatDocumentCommand.Execute(null, this);
                break;

            case "View.ToggleDarkTheme":
                ToggleTheme_Click(this, new RoutedEventArgs());
                break;
            case "View.ToggleSidebar":
                if (ToggleSidebarCommand.CanExecute(null, this))
                    ToggleSidebarCommand.Execute(null, this);
                break;
            case "View.ToggleTerminal":
                if (ToggleTerminalCommand.CanExecute(null, this))
                    ToggleTerminalCommand.Execute(null, this);
                break;
            case "View.TogglePreview":
                TogglePreview();
                break;
            case "View.ZoomIn":
                if (ZoomInCommand.CanExecute(null, this))
                    ZoomInCommand.Execute(null, this);
                break;
            case "View.ZoomOut":
                if (ZoomOutCommand.CanExecute(null, this))
                    ZoomOutCommand.Execute(null, this);
                break;
            case "View.ResetZoom":
                if (ResetZoomCommand.CanExecute(null, this))
                    ResetZoomCommand.Execute(null, this);
                break;
            case "View.ToggleWordWrap":
                ToggleWordWrap_Click(this, new RoutedEventArgs());
                break;
            case "View.ToggleLineNumbers":
                ToggleLineNumbers_Click(this, new RoutedEventArgs());
                break;
            case "View.ToggleMinimap":
                ToggleMinimap_Click(this, new RoutedEventArgs());
                break;
            case "View.ToggleWhitespace":
                ToggleWhitespace_Click(this, new RoutedEventArgs());
                break;
            case "View.ToggleHighlightLine":
                ToggleHighlightLine_Click(this, new RoutedEventArgs());
                break;
            case "View.ToggleStatusBar":
                ToggleStatusBar_Click(this, new RoutedEventArgs());
                break;

            case "Tools.Settings":
                if (SettingsCommand.CanExecute(null, this))
                    SettingsCommand.Execute(null, this);
                break;
            case "Tools.CompareFiles":
                CompareFiles_Click(this, new RoutedEventArgs());
                break;

            case "Help.ViewHelp":
                Help_Click(this, new RoutedEventArgs());
                break;
            case "Help.About":
                About_Click(this, new RoutedEventArgs());
                break;

            case "Transform.Uppercase":
                if (TransformTextCommand.CanExecute("Uppercase", this))
                    TransformTextCommand.Execute("Uppercase", this);
                break;
            case "Transform.Lowercase":
                if (TransformTextCommand.CanExecute("Lowercase", this))
                    TransformTextCommand.Execute("Lowercase", this);
                break;
            case "Transform.TitleCase":
                if (TransformTextCommand.CanExecute("TitleCase", this))
                    TransformTextCommand.Execute("TitleCase", this);
                break;
            case "Transform.SentenceCase":
                if (TransformTextCommand.CanExecute("SentenceCase", this))
                    TransformTextCommand.Execute("SentenceCase", this);
                break;
            case "Transform.CamelCase":
                if (TransformTextCommand.CanExecute("CamelCase", this))
                    TransformTextCommand.Execute("CamelCase", this);
                break;
            case "Transform.SnakeCase":
                if (TransformTextCommand.CanExecute("SnakeCase", this))
                    TransformTextCommand.Execute("SnakeCase", this);
                break;
            case "Transform.SortAsc":
                if (TransformTextCommand.CanExecute("SortAsc", this))
                    TransformTextCommand.Execute("SortAsc", this);
                break;
            case "Transform.SortDesc":
                if (TransformTextCommand.CanExecute("SortDesc", this))
                    TransformTextCommand.Execute("SortDesc", this);
                break;
            case "Transform.RemoveDuplicates":
                if (TransformTextCommand.CanExecute("RemoveDuplicates", this))
                    TransformTextCommand.Execute("RemoveDuplicates", this);
                break;
            case "Transform.TrimBoth":
                if (TransformTextCommand.CanExecute("TrimBoth", this))
                    TransformTextCommand.Execute("TrimBoth", this);
                break;

            case "Transform.UrlEncode":
                if (TransformTextCommand.CanExecute("UrlEncode", this))
                    TransformTextCommand.Execute("UrlEncode", this);
                break;
            case "Transform.UrlDecode":
                if (TransformTextCommand.CanExecute("UrlDecode", this))
                    TransformTextCommand.Execute("UrlDecode", this);
                break;
            case "Transform.Base64Encode":
                if (TransformTextCommand.CanExecute("Base64Encode", this))
                    TransformTextCommand.Execute("Base64Encode", this);
                break;
            case "Transform.Base64Decode":
                if (TransformTextCommand.CanExecute("Base64Decode", this))
                    TransformTextCommand.Execute("Base64Decode", this);
                break;
            case "Transform.HtmlEncode":
                if (TransformTextCommand.CanExecute("HtmlEncode", this))
                    TransformTextCommand.Execute("HtmlEncode", this);
                break;
            case "Transform.HtmlDecode":
                if (TransformTextCommand.CanExecute("HtmlDecode", this))
                    TransformTextCommand.Execute("HtmlDecode", this);
                break;
            case "Transform.MinifyJsonXml":
                if (TransformTextCommand.CanExecute("MinifyJsonXml", this))
                    TransformTextCommand.Execute("MinifyJsonXml", this);
                break;
        }
    }

    private void GoToSymbol_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CurrentContext?.Editor != null;
    }

    private void GoToSymbol_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx?.Editor == null) return;

        var outline = OutlineService.BuildOutline(ctx.Editor.Text, ctx.FilePath);
        if (outline == null || outline.Count == 0)
        {
            StatusText.Text = "No symbols/headings found in this document.";
            return;
        }

        var dlg = new GoToSymbolDialog(outline) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedLineNumber > 0)
        {
            var ed = ctx.Editor;
            int line = Math.Clamp(dlg.SelectedLineNumber, 1, ed.Document.LineCount);
            ed.ScrollToLine(line);
            var lineSeg = ed.Document.GetLineByNumber(line);
            ed.Select(lineSeg.Offset, 0);
            ed.TextArea.Caret.BringCaretToView();
            ed.Focus();
        }
    }

    private void NewWindow_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        string? path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not start new window:\n{ex.Message}", "HyperNote", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TimeDate_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx?.Editor != null && !ctx.IsReadOnlyView)
        {
            ctx.Editor.Document.Replace(ctx.Editor.CaretOffset, 0, DateTime.Now.ToString());
        }
    }

    private void Zoom_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CurrentContext != null && (CurrentContext.Editor != null || CurrentContext.LargeView != null || CurrentContext.ImageViewer != null);
    }

    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx == null) return;
        if (ctx.Editor != null)
        {
            ctx.Editor.FontSize = Math.Clamp(ctx.Editor.FontSize + 1, 6.0, 60.0);
            UpdateStatusBar();
        }
        else if (ctx.LargeView != null)
        {
            ctx.LargeView.ViewFontSize = Math.Clamp(ctx.LargeView.ViewFontSize + 1, 6.0, 60.0);
            UpdateStatusBar();
        }
        else if (ctx.ImageViewer != null)
        {
            ctx.ImageViewer.ZoomIn();
            UpdateStatusBar();
        }
    }

    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx == null) return;
        if (ctx.Editor != null)
        {
            ctx.Editor.FontSize = Math.Clamp(ctx.Editor.FontSize - 1, 6.0, 60.0);
            UpdateStatusBar();
        }
        else if (ctx.LargeView != null)
        {
            ctx.LargeView.ViewFontSize = Math.Clamp(ctx.LargeView.ViewFontSize - 1, 6.0, 60.0);
            UpdateStatusBar();
        }
        else if (ctx.ImageViewer != null)
        {
            ctx.ImageViewer.ZoomOut();
            UpdateStatusBar();
        }
    }

    private void ResetZoom_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx == null) return;
        double defaultSize = SettingsService.Instance.Settings.EditorFontSize;
        if (ctx.Editor != null)
        {
            ctx.Editor.FontSize = defaultSize;
            UpdateStatusBar();
        }
        else if (ctx.LargeView != null)
        {
            ctx.LargeView.ViewFontSize = defaultSize;
            UpdateStatusBar();
        }
        else if (ctx.ImageViewer != null)
        {
            ctx.ImageViewer.ResetZoom();
            UpdateStatusBar();
        }
    }

    private void ToggleWordWrap_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance.Settings;
        s.WordWrap = !s.WordWrap;
        SettingsService.Instance.Save();
        ApplySettingsToAllEditors();
        UpdateViewMenuCheckmarks();
    }

    private void ToggleLineNumbers_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance.Settings;
        s.ShowLineNumbers = !s.ShowLineNumbers;
        SettingsService.Instance.Save();
        ApplySettingsToAllEditors();
        UpdateViewMenuCheckmarks();
    }

    private void ToggleMinimap_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance.Settings;
        s.ShowMinimap = !s.ShowMinimap;
        SettingsService.Instance.Save();
        ApplySettingsToAllEditors();
        UpdateViewMenuCheckmarks();
    }

    private void ToggleWhitespace_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance.Settings;
        s.ShowWhitespace = !s.ShowWhitespace;
        SettingsService.Instance.Save();
        ApplySettingsToAllEditors();
        UpdateViewMenuCheckmarks();
    }

    private void ToggleHighlightLine_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Instance.Settings;
        s.HighlightCurrentLine = !s.HighlightCurrentLine;
        SettingsService.Instance.Save();
        ApplySettingsToAllEditors();
        UpdateViewMenuCheckmarks();
    }

    private void ToggleStatusBar_Click(object sender, RoutedEventArgs e)
    {
        bool visible = AppStatusBar.Visibility == Visibility.Visible;
        AppStatusBar.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        UpdateViewMenuCheckmarks();
    }

    private void UpdateViewMenuCheckmarks()
    {
        var s = SettingsService.Instance.Settings;
        if (WordWrapMenu != null) WordWrapMenu.IsChecked = s.WordWrap;
        if (LineNumbersMenu != null) LineNumbersMenu.IsChecked = s.ShowLineNumbers;
        if (MinimapMenu != null) MinimapMenu.IsChecked = s.ShowMinimap;
        if (WhitespaceMenu != null) WhitespaceMenu.IsChecked = s.ShowWhitespace;
        if (HighlightLineMenu != null) HighlightLineMenu.IsChecked = s.HighlightCurrentLine;
        if (StatusBarMenu != null && AppStatusBar != null) StatusBarMenu.IsChecked = AppStatusBar.Visibility == Visibility.Visible;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Controls.AboutDialog { Owner = this };
        dlg.ShowDialog();
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/google-deepmind") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open help link:\n{ex.Message}", "HyperNote", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            var editor = (TextEditor)sender;
            double step = 1.0;
            double nextSize = e.Delta > 0 ? editor.FontSize + step : editor.FontSize - step;
            editor.FontSize = Math.Clamp(nextSize, 6.0, 60.0);
            UpdateStatusBar();
        }
    }

    private void ZoomStatus_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var ctx = CurrentContext;
            if (ctx == null) return;

            double defaultSize = SettingsService.Instance.Settings.EditorFontSize;

            if (ctx.Editor != null)
            {
                ctx.Editor.FontSize = defaultSize;
                UpdateStatusBar();
            }
            else if (ctx.LargeView != null)
            {
                ctx.LargeView.ViewFontSize = defaultSize;
                UpdateStatusBar();
            }
            else if (ctx.ImageViewer != null)
            {
                ctx.ImageViewer.ResetZoom();
                UpdateStatusBar();
            }
        }
    }

    private void LanguageSelector_Click(object sender, RoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx?.Editor == null) return;

        var menu = new ContextMenu();

        var plainItem = new MenuItem { Header = "Plain Text" };
        plainItem.Click += (s, ev) =>
        {
            ctx.Editor.SyntaxHighlighting = null;
            UpdateStatusBar();
            UpdateEditorLayoutCapabilities(ctx);
        };
        menu.Items.Add(plainItem);
        menu.Items.Add(new Separator());

        var definitions = HighlightingManager.Instance.HighlightingDefinitions
            .OrderBy(d => d.Name)
            .ToList();

        foreach (var def in definitions)
        {
            var item = new MenuItem { Header = def.Name };
            var selectedDef = def;
            item.Click += (s, ev) =>
            {
                ctx.Editor.SyntaxHighlighting = selectedDef;
                UpdateStatusBar();
                UpdateEditorLayoutCapabilities(ctx);
            };
            menu.Items.Add(item);
        }

        if (sender is FrameworkElement element)
        {
            menu.PlacementTarget = element;
            menu.Placement = PlacementMode.Top;
            menu.IsOpen = true;
        }
    }

    private void FuzzySearch_Executed(object s, ExecutedRoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentWorkspaceFolder))
        {
            StatusText.Text = "Please open a folder first to search files.";
            return;
        }
        var dlg = new FuzzySwitcherDialog(_currentWorkspaceFolder) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedFilePath != null)
        {
            OpenFile(dlg.SelectedFilePath);
        }
    }

    private void FindInFiles_Executed(object s, ExecutedRoutedEventArgs e)
    {
        if (SidebarPanel.Visibility != Visibility.Visible)
        {
            ToggleSidebar();
        }

        SwitchSidebarTab(SidebarTab.Search);

        string selection = GetActiveEditorSelection();
        if (!string.IsNullOrEmpty(selection) && selection.IndexOfAny(new[] { '\r', '\n' }) == -1)
        {
            SearchTermBox.Text = selection;
        }

        SearchTermBox.Focus();
        SearchTermBox.SelectAll();
    }

    private enum SidebarTab { Explorer, Search, Bookmarks }

    private void SidebarTabExplorer_Click(object sender, RoutedEventArgs e)
    {
        SwitchSidebarTab(SidebarTab.Explorer);
    }

    private void SidebarTabSearch_Click(object sender, RoutedEventArgs e)
    {
        SwitchSidebarTab(SidebarTab.Search);
    }

    private void SwitchSidebarTab(SidebarTab tab)
    {
        SidebarTabExplorer.SetResourceReference(Control.ForegroundProperty, tab == SidebarTab.Explorer ? "App.Accent" : "Editor.LineNumbers");
        SidebarTabExplorer.FontWeight = tab == SidebarTab.Explorer ? FontWeights.Bold : FontWeights.Normal;

        SidebarTabSearch.SetResourceReference(Control.ForegroundProperty, tab == SidebarTab.Search ? "App.Accent" : "Editor.LineNumbers");
        SidebarTabSearch.FontWeight = tab == SidebarTab.Search ? FontWeights.Bold : FontWeights.Normal;

        SidebarTabBookmarks.SetResourceReference(Control.ForegroundProperty, tab == SidebarTab.Bookmarks ? "App.Accent" : "Editor.LineNumbers");
        SidebarTabBookmarks.FontWeight = tab == SidebarTab.Bookmarks ? FontWeights.Bold : FontWeights.Normal;

        ExplorerTree.Visibility = tab == SidebarTab.Explorer ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = tab == SidebarTab.Search ? Visibility.Visible : Visibility.Collapsed;
        BookmarksPanel.Visibility = tab == SidebarTab.Bookmarks ? Visibility.Visible : Visibility.Collapsed;

        if (tab == SidebarTab.Bookmarks)
        {
            RefreshBookmarksList();
        }
    }

    private void RefreshBookmarksList()
    {
        var items = new List<BookmarkItem>();
        var allBookmarks = BookmarkService.Instance.GetAllBookmarks();

        foreach (var kv in allBookmarks)
        {
            string filePath = kv.Key;
            var lines = kv.Value;
            if (lines.Count == 0) continue;

            TabContext? openTab = null;
            foreach (TabItem tab in Tabs.Items)
            {
                if (tab.DataContext is TabContext tc && string.Equals(tc.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    openTab = tc;
                    break;
                }
            }

            List<string>? fileLines = null;
            if (openTab == null && File.Exists(filePath))
            {
                try
                {
                    fileLines = new List<string>(File.ReadLines(filePath));
                }
                catch { }
            }

            foreach (int lineNum in lines)
            {
                string preview = "";
                if (openTab != null && openTab.Editor != null)
                {
                    try
                    {
                        var docLine = openTab.Editor.Document.GetLineByNumber(lineNum);
                        preview = openTab.Editor.Document.GetText(docLine.Offset, docLine.Length);
                    }
                    catch { }
                }
                else if (fileLines != null && lineNum >= 1 && lineNum <= fileLines.Count)
                {
                    preview = fileLines[lineNum - 1];
                }

                items.Add(new BookmarkItem
                {
                    FilePath = filePath,
                    LineNumber = lineNum,
                    PreviewText = preview.Trim()
                });
            }
        }

        items.Sort((a, b) =>
        {
            int fileCompare = string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            if (fileCompare != 0) return fileCompare;
            return a.LineNumber.CompareTo(b.LineNumber);
        });

        BookmarksList.ItemsSource = items;
    }

    private string GetActiveEditorSelection()
    {
        var ctx = CurrentContext;
        return ctx?.Editor?.SelectedText ?? string.Empty;
    }

    private void SearchTermBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            StartSearch();
            e.Handled = true;
        }
    }

    private void SearchBtn_Click(object sender, RoutedEventArgs e)
    {
        StartSearch();
    }

    private async void StartSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        string? root = _currentWorkspaceFolder;
        if (string.IsNullOrEmpty(root))
        {
            SearchStatusText.Text = "Please open a folder (File -> Open Folder) to search in files.";
            SearchStatusText.SetResourceReference(TextBlock.ForegroundProperty, "Editor.LineNumbers");
            SearchResultsTree.ItemsSource = null;
            return;
        }

        string term = SearchTermBox.Text;
        if (string.IsNullOrEmpty(term))
        {
            SearchStatusText.Text = "Please enter a search term.";
            SearchStatusText.SetResourceReference(TextBlock.ForegroundProperty, "Editor.LineNumbers");
            SearchResultsTree.ItemsSource = null;
            return;
        }

        bool matchCase = SearchMatchCaseToggle.IsChecked == true;
        bool wholeWord = SearchWholeWordToggle.IsChecked == true;
        bool useRegex = SearchRegexToggle.IsChecked == true;

        if (useRegex)
        {
            try
            {
                TextSearch.BuildRegex(term, matchCase, wholeWord, useRegex);
            }
            catch (ArgumentException ex)
            {
                SearchStatusText.Text = $"Invalid regex: {ex.Message}";
                SearchStatusText.Foreground = Brushes.Red;
                SearchResultsTree.ItemsSource = null;
                return;
            }
        }

        SearchStatusText.Text = "Searching...";
        SearchStatusText.SetResourceReference(TextBlock.ForegroundProperty, "Editor.LineNumbers");
        SearchProgress.Visibility = Visibility.Visible;
        SearchResultsTree.ItemsSource = null;

        try
        {
            var results = await Task.Run(() =>
            {
                var filesToSearch = new List<string>();
                void Walk(string dir)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(dir))
                        {
                            filesToSearch.Add(file);
                        }
                        foreach (var subDir in Directory.EnumerateDirectories(dir))
                        {
                            if (WorkspaceService.IsIgnored(Path.GetFileName(subDir))) continue;
                            Walk(subDir);
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                }

                Walk(root);

                var list = new List<SearchResultFileNode>();
                int filesSearched = 0;
                int totalMatches = 0;
                int filesWithMatches = 0;

                foreach (string file in filesToSearch)
                {
                    token.ThrowIfCancellationRequested();

                    var matches = SearchFile(file, term, matchCase, wholeWord, useRegex);
                    if (matches.Count > 0)
                    {
                        var relPath = Path.GetRelativePath(root, file);
                        var fileNode = new SearchResultFileNode
                        {
                            FilePath = file,
                            RelativePath = relPath
                        };
                        foreach (var m in matches)
                        {
                            fileNode.Matches.Add(new SearchResultMatchNode(fileNode)
                            {
                                LineNumber = m.LineNumber,
                                Column = m.Column,
                                Length = m.Length,
                                LineText = m.LineText,
                                SearchTerm = term,
                                MatchCase = matchCase,
                                WholeWord = wholeWord,
                                UseRegex = useRegex
                            });
                        }

                        lock (list)
                        {
                            list.Add(fileNode);
                        }
                        filesWithMatches++;
                        totalMatches += matches.Count;
                    }

                    filesSearched++;
                    if (filesSearched % 50 == 0 || filesSearched == filesToSearch.Count)
                    {
                        int searched = filesSearched;
                        int total = filesToSearch.Count;
                        int foundCount = totalMatches;
                        int foundFiles = filesWithMatches;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                SearchStatusText.Text = $"Searched {searched} of {total} files... Found {foundCount} matches.";
                            }
                        }));
                    }
                }

                return list;
            }, token);

            SearchProgress.Visibility = Visibility.Collapsed;
            int matchCount = results.Sum(r => r.Matches.Count);
            SearchStatusText.Text = $"Found {matchCount} match{(matchCount == 1 ? "" : "es")} in {results.Count} file{(results.Count == 1 ? "" : "s")}.";
            SearchResultsTree.ItemsSource = results;
        }
        catch (OperationCanceledException)
        {
            SearchProgress.Visibility = Visibility.Collapsed;
            SearchStatusText.Text = "Search canceled.";
        }
        catch (Exception ex)
        {
            SearchProgress.Visibility = Visibility.Collapsed;
            SearchStatusText.Text = $"Error: {ex.Message}";
        }
    }

    private List<(int LineNumber, int Column, int Length, string LineText)> SearchFile(
        string filePath, string term, bool matchCase, bool wholeWord, bool useRegex)
    {
        var matches = new List<(int, int, int, string)>();

        if (IsBinaryFile(filePath)) return matches;

        try
        {
            var regex = TextSearch.BuildRegex(term, matchCase, wholeWord, useRegex);

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            string? line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (line.Length > 10000) continue; // Skip long minified lines

                var lineMatches = regex.Matches(line);
                foreach (Match m in lineMatches)
                {
                    if (m.Length > 0)
                    {
                        matches.Add((lineNumber, m.Index, m.Length, line));
                    }
                }
            }
        }
        catch { }

        return matches;
    }

    private static bool IsBinaryFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] buffer = new byte[4096];
            int read = stream.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return true;
            }
        }
        catch { }
        return false;
    }

    private void SearchResultsTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchResultsTree.SelectedItem is SearchResultMatchNode matchNode)
        {
            OpenFile(matchNode.Parent.FilePath);
            var ctx = CurrentContext;
            if (ctx != null)
            {
                NavigateToLineInContext(ctx, matchNode.LineNumber, matchNode.Column, matchNode.Length);
            }
        }
        else if (SearchResultsTree.SelectedItem is SearchResultFileNode fileNode)
        {
            OpenFile(fileNode.FilePath);
        }
    }

    private void NavigateToLineInContext(TabContext ctx, int lineNumber, int lineOffset, int matchLength)
    {
        if (ctx.LargeView != null)
        {
            ctx.LargeView.GoToLine(lineNumber);
        }
        else if (ctx.Editor != null)
        {
            int maxLine = ctx.Editor.Document.LineCount;
            int lineNum = Math.Clamp(lineNumber, 1, maxLine);
            var line = ctx.Editor.Document.GetLineByNumber(lineNum);

            int selectStart = line.Offset + Math.Clamp(lineOffset, 0, line.Length);
            int selectLength = Math.Clamp(matchLength, 0, line.Offset + line.Length - selectStart);

            ctx.Editor.Select(selectStart, selectLength);
            ctx.Editor.ScrollToLine(lineNum);
            ctx.Editor.TextArea.Caret.BringCaretToView();
            ctx.Editor.Focus();
        }
    }

    private void Exit_Click(object s, RoutedEventArgs e) => Close();

    private void Settings_Executed(object s, ExecutedRoutedEventArgs e)
    {
        var dlg = new SettingsDialog(SettingsService.Instance.Settings) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        // Theme may have changed — use ThemeManager so ThemeChanged fires.
        var newTheme = SettingsService.Instance.Settings.DarkTheme ? AppTheme.Dark : AppTheme.Light;
        if (newTheme != ThemeManager.Current)
            ThemeManager.Apply(newTheme);
        else
            SettingsService.Instance.Save(); // save non-theme changes

        // Propagate editor settings to all open tabs.
        ApplySettingsToAllEditors();
        UpdateViewMenuCheckmarks();

        // Trim recent-files list to new cap.
        var st = SettingsService.Instance.Settings;
        if (st.RecentFiles.Count > st.MaxRecentFiles)
        {
            st.RecentFiles.RemoveRange(st.MaxRecentFiles,
                st.RecentFiles.Count - st.MaxRecentFiles);
        }
        RebuildRecentMenu();

        StatusText.Text = "Settings saved.";
    }

    private void GoToLine_CanExecute(object s, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CurrentContext != null;
    }

    private void GoToLine_Executed(object s, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx == null) return;

        if (ctx.LargeView != null)
        {
            int maxLine = ctx.LargeView.LineCount;
            int currentLine = ctx.LargeView.CurrentLine;
            var dlg = new GoToLineDialog(maxLine, currentLine) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.LineNumber > 0)
            {
                ctx.LargeView.GoToLine(dlg.LineNumber);
            }
        }
        else if (ctx.Editor != null)
        {
            int maxLine = ctx.Editor.Document.LineCount;
            int currentLine = ctx.Editor.TextArea.Caret.Line;
            var dlg = new GoToLineDialog(maxLine, currentLine) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.LineNumber > 0)
            {
                var line = ctx.Editor.Document.GetLineByNumber(dlg.LineNumber);
                ctx.Editor.Select(line.Offset, 0);
                ctx.Editor.ScrollToLine(dlg.LineNumber);
                ctx.Editor.TextArea.Caret.BringCaretToView();
                ctx.Editor.Focus();
            }
        }
    }

    // =====================================================================
    //  Session restore ("Hot Exit")
    // =====================================================================

    public class SessionState
    {
        public List<TabState> Tabs { get; set; } = new();
        public int ActiveTabIndex { get; set; } = 0;
    }

    public class TabState
    {
        public string? FilePath { get; set; }
        public string? Title { get; set; }
        public bool IsDirty { get; set; }
        public string? BackupPath { get; set; }
        public string? EncodingName { get; set; }
        public int CaretOffset { get; set; }
        public double ScrollHorizontalOffset { get; set; }
        public double ScrollVerticalOffset { get; set; }
        public bool OutlineVisible { get; set; }
        public bool PreviewVisible { get; set; }
    }

    private void SaveBackup(TabContext ctx)
    {
        if (ctx.Editor == null || ctx.IsReadOnlyView) return;

        string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "backups");
        try
        {
            Directory.CreateDirectory(backupDir);
            if (string.IsNullOrEmpty(ctx.BackupPath))
            {
                ctx.BackupPath = Path.Combine(backupDir, Guid.NewGuid().ToString() + ".txt");
            }
            File.WriteAllText(ctx.BackupPath, ctx.Editor.Text, ctx.Encoding);
            SaveSessionState();
        }
        catch { }
    }

    private void SaveSessionState()
    {
        if (!SettingsService.Instance.Settings.RememberOpenFiles)
        {
            DeleteSessionAndBackups();
            return;
        }

        var session = new SessionState
        {
            ActiveTabIndex = Tabs.SelectedIndex,
            Tabs = new List<TabState>()
        };

        foreach (TabItem tab in Tabs.Items)
        {
            if (tab.Tag is TabContext ctx)
            {
                if (ctx.BackupTimer != null && ctx.BackupTimer.IsEnabled)
                {
                    ctx.BackupTimer.Stop();
                    SaveBackup(ctx);
                }

                var tState = new TabState
                {
                    FilePath = ctx.FilePath,
                    Title = ctx.HeaderText.Text.TrimEnd('*', ' '),
                    IsDirty = ctx.IsDirty,
                    BackupPath = ctx.BackupPath,
                    EncodingName = ctx.Encoding.WebName,
                    OutlineVisible = ctx.OutlineColumn != null && ctx.OutlineColumn.Width.Value > 0,
                    PreviewVisible = ctx.PreviewColumn != null && ctx.PreviewColumn.Width.Value > 0
                };

                if (ctx.Editor != null)
                {
                    tState.CaretOffset = ctx.Editor.CaretOffset;
                    tState.ScrollHorizontalOffset = ctx.Editor.HorizontalOffset;
                    tState.ScrollVerticalOffset = ctx.Editor.VerticalOffset;
                }
                else if (ctx.LargeView != null)
                {
                    tState.CaretOffset = ctx.LargeView.SelectedIndex;
                    tState.ScrollVerticalOffset = ctx.LargeView.VerticalScrollOffset;
                }

                session.Tabs.Add(tState);
            }
        }

        try
        {
            string sessionFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "session.json");
            string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(sessionFile, json);
        }
        catch { }
    }


    private bool RestoreSession()
    {
        string sessionFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "session.json");
        return LoadSessionFile(sessionFile, isAutoRestore: true);
    }

    private bool LoadSessionFile(string sessionFile, bool isAutoRestore = false)

    {
        if (!File.Exists(sessionFile)) return false;

        SessionState? session = null;
        try
        {
            string json = File.ReadAllText(sessionFile);
            session = JsonSerializer.Deserialize<SessionState>(json);
        }
        catch { return false; }

        if (session == null || session.Tabs == null || session.Tabs.Count == 0) return false;

        if (!isAutoRestore)
        {
            var warnRes = MessageBox.Show(this, "Opening a new session will close all currently open tabs. Unsaved changes will be lost. Continue?", "Open Session", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (warnRes != MessageBoxResult.Yes) return false;

            // Close current watchers and clean tabs
            foreach (TabItem tab in Tabs.Items)
            {
                if (tab.Tag is TabContext oldCtx)
                {
                    StopFileWatcher(oldCtx);
                    oldCtx.FoldTimer?.Stop();
                    oldCtx.PreviewTimer?.Stop();
                    oldCtx.OutlineTimer?.Stop();
                    oldCtx.BackupTimer?.Stop();
                    oldCtx.LargeView?.Dispose();
                    oldCtx.Preview?.Dispose();
                }
            }
            Tabs.Items.Clear();
        }

        var activeBackupPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tabState in session.Tabs)
        {
            TabContext? ctx = null;
            Encoding encoding = Encoding.UTF8;
            try
            {
                encoding = Encoding.GetEncoding(tabState.EncodingName ?? "utf-8");
            }
            catch { }

            if (!string.IsNullOrEmpty(tabState.FilePath))
            {
                string path = tabState.FilePath;
                bool fileExists = File.Exists(path);
                bool hasBackup = !string.IsNullOrEmpty(tabState.BackupPath) && File.Exists(tabState.BackupPath);

                if (fileExists || hasBackup)
                {
                    long threshold = SettingsService.Instance.Settings.LargeFileThresholdBytes;
                    bool isLarge = fileExists && new FileInfo(path).Length >= threshold;

                    if (isLarge)
                    {
                        var view = new LargeFileView();
                        var built = BuildEditorLayout(view);
                        ctx = CreateTab(tabState.Title ?? Path.GetFileName(path), built.Container);
                        ctx.FilePath = path;
                        ctx.IsReadOnlyView = false;
                        ctx.LargeView = view;

                        ApplyLayoutResult(ctx, built);
                        StartFileWatcher(ctx);

                        built.PreviewToggle.Click += (_, _) => SetPreviewVisible(ctx, built.PreviewToggle.IsChecked == true);
                        built.OutlineToggle.Click += (_, _) => SetOutlineVisible(ctx, built.OutlineToggle.IsChecked == true);

                        view.SelectionChanged += (_, _) => {
                            if (CurrentContext?.LargeView == view)
                                UpdateStatusBar();
                        };

                        view.ZoomChanged += (_, _) => {
                            if (CurrentContext?.LargeView == view)
                                UpdateStatusBar();
                        };

                        view.DocumentChanged += (s, e) => {
                            ctx.IsDirty = view.IsDirty;
                            ctx.UpdateHeader();
                        };
                        view.FileCleared += (s, e) => ConvertLargeTabToNormalTab(ctx);

                        view.Open(path, encoding);
                        UpdateEditorLayoutCapabilities(ctx);
                    }
                    else
                    {
                        var editor = CreateEditor();
                        var built = BuildEditorLayout(editor);
                        ctx = CreateTab(tabState.Title ?? Path.GetFileName(path), built.Container);
                        ctx.FilePath = path;
                        ctx.Editor = editor;
                        ctx.Encoding = encoding;
                        ApplyLayoutResult(ctx, built);
                        StartFileWatcher(ctx);

                        built.PreviewToggle.Click += (_, _) => SetPreviewVisible(ctx, built.PreviewToggle.IsChecked == true);
                        built.OutlineToggle.Click += (_, _) => SetOutlineVisible(ctx, built.OutlineToggle.IsChecked == true);

                        built.OutlineTree.SelectedItemChanged += (s, e) => {
                            if (built.OutlineTree.SelectedItem is OutlineNode node && ctx.Editor != null)
                            {
                                var ed = ctx.Editor;
                                int line = Math.Clamp(node.LineNumber, 1, ed.Document.LineCount);
                                ed.ScrollToLine(line);
                                var lineSeg = ed.Document.GetLineByNumber(line);
                                ed.Select(lineSeg.Offset, 0);
                                ed.TextArea.Caret.BringCaretToView();
                            }
                        };

                        var ext = Path.GetExtension(path).ToLowerInvariant();
                        ctx.FoldKind = SyntaxMapper.FoldKindForExtension(ext);
                        WireEditor(ctx);

                        if (hasBackup)
                        {
                            ctx.BackupPath = tabState.BackupPath;
                            activeBackupPaths.Add(Path.GetFullPath(tabState.BackupPath!));
                            try
                            {
                                editor.Text = File.ReadAllText(tabState.BackupPath!);
                                ctx.IsDirty = true;
                            }
                            catch
                            {
                                if (fileExists)
                                {
                                    editor.Text = File.ReadAllText(path);
                                    ctx.IsDirty = false;
                                }
                            }
                        }
                        else
                        {
                            try
                            {
                                editor.Text = File.ReadAllText(path);
                                ctx.IsDirty = false;
                            }
                            catch { }
                        }

                        editor.SyntaxHighlighting = SyntaxMapper.ForExtension(ext);
                        UpdateEditorLayoutCapabilities(ctx);
                    }
                }
            }
            else
            {
                var editor = CreateEditor();
                var built = BuildEditorLayout(editor);
                ctx = CreateTab(tabState.Title ?? $"Untitled-{++_untitledCounter}", built.Container);
                ctx.Editor = editor;
                ctx.Encoding = encoding;
                ApplyLayoutResult(ctx, built);

                built.PreviewToggle.Click += (_, _) => SetPreviewVisible(ctx, built.PreviewToggle.IsChecked == true);
                built.OutlineToggle.Click += (_, _) => SetOutlineVisible(ctx, built.OutlineToggle.IsChecked == true);

                WireEditor(ctx);

                bool hasBackup = !string.IsNullOrEmpty(tabState.BackupPath) && File.Exists(tabState.BackupPath);
                if (hasBackup)
                {
                    ctx.BackupPath = tabState.BackupPath;
                    activeBackupPaths.Add(Path.GetFullPath(tabState.BackupPath!));
                    try
                    {
                        editor.Text = File.ReadAllText(tabState.BackupPath!);
                        ctx.IsDirty = tabState.IsDirty;
                    }
                    catch { }
                }
                else
                {
                    editor.Text = "";
                    ctx.IsDirty = false;
                }

                UpdateEditorLayoutCapabilities(ctx);
            }

            if (ctx != null)
            {
                ctx.UpdateHeader();

                if (tabState.OutlineVisible && ctx.OutlineColumn != null)
                {
                    SetOutlineVisible(ctx, true);
                    if (ctx.OutlineToggle != null) ctx.OutlineToggle.IsChecked = true;
                }
                if (tabState.PreviewVisible && ctx.PreviewColumn != null)
                {
                    SetPreviewVisible(ctx, true);
                    if (ctx.PreviewToggle != null) ctx.PreviewToggle.IsChecked = true;
                }

                RestoreOffsets(ctx, tabState);

                // Set _untitledCounter to match restored titles
                if (tabState.FilePath == null && tabState.Title != null)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(tabState.Title, @"Untitled-(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                    {
                        _untitledCounter = Math.Max(_untitledCounter, num);
                    }
                }
            }
        }

        CleanupOrphanedBackups(activeBackupPaths);

        if (Tabs.Items.Count > 0)
        {
            int index = Math.Clamp(session.ActiveTabIndex, 0, Tabs.Items.Count - 1);
            Tabs.SelectedIndex = index;
        }
        else
        {
            NewTab();
        }

        return true;
    }


    private void RestoreOffsets(TabContext ctx, TabState state)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ctx.Editor != null)
            {
                ctx.Editor.CaretOffset = Math.Clamp(state.CaretOffset, 0, ctx.Editor.Document.TextLength);
                ctx.Editor.ScrollToHorizontalOffset(state.ScrollHorizontalOffset);
                ctx.Editor.ScrollToVerticalOffset(state.ScrollVerticalOffset);
            }
            else if (ctx.LargeView != null)
            {
                ctx.LargeView.SelectedIndex = state.CaretOffset;
                ctx.LargeView.VerticalScrollOffset = state.ScrollVerticalOffset;
            }
        }), DispatcherPriority.Background);
    }

    private void CleanupOrphanedBackups(HashSet<string> activeBackupPaths)
    {
        try
        {
            string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "backups");
            if (Directory.Exists(backupDir))
            {
                foreach (var file in Directory.GetFiles(backupDir, "*.txt"))
                {
                    string fullPath = Path.GetFullPath(file);
                    if (!activeBackupPaths.Contains(fullPath))
                    {
                        File.Delete(file);
                    }
                }
            }
        }
        catch { }
    }

    private void DeleteSessionAndBackups()
    {
        try
        {
            string sessionFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "session.json");
            if (File.Exists(sessionFile)) File.Delete(sessionFile);

            string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "backups");
            if (Directory.Exists(backupDir))
            {
                foreach (var file in Directory.GetFiles(backupDir, "*.txt"))
                {
                    File.Delete(file);
                }
            }
        }
        catch { }
    }

    private void FileInfoText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx == null) return;

        var dlg = new EncodingPickerDialog { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedItem != null)
        {
            var encoding = dlg.SelectedItem.Encoding;
            if (dlg.ActionType == "reopen")
            {
                ReopenFileWithEncoding(ctx, encoding);
            }
            else if (dlg.ActionType == "save")
            {
                SaveFileWithEncoding(ctx, encoding);
            }
        }
    }

    private void ReopenFileWithEncoding(TabContext ctx, Encoding encoding)
    {
        if (string.IsNullOrEmpty(ctx.FilePath)) return;

        if (ctx.Editor != null)
        {
            if (ctx.IsDirty)
            {
                var res = MessageBox.Show(this, "Reopening the file will discard your unsaved changes. Do you want to continue?", "Reopen with Encoding", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }

            try
            {
                string text = File.ReadAllText(ctx.FilePath, encoding);
                ctx.Editor.Text = text;
                ctx.IsDirty = false;
                ctx.Encoding = encoding;
                ctx.UpdateHeader();
                UpdateStatusBar();
                UpdateOutlineForActiveTab();
                if (ctx.PreviewColumn != null && ctx.PreviewColumn.Width.Value > 0)
                    RenderPreview(ctx);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error reopening file with encoding {encoding.EncodingName}:\n{ex.Message}", "Reopen with Encoding", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else if (ctx.LargeView != null)
        {
            try
            {
                ctx.Encoding = encoding;
                ctx.LargeView.Open(ctx.FilePath, encoding);
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error reopening file with encoding {encoding.EncodingName}:\n{ex.Message}", "Reopen with Encoding", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SaveFileWithEncoding(TabContext ctx, Encoding encoding)
    {
        ctx.Encoding = encoding;
        ctx.IsDirty = true;
        ctx.UpdateHeader();
        UpdateStatusBar();
    }

    private void Explorer_NewFile_Click(object sender, RoutedEventArgs e)
    {
        var node = ExplorerTree.SelectedItem as WorkspaceNode;
        string? targetFolder = null;
        if (node != null)
        {
            targetFolder = node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath);
        }
        else
        {
            targetFolder = _currentWorkspaceFolder;
        }

        if (string.IsNullOrEmpty(targetFolder)) return;

        var dlg = new InputDialog("New File", "Enter file name:", "untitled.txt") { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
        {
            string newPath = Path.Combine(targetFolder, dlg.InputText.Trim());
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                MessageBox.Show(this, "A file or folder with this name already exists.", "New File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                File.WriteAllText(newPath, "");
                if (node != null && node.IsDirectory)
                {
                    node.IsExpanded = true;
                    node.Refresh();
                }
                else if (!string.IsNullOrEmpty(_currentWorkspaceFolder))
                {
                    ExplorerTree.ItemsSource = WorkspaceService.LoadWorkspace(_currentWorkspaceFolder);
                }
                OpenFile(newPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error creating file:\n{ex.Message}", "New File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Explorer_NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var node = ExplorerTree.SelectedItem as WorkspaceNode;
        string? targetFolder = null;
        if (node != null)
        {
            targetFolder = node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath);
        }
        else
        {
            targetFolder = _currentWorkspaceFolder;
        }

        if (string.IsNullOrEmpty(targetFolder)) return;

        var dlg = new InputDialog("New Folder", "Enter folder name:", "New Folder") { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
        {
            string newPath = Path.Combine(targetFolder, dlg.InputText.Trim());
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                MessageBox.Show(this, "A file or folder with this name already exists.", "New Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Directory.CreateDirectory(newPath);
                if (node != null && node.IsDirectory)
                {
                    node.IsExpanded = true;
                    node.Refresh();
                }
                else if (!string.IsNullOrEmpty(_currentWorkspaceFolder))
                {
                    ExplorerTree.ItemsSource = WorkspaceService.LoadWorkspace(_currentWorkspaceFolder);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error creating folder:\n{ex.Message}", "New Folder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Explorer_Rename_Click(object sender, RoutedEventArgs e)
    {
        var node = ExplorerTree.SelectedItem as WorkspaceNode;
        if (node == null || string.IsNullOrEmpty(node.FullPath)) return;

        string oldPath = node.FullPath;
        string parentDir = Path.GetDirectoryName(oldPath) ?? "";
        string currentName = Path.GetFileName(oldPath);

        var dlg = new InputDialog("Rename", "Enter new name:", currentName) { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText) && dlg.InputText.Trim() != currentName)
        {
            string newPath = Path.Combine(parentDir, dlg.InputText.Trim());
            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                MessageBox.Show(this, "A file or folder with this name already exists.", "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (node.IsDirectory)
                {
                    Directory.Move(oldPath, newPath);
                }
                else
                {
                    File.Move(oldPath, newPath);
                    foreach (TabItem tab in Tabs.Items)
                    {
                        if (tab.Tag is TabContext ctx && ctx.FilePath != null && Path.GetFullPath(ctx.FilePath) == Path.GetFullPath(oldPath))
                        {
                            ctx.FilePath = newPath;
                            ctx.UpdateHeader();
                            if (CurrentContext == ctx) StatusText.Text = newPath;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(_currentWorkspaceFolder))
                {
                    ExplorerTree.ItemsSource = WorkspaceService.LoadWorkspace(_currentWorkspaceFolder);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error renaming:\n{ex.Message}", "Rename", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Explorer_Delete_Click(object sender, RoutedEventArgs e)
    {
        var node = ExplorerTree.SelectedItem as WorkspaceNode;
        if (node == null || string.IsNullOrEmpty(node.FullPath)) return;

        string targetPath = node.FullPath;
        string typeStr = node.IsDirectory ? "folder" : "file";
        var res = MessageBox.Show(this, $"Are you sure you want to delete this {typeStr}?\n\n{Path.GetFileName(targetPath)}", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res == MessageBoxResult.Yes)
        {
            try
            {
                if (node.IsDirectory)
                {
                    Directory.Delete(targetPath, true);
                }
                else
                {
                    File.Delete(targetPath);
                    TabContext? tabToClose = null;
                    foreach (TabItem tab in Tabs.Items)
                    {
                        if (tab.Tag is TabContext ctx && ctx.FilePath != null && Path.GetFullPath(ctx.FilePath) == Path.GetFullPath(targetPath))
                        {
                            tabToClose = ctx;
                            break;
                        }
                    }
                    if (tabToClose != null) CloseTab(tabToClose);
                }

                if (!string.IsNullOrEmpty(_currentWorkspaceFolder))
                {
                    ExplorerTree.ItemsSource = WorkspaceService.LoadWorkspace(_currentWorkspaceFolder);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error deleting:\n{ex.Message}", "Delete", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Explorer_Reveal_Click(object sender, RoutedEventArgs e)
    {
        var node = ExplorerTree.SelectedItem as WorkspaceNode;
        if (node == null || string.IsNullOrEmpty(node.FullPath)) return;

        try
        {
            string path = node.FullPath;
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not reveal in Explorer:\n{ex.Message}", "Reveal in Explorer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Explorer_OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        var node = ExplorerTree.SelectedItem as WorkspaceNode;
        if (node == null || string.IsNullOrEmpty(node.FullPath)) return;

        string targetDir = node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath) ?? "";
        if (string.IsNullOrEmpty(targetDir)) return;

        if (Terminal.Visibility != Visibility.Visible)
        {
            ToggleTerminal();
        }

        Terminal.RunCommand($"cd \"{targetDir}\"");
    }

    private void StartFileWatcher(TabContext ctx)
    {
        ctx.FileWatcher?.Dispose();
        ctx.FileWatcher = null;

        if (string.IsNullOrEmpty(ctx.FilePath) || !File.Exists(ctx.FilePath)) return;

        ctx.FileWatcher = new FileWatcherService(ctx.FilePath, (changeType) =>
        {
            if (changeType == WatcherChangeTypes.Deleted)
            {
                if (ctx.FileChangedBar != null)
                {
                    ctx.FileChangedBar.Visibility = Visibility.Visible;
                    var textBlock = (ctx.FileChangedBar.Child as Grid)?.Children.OfType<TextBlock>().FirstOrDefault();
                    if (textBlock != null) textBlock.Text = "⚠️ This file has been deleted on disk.";
                    if (ctx.FileChangedReloadBtn != null) ctx.FileChangedReloadBtn.IsEnabled = false;
                }
            }
            else
            {
                if (ctx.IsDirty)
                {
                    if (ctx.FileChangedBar != null)
                    {
                        ctx.FileChangedBar.Visibility = Visibility.Visible;
                        var textBlock = (ctx.FileChangedBar.Child as Grid)?.Children.OfType<TextBlock>().FirstOrDefault();
                        if (textBlock != null) textBlock.Text = "📄 This file has been modified externally.";
                        if (ctx.FileChangedReloadBtn != null) ctx.FileChangedReloadBtn.IsEnabled = true;
                    }
                }
                else
                {
                    ReloadFileFromDisk(ctx);
                }
            }
        });
    }

    private void StopFileWatcher(TabContext ctx)
    {
        ctx.FileWatcher?.Dispose();
        ctx.FileWatcher = null;
        if (ctx.FileChangedBar != null) ctx.FileChangedBar.Visibility = Visibility.Collapsed;
    }

    private void ReloadFileFromDisk(TabContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.FilePath) || !File.Exists(ctx.FilePath)) return;

        try
        {
            ctx.FileWatcher?.Suspend();
            if (ctx.Editor != null)
            {
                var loaded = FileService.ReadAllTextShared(ctx.FilePath);
                ctx.Editor.Text = loaded.Text;
                ctx.Encoding = loaded.Encoding;
                ctx.IsDirty = false;
                ctx.UpdateHeader();
                UpdateStatusBar();
                UpdateOutlineForActiveTab();
                if (ctx.PreviewColumn != null && ctx.PreviewColumn.Width.Value > 0)
                    RenderPreview(ctx);
                StatusText.Text = $"Reloaded {ctx.FilePath} from disk.";
            }
            else if (ctx.LargeView != null)
            {
                ctx.LargeView.Open(ctx.FilePath, ctx.Encoding);
                StatusText.Text = $"Reloaded {ctx.FilePath} from disk.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to reload file: {ex.Message}";
        }
        finally
        {
            ctx.FileWatcher?.Resume();
            if (ctx.FileChangedBar != null) ctx.FileChangedBar.Visibility = Visibility.Collapsed;
        }
    }

    private void SetupFileChangedBar(TabContext ctx, EditorLayoutResult built)
    {
        ctx.FileChangedBar = built.FileChangedBar;
        ctx.FileChangedReloadBtn = built.ReloadButton;
        built.ReloadButton.Click += (s, e) => ReloadFileFromDisk(ctx);
        built.DismissButton.Click += (s, e) => { if (ctx.FileChangedBar != null) ctx.FileChangedBar.Visibility = Visibility.Collapsed; };
    }

    private List<string>? _orphanedBackupFiles;

    private void CheckForOrphanedBackups()
    {
        string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "backups");
        if (!Directory.Exists(backupDir)) return;

        try
        {
            var filesOnDisk = Directory.GetFiles(backupDir, "*.txt");
            if (filesOnDisk.Length == 0) return;

            var activeBackups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TabItem tab in Tabs.Items)
            {
                if (tab.Tag is TabContext ctx && !string.IsNullOrEmpty(ctx.BackupPath))
                {
                    activeBackups.Add(Path.GetFullPath(ctx.BackupPath));
                }
            }

            var orphans = new List<string>();
            foreach (var file in filesOnDisk)
            {
                if (!activeBackups.Contains(Path.GetFullPath(file)))
                {
                    orphans.Add(file);
                }
            }

            if (orphans.Count > 0)
            {
                _orphanedBackupFiles = orphans;
                RecoveryBanner.Visibility = Visibility.Visible;
            }
        }
        catch { }
    }

    private void RecoverOrphans_Click(object sender, RoutedEventArgs e)
    {
        RecoveryBanner.Visibility = Visibility.Collapsed;
        if (_orphanedBackupFiles == null) return;

        try
        {
            foreach (var file in _orphanedBackupFiles)
            {
                if (!File.Exists(file)) continue;

                var editor = CreateEditor();
                var built = BuildEditorLayout(editor);
                
                string title = "Recovered Document";
                try
                {
                    string content = File.ReadAllText(file);
                    editor.Text = content;
                }
                catch { }

                var ctx = CreateTab(title, built.Container);
                ctx.Editor = editor;
                ctx.Encoding = Encoding.UTF8;
                ctx.IsDirty = true;
                ctx.BackupPath = file;
                ApplyLayoutResult(ctx, built);

                built.PreviewToggle.Click += (_, _) => SetPreviewVisible(ctx, built.PreviewToggle.IsChecked == true);
                built.OutlineToggle.Click += (_, _) => SetOutlineVisible(ctx, built.OutlineToggle.IsChecked == true);

                ctx.UpdateHeader();
                WireEditor(ctx);
                UpdateEditorLayoutCapabilities(ctx);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to recover files:\n{ex.Message}", "Crash Recovery", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _orphanedBackupFiles = null;
        }
    }

    private void DiscardOrphans_Click(object sender, RoutedEventArgs e)
    {
        RecoveryBanner.Visibility = Visibility.Collapsed;
        if (_orphanedBackupFiles == null) return;

        try
        {
            foreach (var file in _orphanedBackupFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
        catch { }
        finally
        {
            _orphanedBackupFiles = null;
        }
    }

    public void FlushAllBackupsForCrash()
    {
        string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "backups");
        try
        {
            Directory.CreateDirectory(backupDir);
            foreach (TabItem tab in Tabs.Items)
            {
                if (tab.Tag is TabContext ctx && ctx.Editor != null && ctx.IsDirty && !ctx.IsReadOnlyView)
                {
                    if (string.IsNullOrEmpty(ctx.BackupPath))
                    {
                        ctx.BackupPath = Path.Combine(backupDir, Guid.NewGuid().ToString() + ".txt");
                    }
                    File.WriteAllText(ctx.BackupPath, ctx.Editor.Text, ctx.Encoding);
                }
            }
            SaveSessionState();
        }
        catch { }
    }

    private void SaveSessionAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("Save Session", "Enter session name:", "MySession") { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.InputText))
        {
            string name = dlg.InputText.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            string sessionsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "sessions");
            try
            {
                Directory.CreateDirectory(sessionsDir);
                string sessionPath = Path.Combine(sessionsDir, name + ".json");

                var session = new SessionState
                {
                    ActiveTabIndex = Tabs.SelectedIndex,
                    Tabs = new List<TabState>()
                };

                foreach (TabItem tab in Tabs.Items)
                {
                    if (tab.Tag is TabContext ctx)
                    {
                        var tState = new TabState
                        {
                            FilePath = ctx.FilePath,
                            Title = ctx.HeaderText.Text.TrimEnd('*', ' '),
                            IsDirty = ctx.IsDirty,
                            BackupPath = ctx.BackupPath,
                            EncodingName = ctx.Encoding.WebName,
                            OutlineVisible = ctx.OutlineColumn != null && ctx.OutlineColumn.Width.Value > 0,
                            PreviewVisible = ctx.PreviewColumn != null && ctx.PreviewColumn.Width.Value > 0
                        };

                        if (ctx.Editor != null)
                        {
                            tState.CaretOffset = ctx.Editor.CaretOffset;
                            tState.ScrollHorizontalOffset = ctx.Editor.HorizontalOffset;
                            tState.ScrollVerticalOffset = ctx.Editor.VerticalOffset;
                        }
                        else if (ctx.LargeView != null)
                        {
                            tState.CaretOffset = ctx.LargeView.SelectedIndex;
                            tState.ScrollVerticalOffset = ctx.LargeView.VerticalScrollOffset;
                        }

                        session.Tabs.Add(tState);
                    }
                }

                string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sessionPath, json);
                StatusText.Text = $"Session saved as '{name}'";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save session:\n{ex.Message}", "Save Session", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OpenSession_Click(object sender, RoutedEventArgs e)
    {
        string sessionsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "sessions");
        if (!Directory.Exists(sessionsDir))
        {
            MessageBox.Show(this, "No saved sessions found.", "Open Session", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var files = Directory.GetFiles(sessionsDir, "*.json");
        if (files.Length == 0)
        {
            MessageBox.Show(this, "No saved sessions found.", "Open Session", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sessionNames = new List<string>();
        foreach (var file in files)
        {
            sessionNames.Add(Path.GetFileNameWithoutExtension(file));
        }

        var picker = new Controls.SessionPickerDialog(sessionNames, "Open", "Open Session") { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedSession != null)
        {
            string sessionPath = Path.Combine(sessionsDir, picker.SelectedSession + ".json");
            LoadSessionFile(sessionPath);
        }
    }

    private void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        string sessionsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote", "sessions");
        if (!Directory.Exists(sessionsDir))
        {
            MessageBox.Show(this, "No saved sessions found.", "Delete Session", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var files = Directory.GetFiles(sessionsDir, "*.json");
        if (files.Length == 0)
        {
            MessageBox.Show(this, "No saved sessions found.", "Delete Session", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sessionNames = new List<string>();
        foreach (var file in files)
        {
            sessionNames.Add(Path.GetFileNameWithoutExtension(file));
        }

        var picker = new Controls.SessionPickerDialog(sessionNames, "Delete", "Delete Session") { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedSession != null)
        {
            string sessionPath = Path.Combine(sessionsDir, picker.SelectedSession + ".json");
            try
            {
                if (File.Exists(sessionPath))
                {
                    File.Delete(sessionPath);
                    StatusText.Text = $"Session '{picker.SelectedSession}' deleted.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to delete session:\n{ex.Message}", "Delete Session", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // Bookmark Command Handlers
    private void ActiveEditor_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = CurrentContext is { IsReadOnlyView: false, Editor: not null };
    }

    private void ToggleBookmark_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx is { IsReadOnlyView: false, Editor: not null, FilePath: not null })
        {
            int line = ctx.Editor.TextArea.Caret.Line;
            BookmarkService.Instance.ToggleBookmark(ctx.FilePath, line);
        }
    }

    private void NextBookmark_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx is { Editor: not null, FilePath: not null })
        {
            var bookmarks = new List<int>(BookmarkService.Instance.GetBookmarks(ctx.FilePath));
            if (bookmarks.Count == 0) return;
            bookmarks.Sort();

            int currentLine = ctx.Editor.TextArea.Caret.Line;
            int nextLine = bookmarks[0];
            foreach (int b in bookmarks)
            {
                if (b > currentLine)
                {
                    nextLine = b;
                    break;
                }
            }
            ctx.Editor.ScrollTo(nextLine, 1);
            ctx.Editor.TextArea.Caret.Line = nextLine;
        }
    }

    private void PrevBookmark_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx is { Editor: not null, FilePath: not null })
        {
            var bookmarks = new List<int>(BookmarkService.Instance.GetBookmarks(ctx.FilePath));
            if (bookmarks.Count == 0) return;
            bookmarks.Sort();

            int currentLine = ctx.Editor.TextArea.Caret.Line;
            int prevLine = bookmarks[bookmarks.Count - 1];
            for (int i = bookmarks.Count - 1; i >= 0; i--)
            {
                if (bookmarks[i] < currentLine)
                {
                    prevLine = bookmarks[i];
                    break;
                }
            }
            ctx.Editor.ScrollTo(prevLine, 1);
            ctx.Editor.TextArea.Caret.Line = prevLine;
        }
    }

    private void ClearBookmarks_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var ctx = CurrentContext;
        if (ctx is { FilePath: not null })
        {
            BookmarkService.Instance.ClearBookmarks(ctx.FilePath);
        }
    }

    private void BookmarksList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BookmarksList.SelectedItem is BookmarkItem item)
        {
            OpenFile(item.FilePath);
            var ctx = CurrentContext;
            if (ctx is { Editor: not null })
            {
                ctx.Editor.ScrollTo(item.LineNumber, 1);
                ctx.Editor.TextArea.Caret.Line = item.LineNumber;
            }
        }
    }

    private void SidebarTabBookmarks_Click(object sender, RoutedEventArgs e)
    {
        SwitchSidebarTab(SidebarTab.Bookmarks);
    }


}




public class SearchResultFileNode
{
    public string FilePath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public System.Collections.ObjectModel.ObservableCollection<SearchResultMatchNode> Matches { get; } = new();

    public string DisplayText => $"{System.IO.Path.GetFileName(FilePath)} ({Matches.Count} match{(Matches.Count == 1 ? "" : "es")}) in {System.IO.Path.GetDirectoryName(RelativePath)}";
}

public class SearchResultMatchNode
{
    public SearchResultFileNode Parent { get; }
    public int LineNumber { get; set; }
    public int Column { get; set; }
    public int Length { get; set; }
    public string LineText { get; set; } = "";

    public string SearchTerm { get; set; } = "";
    public bool MatchCase { get; set; }
    public bool WholeWord { get; set; }
    public bool UseRegex { get; set; }

    public SearchResultMatchNode(SearchResultFileNode parent)
    {
        Parent = parent;
    }
}

public class InputDialog : Window
{
    private readonly TextBox _textBox;
    private bool _isClosing;

    public string InputText => _textBox.Text;

    public InputDialog(string title, string promptText, string defaultVal = "")
    {
        Title = title;
        Width = 350;
        Height = 120;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10)
        };
        border.SetResourceReference(Border.BackgroundProperty, "App.Background");
        border.SetResourceReference(Border.BorderBrushProperty, "App.Border");

        var shadow = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 12,
            ShadowDepth = 2,
            Direction = 270,
            Color = System.Windows.Media.Colors.Black,
            Opacity = 0.45
        };
        border.Effect = shadow;

        var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

        var prompt = new TextBlock
        {
            Text = promptText,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        };
        prompt.SetResourceReference(TextBlock.ForegroundProperty, "App.Foreground");

        _textBox = new TextBox
        {
            Text = defaultVal,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 4, 2, 4)
        };
        _textBox.SetResourceReference(TextBox.ForegroundProperty, "App.Foreground");
        _textBox.SetResourceReference(TextBox.BorderBrushProperty, "App.Border");
        _textBox.SetResourceReference(TextBox.CaretBrushProperty, "App.Foreground");

        _textBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
                _isClosing = true;
                Close();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                _isClosing = true;
                Close();
            }
        };

        stack.Children.Add(prompt);
        stack.Children.Add(_textBox);
        border.Child = stack;
        Content = border;

        Loaded += (s, e) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };

        Deactivated += (s, e) =>
        {
            if (!_isClosing)
            {
                Close();
            }
        };
        
        Closing += (s, e) => _isClosing = true;
    }
}

public class BookmarkItem
{
    public string FilePath { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public int LineNumber { get; set; }
    public string PreviewText { get; set; } = "";
}


