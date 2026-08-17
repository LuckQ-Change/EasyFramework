using System;

namespace EasyFramework
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error,
    }

    public static class Log
    {
        public static LogLevel MinLevel = LogLevel.Debug;

        public static Action<LogLevel, string> Handler = (level, msg) =>
        {
            Console.WriteLine($"[{level}] {msg}");
        };

        public static Action<string, Exception> ExceptionHandler = (msg, exception) =>
        {
            Console.WriteLine($"[Error] {msg}");
            Console.WriteLine(exception);
        };

        public static void Debug(string msg) => Write(LogLevel.Debug, msg);
        public static void Info(string msg) => Write(LogLevel.Info, msg);
        public static void Warn(string msg) => Write(LogLevel.Warn, msg);
        public static void Error(string msg) => Write(LogLevel.Error, msg);
        public static void Error(string msg, Exception exception)
        {
            if (LogLevel.Error < MinLevel) return;
            if (exception == null)
            {
                Write(LogLevel.Error, msg);
                return;
            }

            try { ExceptionHandler?.Invoke(msg, exception); }
            catch { }
        }

        private static void Write(LogLevel level, string msg)
        {
            if (level < MinLevel) return;
            try { Handler?.Invoke(level, msg); }
            catch { }
        }
    }
}
