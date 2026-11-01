using System;

namespace Couchpotato
{
    /// <summary>Minimal logger. Never pass typed text or key codes to it.</summary>
    public static class Log
    {
        public static Action<string> Sink = Console.Error.WriteLine;

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);

        static void Write(string level, string message)
        {
            try { Sink?.Invoke(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + message); }
            catch { }
        }
    }
}
