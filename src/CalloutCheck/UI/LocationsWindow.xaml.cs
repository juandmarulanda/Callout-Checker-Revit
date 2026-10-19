using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace CalloutCheck.UI
{
    /// <summary>Small popup listing the sheets (and host views) where a detail is called out.</summary>
    public partial class LocationsWindow : Window
    {
        public string ViewName { get; }
        public string Summary { get; }
        public IReadOnlyList<LocationRow> Rows { get; }
        public ICommand ShowCalloutCommand { get; }

        internal LocationsWindow(MainViewModel vm, string viewName, string reference, List<LocationRow> rows)
        {
            InitializeComponent();
            ViewName = viewName;
            Rows = rows;
            ShowCalloutCommand = vm.ShowCalloutCommand;
            int onSheets = rows.Count(r => r.OnSheet);
            Summary = $"Detail {reference}  ·  {rows.Count} callout(s), {onSheets} on sheets";
            Icon = App.LoadIcon(32);
            DataContext = this;
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
