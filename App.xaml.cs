using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Switch
{
    public partial class App : System.Windows.Application
    {
        private static bool IsBrowserBlockTarget(string? appName)
        {
            if (string.IsNullOrEmpty(appName)) return false;
            var name = appName.ToLowerInvariant();
            return name.Contains("chrome") || name.Contains("edge") || name.Contains("firefox") || name.Contains("opera") || name.Contains("brave") || name.Contains("vivaldi");
        }

        private System.Threading.Timer? _backgroundTimer;


        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SessionEnding += App_SessionEnding;

            var args = Environment.GetCommandLineArgs();
            var headless = args.Any(a => string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase));

            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? args.FirstOrDefault() ?? Environment.GetCommandLineArgs()[0];
                var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                runKey?.SetValue("Switch", $"\"{exePath}\" --headless");
            }
            catch (Exception ex)
            {
                Service.Logger.Log($"Autostart registry failed: {ex.Message}");
            }

            if (Service.BlockStore.Instance.HasActiveBlocks)
            {
                Service.Logger.Log("[Startup] Active blocks present on app launch; blocker protection remains active.");
            }

            if (Service.ProcessProtection.Enable(out string protErr))
            {
                Service.Logger.Log("[Protection] Process protection ENABLED permanently");
            }
            else
            {
                Service.Logger.Log($"[Protection] Failed to enable process protection: {protErr}");
            }

            EnsureServiceRunning();

            _backgroundTimer = new System.Threading.Timer((o) =>
            {
                try
                {
                    var blocks = Service.BlockStore.Instance.Blocks;
                    var running = Process.GetProcesses();
                    foreach (var proc in running)
                    {
                        try
                        {
                            if (proc.Id == Process.GetCurrentProcess().Id) continue;
                            string? exeName = null;
                            try { exeName = Service.ProcessHelper.GetProcessExeName(proc); }
                            catch { exeName = proc.ProcessName; }

                            foreach (var block in blocks)
                            {
                                try
                                {
                                    if (block.IsFolder)
                                    {
                                        if (block.IsLocked && !block.FolderLocked && !string.IsNullOrEmpty(block.FolderPath))
                                        {
                                            if (Service.FolderLocker.TryLockFolder(block.FolderPath, block.Id, out var ferr))
                                            {
                                                block.FolderLocked = true;
                                                Service.BlockStore.Instance.UpdateBlock(block);
                                                Service.Logger.Log($"Locked folder for block {block.AppName}");
                                            }
                                            else
                                            {
                                                Service.Logger.Log($"Folder lock failed for {block.AppName}: {ferr}");
                                            }
                                        }
                                        else if (!block.IsLocked && block.FolderLocked && !string.IsNullOrEmpty(block.FolderPath))
                                        {
                                            if (Service.FolderLocker.TryRestoreFolder(block.FolderPath, block.Id, out var rerr))
                                            {
                                                block.FolderLocked = false;
                                                Service.BlockStore.Instance.UpdateBlock(block);
                                                Service.Logger.Log($"Restored folder ACL for {block.AppName}");
                                            }
                                            else
                                            {
                                                Service.Logger.Log($"Folder restore failed for {block.AppName}: {rerr}");
                                            }
                                        }
                                    }
                                }
                                catch { }

                                if (!block.IsLocked) continue;
                                var target = (block.AppName ?? "").Trim();
                                if (string.IsNullOrEmpty(target)) continue;

                                var exePath = Service.ProcessHelper.TryGetProcessExePath(proc);
                                var exeBase = exeName ?? (string.IsNullOrEmpty(exePath) ? proc.ProcessName : System.IO.Path.GetFileNameWithoutExtension(exePath));

                                if (string.Equals(exeBase, target, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(exeBase) && exeBase.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                    (proc.ProcessName ?? "").IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    try
                                    {
                                        if (proc.CloseMainWindow())
                                        {
                                            if (!proc.WaitForExit(1500)) proc.Kill();
                                        }
                                        else
                                        {
                                            proc.Kill();
                                        }

                                        Service.Logger.Log($"Background terminated process {proc.ProcessName} (exe: {exeName}) due to block {block.AppName}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Service.Logger.Log($"Background kill failed for {proc.ProcessName}: {ex.Message}");
                                    }
                                }

                                try
                                {
                                    if (Service.SettingsStore.Instance.InstallerBlockingEnabled && IsBrowserBlockTarget(block.AppName))
                                    {
                                        var installers = new[] { "msiexec", "setup", "install", "installer", "setup.exe", "chrome_installer", "chrome_setup", "edgemsi" };
                                        var procExe = exeBase.ToLowerInvariant();
                                        foreach (var ins in installers)
                                        {
                                            if (procExe.Contains(ins) || (proc.ProcessName ?? "").IndexOf(ins, StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                try
                                                {
                                                    proc.Kill();
                                                    Service.Logger.Log($"Installer-block: terminated installer process {proc.ProcessName} ({proc.Id}) because browser block is active");
                                                }
                                                catch { }
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (Exception inner) { Service.Logger.Log($"Error inspecting process {proc.ProcessName}: {inner.Message}"); }
                    }
                }
                catch (Exception ex) { Service.Logger.Log($"Background enforcement error: {ex}"); }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

            if (!headless)
            {
                var win = new MainWindow();
                win.Show();
            }
        }

        private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            if (Service.BlockStore.Instance.HasActiveBlocks)
            {
                e.Cancel = true;
                Service.Logger.Log("[Shutdown] Session shutdown blocked because active blocks are still in effect.");
            }
        }

        private static void EnsureServiceRunning()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                const string svcName = "SwitchBlocker";
                try
                {
                    var mainExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    var dir = System.IO.Path.GetDirectoryName(mainExe) ?? "";
                    var svcExe = System.IO.Path.Combine(dir, "Switch.Service.Host.exe");

                    if (!System.IO.File.Exists(svcExe))
                    {
                        Service.Logger.Log($"[ServiceManager] Switch.Service.Host.exe not found at {svcExe} — skipping service install");
                        return;
                    }

                    using var sc = new System.ServiceProcess.ServiceController(svcName);
                    try
                    {
                        var status = sc.Status;
                        if (status == System.ServiceProcess.ServiceControllerStatus.Stopped ||
                            status == System.ServiceProcess.ServiceControllerStatus.Paused)
                        {
                            sc.Start();
                            sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                            Service.Logger.Log("[ServiceManager] Started existing SwitchBlocker service");
                        }
                        else
                        {
                            Service.Logger.Log($"[ServiceManager] SwitchBlocker service already in state: {status}");
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        Service.Logger.Log("[ServiceManager] SwitchBlocker not installed — installing now");
                        var psi = new ProcessStartInfo(svcExe, "--install")
                        {
                            UseShellExecute = true,
                            Verb = "runas",
                            CreateNoWindow = true
                        };
                        var p = Process.Start(psi);
                        p?.WaitForExit(15000);
                        Service.Logger.Log($"[ServiceManager] Install exited with code {p?.ExitCode}");
                    }
                }
                catch (Exception ex)
                {
                    Service.Logger.Log($"[ServiceManager] EnsureServiceRunning error: {ex.Message}");
                }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _backgroundTimer?.Dispose();
            base.OnExit(e);
        }
    }
}
