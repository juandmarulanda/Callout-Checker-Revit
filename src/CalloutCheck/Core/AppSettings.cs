using System;
using System.IO;

namespace CalloutCheck.Core
{
    /// <summary>Tiny per-user settings files in %AppData%\CalloutCheck\.</summary>
    internal static class AppSettings
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CalloutCheck");

        public const string DefaultSheetFilter = "DETAIL";

        public static string LoadSheetFilter() => Load("sheet-filter.txt", DefaultSheetFilter);
        public static void SaveSheetFilter(string value) => Save("sheet-filter.txt", value);

        public static string Load(string file, string fallback)
        {
            try
            {
                string path = Path.Combine(Folder, file);
                return File.Exists(path) ? File.ReadAllText(path).Trim() : fallback;
            }
            catch (Exception) { return fallback; }
        }

        public static void Save(string file, string value)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, file), value ?? string.Empty);
            }
            catch (Exception) { }
        }
    }
}
