using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CalloutCheck.Core;

namespace CalloutCheck.UI
{
    public sealed class DoorParamRow : ObservableObject
    {
        private bool _isChecked;

        public string Name { get; set; }
        public string Kind { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set => Set(ref _isChecked, value);
        }
    }

    public sealed class DoorGroupRow : ObservableObject
    {
        private string _newTag;

        internal DoorGroup Group { get; }

        internal DoorGroupRow(DoorGroup group, ISet<string> tagsUsedElsewhere)
        {
            Group = group;
            var conflicts = group.Tags.Keys.Where(tagsUsedElsewhere.Contains).ToList();
            if (group.Tags.Count > 1) { Status = "Mixed tags"; Problem = true; }
            else if (group.Tags.Count == 0) { Status = "No tag"; Problem = true; }
            else if (group.MissingTags > 0) { Status = "Some untagged"; Problem = true; }
            else if (conflicts.Count > 0) { Status = "Tag reused"; Problem = true; }
            else Status = "OK";

            if (conflicts.Count > 0)
                ConflictText = $"{string.Join(", ", conflicts)} is also used by a different door group";
            _newTag = group.Tags.OrderByDescending(t => t.Value).Select(t => t.Key).FirstOrDefault() ?? string.Empty;
        }

        public string Title => $"{Group.FamilyName} : {Group.TypeName}";
        public int Count => Group.DoorIds.Count;
        public string CountText => Count == 1 ? "1 door" : $"{Count} doors";
        public string Status { get; }
        public bool Problem { get; }
        public string ConflictText { get; }
        public bool HasConflict => !string.IsNullOrEmpty(ConflictText);

        public string DimensionsText => Group.Dimensions.Count == 0
            ? "(no dimensions compared)"
            : string.Join("   ·   ", Group.Dimensions.Select(d => $"{d.Key}: {d.Value}"));

        public string TagsText
        {
            get
            {
                var parts = Group.Tags.OrderBy(t => t.Key, NaturalComparer.Instance)
                    .Select(t => t.Value == Count ? t.Key : $"{t.Key} ×{t.Value}").ToList();
                if (Group.MissingTags > 0) parts.Add($"(empty) ×{Group.MissingTags}");
                return string.Join(",  ", parts);
            }
        }

        internal string SortTag => Group.Tags.OrderByDescending(t => t.Value).Select(t => t.Key).FirstOrDefault() ?? "~";

        public string NewTag
        {
            get => _newTag;
            set => Set(ref _newTag, value);
        }
    }

    /// <summary>"Door Tags" tab: groups identical doors so they can share one tag.</summary>
    public sealed class DoorTagsViewModel : ObservableObject
    {
        private const string TagParamFile = "door-tag-parameter.txt";
        private const string CompareFile = "door-compare-parameters.txt";

        private readonly MainViewModel _main;
        private Document _doc;
        private List<DoorGroupRow> _all = new List<DoorGroupRow>();
        private string _tagParameter = AppSettings.Load(TagParamFile, DoorGroupService.DefaultTagParameter);
        private bool _activeViewOnly;
        private bool _onlyProblems;
        private string _search = string.Empty;
        private string _summary = "Click 'Analyze doors' to group identical doors.";
        private bool _paramsExpanded;

        internal DoorTagsViewModel(MainViewModel main)
        {
            _main = main;
            AnalyzeCommand = new RelayCommand(_ => Analyze(reloadParameters: true));
            ReanalyzeCommand = new RelayCommand(_ => Analyze(reloadParameters: false));
            SelectGroupCommand = new RelayCommand(p => SelectGroup(p as DoorGroupRow));
            SetTagCommand = new RelayCommand(p => SetTag(p as DoorGroupRow));
        }

        public ObservableCollection<DoorGroupRow> Groups { get; } = new ObservableCollection<DoorGroupRow>();
        public ObservableCollection<DoorParamRow> CompareParameters { get; } = new ObservableCollection<DoorParamRow>();
        public ObservableCollection<string> TagParameters { get; } = new ObservableCollection<string>();

        public string TagParameter { get => _tagParameter; set => Set(ref _tagParameter, value); }
        public bool ActiveViewOnly { get => _activeViewOnly; set => Set(ref _activeViewOnly, value); }
        public bool ParamsExpanded { get => _paramsExpanded; set => Set(ref _paramsExpanded, value); }
        public string Summary { get => _summary; set => Set(ref _summary, value); }

        public string ParamsHeader =>
            $"Compare dimensions: {CompareParameters.Count(p => p.IsChecked)} of {CompareParameters.Count} parameters (family and type are always compared)";

        public bool OnlyProblems
        {
            get => _onlyProblems;
            set { if (Set(ref _onlyProblems, value)) ApplyFilter(); }
        }

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) ApplyFilter(); }
        }

        public ICommand AnalyzeCommand { get; }
        public ICommand ReanalyzeCommand { get; }
        public ICommand SelectGroupCommand { get; }
        public ICommand SetTagCommand { get; }

        private void Analyze(bool reloadParameters)
        {
            string tagParam = (TagParameter ?? string.Empty).Trim();
            if (tagParam.Length == 0)
            {
                _main.Status = "Choose the parameter that holds the door tag.";
                return;
            }
            AppSettings.Save(TagParamFile, tagParam);
            _main.Status = "Reading doors...";

            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    _main.Status = "Open a project first.";
                    return;
                }

                List<FamilyInstance> doors = DoorGroupService.CollectDoors(doc, ActiveViewOnly ? uidoc.ActiveView : null);

                if (reloadParameters || CompareParameters.Count == 0 || !MainViewModel.IsSame(doc, _doc))
                    LoadParameterLists(doors);

                var compare = CompareParameters.Where(p => p.IsChecked).Select(p => p.Name).ToList();
                AppSettings.Save(CompareFile, string.Join(";", compare));

                List<DoorGroup> groups = DoorGroupService.Group(doc, doors, compare, tagParam);

                // A tag is "reused" when it appears in more than one group.
                var tagGroups = groups.SelectMany(g => g.Tags.Keys.Select(t => new { t, g }))
                    .GroupBy(x => x.t, StringComparer.OrdinalIgnoreCase)
                    .Where(x => x.Select(y => y.g).Distinct().Count() > 1)
                    .Select(x => x.Key);
                var reused = new HashSet<string>(tagGroups, StringComparer.OrdinalIgnoreCase);

                _doc = doc;
                _all = groups.Select(g => new DoorGroupRow(g, reused))
                    .OrderBy(r => r.SortTag, NaturalComparer.Instance)
                    .ThenBy(r => r.Title, NaturalComparer.Instance)
                    .ToList();
                ApplyFilter();

                int problems = _all.Count(r => r.Problem);
                Summary = $"{doors.Count} doors in {_all.Count} groups  ·  {problems} group(s) need attention";
                _main.Status = $"Doors analyzed at {DateTime.Now:t}.";
            });
        }

        private void LoadParameterLists(List<FamilyInstance> doors)
        {
            string saved = AppSettings.Load(CompareFile, null);
            var savedSet = saved == null
                ? null
                : new HashSet<string>(saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

            CompareParameters.Clear();
            foreach (DoorParamInfo info in DoorGroupService.GetDimensionParameters(doors))
            {
                var row = new DoorParamRow
                {
                    Name = info.Name,
                    Kind = info.IsLength ? "length" : "number",
                    IsChecked = savedSet == null || savedSet.Contains(info.Name),
                };
                row.PropertyChanged += (_, __) => OnPropertyChanged(nameof(ParamsHeader));
                CompareParameters.Add(row);
            }
            OnPropertyChanged(nameof(ParamsHeader));

            string current = TagParameter;
            TagParameters.Clear();
            foreach (string name in DoorGroupService.GetTextParameters(doors)) TagParameters.Add(name);
            TagParameter = current;
        }

        private void ApplyFilter()
        {
            Groups.Clear();
            IEnumerable<DoorGroupRow> rows = _all;
            if (OnlyProblems) rows = rows.Where(r => r.Problem);
            foreach (DoorGroupRow r in rows.Matching(Search, x => x.Title + " " + x.TagsText + " " + x.DimensionsText))
                Groups.Add(r);
        }

        private void SelectGroup(DoorGroupRow row)
        {
            if (row == null) return;
            var ids = row.Group.DoorIds.ToList();
            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null || !MainViewModel.IsSame(uidoc.Document, _doc))
                {
                    _main.Status = "The active project changed. Click 'Analyze doors' again.";
                    return;
                }
                var existing = ids.Where(id => uidoc.Document.GetElement(id) != null).ToList();
                uidoc.Selection.SetElementIds(existing);
                _main.Status = $"Selected {existing.Count} doors ({row.Title}). Edit the tag in Properties, or use 'Set tag'.";
            });
        }

        private void SetTag(DoorGroupRow row)
        {
            if (row == null) return;
            string value = (row.NewTag ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                _main.Status = "Type the tag first (for example D2).";
                return;
            }
            string tagParam = TagParameter;
            var ids = row.Group.DoorIds.ToList();
            App.Tasks.Run(app =>
            {
                Document doc = app.ActiveUIDocument?.Document;
                if (!MainViewModel.IsSame(doc, _doc))
                {
                    _main.Status = "The active project changed. Click 'Analyze doors' again.";
                    return;
                }
                int failed = DoorGroupService.SetTag(doc, ids, tagParam, value);
                _main.Status = failed == 0
                    ? $"Set {tagParam} = {value} on {ids.Count} doors."
                    : $"Set {tagParam} = {value} on {ids.Count - failed} doors; {failed} could not be changed (in a group or read-only).";
                Analyze(reloadParameters: false);
            });
        }
    }
}
