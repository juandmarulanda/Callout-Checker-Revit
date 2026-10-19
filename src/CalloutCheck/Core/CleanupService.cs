using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CalloutCheck.Core
{
    internal sealed class UnplacedViewInfo
    {
        public ElementId ViewId { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public bool IsActive { get; set; }
    }

    internal sealed class JoinResult
    {
        public int Total { get; set; }
        public int Updated { get; set; }
        public List<string> Failures { get; } = new List<string>();
    }

    internal static class CleanupService
    {
        /// <summary>
        /// Section views (and optionally detail callouts) that are not on any sheet, neither
        /// themselves nor through a dependent view. View templates are never listed.
        /// </summary>
        public static List<UnplacedViewInfo> FindUnplacedSections(Document doc, View activeView, bool includeCallouts)
        {
            var placed = new HashSet<ElementId>(
                new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(vp => vp.ViewId));

            var result = new List<UnplacedViewInfo>();
            foreach (ViewSection view in new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>())
            {
                if (view.IsTemplate) continue;
                bool isSection = view.ViewType == ViewType.Section;
                bool isCallout = view.ViewType == ViewType.Detail;
                if (!isSection && !(includeCallouts && isCallout)) continue;
                if (placed.Contains(view.Id)) continue;
                if (view.GetDependentViewIds().Any(placed.Contains)) continue;

                result.Add(new UnplacedViewInfo
                {
                    ViewId = view.Id,
                    Name = view.Name,
                    Kind = isSection ? "Section" : "Callout",
                    IsActive = activeView != null && activeView.Id == view.Id,
                });
            }
            return result.OrderBy(r => r.Kind).ThenBy(r => r.Name, NaturalComparer.Instance).ToList();
        }

        /// <summary>Deletes the views (and with them their markers). Returns how many were deleted.</summary>
        public static int DeleteViews(Document doc, IList<ElementId> viewIds, out List<string> failures)
        {
            failures = new List<string>();
            int deleted = 0;
            using (var t = new Transaction(doc, "CalloutCheck - Delete unplaced sections"))
            {
                t.Start();
                foreach (ElementId id in viewIds)
                {
                    Element view = doc.GetElement(id);
                    if (view == null) continue;
                    string name = view.Name;
                    try
                    {
                        doc.Delete(id);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{name}: {ex.Message}");
                    }
                }
                t.Commit();
            }
            return deleted;
        }

        /// <summary>Disallows the join at both ends of every structural framing element given.</summary>
        public static JoinResult DisallowJoins(Document doc, IEnumerable<FamilyInstance> beams)
        {
            var result = new JoinResult();
            using (var t = new Transaction(doc, "CalloutCheck - Disallow beam joins"))
            {
                t.Start();
                foreach (FamilyInstance beam in beams)
                {
                    result.Total++;
                    try
                    {
                        StructuralFramingUtils.DisallowJoinAtEnd(beam, 0);
                        StructuralFramingUtils.DisallowJoinAtEnd(beam, 1);
                        result.Updated++;
                    }
                    catch (Exception ex)
                    {
                        result.Failures.Add($"{beam.Id}: {ex.Message}");
                    }
                }
                t.Commit();
            }
            return result;
        }

        public static List<FamilyInstance> CollectBeams(Document doc, ICollection<ElementId> onlyThese)
        {
            IEnumerable<FamilyInstance> beams = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>();
            if (onlyThese != null)
            {
                var set = new HashSet<ElementId>(onlyThese);
                beams = beams.Where(b => set.Contains(b.Id));
            }
            return beams.ToList();
        }
    }
}
