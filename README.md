# HyperNote

A fast, native Windows text editor (WPF / .NET 8) designed to open multi‑gigabyte
files without memory issues.

## Features

- **Huge file support** — files at or above 32 MB (configurable) open in a streaming,
  read‑only viewer. Only line *offsets* are kept in memory (8 bytes/line); line text is
  read from disk on demand, so a 5 GB log uses a flat, tiny memory footprint. A
  background indexer shows progress while you can already scroll the file. Includes
  go‑to‑line.
- **Tabs** — open many files at once; close with the ✕ button or Ctrl+W; drag & drop
  files onto the window.
- **New files** — Ctrl+N creates an Untitled tab; Save As assigns a path and picks up
  syntax highlighting automatically.
- **Syntax highlighting** for well‑known extensions (C#, C/C++, Java, Python, JS/TS,
  JSON, XML/XAML, HTML, CSS, SQL, PowerShell, PHP, VB, Markdown, diff/patch, TeX, …).
- **Live preview (Markdown & HTML)** — for `.md` and `.html`/`.htm` files a
  side‑by‑side preview pane renders live as you type. Toggle it three ways: the
  **Preview** button above the editor (mouse), **Ctrl+Shift+V**, or
  View → Toggle Preview. Markdown is rendered with Markdig and themed to match the
  app; HTML renders your own document. The preview is served from a stable virtual
  origin, so **relative images/CSS resolve** from the file's folder and **scroll
  position is preserved** across live updates. The splitter between editor and
  preview is draggable.
- **PDF viewing** — `.pdf` files open in a tab using the WebView2 (Edge) built‑in PDF
  renderer: zoom, search, page navigation included.
- **Hierarchical folding** — tag-based markup (HTML, XML, XAML, SVG, `.csproj`,
  `.config`, `.resx`, RSS/Atom, and other angle-bracket formats) gets collapsible
  open/close **tag folding** via a custom fault-tolerant parser: it handles HTML void
  elements (`<br>`, `<img>`…), optionally-closed tags (`<li>`, `<p>`), `>` inside
  attribute values, raw `<script>`/`<style>` bodies, and multi-line `<!-- comments -->`,
  and keeps folding even while markup is malformed mid-edit. JSON (and braced languages
  like C#/JS/CSS) get `{…}` / `[…]` block folding. Collapse/expand via the markers in
  the fold margin next to the line numbers.
- **Opens locked files** — all reads use `FileShare.ReadWrite | FileShare.Delete`, so
  files held open by other applications (active log files, etc.) open fine. If a file
  is locked for *writing* when you save, HyperNote offers Save As. (Files locked
  with a true exclusive lock — `FileShare.None` — cannot be read by any normal Win32
  API; that would require Volume Shadow Copy.)
- **Recent files** — File → Open Recent, persisted to `%APPDATA%\HyperNote\settings.json`.
- **Light & dark themes** — View → Dark Theme; remembered across sessions; applies to
  the editor, large‑file viewer, and markdown preview.
- **Line numbers** — always on, in both the editor and the large‑file viewer.
- **Find & Replace** — a Find / Find&Replace tool window (Edit menu, **Ctrl+F** / **Ctrl+H**) with **Match case**, **Match whole word only**, and **Direction (Up/Down)** options, plus **Replace**, **Replace All**, and **Count**. **F3** / **Shift+F3** repeat the search forward / backward. Searching wraps around the document and operates on whichever editor tab is active; the window stays open while you keep editing.
- Atomic saves (write temp + replace) so a crash mid‑save can't corrupt your file.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+N | New file |
| Ctrl+O | Open file(s) |
| Ctrl+S / Ctrl+Shift+S | Save / Save As |
| Ctrl+W | Close tab |
| Ctrl+F | Find |
| Ctrl+H | Find & Replace |
| F3 / Shift+F3 | Find next / previous |
| Ctrl+Shift+V | Toggle preview (Markdown/HTML) |

## Building

Requirements (on Windows):

1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) —
   preinstalled on Windows 11 and most updated Windows 10 machines.

```powershell
cd HyperNote
dotnet build -c Release
dotnet run -c Release
```

Or open the folder in Visual Studio 2022 and press F5. To produce a single‑file
distributable exe:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

> Note: WPF only builds on Windows — this project cannot be compiled on Linux/macOS.

## Architecture notes

- **Two‑mode opening.** `MainWindow.OpenFile` routes by extension and size:
  PDFs → WebView2 tab; files ≥ threshold → `LargeFileView`; everything else →
  AvalonEdit editor with highlighting/folding.
- **`LargeFileLineProvider`** (Services/) is the core of huge‑file support. It exposes
  the file as a virtual `IList` of `LineItem`s to a WPF `VirtualizingStackPanel`, so
  only visible rows are ever materialized. A background thread scans for `\n` offsets
  in 1 MB chunks and publishes progress every 250 ms. Large‑file mode is intentionally
  **read‑only**: in‑place editing of multi‑GB files requires a piece‑table engine and
  rewrite‑on‑save semantics that trade away exactly the safety and speed this mode is
  for. Adjust the threshold via `LargeFileThresholdBytes` in
  `%APPDATA%\HyperNote\settings.json`.
- **Encoding.** Normal editor tabs detect BOM encodings and preserve them on save.
  The large‑file viewer assumes UTF‑8/ASCII (invalid bytes shown as replacement
  characters), which covers virtually all gigabyte‑class files (logs, exports, dumps).
- **Known cosmetic limits.** Menu *dropdown popups* use system colors (readable in
  both themes but not fully dark‑skinned — fully retemplating WPF menus is verbose),
  and AvalonEdit's built‑in highlighting palettes are tuned for light backgrounds, so
  some token colors are lower‑contrast in dark mode. Both are straightforward
  follow‑ups if you want them polished.

## Project layout

```
HyperNote/
├── HyperNote.csproj          # .NET 8 WPF project + NuGet refs
├── App.xaml / App.xaml.cs       # startup, theme restore
├── MainWindow.xaml / .cs        # tabs, menus, commands, editors, preview, PDF
├── Themes/Light.xaml, Dark.xaml # theme resource dictionaries
├── Controls/LargeFileView.*     # virtualized huge-file viewer UI
└── Services/
    ├── LargeFileLineProvider.cs # background line indexer + on-demand reads
    ├── FileService.cs           # lock-tolerant open, atomic save
    ├── SyntaxServices.cs        # extension→highlighting map, JSON folding
    ├── SettingsService.cs       # settings + recent files persistence
    └── ThemeManager.cs          # light/dark switching
```
