using System.Collections.Generic;
using System.Windows.Input;

namespace CalloutCheck.UI
{
    /// <summary>Simple step-by-step help shown by the "?" buttons.</summary>
    public static class Help
    {
        public static ICommand ShowCommand { get; } = new RelayCommand(p => Show(p as string));

        private static readonly Dictionary<string, (string Title, string[] Steps)> Topics =
            new Dictionary<string, (string, string[])>
            {
                ["audit"] = ("Callout Audit", new[]
                {
                    "Make sure your detail sheets are checked in 'Detail sheets' (sheets with DETAIL in the name are checked for you).",
                    "Look at 'Missing callouts': these details are on a sheet but no plan points to them yet.",
                    "Click 'Copy' next to a detail. Its name is now on the clipboard.",
                    "In Revit, draw a callout with 'Reference other view' and paste the name to pick that detail.",
                    "Click 'Refresh'. The detail moves to 'Called out'.",
                    "In 'Called out', the number tells you how many callouts point to each detail. Click 'Where?' to see the sheets.",
                    "Click any detail name to open it.",
                }),
                ["sheets"] = ("Detail sheets", new[]
                {
                    "Type a word that all your detail sheets share, for example DETAIL (or A5).",
                    "Click 'Select matching'. Those sheets get checked.",
                    "You can also check or uncheck sheets by hand.",
                    "Click 'Refresh' to update the lists.",
                }),
                ["renumber"] = ("Renumber Views", new[]
                {
                    "Open the sheet you want to renumber in Revit.",
                    "Click 'Pick order'.",
                    "Click the views on the sheet one by one, in the order you want: first view = 1, second = 2...",
                    "Press ESC when you are done.",
                    "Check the 'New #' column. Use the arrows to move a view up or down if needed.",
                    "Click 'Apply numbering'. Ctrl+Z undoes it.",
                }),
                ["doors"] = ("Door Tags", new[]
                {
                    "Check that 'Tag parameter' is the parameter where you write the door tag (LCH_Txt_Mark1).",
                    "Click 'Analyze doors'.",
                    "Each card is one kind of door: same family, type, size and hinge side. They should all have the same tag.",
                    "Red cards need attention: mixed tags, missing tags, or a tag already used by another kind of door.",
                    "Click 'Select' to select all doors of that card in Revit and change the tag once in Properties.",
                    "Or type the tag in the small box and click 'Set tag'.",
                    "Trim and hardware are skipped automatically (only families starting with 05 / 06 or with 'garage' in the name are used).",
                }),
                ["compare"] = ("Compare parameters", new[]
                {
                    "These are the door parameters used to decide if two doors are the same.",
                    "Checked = must be equal. Unchecked = ignored.",
                    "Uncheck a parameter if it should not split doors into different tags.",
                    "Click 'Regroup' to apply. 'Defaults' goes back to the recommended choice.",
                }),
                ["sections"] = ("Delete unplaced sections", new[]
                {
                    "Click 'Find'. You get every section that is not on any sheet (your working sections).",
                    "Uncheck any section you want to keep. Click a name to open it and check it first.",
                    "Click 'Delete checked' and confirm.",
                    "The sections and their marks are removed from the model. Ctrl+Z undoes it.",
                    "Tip: tick 'Also include callouts' to list working callouts too.",
                }),
                ["joins"] = ("Disallow beam joins", new[]
                {
                    "Click 'Disallow joins on all beams' and confirm.",
                    "Every structural framing element stops joining at both ends.",
                    "To change only some beams: select them in Revit first and tick 'Only selected beams'.",
                    "The result line shows how many were updated. Ctrl+Z undoes it.",
                }),
            };

        public static void Show(string key)
        {
            if (key == null || !Topics.TryGetValue(key, out var topic)) return;
            var window = new HelpWindow(topic.Title, topic.Steps) { Owner = MainWindow.Instance };
            window.ShowDialog();
        }
    }
}
