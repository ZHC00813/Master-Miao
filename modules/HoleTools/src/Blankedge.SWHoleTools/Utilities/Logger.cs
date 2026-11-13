using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Blankedge.SWHoleTools.Utilities
{
    public static class Logger
    {
        private static readonly object Sync = new object();
        private static string _logPath;

        public static string LogPath
        {
            get
            {
                EnsureInitialized();
                return _logPath;
            }
        }

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception exception = null) => Write("ERROR", message, exception);

        private static void EnsureInitialized()
        {
            if (!string.IsNullOrWhiteSpace(_logPath))
            {
                return;
            }

            lock (Sync)
            {
                if (!string.IsNullOrWhiteSpace(_logPath))
                {
                    return;
                }

                string directory = Path.Combine(Path.GetTempPath(), "Blankedge.SWHoleTools", "Logs");
                Directory.CreateDirectory(directory);
                _logPath = Path.Combine(directory, "SWHoleTools-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
            }
        }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                EnsureInitialized();
                string suffix = exception == null ? string.Empty : " | " + exception;
                string line = $"{DateTime.Now:O} [{level}] {message}{suffix}";
                Debug.WriteLine(line);
                lock (Sync)
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never destabilize SOLIDWORKS.
            }
        }
    }
}
