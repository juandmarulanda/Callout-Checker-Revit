using System.Linq;
using System.Windows;

namespace CalloutCheck.UI
{
    public sealed class HelpStep
    {
        public int Number { get; set; }
        public string Text { get; set; }
    }

    public partial class HelpWindow : Window
    {
        internal HelpWindow(string title, string[] steps)
        {
            InitializeComponent();
            Title = $"How it works - {title}";
            Heading.Text = title;
            StepList.ItemsSource = steps.Select((s, i) => new HelpStep { Number = i + 1, Text = s }).ToList();
            Icon = App.LoadIcon(32);
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
