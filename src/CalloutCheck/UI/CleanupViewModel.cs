using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CalloutCheck.Core;

namespace CalloutCheck.UI
{
    public sealed class UnplacedViewRow : ObservableObject
    {
        private bool _isChecked = true;

        internal UnplacedViewInfo Info { get; }
        internal UnplacedViewRow(UnplacedViewInfo info)
        {
            Info = info;
            _isChecked = !info.IsActive;
        }

        public string Name => Info.Name;
        public string Kind => Info.IsActive ? $"{Info.Kind} (open now - close it to delete)" : Info.Kind;
        public bool CanDelete => !Info.IsActive;

        public bool IsChecked
        {
            get => _isChecked;
            set => Set(ref _isChecked, value && CanDelete);
        }
    }

    /// <summary>"Cleanup" tab: delete sections that are not on any sheet, and disallow beam joins.</summary>
    public sealed class CleanupViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private Document _doc;
        private bool _includeCallouts;
        private bool _onlySelectedBeams;
        private string _sectionsHeader = "Sections not on any sheet: click 'Find' to list them.";
        private string _joinsResult = string.Empty;

        internal CleanupViewModel(MainViewModel main)
        {
            _main = main;
            FindSectionsCommand = new RelayCommand(_ => FindSections());
            DeleteSectionsCommand = new RelayCommand(_ => DeleteSections());
            CheckAllCommand = new RelayCommand(_ => { foreach (var r in Sections) r.IsChecked = true; UpdateHeader(); });
            UncheckAllCommand = new RelayCommand(_ => { foreach (var r in Sections) r.IsChecked = false; UpdateHeader(); });
            OpenViewCommand = new RelayCommand(p => OpenView(p as UnplacedViewRow));
            DisallowJoinsCommand = new RelayCommand(_ => DisallowJoins());
        }

        public ObservableCollection<UnplacedViewRow> Sections { get; } = new ObservableCollection<UnplacedViewRow>();

        public bool IncludeCallouts { get => _includeCallouts; set => Set(ref _includeCallouts, value); }
        public bool OnlySelectedBeams { get => _onlySelectedBeams; set => Set(ref _onlySelectedBeams, value); }
        public string SectionsHeader { get => _sectionsHeader; set => Set(ref _sectionsHeader, value); }
        public string JoinsResult { get => _joinsResult; set => Set(ref _joinsResult, value); }

        public ICommand FindSectionsCommand { get; }
        public ICommand DeleteSectionsCommand { get; }
        public ICommand CheckAllCommand { get; }
        public ICommand UncheckAllCommand { get; }
        public ICommand OpenViewCommand { get; }
        public ICommand DisallowJoinsCommand { get; }

        private void FindSections()
        {
            bool includeCallouts = IncludeCallouts;
            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null || uidoc.Document.IsFamilyDocument)
                {
                    _main.Status = "Open a project first.";
                    return;
                }
                _doc = uidoc.Document;
                Sections.Clear();
                foreach (UnplacedViewInfo info in CleanupService.FindUnplacedSections(_doc, uidoc.ActiveView, includeCallouts))
                {
                    var row = new UnplacedViewRow(info);
                    row.PropertyChanged += (_, __) => UpdateHeader();
                    Sections.Add(row);
                }
                UpdateHeader();
                _main.Status = Sections.Count == 0 ? "Every section is on a sheet. Nothing to clean." : $"Found {Sections.Count} views that are not on any sheet.";
            });
        }

        private void UpdateHeader()
        {
            SectionsHeader = Sections.Count == 0
                ? "Sections not on any sheet: none found."
                : $"Not on any sheet: {Sections.Count}  ·  {Sections.Count(s => s.IsChecked)} checked for deletion";
        }

        private void DeleteSections()
        {
            var ids = Sections.Where(s => s.IsChecked).Select(s => s.Info.ViewId).ToList();
            if (ids.Count == 0)
            {
                _main.Status = "Nothing checked. Click 'Find' first.";
                return;
            }
            App.Tasks.Run(app =>
            {
                Document doc = app.ActiveUIDocument?.Document;
                if (!MainViewModel.IsSame(doc, _doc))
                {
                    _main.Status = "The active project changed. Click 'Find' again.";
                    return;
                }
                var confirm = new TaskDialog("CalloutCheck")
                {
                    MainInstruction = $"Delete {ids.Count} views that are not on any sheet?",
                    MainContent = "Their section marks disappear from plans and elevations too. You can undo it with Ctrl+Z.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                if (confirm.Show() != TaskDialogResult.Yes) return;

                int deleted = CleanupService.DeleteViews(doc, ids, out List<string> failures);
                _main.Status = failures.Count == 0
                    ? $"Deleted {deleted} views."
                    : $"Deleted {deleted} views; {failures.Count} could not be deleted.";
                if (failures.Count > 0)
                    TaskDialog.Show("CalloutCheck", "Could not delete:\n" + string.Join("\n", failures.Take(15)));
                FindSections();
            });
        }

        private void OpenView(UnplacedViewRow row)
        {
            if (row == null) return;
            ElementId id = row.Info.ViewId;
            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null || !MainViewModel.IsSame(uidoc.Document, _doc)) return;
                if (uidoc.Document.GetElement(id) is View view) uidoc.RequestViewChange(view);
            });
        }

        private void DisallowJoins()
        {
            bool onlySelected = OnlySelectedBeams;
            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    _main.Status = "Open a project first.";
                    return;
                }

                ICollection<ElementId> selection = onlySelected ? uidoc.Selection.GetElementIds() : null;
                List<FamilyInstance> beams = CleanupService.CollectBeams(doc, selection);
                if (beams.Count == 0)
                {
                    JoinsResult = onlySelected ? "No structural framing in the current selection." : "No structural framing in this project.";
                    return;
                }

                var confirm = new TaskDialog("CalloutCheck")
                {
                    MainInstruction = $"Disallow joins at both ends of {beams.Count} beams?",
                    MainContent = "You can undo it with Ctrl+Z.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.Yes,
                };
                if (confirm.Show() != TaskDialogResult.Yes) return;

                JoinResult result = CleanupService.DisallowJoins(doc, beams);
                JoinsResult = $"Structural framing found: {result.Total}   ·   Updated: {result.Updated}   ·   Failed: {result.Failures.Count}";
                _main.Status = $"Joins disallowed on {result.Updated} beams.";
                if (result.Failures.Count > 0)
                    TaskDialog.Show("CalloutCheck", "Some beams could not be changed:\n" + string.Join("\n", result.Failures.Take(15)));
            });
        }
    }
}
