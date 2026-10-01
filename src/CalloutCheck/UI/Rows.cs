using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using CalloutCheck.Core;

namespace CalloutCheck.UI
{
    public sealed class SheetRow : ObservableObject
    {
        private bool _isChecked;

        internal SheetInfo Sheet { get; }
        internal SheetRow(SheetInfo sheet) => Sheet = sheet;

        public string Display => Sheet.Display;

        public bool IsChecked
        {
            get => _isChecked;
            set => Set(ref _isChecked, value);
        }
    }

    public sealed class DetailRow
    {
        internal DetailEntry Entry { get; }
        internal DetailRow(DetailEntry entry, int count)
        {
            Entry = entry;
            Count = count;
        }

        public string ViewName => Entry.ViewName;
        public int Count { get; }
        public string Reference => $"{Entry.Placement.DetailNumber} / {Entry.Placement.SheetNumber}";
        public string ToolTip => $"Detail {Entry.Placement.DetailNumber} on sheet {Entry.Placement.SheetNumber} - {Entry.Placement.SheetName}\nClick to open the view.";
        public string CountText => Count == 1 ? "1 callout" : $"{Count} callouts";
    }

    public sealed class LocationRow
    {
        internal CalloutLocation Location { get; }
        internal LocationRow(CalloutLocation location) => Location = location;

        public string Sheets => Location.SheetText;
        public string HostView => $"in view: {Location.HostViewName}";
        public bool OnSheet => Location.OnSheet;
    }

    public sealed class ViewportRow : ObservableObject
    {
        private bool _included = true;
        private string _newNumber;
        private int _pickOrder;

        internal ViewportInfo Info { get; }
        internal ViewportRow(ViewportInfo info) => Info = info;

        internal ElementId ViewportId => Info.ViewportId;
        public string CurrentNumber => Info.DetailNumber;
        public string ViewName => Info.ViewName;
        public string ViewKind => Info.ViewKind;

        /// <summary>0 = not picked, otherwise the click order in Revit.</summary>
        public int PickOrder
        {
            get => _pickOrder;
            set { if (Set(ref _pickOrder, value)) OnPropertyChanged(nameof(PickText)); }
        }

        public string PickText => PickOrder > 0 ? $"#{PickOrder}" : "-";

        public bool Included
        {
            get => _included;
            set { if (Set(ref _included, value)) IncludedChanged?.Invoke(); }
        }

        public string NewNumber
        {
            get => _newNumber;
            set { if (Set(ref _newNumber, value)) OnPropertyChanged(nameof(Changes)); }
        }

        public bool Changes => !string.Equals(NewNumber, CurrentNumber, System.StringComparison.OrdinalIgnoreCase);

        internal System.Action IncludedChanged;
    }

    internal static class RowExtensions
    {
        public static IEnumerable<T> Matching<T>(this IEnumerable<T> rows, string search, System.Func<T, string> text) =>
            string.IsNullOrWhiteSpace(search)
                ? rows
                : rows.Where(r => text(r)?.IndexOf(search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
