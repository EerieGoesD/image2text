using System;
using System.Collections.Generic;

namespace Image2Text
{
    public enum LogLevel { Info, Warn, Error, Event }

    public record LogEntry(DateTime Time, LogLevel Level, string Message);

    public static class Logger
    {
        private static readonly List<LogEntry> entries = new();
        public static event Action<LogEntry>? EntryAdded;

        public static IReadOnlyList<LogEntry> Entries => entries;

        public static void Info(string msg)  => Add(LogLevel.Info, msg);
        public static void Warn(string msg)  => Add(LogLevel.Warn, msg);
        public static void Error(string msg) => Add(LogLevel.Error, msg);
        public static void Event(string msg) => Add(LogLevel.Event, msg);

        private static void Add(LogLevel level, string msg)
        {
            var entry = new LogEntry(DateTime.Now, level, msg);
            entries.Add(entry);
            // Cap memory: keep last 5000 entries
            if (entries.Count > 5000) entries.RemoveRange(0, entries.Count - 5000);
            EntryAdded?.Invoke(entry);
        }

        public static void Clear() => entries.Clear();
    }
}
