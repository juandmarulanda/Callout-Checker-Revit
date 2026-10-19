using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CalloutCheck.Core
{
    internal sealed class SheetInfo
    {
        public ElementId Id { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public string Display => $"{Number} - {Name}";
    }

    /// <summary>Where a drafting view is placed (its viewport on a detail sheet).</summary>
    internal sealed class SheetPlacement
    {
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public string DetailNumber { get; set; }
    }

    /// <summary>One reference callout / section / elevation pointing at a drafting view.</summary>
    internal sealed class CalloutLocation
    {
        public ElementId CalloutId { get; set; }
        public ElementId HostViewId { get; set; }
        public string HostViewName { get; set; }
        public List<SheetPlacement> HostSheets { get; set; } = new List<SheetPlacement>();
        public bool OnSheet => HostSheets.Count > 0;

        public string SheetText => OnSheet
            ? string.Join(", ", HostSheets.Select(s => $"{s.SheetNumber} - {s.SheetName}").Distinct())
            : "(host view is not placed on any sheet)";
    }

    internal sealed class DetailEntry
    {
        public ElementId ViewId { get; set; }
        public string ViewName { get; set; }
        public SheetPlacement Placement { get; set; }
        public List<CalloutLocation> Locations { get; set; } = new List<CalloutLocation>();

        public int CountCallouts(bool onlyOnSheets) =>
            onlyOnSheets ? Locations.Count(l => l.OnSheet) : Locations.Count;
    }

    internal sealed class ViewportInfo
    {
        public ElementId ViewportId { get; set; }
        public string DetailNumber { get; set; }
        public string ViewName { get; set; }
        public string ViewKind { get; set; }
    }
}
