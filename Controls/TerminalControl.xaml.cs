using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HyperNote.Services;

namespace HyperNote.Controls;

public partial class TerminalControl : UserControl, IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private bool _isDisposed;

    public event Action? CloseRequested;
    public event Action<string>? PushOutputRequested;

    public TerminalControl()
    {
        InitializeComponent();
        Loaded += TerminalControl_Loaded;
    }

    public void ApplySettings()
    {
        var settings = SettingsService.Instance.Settings;
        var ff = new System.Windows.Media.FontFamily(settings.TerminalFontFamily);
        TerminalOutput.FontFamily = ff;
        TerminalOutput.FontSize = settings.TerminalFontSize;
        TerminalInput.FontFamily = ff;
        TerminalInput.FontSize = settings.TerminalFontSize;

        // Restart process if shell changed
        if (_process != null && !_process.HasExited && 
            !string.Equals(_process.StartInfo.FileName, settings.TerminalShellPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _stdin?.WriteLine("exit");
                if (!_process.WaitForExit(300))
                {
                    _process.Kill();
                }
            }
            catch {}
            _stdin?.Dispose();
            _process?.Dispose();
            _process = null;
        }
    }

    private void TerminalControl_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        if (_process == null)
        {
            StartShell();
        }
    }

    public void FocusInput()
    {
        TerminalInput.Focus();
    }

    public void RunCommand(string command)
    {
        if (_stdin != null && _process != null && !_process.HasExited)
        {
            AppendText($"PS > {command}\n");
            _stdin.WriteLine(command);
        }
        else
        {
            StartShell();
            if (_stdin != null)
            {
                AppendText($"PS > {command}\n");
                _stdin.WriteLine(command);
            }
        }
    }

    private void StartShell()
    {
        try
        {
            string shell = SettingsService.Instance.Settings.TerminalShellPath;
            if (string.IsNullOrEmpty(shell)) shell = "powershell.exe";

            bool isPowerShell = shell.Contains("powershell", StringComparison.OrdinalIgnoreCase) || shell.EndsWith("pwsh", StringComparison.OrdinalIgnoreCase);

            var info = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = isPowerShell ? "-NoLogo -NoProfile" : "",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _process = new Process { StartInfo = info };
            _process.Start();

            _stdin = _process.StandardInput;

            // Start background readers
            Task.Run(() => ReadStreamAsync(_process.StandardOutput));
            Task.Run(() => ReadStreamAsync(_process.StandardError));

            // Write initial message
            string shellName = Path.GetFileNameWithoutExtension(shell);
            AppendText($"{shellName} session started.\n");
        }
        catch (Exception ex)
        {
            AppendText($"Failed to start shell: {ex.Message}\n");
        }
    }

    private async Task ReadStreamAsync(StreamReader reader)
    {
        char[] buffer = new char[1024];
        int read;
        try
        {
            while (!_isDisposed && (read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                string text = new string(buffer, 0, read);
                _ = Dispatcher.BeginInvoke(() => AppendText(text));
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            _ = Dispatcher.BeginInvoke(() => AppendText($"\n[Error reading stream: {ex.Message}]\n"));
        }
    }

    private void AppendText(string text)
    {
        TerminalOutput.AppendText(text);
        TerminalOutput.ScrollToEnd();
    }

    private void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string command = TerminalInput.Text;
            TerminalInput.Clear();

            if (_stdin != null && _process != null && !_process.HasExited)
            {
                AppendText($"PS > {command}\n");
                _stdin.WriteLine(command);
            }
            else
            {
                AppendText("[Shell process has exited. Restarting...]\n");
                StartShell();
                if (_stdin != null)
                {
                    AppendText($"PS > {command}\n");
                    _stdin.WriteLine(command);
                }
            }
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _stdin?.WriteLine("exit");
                // Wait briefly for clean exit
                if (!_process.WaitForExit(300))
                {
                    _process.Kill();
                }
            }
        }
        catch { }
        finally
        {
            _stdin?.Dispose();
            _process?.Dispose();
            _process = null;
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }

    private void PushOutputBtn_Click(object sender, RoutedEventArgs e)
    {
        PushOutputRequested?.Invoke(TerminalOutput.Text);
    }
}
