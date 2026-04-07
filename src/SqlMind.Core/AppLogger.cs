using System.Collections.Concurrent;

namespace SqlMind.Core;

public static class AppLogger
{
    private static readonly ConcurrentQueue<LogEntry> _logs = new();
    private const int Max = 150;

    public static void Info(string msg, string? cid = null)    => Add("INFO",    msg, cid);
    public static void Success(string msg, string? cid = null) => Add("SUCCESS", msg, cid);
    public static void Warn(string msg, string? cid = null)    => Add("WARN",    msg, cid);
    public static void Error(string msg, string? cid = null)   => Add("ERROR",   msg, cid);

    private static void Add(string level, string msg, string? cid)
    {
        _logs.Enqueue(new LogEntry { Timestamp = DateTime.UtcNow, Level = level, Message = msg, CorrelationId = cid });
        while (_logs.Count > Max) _logs.TryDequeue(out _);
    }

    public static List<LogEntry> GetAll() => _logs.ToList();
    public static void Clear() => _logs.Clear();
}

public class LogEntry
{
    public DateTime Timestamp     { get; set; }
    public string   Level         { get; set; } = "INFO";
    public string   Message       { get; set; } = "";
    public string?  CorrelationId { get; set; }
}
