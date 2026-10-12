using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using CalloutCheck.Commands;
using CalloutCheck.Core;

namespace CalloutCheck
{
    /// <summary>Revit entry point: builds the ribbon button and owns the shared ExternalEvent queue.</summary>
    public class App : IExternalApplication
    {
        public const string TabName = "CalloutCheck";

        internal static RevitTaskQueue Tasks { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            Tasks = new RevitTaskQueue();

            try { application.CreateRibbonTab(TabName); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* tab already exists */ }

            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Details");
            var button = new PushButtonData(
                "CalloutCheckShow",
                "Callout\nCheck",
                Assembly.GetExecutingAssembly().Location,
                typeof(ShowWindowCommand).FullName)
            {
                ToolTip = "Find drafting views on detail sheets that have not been called out yet, " +
                          "renumber the viewports on a sheet in pick order, and give identical doors and windows the same tag.",
                LargeImage = LoadIcon(32),
                Image = LoadIcon(16),
            };
            panel.AddItem(button);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            UI.MainWindow.CloseIfOpen();
            Tasks?.Dispose();
            return Result.Succeeded;
        }

        internal static BitmapImage LoadIcon(int size)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/CalloutCheck;component/Resources/icon{size}.png", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
