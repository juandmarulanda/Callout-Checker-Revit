using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CalloutCheck.Core
{
    internal static class RenumberService
    {
        public static List<ViewportInfo> GetViewports(Document doc, ViewSheet sheet)
        {
            var list = new List<ViewportInfo>();
            foreach (ElementId id in sheet.GetAllViewports())
            {
                if (!(doc.GetElement(id) is Viewport vp)) continue;
                var view = doc.GetElement(vp.ViewId) as View;
                list.Add(new ViewportInfo
                {
                    ViewportId = vp.Id,
                    DetailNumber = vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() ?? string.Empty,
                    ViewName = view?.Name ?? "(unknown)",
                    ViewKind = view != null ? Describe(view.ViewType) : string.Empty,
                });
            }
            return list.OrderBy(v => v.DetailNumber, NaturalComparer.Instance).ToList();
        }

        /// <summary>Lets the user click viewports on the active sheet one by one; ESC finishes.</summary>
        public static List<ElementId> PickInOrder(UIDocument uidoc, ViewSheet sheet)
        {
            var picked = new List<ElementId>();
            var filter = new ViewportOnSheetFilter(sheet.Id, picked);
            while (true)
            {
                try
                {
                    Reference r = uidoc.Selection.PickObject(
                        ObjectType.Element, filter,
                        $"CalloutCheck: pick view #{picked.Count + 1} in the new order (ESC when done)");
                    picked.Add(r.ElementId);
                    uidoc.Selection.SetElementIds(picked);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }
            }
            return picked;
        }

        /// <summary>Applies new detail numbers. Goes through temporary values so swaps never collide.</summary>
        public static void Apply(Document doc, ViewSheet sheet, IList<KeyValuePair<ElementId, string>> assignments)
        {
            var assigned = new HashSet<ElementId>(assignments.Select(a => a.Key));
            var finalNumbers = assignments.Select(a => a.Value.Trim()).ToList();

            if (finalNumbers.Any(string.IsNullOrEmpty))
                throw new System.InvalidOperationException("Detail numbers cannot be empty.");

            // Numbers held by viewports we are not touching must stay unique too.
            var kept = sheet.GetAllViewports()
                .Where(id => !assigned.Contains(id))
                .Select(id => doc.GetElement(id)?.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString())
                .Where(s => s != null);
            var duplicates = finalNumbers.Concat(kept)
                .GroupBy(s => s, System.StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicates.Count > 0)
                throw new System.InvalidOperationException("Duplicate detail numbers on this sheet: " + string.Join(", ", duplicates));

            using (var t = new Transaction(doc, "CalloutCheck - Renumber views"))
            {
                t.Start();
                int i = 0;
                foreach (var a in assignments)
                    DetailNumberParam(doc, a.Key)?.Set($"~cc{++i}~{a.Key}");
                foreach (var a in assignments)
                    DetailNumberParam(doc, a.Key)?.Set(a.Value.Trim());
                t.Commit();
            }
        }

        private static Parameter DetailNumberParam(Document doc, ElementId viewportId)
        {
            Parameter p = doc.GetElement(viewportId)?.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
            return p != null && !p.IsReadOnly ? p : null;
        }

        private static string Describe(ViewType type)
        {
            switch (type)
            {
                case ViewType.DraftingView: return "Drafting";
                case ViewType.Detail: return "Detail";
                case ViewType.Section: return "Section";
                case ViewType.Elevation: return "Elevation";
                case ViewType.FloorPlan: return "Floor Plan";
                case ViewType.CeilingPlan: return "Ceiling Plan";
                case ViewType.Legend: return "Legend";
                case ViewType.ThreeD: return "3D";
                default: return type.ToString();
            }
        }

        private sealed class ViewportOnSheetFilter : ISelectionFilter
        {
            private readonly ElementId _sheetId;
            private readonly List<ElementId> _picked;

            public ViewportOnSheetFilter(ElementId sheetId, List<ElementId> picked)
            {
                _sheetId = sheetId;
                _picked = picked;
            }

            public bool AllowElement(Element elem) =>
                elem is Viewport vp && vp.SheetId == _sheetId && !_picked.Contains(vp.Id);

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
