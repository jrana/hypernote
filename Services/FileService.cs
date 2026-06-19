using System.IO;
using System.Text;

namespace HyperNote.Services;

/// <summary>
/// File access that tolerates files held open by other applications.
///
/// Strategy: always open with the most permissive sharing flags
/// (FileShare.ReadWrite | FileShare.Delete). Most apps (log writers, Office,
/// databases writing to text logs, etc.) open files with at least read sharing,
/// so requesting permissive sharing on our side lets us read them while they
/// remain open and even while they are being written to.
///
/// Note: a file opened by another process with FileShare.None (a true exclusive
/// lock) cannot be read by normal Win32 APIs at all; that requires kernel-level
/// tricks such as Volume Shadow Copy and is out of scope here. In practice the
/// permissive-share open covers the overwhelming majority of "file is in use"
/// errors users hit in editors like Notepad.
/// </summary>
public static class FileService
{
    public static FileStream OpenSharedRead(string path, int bufferSize = 1 << 16) =>
        new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize, FileOptions.RandomAccess);

    public record TextLoadResult(string Text, Encoding Encoding);

    /// <summary>Reads an entire text file with BOM-based encoding detection, using shared access.</summary>
    public static TextLoadResult ReadAllTextShared(string path)
    {
        using var stream = OpenSharedRead(path);
        using var reader = new StreamReader(stream, new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        return new TextLoadResult(text, reader.CurrentEncoding);
    }

    /// <summary>
    /// Saves text atomically: writes to a temp file in the same directory, then replaces.
    /// Prevents data loss if the app dies mid-write.
    /// </summary>
    public static void WriteAllTextSafe(string path, string text, Encoding encoding, string? lineEnding = null)
    {
        if (lineEnding != null)
        {
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            if (lineEnding == "CRLF")
                text = text.Replace("\n", "\r\n");
            else if (lineEnding == "CR")
                text = text.Replace("\n", "\r");
        }

        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string tmp = Path.Combine(dir, "." + Path.GetFileName(path) + ".qnp-tmp");
        File.WriteAllText(tmp, text, encoding);
        if (File.Exists(path))
            File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(tmp, path);
    }
}
