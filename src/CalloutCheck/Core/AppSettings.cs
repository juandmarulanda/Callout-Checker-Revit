using System;
using System.IO;

namespace CalloutCheck.Core
{
    /// <summary>Tiny per-user settings file (%AppData%\CalloutCheck\sheet-filter.txt).</summary>
    internal static class AppSettings
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CalloutCheck", "sheet-filter.txt");

        public const string DefaultSheetFilter = "DETAIL";

        public static string LoadSheetFilter()
        {
            try { return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : DefaultSheetFilter; }
            catch (Exception) { return DefaultSheetFilter; }
        }

        public static void SaveSheetFilter(string value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, value ?? string.Empty);
            }
            catch (Exception) { }
        }
    }
}
