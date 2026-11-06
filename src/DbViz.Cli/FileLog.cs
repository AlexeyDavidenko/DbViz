using System.Text;
using DbViz.Core;

namespace DbViz.Cli;

/// <summary>Logs to the console and to out/logs/dbviz-&lt;command&gt;-&lt;time&gt;.log (full stack traces go to the file only).</summary>
public sealed class FileLog : ILog, IDisposable
{
    private readonly StreamWriter? _file;
    public string? FilePath { get; }

    public FileLog(string? logDir, string command)
    {
        if (logDir is null) return;
        Directory.CreateDirectory(logDir);
        FilePath = Path.Combine(logDir, $"dbviz-{command}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _file = new StreamWriter(FilePath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Info(string message) => Write("INFO ", message, ConsoleColor.Gray);
    public void Warn(string message) => Write("WARN ", message, ConsoleColor.Yellow);

    public void Error(string message, Exception? ex = null)
    {
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}", ConsoleColor.Red);
        if (ex is not null) _file?.WriteLine(ex.ToString());
    }

    private void Write(string level, string message, ConsoleColor color)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {level} {message}";
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(line);
        Console.ForegroundColor = prev;
        _file?.WriteLine(line);
    }

    public void Dispose() => _file?.Dispose();
}
