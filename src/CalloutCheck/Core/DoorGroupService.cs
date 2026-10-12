using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace CalloutCheck.Core
{
    /// <summary>A parameter that can take part in the "same door" comparison.</summary>
    internal sealed class DoorParamInfo
    {
        public string Name { get; set; }
        public bool IsLength { get; set; }
    }

    /// <summary>Doors that share family, type and the compared dimension values.</summary>
    internal sealed class DoorGroup
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public List<KeyValuePair<string, string>> Dimensions { get; set; } = new List<KeyValuePair<string, string>>();
        public List<ElementId> DoorIds { get; set; } = new List<ElementId>();
        public Dictionary<string, int> Tags { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public int MissingTags { get; set; }
    }

    internal static class DoorGroupService
    {
        public const string DefaultTagParameter = "LCH_Txt_Mark1";

        public static List<FamilyInstance> CollectDoors(Document doc, View scopeView)
        {
            var collector = scopeView != null
                ? new FilteredElementCollector(doc, scopeView.Id)
                : new FilteredElementCollector(doc);
            return collector
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();
        }

        /// <summary>
        /// Instance parameters in the Dimensions group (lengths and whole numbers such as
        /// "Unit Width Options"), i.e. the per-instance variations a family can have.
        /// </summary>
        public static List<DoorParamInfo> GetDimensionParameters(IEnumerable<FamilyInstance> doors)
        {
            var found = new Dictionary<string, DoorParamInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (FamilyInstance door in doors)
            {
                foreach (Parameter p in door.Parameters)
                {
                    if (p?.Definition == null || found.ContainsKey(p.Definition.Name)) continue;
                    if (p.Definition.GetGroupTypeId() != GroupTypeId.Geometry) continue;

                    ForgeTypeId spec = p.Definition.GetDataType();
                    bool isLength = spec == SpecTypeId.Length;
                    bool isWhole = spec == SpecTypeId.Int.Integer;
                    if (!isLength && !isWhole) continue;

                    found[p.Definition.Name] = new DoorParamInfo { Name = p.Definition.Name, IsLength = isLength };
                }
            }
            return found.Values.OrderBy(v => v.Name, NaturalComparer.Instance).ToList();
        }

        /// <summary>Writable text instance parameters, candidates for holding the door tag.</summary>
        public static List<string> GetTextParameters(IEnumerable<FamilyInstance> doors)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FamilyInstance door in doors.Take(200))
                foreach (Parameter p in door.Parameters)
                    if (p?.Definition != null && p.StorageType == StorageType.String && !p.IsReadOnly)
                        names.Add(p.Definition.Name);
            return names.OrderBy(n => n, NaturalComparer.Instance).ToList();
        }

        public static List<DoorGroup> Group(Document doc, IEnumerable<FamilyInstance> doors,
                                            IList<string> compareParams, string tagParam)
        {
            var groups = new Dictionary<string, DoorGroup>();
            foreach (FamilyInstance door in doors)
            {
                FamilySymbol symbol = door.Symbol;
                if (symbol == null) continue;

                var keyParts = new List<string> { symbol.Id.ToString() };
                var dims = new List<KeyValuePair<string, string>>();
                foreach (string name in compareParams)
                {
                    Parameter p = door.LookupParameter(name) ?? symbol.LookupParameter(name);
                    keyParts.Add(KeyOf(p));
                    if (p != null) dims.Add(new KeyValuePair<string, string>(name, p.AsValueString() ?? KeyOf(p)));
                }

                string key = string.Join("|", keyParts);
                if (!groups.TryGetValue(key, out DoorGroup group))
                {
                    groups[key] = group = new DoorGroup
                    {
                        FamilyName = symbol.FamilyName,
                        TypeName = symbol.Name,
                        Dimensions = dims,
                    };
                }

                group.DoorIds.Add(door.Id);
                string tag = door.LookupParameter(tagParam)?.AsString()?.Trim();
                if (string.IsNullOrEmpty(tag)) group.MissingTags++;
                else group.Tags[tag] = group.Tags.TryGetValue(tag, out int n) ? n + 1 : 1;
            }
            return groups.Values.ToList();
        }

        /// <summary>Writes the tag to every door; returns how many could not be changed.</summary>
        public static int SetTag(Document doc, IEnumerable<ElementId> doorIds, string tagParam, string value)
        {
            int failed = 0;
            using (var t = new Transaction(doc, "CalloutCheck - Set door tag"))
            {
                t.Start();
                foreach (ElementId id in doorIds)
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
                case StorageType.String: return p.AsString() ?? string.Empty;
                case StorageType.ElementId: return p.AsElementId()?.ToString() ?? "-";
                default: return "-";
            }
        }
    }
}
