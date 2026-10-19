using System;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;

namespace CalloutCheck.UI
{
    public partial class MainWindow : Window
    {
        internal static MainWindow Instance { get; private set; }

        private readonly MainViewModel _vm = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
            Icon = App.LoadIcon(32);
            HeaderIcon.Source = App.LoadIcon(32);
        }

        internal static void ShowSingleton(UIApplication uiapp)
        {
            if (Instance != null)
            {
                if (Instance.WindowState == WindowState.Minimized) Instance.WindowState = WindowState.Normal;
                Instance.Activate();
                Instance._vm.Refresh();
                return;
            }

            var window = new MainWindow();
            new WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
            window.Closed += (_, __) => Instance = null;
            Instance = window;
            window.Show();
            window._vm.Refresh(keepSheetSelection: false);
        }

        internal static void CloseIfOpen()
        {
            try { Instance?.Close(); }
            catch (Exception) { }
        }
    }
}
