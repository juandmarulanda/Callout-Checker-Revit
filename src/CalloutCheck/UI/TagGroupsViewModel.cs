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
    public sealed class CompareParamRow : ObservableObject
    {
        private bool _isChecked;

        internal TagParamInfo Info { get; set; }
        public string Name => Info.Name;
        public string Kind => $"{Info.GroupLabel} · {KindLabel(Info.Kind)}";

        public bool IsChecked
        {
            get => _isChecked;
            set => Set(ref _isChecked, value);
        }

        private static string KindLabel(ParamKind kind)
        {
            switch (kind)
            {
                case ParamKind.Length: return "length";
                case ParamKind.Number: return "number";
                case ParamKind.YesNo: return "yes/no";
                case ParamKind.FamilyType: return "type";
                default: return "text";
            }
        }
    }

    public sealed class TagGroupRow : ObservableObject
    {
        private string _newTag;
        private readonly string _noun;

        internal TagGroup Group { get; }

        internal TagGroupRow(TagGroup group, ISet<string> tagsUsedElsewhere, string noun, string nounPlural)
        {
            Group = group;
            _noun = noun;
            var conflicts = group.Tags.Keys.Where(tagsUsedElsewhere.Contains).ToList();
            if (group.Tags.Count > 1) { Status = "Mixed tags"; Problem = true; }
            else if (group.Tags.Count == 0) { Status = "No tag"; Problem = true; }
            else if (group.MissingTags > 0) { Status = "Some untagged"; Problem = true; }
            else if (conflicts.Count > 0) { Status = "Tag reused"; Problem = true; }
            else Status = "OK";

            if (conflicts.Count > 0)
                ConflictText = $"{string.Join(", ", conflicts)} is also used by a different {noun} group";
            CountText = Count == 1 ? $"1 {noun}" : $"{Count} {nounPlural}";
            _newTag = group.Tags.OrderByDescending(t => t.Value).Select(t => t.Key).FirstOrDefault() ?? string.Empty;
        }

        public string Title => $"{Group.FamilyName} : {Group.TypeName}";
        public int Count => Group.ElementIds.Count;
        public string CountText { get; }
        public string Status { get; }
        public bool Problem { get; }
        public string ConflictText { get; }
        public bool HasConflict => !string.IsNullOrEmpty(ConflictText);

        public string DetailsText => Group.Details.Count == 0
            ? "(no options set)"
            : string.Join("   ·   ", Group.Details);

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

    /// <summary>
    /// "Door Tags" / "Window Tags" tabs: groups identical elements (same family, type and
    /// compared instance parameters) so they can share one tag.
    /// </summary>
    public sealed class TagGroupsViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly TagProfile _profile;
        private Document _doc;
        private List<TagGroupRow> _all = new List<TagGroupRow>();
        private string _tagParameter;
        private string _nameRule;
        private bool _activeViewOnly;
        private bool _onlyProblems;
        private string _search = string.Empty;
        private string _summary;
        private bool _paramsExpanded;

        internal TagGroupsViewModel(MainViewModel main, TagProfile profile)
        {
            _main = main;
            _profile = profile;
            _tagParameter = AppSettings.Load(File("tag-parameter"), TagGroupService.DefaultTagParameter);
            _nameRule = AppSettings.Load(File("name-rule"), profile.DefaultNameRule);
            _summary = $"Click 'Analyze {profile.NounPlural}' to group identical {profile.NounPlural}.";

            AnalyzeCommand = new RelayCommand(_ => Analyze(reloadParameters: true));
            ReanalyzeCommand = new RelayCommand(_ => Analyze(reloadParameters: false));
            ResetParametersCommand = new RelayCommand(_ => ResetParameters());
            SelectGroupCommand = new RelayCommand(p => SelectGroup(p as TagGroupRow));
            SetTagCommand = new RelayCommand(p => SetTag(p as TagGroupRow));
        }

        // Settings file per profile; "v2" because 1.2 compares more parameters than 1.1.
        private string File(string name) => $"{_profile.Key}-{name}-v2.txt";

        public string Header => $"  {_profile.Title} Tags  ";
        public string AnalyzeLabel => $"Analyze {_profile.NounPlural}";
        public string IntroText =>
            $"{_profile.Title}s with the same family, type, size and options are grouped together and should share one tag. " +
            $"Use 'Select' to select every {_profile.Noun} of a group in Revit and edit the tag once in Properties, or type it here and click 'Set tag'.";
        public string ActiveViewLabel => $"Only {_profile.NounPlural} in the active view";
        public string NameRuleHelp => _profile.NameRuleHelp;

        public ObservableCollection<TagGroupRow> Groups { get; } = new ObservableCollection<TagGroupRow>();
        public ObservableCollection<CompareParamRow> CompareParameters { get; } = new ObservableCollection<CompareParamRow>();
        public ObservableCollection<string> TagParameters { get; } = new ObservableCollection<string>();

        public string TagParameter { get => _tagParameter; set => Set(ref _tagParameter, value); }
        public string NameRule { get => _nameRule; set => Set(ref _nameRule, value); }
        public bool ActiveViewOnly { get => _activeViewOnly; set => Set(ref _activeViewOnly, value); }
        public bool ParamsExpanded { get => _paramsExpanded; set => Set(ref _paramsExpanded, value); }
        public string Summary { get => _summary; set => Set(ref _summary, value); }

        public string ParamsHeader =>
            $"Compare: {CompareParameters.Count(p => p.IsChecked)} of {CompareParameters.Count} parameters (family and type are always compared)";

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
        public ICommand ResetParametersCommand { get; }
        public ICommand SelectGroupCommand { get; }
        public ICommand SetTagCommand { get; }

        private void Analyze(bool reloadParameters)
        {
            string tagParam = (TagParameter ?? string.Empty).Trim();
            if (tagParam.Length == 0)
            {
                _main.Status = $"Choose the parameter that holds the {_profile.Noun} tag.";
                return;
            }
            AppSettings.Save(File("tag-parameter"), tagParam);
            AppSettings.Save(File("name-rule"), NameRule);
            string rule = NameRule;
            _main.Status = $"Reading {_profile.NounPlural}...";

            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null || doc.IsFamilyDocument)
                {
                    _main.Status = "Open a project first.";
                    return;
                }

                List<FamilyInstance> all = TagGroupService.Collect(doc, _profile.Category, ActiveViewOnly ? uidoc.ActiveView : null);
                List<FamilyInstance> elements = all.Where(e => TagGroupService.MatchesNameRule(e, rule)).ToList();
                int skipped = all.Count - elements.Count;

                if (reloadParameters || CompareParameters.Count == 0 || !MainViewModel.IsSame(doc, _doc))
                    LoadParameterLists(elements, tagParam);

                var compare = CompareParameters.Where(p => p.IsChecked).Select(p => p.Info).ToList();
                AppSettings.Save(File("compare"), string.Join(";", compare.Select(c => c.Name)));

                List<TagGroup> groups = TagGroupService.Group(elements, compare, tagParam);

                // A tag is "reused" when it appears in more than one group.
                var reused = new HashSet<string>(
                    groups.SelectMany(g => g.Tags.Keys.Select(t => new { t, g }))
                        .GroupBy(x => x.t, StringComparer.OrdinalIgnoreCase)
                        .Where(x => x.Select(y => y.g).Distinct().Count() > 1)
                        .Select(x => x.Key),
                    StringComparer.OrdinalIgnoreCase);

                _doc = doc;
                _all = groups.Select(g => new TagGroupRow(g, reused, _profile.Noun, _profile.NounPlural))
                    .OrderBy(r => r.SortTag, NaturalComparer.Instance)
                    .ThenBy(r => r.Title, NaturalComparer.Instance)
                    .ToList();
                ApplyFilter();

                int problems = _all.Count(r => r.Problem);
                Summary = $"{elements.Count} {_profile.NounPlural} in {_all.Count} groups  ·  {problems} group(s) need attention" +
                          $"  ·  {skipped} other families skipped (name rule)";
                _main.Status = $"{_profile.Title}s analyzed at {DateTime.Now:t}.";
            });
        }

        private void LoadParameterLists(List<FamilyInstance> elements, string tagParam)
        {
            string saved = AppSettings.Load(File("compare"), null);
            var savedSet = saved == null
                ? null
                : new HashSet<string>(saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

            CompareParameters.Clear();
            foreach (TagParamInfo info in TagGroupService.GetCandidateParameters(elements, tagParam))
            {
                var row = new CompareParamRow
                {
                    Info = info,
                    IsChecked = savedSet == null ? _profile.CheckedByDefault(info) : savedSet.Contains(info.Name),
                };
                row.PropertyChanged += (_, __) => OnPropertyChanged(nameof(ParamsHeader));
                CompareParameters.Add(row);
            }
            OnPropertyChanged(nameof(ParamsHeader));

            string current = TagParameter;
            TagParameters.Clear();
            foreach (string name in TagGroupService.GetTextParameters(elements)) TagParameters.Add(name);
            TagParameter = current;
        }

        private void ResetParameters()
        {
            foreach (CompareParamRow row in CompareParameters)
                row.IsChecked = _profile.CheckedByDefault(row.Info);
            Analyze(reloadParameters: false);
        }

        private void ApplyFilter()
        {
            Groups.Clear();
            IEnumerable<TagGroupRow> rows = _all;
            if (OnlyProblems) rows = rows.Where(r => r.Problem);
            foreach (TagGroupRow r in rows.Matching(Search, x => x.Title + " " + x.TagsText + " " + x.DetailsText))
                Groups.Add(r);
        }

        private void SelectGroup(TagGroupRow row)
        {
            if (row == null) return;
            var ids = row.Group.ElementIds.ToList();
            App.Tasks.Run(app =>
            {
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null || !MainViewModel.IsSame(uidoc.Document, _doc))
                {
                    _main.Status = $"The active project changed. Click '{AnalyzeLabel}' again.";
                    return;
                }
                var existing = ids.Where(id => uidoc.Document.GetElement(id) != null).ToList();
                uidoc.Selection.SetElementIds(existing);
                _main.Status = $"Selected {existing.Count} {_profile.NounPlural} ({row.Title}). Edit the tag in Properties, or use 'Set tag'.";
            });
        }

        private void SetTag(TagGroupRow row)
        {
            if (row == null) return;
            string value = (row.NewTag ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                _main.Status = "Type the tag first.";
                return;
            }
            string tagParam = TagParameter;
            var ids = row.Group.ElementIds.ToList();
            App.Tasks.Run(app =>
            {
                Document doc = app.ActiveUIDocument?.Document;
                if (!MainViewModel.IsSame(doc, _doc))
                {
                    _main.Status = $"The active project changed. Click '{AnalyzeLabel}' again.";
                    return;
                }
                int failed = TagGroupService.SetTag(doc, ids, tagParam, value);
                _main.Status = failed == 0
                    ? $"Set {tagParam} = {value} on {ids.Count} {_profile.NounPlural}."
                    : $"Set {tagParam} = {value} on {ids.Count - failed} {_profile.NounPlural}; {failed} could not be changed (in a group or read-only).";
                Analyze(reloadParameters: false);
            });
        }
    }
}
