using System;
using System.IO;

namespace HyperNote.Services;

public class HexLineItem
{
    public long Offset { get; set; }
    public string OffsetHex => Offset.ToString("X8");
    public string BytesHex { get; set; } = "";
    public string Ascii { get; set; } = "";
}

public class HexFileProvider : System.Collections.IList, IDisposable
{
    private readonly FileStream _stream;
    private readonly long _fileLength;
    private int _bytesPerRow;

    public HexFileProvider(string path, int bytesPerRow)
    {
        _bytesPerRow = bytesPerRow;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _fileLength = _stream.Length;
    }

    public int BytesPerRow
    {
        get => _bytesPerRow;
        set => _bytesPerRow = value;
    }

    public long FileLength => _fileLength;

    public byte GetByteAtOffset(long offset)
    {
        if (offset < 0 || offset >= _fileLength) return 0;
        lock (_stream)
        {
            _stream.Seek(offset, SeekOrigin.Begin);
            return (byte)_stream.ReadByte();
        }
    }

    public object? this[int index]
    {
        get
        {
            long offset = (long)index * _bytesPerRow;
            if (offset >= _fileLength) return null;

            byte[] buffer = new byte[_bytesPerRow];
            int bytesRead;
            lock (_stream)
            {
                _stream.Seek(offset, SeekOrigin.Begin);
                bytesRead = _stream.Read(buffer, 0, _bytesPerRow);
            }

            var hexBytes = new string[bytesRead];
            var asciiChars = new char[bytesRead];
            for (int i = 0; i < bytesRead; i++)
            {
                byte b = buffer[i];
                hexBytes[i] = b.ToString("X2");
                asciiChars[i] = (b >= 32 && b <= 126) ? (char)b : '.';
            }

            string hexStr = string.Join(" ", hexBytes);
            if (bytesRead < _bytesPerRow)
            {
                hexStr = hexStr.PadRight(_bytesPerRow * 3 - 1);
            }

            return new HexLineItem
            {
                Offset = offset,
                BytesHex = hexStr,
                Ascii = new string(asciiChars)
            };
        }
        set => throw new NotSupportedException();
    }

    public int Count => (int)((_fileLength + _bytesPerRow - 1) / _bytesPerRow);

    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(object? value) => false;
    public int IndexOf(object? value) => -1;
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) => throw new NotSupportedException();
    public object SyncRoot => this;
    public bool IsSynchronized => false;
    public System.Collections.IEnumerator GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
