using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CalloutCheck.Core;

namespace CalloutCheck.UI
{
    public sealed class MainViewModel : ObservableObject
    {
        private Document _auditDoc;
        private List<DetailEntry> _entries = new List<DetailEntry>();
        private string _documentTitle = "No project loaded";
        private string _status = "Ready";
        private string _sheetFilter = AppSettings.LoadSheetFilter();
        private string _search = string.Empty;
        private bool _onlyOnSheets = true;
        private bool _sheetsExpanded;

        private Document _renumberDoc;
        private ElementId _renumberSheetId;
        private string _renumberSheetText = "No sheet loaded. Open a sheet in Revit, then click 'Load active sheet' or 'Pick order'.";
        private string _startNumber = "1";
        private bool _numberUnpickedAfter = true;

        public MainViewModel()
        {
            Doors = new DoorTagsViewModel(this);
            RefreshCommand = new RelayCommand(_ => Refresh());
            ApplySheetFilterCommand = new RelayCommand(_ => { ApplySheetFilter(); Refresh(keepSheetSelection: true); });
            CheckAllSheetsCommand = new RelayCommand(_ => { foreach (var s in Sheets) s.IsChecked = true; });
            UncheckAllSheetsCommand = new RelayCommand(_ => { foreach (var s in Sheets) s.IsChecked = false; });
            CopyNameCommand = new RelayCommand(p => CopyName(p as DetailRow));
            OpenViewCommand = new RelayCommand(p => OpenView(p as DetailRow));
            ShowLocationsCommand = new RelayCommand(p => ShowLocations(p as DetailRow));
            ShowCalloutCommand = new RelayCommand(p => ShowCallout(p as LocationRow));

            LoadActiveSheetCommand = new RelayCommand(_ => LoadActiveSheet(pick: false));
            PickOrderCommand = new RelayCommand(_ => LoadActiveSheet(pick: true));
            MoveUpCommand = new RelayCommand(p => Move(p as ViewportRow, -1));
            MoveDownCommand = new RelayCommand(p => Move(p as ViewportRow, +1));
            IncludeAllCommand = new RelayCommand(_ => { foreach (var r in Viewports) r.Included = true; });
            ApplyRenumberCommand = new RelayCommand(_ => ApplyRenumber());
        }

        // ---------------------------------------------------------------- shared

        public string DocumentTitle { get => _documentTitle; set => Set(ref _documentTitle, value); }
        public string Status { get => _status; set => Set(ref _status, value); }
        public DoorTagsViewModel Doors { get; }
        public string AuthorLine => $"CalloutCheck v{typeof(MainViewModel).Assembly.GetName().Version.ToString(3)}  ·  Author: Juan Marulanda";

        // ---------------------------------------------------------------- callout audit

        public ObservableCollection<SheetRow> Sheets { get; } = new ObservableCollection<SheetRow>();
        public ObservableCollection<DetailRow> Untagged { get; } = new ObservableCollection<DetailRow>();
        public ObservableCollection<DetailRow> Tagged { get; } = new ObservableCollection<DetailRow>();

        public string UntaggedHeader => $"Missing callouts ({Untagged.Count})";
        public string TaggedHeader => $"Called out ({Tagged.Count})";
        public string SheetsHeader => $"Detail sheets: {Sheets.Count(s => s.IsChecked)} selected of {Sheets.Count}";

        public string SheetFilter { get => _sheetFilter; set => Set(ref _sheetFilter, value); }
        public bool SheetsExpanded { get => _sheetsExpanded; set => Set(ref _sheetsExpanded, value); }

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) RebuildLists(); }
        }

        public bool OnlyOnSheets
        {
            get => _onlyOnSheets;
            set { if (Set(ref _onlyOnSheets, value)) RebuildLists(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ApplySheetFilterCommand { get; }
        public ICommand CheckAllSheetsCommand { get; }
        public ICommand UncheckAllSheetsCommand { get; }
        public ICommand CopyNameCommand { get; }
        public ICommand OpenViewCommand { get; }
        public ICommand ShowLocationsCommand { get; }
        public ICommand ShowCalloutCommand { get; }

        public void Refresh(bool keepSheetSelection = true)
        {
            Status = "Reading the model...";
            var previouslyChecked = new HashSet<string>(Sheets.Where(s => s.IsChecked).Select(s => s.Sheet.Number));
            bool hadSheets = Sheets.Count > 0;

            App.Tasks.Run(app =>
            {
                Document doc = app.ActiveUIDocument?.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    Status = "Open a project to use CalloutCheck.";
                    return;
                }

                bool sameDoc = IsSame(doc, _auditDoc);
                List<SheetInfo> sheets = CalloutAuditService.GetSheets(doc);

                Sheets.Clear();
                foreach (SheetInfo s in sheets)
                {
                    var row = new SheetRow(s);
                    row.IsChecked = keepSheetSelection && hadSheets && sameDoc
                        ? previouslyChecked.Contains(s.Number)
                        : MatchesFilter(s);
                    row.PropertyChanged += (_, __) => OnPropertyChanged(nameof(SheetsHeader));
                    Sheets.Add(row);
                }
                OnPropertyChanged(nameof(SheetsHeader));

                var selected = Sheets.Where(s => s.IsChecked).Select(s => s.Sheet.Id).ToList();
                _entries = CalloutAuditService.Audit(doc, selected);
                _auditDoc = doc;
                DocumentTitle = doc.Title;
                RebuildLists();

                Status = selected.Count == 0
                    ? "No detail sheets selected. Expand 'Detail sheets' and choose the sheets that hold your details."
                    : $"Checked {_entries.Count} drafting views on {selected.Count} detail sheets at {DateTime.Now:t}.";
            });
        }

        private void ApplySheetFilter()
        {
            AppSettings.SaveSheetFilter(SheetFilter);
            foreach (SheetRow s in Sheets) s.IsChecked = MatchesFilter(s.Sheet);
        }

        /// <summary>Matches "number - name" against any of the ';' separated keywords.</summary>
        private bool MatchesFilter(SheetInfo sheet)
        {
            string[] keys = (SheetFilter ?? string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0)
                .ToArray();
            return keys.Any(k => sheet.Display.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RebuildLists()
        {
            Untagged.Clear();
            Tagged.Clear();
            foreach (DetailEntry e in _entries.Matching(Search, x => x.ViewName + " " + x.Placement.SheetNumber))
            {
                int count = e.CountCallouts(OnlyOnSheets);
                (count == 0 ? Untagged : Tagged).Add(new DetailRow(e, count));
            }
            OnPropertyChanged(nameof(UntaggedHeader));
            OnPropertyChanged(nameof(TaggedHeader));
        }

        private void CopyName(DetailRow row)
        {
            if (row == null) return;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(row.ViewName, true);
                    Status = $"Copied to clipboard: {row.ViewName}";
                    return;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    System.Threading.Thread.Sleep(50); // clipboard busy, try again
                }
            }
            Status = "Could not access the clipboard, please try again.";
        }

        private void OpenView(DetailRow row)
        {
            if (row == null) return;
            ElementId viewId = row.Entry.ViewId;
            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (!CheckDocument(uidoc, _auditDoc)) return;
                if (uidoc.Document.GetElement(viewId) is View view)
                {
                    uidoc.RequestViewChange(view);
                    Status = $"Opened: {view.Name}";
                }
            });
        }

        private void ShowLocations(DetailRow row)
        {
            if (row == null) return;
            var rows = row.Entry.Locations
                .OrderByDescending(l => l.OnSheet)
                .ThenBy(l => l.HostSheets.Select(s => s.SheetNumber).FirstOrDefault(), NaturalComparer.Instance)
                .Select(l => new LocationRow(l))
                .ToList();
            var window = new LocationsWindow(this, row.ViewName, row.Reference, rows)
            {
                Owner = MainWindow.Instance,
            };
            window.Show();
        }

        private void ShowCallout(LocationRow row)
        {
            if (row == null) return;
            ElementId calloutId = row.Location.CalloutId;
            ElementId hostId = row.Location.HostViewId;
            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (!CheckDocument(uidoc, _auditDoc)) return;
                Document doc = uidoc.Document;
                if (doc.GetElement(calloutId) == null)
                {
                    Status = "That callout no longer exists. Click Refresh.";
                    return;
                }
                if (doc.GetElement(hostId) is View host && uidoc.ActiveView.Id != host.Id)
                    uidoc.ActiveView = host;
                uidoc.Selection.SetElementIds(new List<ElementId> { calloutId });
                uidoc.ShowElements(calloutId);
                Status = $"Showing callout in view: {row.Location.HostViewName}";
            });
        }

        // ---------------------------------------------------------------- renumber

        public ObservableCollection<ViewportRow> Viewports { get; } = new ObservableCollection<ViewportRow>();

        public string RenumberSheetText { get => _renumberSheetText; set => Set(ref _renumberSheetText, value); }

        public string StartNumber
        {
            get => _startNumber;
            set { if (Set(ref _startNumber, value)) RecomputeNumbers(); }
        }

        public bool NumberUnpickedAfter { get => _numberUnpickedAfter; set => Set(ref _numberUnpickedAfter, value); }

        public ICommand LoadActiveSheetCommand { get; }
        public ICommand PickOrderCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand IncludeAllCommand { get; }
        public ICommand ApplyRenumberCommand { get; }

        private void LoadActiveSheet(bool pick)
        {
            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (!(uidoc?.ActiveView is ViewSheet sheet))
                {
                    TaskDialog.Show("CalloutCheck", "Open the sheet you want to renumber first, then try again.");
                    return;
                }

                List<ViewportInfo> infos = RenumberService.GetViewports(uidoc.Document, sheet);
                var rows = infos.Select(i => new ViewportRow(i)).ToList();

                if (pick)
                {
                    Status = "Picking: click the views on the sheet in the order you want. Press ESC when done.";
                    List<ElementId> picked = RenumberService.PickInOrder(uidoc, sheet);
                    if (picked.Count == 0)
                    {
                        Status = "Nothing picked.";
                        return;
                    }
                    var order = picked.Select((id, i) => new { id, i }).ToDictionary(x => x.id, x => x.i + 1);
                    foreach (ViewportRow r in rows)
                    {
                        r.PickOrder = order.TryGetValue(r.ViewportId, out int n) ? n : 0;
                        r.Included = r.PickOrder > 0 || NumberUnpickedAfter;
                    }
                    rows = rows.OrderBy(r => r.PickOrder == 0 ? int.MaxValue : r.PickOrder)
                               .ThenBy(r => r.CurrentNumber, NaturalComparer.Instance)
                               .ToList();
                }

                _renumberDoc = uidoc.Document;
                _renumberSheetId = sheet.Id;
                RenumberSheetText = $"{sheet.SheetNumber} - {sheet.Name}   ({rows.Count} views)";
                Viewports.Clear();
                foreach (ViewportRow r in rows)
                {
                    r.IncludedChanged = RecomputeNumbers;
                    Viewports.Add(r);
                }
                RecomputeNumbers();
                Status = pick
                    ? "Review the new numbers, adjust with the arrows if needed, then click 'Apply numbering'."
                    : "Reorder with the arrows (or use 'Pick order'), then click 'Apply numbering'.";
            });
        }

        private void Move(ViewportRow row, int delta)
        {
            if (row == null) return;
            int i = Viewports.IndexOf(row);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= Viewports.Count) return;
            Viewports.Move(i, j);
            RecomputeNumbers();
        }

        /// <summary>
        /// Included rows get consecutive numbers in list order, skipping numbers that
        /// excluded rows keep. Non-numeric start values fall back to 1.
        /// </summary>
        private void RecomputeNumbers()
        {
            if (!int.TryParse(StartNumber?.Trim(), out int n) || n < 0) n = 1;
            var reserved = new HashSet<string>(
                Viewports.Where(r => !r.Included).Select(r => r.CurrentNumber),
                StringComparer.OrdinalIgnoreCase);

            foreach (ViewportRow r in Viewports)
            {
                if (!r.Included)
                {
                    r.NewNumber = r.CurrentNumber;
                    continue;
                }
                while (reserved.Contains(n.ToString())) n++;
                r.NewNumber = (n++).ToString();
            }
        }

        private void ApplyRenumber()
        {
            if (Viewports.Count == 0 || _renumberSheetId == null)
            {
                Status = "Load a sheet first.";
                return;
            }
            var assignments = Viewports
                .Where(r => r.Included && r.Changes)
                .Select(r => new KeyValuePair<ElementId, string>(r.ViewportId, r.NewNumber))
                .ToList();
            if (assignments.Count == 0)
            {
                Status = "Numbers are already in that order. Nothing to change.";
                return;
            }

            ElementId sheetId = _renumberSheetId;
            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (!CheckDocument(uidoc, _renumberDoc)) return;
                if (!(uidoc.Document.GetElement(sheetId) is ViewSheet sheet))
                {
                    Status = "The sheet no longer exists.";
                    return;
                }
                RenumberService.Apply(uidoc.Document, sheet, assignments);

                // Reload to show the new state.
                var infos = RenumberService.GetViewports(uidoc.Document, sheet);
                Viewports.Clear();
                foreach (ViewportInfo info in infos)
                    Viewports.Add(new ViewportRow(info) { IncludedChanged = RecomputeNumbers });
                RecomputeNumbers();
                Status = $"Renumbered {assignments.Count} views on sheet {sheet.SheetNumber}.";
            });
        }

        // ---------------------------------------------------------------- helpers

        private bool CheckDocument(UIDocument uidoc, Document expected)
        {
            if (uidoc != null && IsSame(uidoc.Document, expected)) return true;
            Status = "The active project changed. Click Refresh / reload the sheet first.";
            return false;
        }

        internal static bool IsSame(Document a, Document b)
        {
            if (a == null || b == null || !a.IsValidObject || !b.IsValidObject) return false;
            return a.Equals(b) || (a.Title == b.Title && a.PathName == b.PathName);
        }
    }
}
