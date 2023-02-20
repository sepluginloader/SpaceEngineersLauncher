using System;
using System.IO;

namespace avaness.SpaceEngineersLauncher
{
    public static class LogFile
    {
        private static StreamWriter writer;

        public static void Init(string file)
        {
            try
            {
                writer = File.CreateText(file);
            }
            catch
            {
                writer = null;
            }
        }

        /// <summary>
        /// Writes the specifed text to the log file.
        /// WARNING: Not thread safe!
        /// </summary>
        public static void WriteLine(string text)
        {
            try
            {
                writer?.WriteLine($"{DateTime.UtcNow:O} {text}");
                writer?.Flush();
            }
            catch 
            {
                Dispose();
            }
        }

        public static void Dispose()
        {
            if (writer == null)
                return;

            try
            {
                writer.Flush();
                writer.Close();
            }
            catch { }
            writer = null;
        }
    }
}