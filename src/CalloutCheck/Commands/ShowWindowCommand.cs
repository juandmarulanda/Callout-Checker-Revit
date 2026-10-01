using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CalloutCheck.UI;

namespace CalloutCheck.Commands
{
    /// <summary>Opens (or brings to front) the modeless CalloutCheck window.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShowWindowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            MainWindow.ShowSingleton(commandData.Application);
            return Result.Succeeded;
        }
    }
}
