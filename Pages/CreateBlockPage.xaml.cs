using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Switch.Service;

    // Helper view model for processes list
    public class ProcessListItem
    {
        public string Display { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ExePath { get; set; } = string.Empty;
        public override string ToString() => Display;
    }

namespace Switch.Pages
{
    /// <summary>
    /// Interaction logic for CreateBlockPage.xaml
    /// </summary>
    public partial class CreateBlockPage : Page
    {
        // cached full lists for filtering
        private List<string> _allInstalledApps = new List<string>();
        private List<ProcessListItem> _allProcesses = new List<ProcessListItem>();

        public CreateBlockPage()
        {
            InitializeComponent();
            // initialize date/time pickers - default to current date/time
            EndDatePicker.SelectedDate = DateTime.Now;
            HourCombo.Items.Clear();
            for (int h = 0; h < 24; h++) HourCombo.Items.Add(h.ToString("00"));
            MinuteCombo.Items.Clear();
            for (int m = 0; m < 60; m += 1) MinuteCombo.Items.Add(m.ToString("00"));
            HourCombo.SelectedIndex = DateTime.Now.Hour;
            MinuteCombo.SelectedIndex = DateTime.Now.Minute;
            SelectRunningBtn.Click += SelectRunningBtn_Click;
            SelectProcessesBtn.Click += SelectProcessesBtn_Click;
            RunningSearch.TextChanged += RunningSearch_TextChanged;
            ProcessSearch.TextChanged += ProcessSearch_TextChanged;
            ShowRelatedBtn.Click += ShowRelatedBtn_Click;
            RelatedSearch.TextChanged += RelatedSearch_TextChanged;
            RelatedInstalledList.MouseDoubleClick += RelatedInstalledList_MouseDoubleClick;
            RelatedRunningList.MouseDoubleClick += RelatedRunningList_MouseDoubleClick;
            // slow scroll handlers
            this.MainScroll.PreviewMouseWheel += MainScroll_PreviewMouseWheel;
            RunningList.PreviewMouseWheel += ChildList_PreviewMouseWheel;
            ProcessesList.PreviewMouseWheel += ChildList_PreviewMouseWheel;
            ProcessesList.SelectionChanged += ProcessesList_SelectionChanged;
            RunningList.SelectionChanged += RunningList_SelectionChanged;
            CreateBtn.Click += CreateBtn_Click;
            // Allow editing existing block when AppBox contains an Id tag - wire up when navigated
            SelectFolderBtn.Click += SelectFolderBtn_Click;
            InstallerBlockCheck.Checked += InstallerBlockCheck_Checked;
            InstallerBlockCheck.Unchecked += InstallerBlockCheck_Unchecked;
            CancelBtn.Click += (s, e) => NavigationService?.GoBack();
            // initialize installer-block checkbox from persisted settings
            InstallerBlockCheck.IsChecked = Switch.Service.SettingsStore.Instance.InstallerBlockingEnabled;
        }

        private void SelectFolderBtn_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog();
                // ShowDialog overload that accepts IWin32Window expects a Win32 handle. Use Owner = null when wrapper isn't needed.
                var result = dlg.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    AppBox.Text = dlg.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                Service.Logger.Log($"Folder select failed: {ex.Message}");
            }
        }

        private void Back_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.NavigationService.GoBack();
        }

        private void Lock_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show($"Blocking {AppBox.Text}!");
        }

        private void SelectRunningBtn_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                RunningList.Items.Clear();
                _allInstalledApps.Clear();

                var folders = new[] {
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs)
                }.Where(p => !string.IsNullOrEmpty(p)).Distinct();

                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in folders)
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                    {
                        var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                        if (ext == ".lnk" || ext == ".appref-ms" || ext == ".url" || ext == ".exe")
                        {
                            var name = System.IO.Path.GetFileNameWithoutExtension(file);
                            if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                        }
                    }
                }

                foreach (var n in names.OrderBy(n => n))
                {
                    _allInstalledApps.Add(n);
                }

                // show search box and populate filtered results
                RunningSearch.Text = string.Empty;
                RunningSearch.Visibility = Visibility.Visible;
                RunningList.Visibility = Visibility.Visible;
                UpdateRunningFilter();
                try { RunningSearch.Focus(); } catch { }
            }
            catch (Exception ex)
            {
                Service.Logger.Log($"Failed to enumerate installed apps: {ex.Message}");
            }
        }

        private void SelectProcessesBtn_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                ProcessesList.Items.Clear();
                _allProcesses.Clear();
                var procs = Process.GetProcesses()
                    .OrderBy(p => p.ProcessName)
                    .ToList();

                foreach (var p in procs)
                {
                    try
                    {
                        var exe = Switch.Service.ProcessHelper.GetProcessExecutablePath(p) ?? "";
                        var display = string.IsNullOrEmpty(exe) ? p.ProcessName : System.IO.Path.GetFileName(exe) + "  (" + p.ProcessName + ")";
                        // attach process id so selection can identify process uniquely
                        var pli = new ProcessListItem { Display = display, ProcessId = p.Id, ExePath = exe };
                        _allProcesses.Add(pli);
                    }
                    catch { }
                }

                ProcessSearch.Text = string.Empty;
                ProcessSearch.Visibility = Visibility.Visible;
                ProcessesList.Visibility = Visibility.Visible;
                UpdateProcessFilter();
                try { ProcessSearch.Focus(); } catch { }
            }
            catch (Exception ex)
            {
                Service.Logger.Log($"Failed to enumerate running processes: {ex.Message}");
            }
        }

        private void ProcessesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProcessesList.SelectedItem is ProcessListItem item)
            {
                // Use exe filename (without extension) if available, otherwise process name
                var name = !string.IsNullOrEmpty(item.ExePath) ? System.IO.Path.GetFileNameWithoutExtension(item.ExePath) : item.Display;
                AppBox.Text = name;
                ProcessesList.Visibility = Visibility.Collapsed;
            }
        }

        private void RunningList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RunningList.SelectedItem is string name)
            {
                AppBox.Text = name;
                RunningList.Visibility = Visibility.Collapsed;
                RunningSearch.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowRelatedBtn_Click(object? sender, RoutedEventArgs e)
        {
            // ensure lists are populated
            try { SelectRunningBtn_Click(null, null); } catch { }
            try { SelectProcessesBtn_Click(null, null); } catch { }
            // open popup near button
            RelatedSearch.Text = string.Empty;
            UpdateRelatedFilters();
            RelatedPopup.PlacementTarget = ShowRelatedBtn;
            RelatedPopup.IsOpen = true;
            try { RelatedSearch.Focus(); } catch { }
        }

        private void RelatedSearch_TextChanged(object? sender, TextChangedEventArgs e)
        {
            UpdateRelatedFilters();
        }

        private void UpdateRelatedFilters()
        {
            var q = RelatedSearch.Text?.Trim() ?? string.Empty;
            RelatedInstalledList.Items.Clear();
            RelatedRunningList.Items.Clear();

            IEnumerable<string> inst = _allInstalledApps;
            if (!string.IsNullOrEmpty(q)) inst = inst.Where(s => s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                                                    .OrderBy(s => s.IndexOf(q, StringComparison.OrdinalIgnoreCase));
            foreach (var it in inst) RelatedInstalledList.Items.Add(it);

            IEnumerable<ProcessListItem> run = _allProcesses;
            if (!string.IsNullOrEmpty(q)) run = run.Where(p => p.Display.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                                                    .OrderBy(p => p.Display.IndexOf(q, StringComparison.OrdinalIgnoreCase));
            foreach (var it in run) RelatedRunningList.Items.Add(it.Display + (string.IsNullOrEmpty(it.ExePath) ? "" : " — " + System.IO.Path.GetFileName(it.ExePath)));
        }

        private void RelatedInstalledList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RelatedInstalledList.SelectedItem is string name)
            {
                AppBox.Text = name;
                RelatedPopup.IsOpen = false;
            }
        }

        private void RelatedRunningList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RelatedRunningList.SelectedItem is string s)
            {
                // extract display before separator
                var name = s.Split('—')[0].Trim();
                AppBox.Text = name;
                RelatedPopup.IsOpen = false;
            }
        }

        // Slow scroll: reduce scroll speed on main ScrollViewer
        private void MainScroll_PreviewMouseWheel(object? sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var sv = sender as ScrollViewer ?? this.FindName("MainScroll") as ScrollViewer;
            if (sv == null) return;
            const double factor = 12.0; // smaller = slower
            sv.ScrollToVerticalOffset(sv.VerticalOffset - (e.Delta / 120.0) * factor);
            e.Handled = true;
        }

        // Prevent child list scroll from bubbling to parent and slow it as well
        private void ChildList_PreviewMouseWheel(object? sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var lb = sender as System.Windows.Controls.ListBox;
            if (lb == null) return;
            var sv = System.Windows.Media.VisualTreeHelper.GetChild(lb, 0) as ScrollViewer;
            if (sv == null)
            {
                // try find ScrollViewer inside
                sv = FindVisualChild<ScrollViewer>(lb);
            }
            if (sv != null)
            {
                const double factor = 8.0;
                sv.ScrollToVerticalOffset(sv.VerticalOffset - (e.Delta / 120.0) * factor);
                e.Handled = true;
            }
        }

        private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void RunningSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateRunningFilter();
        }

        private void ProcessSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateProcessFilter();
        }

        private void UpdateRunningFilter()
        {
            var q = RunningSearch.Text?.Trim() ?? string.Empty;
            RunningList.Items.Clear();
            IEnumerable<string> items = _allInstalledApps;
            if (!string.IsNullOrEmpty(q))
            {
                items = items.Where(s => s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                             .OrderBy(s => s.IndexOf(q, StringComparison.OrdinalIgnoreCase));
            }
            foreach (var it in items)
            {
                RunningList.Items.Add(it);
            }
        }

        private void UpdateProcessFilter()
        {
            var q = ProcessSearch.Text?.Trim() ?? string.Empty;
            ProcessesList.Items.Clear();
            IEnumerable<ProcessListItem> items = _allProcesses;
            if (!string.IsNullOrEmpty(q))
            {
                items = items.Where(p => p.Display.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                             .OrderBy(p => p.Display.IndexOf(q, StringComparison.OrdinalIgnoreCase));
            }
            foreach (var it in items)
            {
                ProcessesList.Items.Add(it);
            }
        }

        private void CreateBtn_Click(object? sender, RoutedEventArgs e)
        {
            var name = AppBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            // compute selected end datetime
            if (!EndDatePicker.SelectedDate.HasValue) return;
            var date = EndDatePicker.SelectedDate.Value.Date;
            int hour = HourCombo.SelectedIndex >= 0 ? int.Parse(HourCombo.SelectedItem.ToString()) : 0;
            int minute = MinuteCombo.SelectedIndex >= 0 ? int.Parse(MinuteCombo.SelectedItem.ToString()) : 0;
            var end = date.AddHours(hour).AddMinutes(minute);
            if (end <= DateTime.Now.AddMinutes(1))
            {
                // require at least 1 minute in the future
                System.Windows.MessageBox.Show("Select a future end date/time.", "Invalid end time", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // If CreateBtn.Tag contains an existing block Id, update that block instead of adding a new one
            var editingId = CreateBtn.Tag as string;
            if (!string.IsNullOrEmpty(editingId))
            {
                var existing = BlockStore.Instance.Blocks.FirstOrDefault(b => b.Id == editingId);
                if (existing != null)
                {
                    existing.AppName = name;
                    existing.BlockName = name;
                    existing.IsFolder = Directory.Exists(name);
                    existing.FolderPath = Directory.Exists(name) ? name : string.Empty;
                    // When editing, set StartTime to now so the block becomes active (locked) immediately
                    existing.StartTime = DateTime.Now;
                    existing.EndTime = end;
                    existing.DurationDays = (int)Math.Ceiling((end - existing.StartTime).TotalDays);
                    BlockStore.Instance.UpdateBlock(existing);
                    // clear tag after edit
                    CreateBtn.Tag = null;
                }
            }
            else
            {
                var block = new Switch.Model.BlockItem
                {
                    AppName = name,
                    BlockName = name,
                    IsFolder = Directory.Exists(name),
                    FolderPath = Directory.Exists(name) ? name : string.Empty,
                    StartTime = DateTime.Now,
                    EndTime = end,
                    DurationDays = (int)Math.Ceiling((end - DateTime.Now).TotalDays)
                };
                BlockStore.Instance.AddBlock(block);
            }
            // Navigate back to HomePage
            // Save installer-blocking preference
            SettingsStore.Instance.InstallerBlockingEnabled = InstallerBlockCheck.IsChecked == true;
            SettingsStore.Instance.Save();
            (System.Windows.Application.Current.MainWindow as MainWindow)?.MainFrame.Navigate(new Pages.HomePage());
        }

        private void InstallerBlockCheck_Checked(object? sender, RoutedEventArgs e)
        {
            // informational only
            Service.Logger.Log("Installer blocking enabled by user.");
        }

        private void InstallerBlockCheck_Unchecked(object? sender, RoutedEventArgs e)
        {
            Service.Logger.Log("Installer blocking disabled by user.");
        }
    }
}