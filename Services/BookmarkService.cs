using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HyperNote.Services;

public class BookmarkService
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HyperNote");
    private static readonly string FilePath = Path.Combine(Dir, "bookmarks.json");

    public static BookmarkService Instance { get; } = new();

    private Dictionary<string, HashSet<int>> _bookmarks = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? BookmarksChanged;

    private BookmarkService()
    {
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(File.ReadAllText(FilePath));
                if (data != null)
                {
                    _bookmarks = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in data)
                    {
                        _bookmarks[kv.Key] = new HashSet<int>(kv.Value);
                    }
                }
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var data = new Dictionary<string, List<int>>();
            foreach (var kv in _bookmarks)
            {
                if (kv.Value.Count > 0)
                {
                    var sortedList = new List<int>(kv.Value);
                    sortedList.Sort();
                    data[kv.Key] = sortedList;
                }
            }
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public HashSet<int> GetBookmarks(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return new HashSet<int>();
        filePath = Path.GetFullPath(filePath);
        if (!_bookmarks.TryGetValue(filePath, out var set))
        {
            set = new HashSet<int>();
            _bookmarks[filePath] = set;
        }
        return set;
    }

    public bool ToggleBookmark(string filePath, int line)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        filePath = Path.GetFullPath(filePath);
        var set = GetBookmarks(filePath);
        bool added;
        if (set.Contains(line))
        {
            set.Remove(line);
            added = false;
        }
        else
        {
            set.Add(line);
            added = true;
        }
        Save();
        BookmarksChanged?.Invoke(filePath);
        return added;
    }

    public void ClearBookmarks(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        filePath = Path.GetFullPath(filePath);
        if (_bookmarks.ContainsKey(filePath))
        {
            _bookmarks[filePath].Clear();
            Save();
            BookmarksChanged?.Invoke(filePath);
        }
    }

    public Dictionary<string, HashSet<int>> GetAllBookmarks() => _bookmarks;
}
