using Switch.Pages;
using System.Windows;

namespace Switch
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Use FindName to locate the Frame defined in XAML in case the generated field
            // is not available to the code-behind (prevents CS0103).
            var frame = (System.Windows.Controls.Frame?)FindName("MainFrame");
            frame?.Navigate(new HomePage());

            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Prevent uninstall/close if there are active locks
            if (!Service.BlockStore.Instance.CanUninstallApp())
            {
                var popup = new ModernPopup("Cannot uninstall SWITCH", "Cannot uninstall SWITCH - you have active blocks!");
                popup.Owner = this;
                popup.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                popup.ShowDialog();
                e.Cancel = true;
            }
        }
    }
}