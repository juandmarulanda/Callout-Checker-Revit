using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace CalloutCheck.Core
{
    internal enum ParamKind { Length, Number, YesNo, Text, FamilyType }

    /// <summary>A parameter that can take part in the "same element" comparison.</summary>
    internal sealed class TagParamInfo
    {
        public string Name { get; set; }
        public string GroupLabel { get; set; }
        public ForgeTypeId Group { get; set; }
        public ParamKind Kind { get; set; }
        public bool IsReadOnly { get; set; }
    }

    /// <summary>Settings that differ between doors and windows.</summary>
    internal sealed class TagProfile
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Noun { get; set; }
        public string NounPlural { get; set; }
        public BuiltInCategory Category { get; set; }
        public string DefaultNameRule { get; set; }
        public string NameRuleHelp { get; set; }
        public Func<TagParamInfo, bool> CheckedByDefault { get; set; }

        public static readonly TagProfile Doors = new TagProfile
        {
            Key = "door",
            Title = "Door",
            Noun = "door",
            NounPlural = "doors",
            Category = BuiltInCategory.OST_Doors,
            DefaultNameRule = "05*; 06*; *garage*",
            NameRuleHelp = "Door families start with 05/06 or contain 'garage'. Trim, hardware and other families are skipped.",
            // Size + handing (Hinge Left / Hinge Right and other Construction yes/no options).
            CheckedByDefault = p =>
                (p.Group == GroupTypeId.Geometry && (p.Kind == ParamKind.Length || p.Kind == ParamKind.Number))
                || (p.Group == GroupTypeId.Construction && p.Kind == ParamKind.YesNo),
        };
    }

    /// <summary>Elements that share family, type and the compared parameter values.</summary>
    internal sealed class TagGroup
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public List<string> Details { get; set; } = new List<string>();
        public List<ElementId> ElementIds { get; set; } = new List<ElementId>();
        public Dictionary<string, int> Tags { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int MissingTags { get; set; }
    }

    internal static class TagGroupService
    {
        public const string DefaultTagParameter = "LCH_Txt_Mark1";

        private static readonly ForgeTypeId[] CandidateGroups =
        {
            GroupTypeId.Geometry, GroupTypeId.Construction, GroupTypeId.IdentityData,
        };

        private static readonly HashSet<BuiltInParameter> NeverCompared = new HashSet<BuiltInParameter>
        {
            BuiltInParameter.ALL_MODEL_MARK,
            BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
            BuiltInParameter.ALL_MODEL_IMAGE,
        };

        public static List<FamilyInstance> Collect(Document doc, BuiltInCategory category, View scopeView)
        {
            var collector = scopeView != null
                ? new FilteredElementCollector(doc, scopeView.Id)
                : new FilteredElementCollector(doc);
            return collector
                .OfCategory(category)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();
        }

        /// <summary>
        /// Name rule: ';'-separated patterns matched against "Family : Type", the family name and the type name.
        /// '*' = any text, '#' = one digit. A pattern without '*' matches anywhere in the name
        /// ("(950)" or "(###)" = contains). Examples: "05*; 06*; *garage*" or "(###)".
        /// </summary>
        public static bool MatchesNameRule(FamilyInstance element, string rule)
        {
            FamilySymbol symbol = element.Symbol;
            if (symbol == null) return false;
            string family = Normalize(symbol.FamilyName);
            string type = Normalize(symbol.Name);
            string[] candidates = { $"{family} : {type}", family, type };
            return SplitKeys(rule)
                .Select(ToRegex)
                .Any(rx => candidates.Any(rx.IsMatch));
        }

        public static string DisplayName(FamilyInstance element) =>
            element.Symbol == null ? "(no type)" : $"{Normalize(element.Symbol.FamilyName)} : {Normalize(element.Symbol.Name)}";

        /// <summary>Trims and collapses whitespace (including non-breaking spaces).</summary>
        private static string Normalize(string text) =>
            Regex.Replace((text ?? string.Empty).Replace('\u00A0', ' '), @"\s+", " ").Trim();

        private static Regex ToRegex(string pattern)
        {
            // Built char by char: Regex.Escape also escapes '#', so a plain Replace would miss it.
            string p = Normalize(pattern);
            if (p.IndexOf('*') < 0) p = "*" + p + "*";   // no wildcard = "contains"
            var body = new System.Text.StringBuilder("^");
            foreach (char c in p)
            {
                if (c == '*') body.Append(".*");
                else if (c == '#') body.Append(@"\d");
                else body.Append(Regex.Escape(c.ToString()));
            }
            body.Append('$');
            return new Regex(body.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static IEnumerable<string> SplitKeys(string text) =>
            (text ?? string.Empty).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => k.Length > 0);

        /// <summary>Instance parameters (Dimensions, Construction, Identity Data) that can vary per element.</summary>
        public static List<TagParamInfo> GetCandidateParameters(IEnumerable<FamilyInstance> elements, string tagParam)
        {
            var found = new Dictionary<string, TagParamInfo>(StringComparer.OrdinalIgnoreCase);
            // Every instance of a family has the same parameter set, so one instance per family is enough.
            foreach (FamilyInstance element in elements.GroupBy(e => e.Symbol?.Family?.Id?.ToString() ?? "").Select(g => g.First()))
            {
                foreach (Parameter p in element.Parameters)
                {
                    Definition def = p?.Definition;
                    if (def == null || found.ContainsKey(def.Name)) continue;
                    if (string.Equals(def.Name, tagParam, StringComparison.OrdinalIgnoreCase)) continue;
                    if (def is InternalDefinition internalDef && NeverCompared.Contains(internalDef.BuiltInParameter)) continue;

                    ForgeTypeId group = def.GetGroupTypeId();
                    if (!CandidateGroups.Contains(group)) continue;

                    ParamKind? kind = KindOf(p);
                    if (kind == null) continue;

                    found[def.Name] = new TagParamInfo
                    {
                        Name = def.Name,
                        Group = group,
                        GroupLabel = LabelUtils.GetLabelForGroup(group),
                        Kind = kind.Value,
                        IsReadOnly = p.IsReadOnly,
                    };
                }
            }
            return found.Values
                .OrderBy(v => Array.IndexOf(CandidateGroups, v.Group))
                .ThenBy(v => v.Name, NaturalComparer.Instance)
                .ToList();
        }

        private static ParamKind? KindOf(Parameter p)
        {
            ForgeTypeId spec = p.Definition.GetDataType();
            if (spec == SpecTypeId.Length) return ParamKind.Length;
            if (spec == SpecTypeId.Boolean.YesNo) return ParamKind.YesNo;
            if (spec == SpecTypeId.Int.Integer) return ParamKind.Number;
            switch (p.StorageType)
            {
                case StorageType.String: return ParamKind.Text;
                case StorageType.ElementId: return ParamKind.FamilyType;
                case StorageType.Double when spec == SpecTypeId.Number: return ParamKind.Number;
                default: return null;
            }
        }

        /// <summary>Writable text instance parameters, candidates for holding the tag.</summary>
        public static List<string> GetTextParameters(IEnumerable<FamilyInstance> elements)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FamilyInstance element in elements.GroupBy(e => e.Symbol?.Family?.Id?.ToString() ?? "").Select(g => g.First()))
                foreach (Parameter p in element.Parameters)
                    if (p?.Definition != null && p.StorageType == StorageType.String && !p.IsReadOnly)
                        names.Add(p.Definition.Name);
            return names.OrderBy(n => n, NaturalComparer.Instance).ToList();
        }

        public static List<TagGroup> Group(IEnumerable<FamilyInstance> elements, IList<TagParamInfo> compare, string tagParam)
        {
            var groups = new Dictionary<string, TagGroup>();
            foreach (FamilyInstance element in elements)
            {
                FamilySymbol symbol = element.Symbol;
                if (symbol == null) continue;

                var keyParts = new List<string> { symbol.Id.ToString() };
                var details = new List<string>();
                foreach (TagParamInfo info in compare)
                {
                    Parameter p = element.LookupParameter(info.Name);
                    keyParts.Add(KeyOf(p));
                    string detail = Describe(info, p);
                    if (detail != null) details.Add(detail);
                }

                string key = string.Join("|", keyParts);
                if (!groups.TryGetValue(key, out TagGroup group))
                {
                    groups[key] = group = new TagGroup
                    {
                        FamilyName = symbol.FamilyName,
                        TypeName = symbol.Name,
                        Details = details,
                    };
                }

                group.ElementIds.Add(element.Id);
                string tag = element.LookupParameter(tagParam)?.AsString()?.Trim();
                if (string.IsNullOrEmpty(tag)) group.MissingTags++;
                else group.Tags[tag] = group.Tags.TryGetValue(tag, out int n) ? n + 1 : 1;
            }
            return groups.Values.ToList();
        }

        /// <summary>Short text for the group card. Yes/No options are listed only when ticked.</summary>
        private static string Describe(TagParamInfo info, Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            if (info.Kind == ParamKind.YesNo) return p.AsInteger() == 1 ? $"✓ {info.Name}" : null;
            string value = p.AsValueString();
            if (string.IsNullOrWhiteSpace(value)) value = p.StorageType == StorageType.String ? p.AsString() : null;
            return string.IsNullOrWhiteSpace(value) ? null : $"{info.Name}: {value}";
        }

        /// <summary>Writes the tag to every element; returns how many could not be changed.</summary>
        public static int SetTag(Document doc, IEnumerable<ElementId> ids, string tagParam, string value)
        {
            int failed = 0;
            using (var t = new Transaction(doc, "CalloutCheck - Set tag"))
            {
                t.Start();
                foreach (ElementId id in ids)
                {
                    Parameter p = doc.GetElement(id)?.LookupParameter(tagParam);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) { failed++; continue; }
                    try { p.Set(value); }
                    catch (Exception) { failed++; }
                }
                t.Commit();
            }
            return failed;
        }

        private static string KeyOf(Parameter p)
        {
            if (p == null || !p.HasValue) return "-";
            switch (p.StorageType)
            {
                // 1/1000 inch tolerance so floating noise never splits a group
                case StorageType.Double: return Math.Round(p.AsDouble() * 12000.0).ToString(CultureInfo.InvariantCulture);
                case StorageType.Integer: return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.String: return (p.AsString() ?? string.Empty).Trim().ToUpperInvariant();
                case StorageType.ElementId: return p.AsElementId()?.ToString() ?? "-";
                default: return "-";
            }
        }
    }
}
