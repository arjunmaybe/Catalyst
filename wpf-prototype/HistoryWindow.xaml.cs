using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Catalyst
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();
            Refresh();
        }

        public void Refresh()
        {
            HistoryList.ItemsSource = HistoryStore.Load();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not HistoryEntry entry) return;

            try
            {
                var path = File.Exists(entry.OutputPath) ? entry.OutputPath : entry.SourcePath;
                if (!File.Exists(path))
                {
                    System.Windows.MessageBox.Show(
                        $"File no longer exists:\n{path}", "Catalyst",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Couldn't open the folder:\n{ex.Message}", "Catalyst",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryStore.Clear();
            Refresh();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
