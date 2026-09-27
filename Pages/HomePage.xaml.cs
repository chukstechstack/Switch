using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using Switch.Service;

namespace Switch.Pages
{
    public partial class HomePage : Page
    {
        DispatcherTimer timer;

        public HomePage()
        {
            InitializeComponent();
            RefreshList();
            // Live countdown timer every 1 second
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            timer.Start();
            // Initialize StartWithWindows checkbox
            StartWithWindowsCheck.IsChecked = Switch.Service.SettingsStore.Instance.StartWithWindows;
            StartWithWindowsCheck.Checked += StartWithWindowsCheck_Checked;
            StartWithWindowsCheck.Unchecked += StartWithWindowsCheck_Unchecked;
        }

        private void StartWithWindowsCheck_Checked(object sender, RoutedEventArgs e)
        {
            Switch.Service.SettingsStore.Instance.StartWithWindows = true;
            Switch.Service.SettingsStore.Instance.Save();
            Switch.Service.AutoStartHelper.RegisterStartWithWindows(true);
        }

        private void StartWithWindowsCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            Switch.Service.SettingsStore.Instance.StartWithWindows = false;
            Switch.Service.SettingsStore.Instance.Save();
            Switch.Service.AutoStartHelper.RegisterStartWithWindows(false);
        }

        void OnEditClicked(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            var id = btn.Tag as string;
            if (string.IsNullOrEmpty(id)) return;

            var block = BlockStore.Instance.Blocks.FirstOrDefault(b => b.Id == id);
            if (block == null) return;

            // Navigate to CreateBlockPage but pre-fill fields for editing
            var page = new CreateBlockPage();
            // Use NavigationService to pass block via a temporary property on the page
            // We access internal fields via reflection-lite by setting AppBox and date/time directly
            page.Loaded += (s, ev) =>
            {
                try
                {
                    page.AppBox.Text = block.IsFolder && !string.IsNullOrEmpty(block.FolderPath) ? block.FolderPath : block.AppName;
                    page.EndDatePicker.SelectedDate = block.EndTime;
                    page.HourCombo.SelectedItem = block.EndTime.Hour.ToString("00");
                    page.MinuteCombo.SelectedItem = block.EndTime.Minute.ToString("00");
                    // store the editing id in Tag so CreateBtn handler can detect edit
                    page.CreateBtn.Tag = block.Id;
                }
                catch { }
            };
            NavigationService?.Navigate(page);
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // update countdowns
                foreach (var b in BlockStore.Instance.Blocks.ToList())
                {
                    b.Update();
                }

                CheckAndBlock();

                // Process expired emergency deletes
                var expired = BlockStore.Instance.Blocks.Where(b => b.PendingDeleteEnds.HasValue && b.PendingDeleteEnds.Value <= DateTime.Now).ToList();
                if (expired.Any())
                {
                    foreach (var b in expired)
                    {
                        BlockStore.Instance.ForceDelete(b.Id);
                        Service.Logger.Log($"Emergency delete executed for {b.AppName} ({b.Id})");
                    }
                    BlockStore.Instance.SaveChanges();
                    RefreshList();
                }
            }
            catch (Exception ex)
            {
                Service.Logger.Log($"Timer error: {ex}");
            }
        }

        void RefreshList()
        {
            BlocksList.ItemsSource = null;
            BlocksList.ItemsSource = BlockStore.Instance.Blocks.ToList();
        }

        void OnBlockClicked(object sender, RoutedEventArgs e)
        {
            // legacy block action removed in UI; creation now via Create page
        }

        void OnDeleteClicked(object sender, RoutedEventArgs e)
        {
                var btn = (System.Windows.Controls.Button)sender;
            var id = btn.Tag as string;
            if (string.IsNullOrEmpty(id)) return;

            var block = BlockStore.Instance.Blocks.FirstOrDefault(b => b.Id == id);
            if (block == null) return;

            var appName = block.AppName;
            if (block.IsLocked)
            {
                if (!block.IsPendingDelete)
                {
                    // Start 7-day emergency delete
                    block.StartPendingDelete(TimeSpan.FromDays(7));
                    BlockStore.Instance.SaveChanges();
                    StatusLabel.Text = $"Emergency delete started for {appName}";
                    RefreshList();
                }
                else
                {
                    StatusLabel.Text = $"Emergency delete already pending for {appName}";
                }
            }
            else
            {
                // Not locked - remove immediately
                if (BlockStore.Instance.TryDeleteBlock(id, out string reason))
                {
                    StatusLabel.Text = $"{appName} deleted!";
                    RefreshList();
                }
                else
                {
                    StatusLabel.Text = reason;
                }
            }
        }

        void OnCancelPendingClicked(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            var id = btn.Tag as string;
            if (string.IsNullOrEmpty(id)) return;

            var block = BlockStore.Instance.Blocks.FirstOrDefault(b => b.Id == id);
            if (block == null) return;

            if (block.IsPendingDelete)
            {
                StatusLabel.Text = $"Emergency unlock is not allowed. {block.AppName} must remain blocked for the 7-day waiting period.";
                return;
            }
        }

        // Emergency unlock is intentionally disabled: once pending delete starts, the user must wait the full 7-day period.
        // No immediate override path is allowed.

        void OnUnlockClicked(object sender, RoutedEventArgs e)
        {
            var btn = (System.Windows.Controls.Button)sender;
            var id = btn.Tag as string;
            if (string.IsNullOrEmpty(id)) return;

            // Capture app name for user-friendly message
            var block = BlockStore.Instance.Blocks.FirstOrDefault(b => b.Id == id);
            var appName = block?.AppName ?? id;
            if (BlockStore.Instance.TryDeleteBlock(id, out string reason))
            {
                StatusLabel.Text = $"{appName} deleted!";
                RefreshList();
            }
            else
            {
                // Show modern popup when deletion blocked
                var popup = new Switch.ModernPopup("STAY FOCUSED!", reason ?? "Cannot delete block.");
                popup.Owner = System.Windows.Application.Current.MainWindow;
                popup.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
                popup.ShowDialog();
            }
        }

        void CheckAndBlock()
        {
            try
            {
                var running = Process.GetProcesses();
                foreach (var proc in running)
                {
                    foreach (var block in BlockStore.Instance.Blocks)
                    {
                        if (!block.IsLocked) continue;
                        var pName = proc.ProcessName ?? "";
                        if (pName.IndexOf(block.AppName ?? "", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (block.AppName ?? "").IndexOf(pName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            try
                            {
                                // Avoid killing self
                                if (proc.Id == Process.GetCurrentProcess().Id) continue;

                                // Try graceful close first
                                try
                                {
                                    if (proc.CloseMainWindow())
                                    {
                                        if (!proc.WaitForExit(1500))
                                        {
                                            proc.Kill();
                                        }
                                    }
                                    else
                                    {
                                        proc.Kill();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // Fallback to Kill
                                    try { proc.Kill(); } catch { }
                                    Service.Logger.Log($"Failed to terminate process {pName} ({proc.Id}): {ex.Message}");
                                }

                                if (this.FindName("StatusLabel") is System.Windows.Controls.TextBlock st) st.Text = $"BLOCKED {proc.ProcessName}! {block.CountdownText}";
                            }
                            catch (Exception ex)
                            {
                                Service.Logger.Log($"Error handling process {pName}: {ex}");
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void GoToCreate_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new CreateBlockPage());
        }
    }
}
