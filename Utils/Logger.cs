using System;
using System.IO;

namespace SharpKVM.Utils;

public static class Logger
{
    private static readonly object _lock = new();
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sharpkvm.log");

    public static void Info(string message) => Log("INFO", message, ConsoleColor.White);
    public static void Warn(string message) => Log("WARN", message, ConsoleColor.Yellow);
    public static void Error(string message, Exception? ex = null)
    {
        string fullMsg = ex == null ? message : $"{message}\n{ex}";
        Log("ERROR", fullMsg, ConsoleColor.Red);
    }

    private static void Log(string level, string message, ConsoleColor color)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"[{timestamp}] [{level}] {message}";

        lock (_lock)
        {
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(line);
                Console.ResetColor();

                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
