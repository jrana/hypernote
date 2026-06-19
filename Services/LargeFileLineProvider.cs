using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace HyperNote.Services;

public record struct SearchResult(int LineIndex, int CharIndex, int Length);

public sealed class LineDescriptor
{
    public long FileOffset;
    public int ByteLength;
    public string? MemoryText;
}

public sealed class LargeFileLineProvider : IDisposable
{
    private const int MaxDisplayLineChars = 8000;
    private const int CacheCapacity = 4096;

    private readonly string _path;
    private readonly FileStream _stream;
    private readonly object _streamLock = new();
    private readonly List<LineDescriptor> _lines = new();
    private readonly object _linesLock = new();
    private readonly Dictionary<int, string> _cache = new();
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();

    private volatile int _publishedCount;
    private long _fileLength;

    public event Action<long, long, bool>? IndexProgress;

    public long FileLength => _fileLength;
    public bool IndexingComplete { get; private set; }
    public Encoding Encoding { get; }

    public object SyncRoot { get; } = new();

    public LargeFileLineProvider(string path, Encoding encoding, Dispatcher dispatcher)
    {
        _path = path;
        Encoding = encoding;
        _dispatcher = dispatcher;
        _stream = FileService.OpenSharedRead(path, 1 << 20);
        _fileLength = _stream.Length;
        Task.Run(() => IndexAsync(_cts.Token));
    }

    private void IndexAsync(CancellationToken ct)
    {
        try
        {
            using var scan = FileService.OpenSharedRead(_path, 1 << 20);
            var buffer = new byte[1 << 20];
            long pos = 0;

            long startOffset = 0;
            byte[] bomBytes = new byte[4];
            int bomLen = scan.Read(bomBytes, 0, 4);
            scan.Seek(0, SeekOrigin.Begin);

            if (bomLen >= 2 && bomBytes[0] == 0xFF && bomBytes[1] == 0xFE)
                startOffset = 2;
            else if (bomLen >= 2 && bomBytes[0] == 0xFE && bomBytes[1] == 0xFF)
                startOffset = 2;
            else if (bomLen >= 3 && bomBytes[0] == 0xEF && bomBytes[1] == 0xBB && bomBytes[2] == 0xBF)
                startOffset = 3;

            scan.Seek(startOffset, SeekOrigin.Begin);
            pos = startOffset;

            var lastPublish = DateTime.UtcNow;
            int read;

            string name = Encoding.WebName;
            bool isUtf16Le = name.Equals("utf-16", StringComparison.OrdinalIgnoreCase);
            bool isUtf16Be = name.Equals("utf-16BE", StringComparison.OrdinalIgnoreCase);

            var offsets = new List<long> { startOffset };

            if (isUtf16Le)
            {
                while ((read = scan.Read(buffer, 0, buffer.Length & ~1)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int i = 0; i < read; i += 2)
                    {
                        if (buffer[i] == 0x0A && buffer[i + 1] == 0x00)
                        {
                            offsets.Add(pos + i + 2);
                        }
                    }
                    pos += read;

                    if ((DateTime.UtcNow - lastPublish).TotalMilliseconds > 250)
                    {
                        lastPublish = DateTime.UtcNow;
                        PublishProgress(offsets, pos, done: false);
                    }
                }
            }
            else if (isUtf16Be)
            {
                while ((read = scan.Read(buffer, 0, buffer.Length & ~1)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int i = 0; i < read; i += 2)
                    {
                        if (buffer[i] == 0x00 && buffer[i + 1] == 0x0A)
                        {
                            offsets.Add(pos + i + 2);
                        }
                    }
                    pos += read;

                    if ((DateTime.UtcNow - lastPublish).TotalMilliseconds > 250)
                    {
                        lastPublish = DateTime.UtcNow;
                        PublishProgress(offsets, pos, done: false);
                    }
                }
            }
            else
            {
                while ((read = scan.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] == (byte)'\n')
                        {
                            offsets.Add(pos + i + 1);
                        }
                    }
                    pos += read;

                    if ((DateTime.UtcNow - lastPublish).TotalMilliseconds > 250)
                    {
                        lastPublish = DateTime.UtcNow;
                        PublishProgress(offsets, pos, done: false);
                    }
                }
            }

            if (offsets.Count > 1 && offsets[^1] >= scan.Length)
                offsets.RemoveAt(offsets.Count - 1);

            IndexingComplete = true;
            PublishProgress(offsets, pos, done: true);
        }
        catch (OperationCanceledException) { }
        catch
        {
            IndexingComplete = true;
            PublishProgress(new List<long>(), 0, done: true);
        }
    }

    private void PublishProgress(List<long> offsets, long bytesScanned, bool done)
    {
        lock (_linesLock)
        {
            _lines.Clear();
            long fileLength = _fileLength;
            for (int i = 0; i < offsets.Count; i++)
            {
                long start = offsets[i];
                long end = (i + 1 < offsets.Count) ? offsets[i + 1] : fileLength;
                _lines.Add(new LineDescriptor
                {
                    FileOffset = start,
                    ByteLength = (int)(end - start),
                    MemoryText = null
                });
            }
            _publishedCount = _lines.Count;
        }

        _dispatcher.BeginInvoke(() =>
        {
            IndexProgress?.Invoke(_publishedCount, bytesScanned, done);
        });
    }

    public int Count
    {
        get
        {
            lock (_linesLock) return _publishedCount;
        }
    }

    public string GetLineText(int index)
    {
        return ReadLine(index);
    }

    private string ReadLine(int index)
    {
        LineDescriptor desc;
        lock (_linesLock)
        {
            if (index < 0 || index >= _lines.Count) return "";
            desc = _lines[index];
        }

        if (desc.MemoryText != null)
        {
            return desc.MemoryText;
        }

        lock (_streamLock)
        {
            if (desc.ByteLength <= 0) return "";
            if (_cache.TryGetValue(index, out var cached)) return cached;

            var bytes = new byte[desc.ByteLength];
            _stream.Seek(desc.FileOffset, SeekOrigin.Begin);
            int n = _stream.Read(bytes, 0, desc.ByteLength);

            string text = Encoding.GetString(bytes, 0, n).TrimEnd('\r', '\n');
            if (text.Length > MaxDisplayLineChars)
                text = text[..MaxDisplayLineChars] + "  …";

            if (_cache.Count >= CacheCapacity) _cache.Clear();
            _cache[index] = text;
            return text;
        }
    }

    private string ReadLineDirect(FileStream stream, int index)
    {
        LineDescriptor desc;
        lock (_linesLock)
        {
            if (index < 0 || index >= _lines.Count) return "";
            desc = _lines[index];
        }

        if (desc.MemoryText != null)
        {
            return desc.MemoryText;
        }

        if (desc.ByteLength <= 0) return "";

        var bytes = new byte[desc.ByteLength];
        stream.Seek(desc.FileOffset, SeekOrigin.Begin);
        int n = stream.Read(bytes, 0, desc.ByteLength);

        string text = Encoding.GetString(bytes, 0, n).TrimEnd('\r', '\n');
        if (text.Length > MaxDisplayLineChars)
            text = text[..MaxDisplayLineChars];

        return text;
    }

    public void ReplaceRange(int index, int count, List<string> newLines)
    {
        lock (_linesLock)
        {
            _lines.RemoveRange(index, count);
            var newDescs = newLines.Select(line => new LineDescriptor { MemoryText = line }).ToList();
            _lines.InsertRange(index, newDescs);
            _publishedCount = _lines.Count;
            _cache.Clear();
        }
    }

    public async Task<SearchResult?> FindNextAsync(string term, int startLineIndex, SearchOptions options)
    {
        return await Task.Run<SearchResult?>(() =>
        {
            int totalLines = Count;
            if (totalLines == 0) return null;

            startLineIndex = (startLineIndex % totalLines + totalLines) % totalLines;

            var regex = TextSearch.BuildRegex(term, options.MatchCase, options.WholeWord, options.UseRegex);
            using var stream = FileService.OpenSharedRead(_path);

            int currentLine = startLineIndex;
            int linesChecked = 0;

            while (linesChecked < totalLines)
            {
                string lineText = ReadLineDirect(stream, currentLine);
                if (regex.IsMatch(lineText))
                {
                    var match = regex.Match(lineText);
                    return new SearchResult(currentLine, match.Index, match.Length);
                }

                if (!options.Backward)
                    currentLine = (currentLine + 1) % totalLines;
                else
                    currentLine = (currentLine - 1 + totalLines) % totalLines;
                
                linesChecked++;
            }

            return null;
        });
    }

    public async Task<int> CountMatchesAsync(string term, bool matchCase, bool wholeWord, bool useRegex = false)
    {
        return await Task.Run(() =>
        {
            int totalLines = Count;
            if (totalLines == 0) return 0;

            var regex = TextSearch.BuildRegex(term, matchCase, wholeWord, useRegex);
            using var stream = FileService.OpenSharedRead(_path);

            int matchCount = 0;
            for (int i = 0; i < totalLines; i++)
            {
                string lineText = ReadLineDirect(stream, i);
                matchCount += regex.Matches(lineText).Count;
            }

            return matchCount;
        });
    }

    public void Save(string savePath)
    {
        string tempFile = savePath + ".tmp";
        try
        {
            using (var outStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (var inStream = FileService.OpenSharedRead(_path))
            {
                byte[] lineEndingBytes = Encoding.GetBytes(Environment.NewLine);
                
                List<LineDescriptor> localLines;
                lock (_linesLock)
                {
                    localLines = new List<LineDescriptor>(_lines);
                }

                for (int i = 0; i < localLines.Count; i++)
                {
                    var desc = localLines[i];
                    if (desc.MemoryText != null)
                    {
                        byte[] bytes = Encoding.GetBytes(desc.MemoryText);
                        outStream.Write(bytes, 0, bytes.Length);
                        outStream.Write(lineEndingBytes, 0, lineEndingBytes.Length);
                    }
                    else if (desc.ByteLength > 0)
                    {
                        var bytes = new byte[desc.ByteLength];
                        inStream.Seek(desc.FileOffset, SeekOrigin.Begin);
                        int n = inStream.Read(bytes, 0, desc.ByteLength);
                        outStream.Write(bytes, 0, n);
                    }
                }
            }

            if (File.Exists(savePath))
                File.Replace(tempFile, savePath, null);
            else
                File.Move(tempFile, savePath);
        }
        catch
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
            throw;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_streamLock) _stream.Dispose();
    }
}
