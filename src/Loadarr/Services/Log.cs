using System;
using System.IO;

namespace Loadarr.Services
{
    /// <summary>
    /// Tiny append-only file logger. Writes to %APPDATA%\Loadarr\loadarr.log
    /// when <see cref="Enabled"/> is true. Never throws from a log call —
    /// logging must not be able to break the import flow.
    /// </summary>
    internal static class Log
    {
        private static readonly object _lock = new object();
        private static readonly string _path;

        public static bool Enabled { get; set; }
        public static string Path => _path;

        static Log()
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Loadarr");
                Directory.CreateDirectory(dir);
                _path = System.IO.Path.Combine(dir, "loadarr.log");
            }
            catch
            {
                _path = null;
            }
        }

        public static void Info(string message) => Write("INFO ", message);
        public static void Warn(string message) => Write("WARN ", message);
        public static void Error(string message, Exception ex = null) =>
            Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);

        private static void Write(string level, string message)
        {
            if (!Enabled || _path == null) return;
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                        " [" + level + "] " + message + Environment.NewLine);
                }
            }
            catch
            {
                // swallow — logging is best-effort
            }
        }
    }
}
