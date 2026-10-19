using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CalloutCheck.Core
{
    /// <summary>
    /// Finds the drafting views placed on the chosen detail sheets and every reference
    /// callout / section / elevation in the model that points at them.
    /// </summary>
    internal static class CalloutAuditService
    {
        public static List<SheetInfo> GetSheets(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .Select(s => new SheetInfo { Id = s.Id, Number = s.SheetNumber, Name = s.Name })
                .OrderBy(s => s.Number, NaturalComparer.Instance)
                .ToList();
        }

        public static List<DetailEntry> Audit(Document doc, ICollection<ElementId> detailSheetIds)
        {
            Dictionary<ElementId, List<SheetPlacement>> placements = BuildPlacementMap(doc);
            var detailSheets = new HashSet<ElementId>(detailSheetIds);

            // 1. Drafting views placed on the selected detail sheets.
            var details = new Dictionary<ElementId, DetailEntry>();
            foreach (ElementId sheetId in detailSheets)
            {
                if (!(doc.GetElement(sheetId) is ViewSheet sheet)) continue;
                foreach (ElementId vpId in sheet.GetAllViewports())
                {
                    if (!(doc.GetElement(vpId) is Viewport vp)) continue;
                    if (!(doc.GetElement(vp.ViewId) is View view) || view.ViewType != ViewType.DraftingView) continue;
                    if (details.ContainsKey(view.Id)) continue;

                    details[view.Id] = new DetailEntry
                    {
                        ViewId = view.Id,
                        ViewName = view.Name,
                        Placement = new SheetPlacement
                        {
                            SheetNumber = sheet.SheetNumber,
                            SheetName = sheet.Name,
                            DetailNumber = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() ?? string.Empty,
                        },
                    };
                }
            }

            // 2. Reference markers that point at those drafting views.
            foreach (Element marker in CollectMarkerCandidates(doc))
            {
                ElementId target = GetReferencedViewId(doc, marker);
                if (target == null || !details.TryGetValue(target, out DetailEntry entry)) continue;

                ElementId hostId = marker.OwnerViewId;
                var host = hostId != null && hostId != ElementId.InvalidElementId ? doc.GetElement(hostId) as View : null;
                entry.Locations.Add(new CalloutLocation
                {
                    CalloutId = marker.Id,
                    HostViewId = host?.Id ?? ElementId.InvalidElementId,
                    HostViewName = host?.Name ?? "(unknown view)",
                    HostSheets = host != null ? FindSheetsShowing(doc, host, placements) : new List<SheetPlacement>(),
                });
            }

            return details.Values
                .OrderBy(d => d.Placement.SheetNumber, NaturalComparer.Instance)
                .ThenBy(d => d.Placement.DetailNumber, NaturalComparer.Instance)
                .ToList();
        }

        /// <summary>viewId -> sheets on which the view is placed.</summary>
        private static Dictionary<ElementId, List<SheetPlacement>> BuildPlacementMap(Document doc)
        {
            var map = new Dictionary<ElementId, List<SheetPlacement>>();
            foreach (Viewport vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                if (!(doc.GetElement(vp.SheetId) is ViewSheet sheet) || sheet.IsPlaceholder) continue;
                if (!map.TryGetValue(vp.ViewId, out List<SheetPlacement> list))
                    map[vp.ViewId] = list = new List<SheetPlacement>();
                list.Add(new SheetPlacement
                {
                    SheetNumber = sheet.SheetNumber,
                    SheetName = sheet.Name,
                    DetailNumber = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() ?? string.Empty,
                });
            }
            return map;
        }

        /// <summary>
        /// Sheets where a callout drawn in <paramref name="host"/> appears: the host's own sheet,
        /// plus sheets of its dependent views (annotations are shared with dependents) and of its primary view.
        /// </summary>
        private static List<SheetPlacement> FindSheetsShowing(Document doc, View host, Dictionary<ElementId, List<SheetPlacement>> placements)
        {
            var ids = new List<ElementId> { host.Id };
            try { ids.AddRange(host.GetDependentViewIds()); } catch (Exception) { }
            try
            {
                ElementId primary = host.GetPrimaryViewId();
                if (primary != null && primary != ElementId.InvalidElementId) ids.Add(primary);
            }
            catch (Exception) { }

            var result = new List<SheetPlacement>();
            foreach (ElementId id in ids.Distinct())
                if (placements.TryGetValue(id, out List<SheetPlacement> list))
                    result.AddRange(list);
            return result;
        }

        private static IEnumerable<Element> CollectMarkerCandidates(Document doc)
        {
            // Reference callouts/sections/elevations live in the "Views"(Viewers) family of categories.
            // Names are resolved at runtime so the add-in keeps working if a category is renamed between versions.
            var categories = new List<BuiltInCategory>();
            foreach (string name in new[] { "OST_Viewers", "OST_Callouts", "OST_Sections", "OST_Elev", "OST_ReferenceViewer", "OST_ReferenceViewerSymbol" })
                if (Enum.TryParse(name, out BuiltInCategory bic)) categories.Add(bic);

            return new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(categories))
                .WhereElementIsNotElementType()
                .Where(e => !(e is View) && e.ViewSpecific);
        }

        private static readonly BuiltInParameter? TargetViewParam =
            Enum.TryParse("REFERENCE_VIEWER_TARGET_VIEW", out BuiltInParameter p) ? p : (BuiltInParameter?)null;

        private static ElementId GetReferencedViewId(Document doc, Element marker)
        {
            if (TargetViewParam.HasValue)
            {
                Parameter param = marker.get_Parameter(TargetViewParam.Value);
                if (param != null && param.StorageType == StorageType.ElementId)
                {
                    ElementId id = param.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId) return id;
                }
            }

            try
            {
                ElementId id = ReferenceableViewUtils.GetReferencedViewId(doc, marker.Id);
                return id != null && id != ElementId.InvalidElementId ? id : null;
            }
            catch (Exception)
            {
                return null; // not a reference marker (regular section/callout/elevation)
            }
        }
    }
}
