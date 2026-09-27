using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Switch.Service.Host
{
    /// <summary>
    /// Windows Service that enforces app blocks independently of the Switch UI.
    /// Runs as "SwitchBlocker" under LocalSystem.  Even if the UI is killed via
    /// Task Manager, this service keeps blocking the apps the user locked.
    /// </summary>
    public class BlockEnforcerService : ServiceBase
    {
        public const string SvcName        = "SwitchBlocker";
        public const string SvcDisplayName = "Switch App Blocker";
        public const string SvcDescription =
            "Enforces Switch app blocks. Keeps running even if the Switch UI is closed.";

        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly string _uiExePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Switch",
            "Switch.exe");

        public BlockEnforcerService()
        {
            this.ServiceName         = SvcName;
            this.CanStop             = false;
            this.CanShutdown         = false;
            this.CanPauseAndContinue = false;
            this.AutoLog             = true;
        }

        // ── SCM entry points ────────────────────────────────────────────────────

        protected override void OnStart(string[] args)
        {
            Switch.Service.Logger.Log("[SwitchBlocker] Service starting");

            if (Switch.Service.ProcessProtection.Enable(out string protErr))
            {
                Switch.Service.Logger.Log("[SwitchBlocker] Service process protection enabled");
            }
            else
            {
                Switch.Service.Logger.Log($"[SwitchBlocker] Failed to protect service process: {protErr}");
            }

            _cts      = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunLoop(_cts.Token));
        }

        protected override void OnStop()
        {
            if (Switch.Service.BlockStore.Instance.HasActiveBlocks)
            {
                Switch.Service.Logger.Log("[SwitchBlocker] Stop request rejected while active blocks are still in effect.");
                return;
            }

            Switch.Service.Logger.Log("[SwitchBlocker] Service stopping");
            _cts?.Cancel();
            try { _loopTask?.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }

        // ── Enforcement loop ────────────────────────────────────────────────────

        private static bool IsBrowserBlockTarget(string? appName)
        {
            if (string.IsNullOrEmpty(appName)) return false;
            var n = appName.ToLowerInvariant();
            return n.Contains("chrome") || n.Contains("edge") || n.Contains("firefox") ||
                   n.Contains("opera")  || n.Contains("brave") || n.Contains("vivaldi");
        }

        private async Task RunLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var blocks  = Switch.Service.BlockStore.Instance.Blocks;
                    var running = Process.GetProcesses();
                    int selfId  = Process.GetCurrentProcess().Id;

                    var uiRunning = running.Any(p =>
                        p.ProcessName.Equals("Switch", StringComparison.OrdinalIgnoreCase) ||
                        p.MainModule != null && p.MainModule.FileName.Equals(_uiExePath, StringComparison.OrdinalIgnoreCase));

                    if (!uiRunning && Switch.Service.BlockStore.Instance.HasActiveBlocks)
                    {
                        try
                        {
                            if (File.Exists(_uiExePath))
                            {
                                var psi = new ProcessStartInfo(_uiExePath)
                                {
                                    UseShellExecute = true,
                                    WorkingDirectory = Path.GetDirectoryName(_uiExePath) ?? Environment.CurrentDirectory
                                };
                                Process.Start(psi);
                                Switch.Service.Logger.Log("[SwitchBlocker] UI was missing; relaunched Switch.exe automatically.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Switch.Service.Logger.Log($"[SwitchBlocker] Auto-restart failed: {ex.Message}");
                        }
                    }

                    foreach (var proc in running)
                    {
                        if (token.IsCancellationRequested) break;
                        try
                        {
                            if (proc.Id == selfId) continue;

                            // Get accurate exe name via Win32 QueryFullProcessImageName
                            string? exeName = null;
                            try { exeName = GetProcessExeName(proc); } catch { exeName = proc.ProcessName; }

                            foreach (var block in blocks)
                            {
                                try
                                {
                                    // ── Folder block ACL management ──────────────────
                                    if (block.IsFolder)
                                    {
                                        if (block.IsLocked && !block.FolderLocked && !string.IsNullOrEmpty(block.FolderPath))
                                        {
                                            if (Switch.Service.FolderLocker.TryLockFolder(block.FolderPath, block.Id, out var fe))
                                            {
                                                block.FolderLocked = true;
                                                Switch.Service.BlockStore.Instance.UpdateBlock(block);
                                                Switch.Service.Logger.Log($"[SwitchBlocker] Locked folder for {block.AppName}");
                                            }
                                            else Switch.Service.Logger.Log($"[SwitchBlocker] Folder lock failed for {block.AppName}: {fe}");
                                        }
                                        else if (!block.IsLocked && block.FolderLocked && !string.IsNullOrEmpty(block.FolderPath))
                                        {
                                            if (Switch.Service.FolderLocker.TryRestoreFolder(block.FolderPath, block.Id, out var re))
                                            {
                                                block.FolderLocked = false;
                                                Switch.Service.BlockStore.Instance.UpdateBlock(block);
                                                Switch.Service.Logger.Log($"[SwitchBlocker] Restored folder for {block.AppName}");
                                            }
                                            else Switch.Service.Logger.Log($"[SwitchBlocker] Folder restore failed for {block.AppName}: {re}");
                                        }
                                        continue; // folder blocks don't kill processes
                                    }
                                }
                                catch { }

                                // ── App kill ─────────────────────────────────────────
                                if (!block.IsLocked) continue;
                                var target = (block.AppName ?? "").Trim();
                                if (string.IsNullOrEmpty(target)) continue;

                                var exeBase = exeName ?? proc.ProcessName;

                                bool matched =
                                    string.Equals(exeBase, target, StringComparison.OrdinalIgnoreCase) ||
                                    exeBase.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    proc.ProcessName.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0;

                                if (matched)
                                {
                                    try
                                    {
                                        if (proc.CloseMainWindow()) { if (!proc.WaitForExit(1500)) proc.Kill(); }
                                        else proc.Kill();
                                        Switch.Service.Logger.Log($"[SwitchBlocker] Killed {proc.ProcessName} (block: {block.AppName})");
                                    }
                                    catch (Exception kex)
                                    {
                                        Switch.Service.Logger.Log($"[SwitchBlocker] Kill failed for {proc.ProcessName}: {kex.Message}");
                                    }
                                }

                                // ── Installer blocking ───────────────────────────────
                                try
                                {
                                    if (Switch.Service.SettingsStore.Instance.InstallerBlockingEnabled &&
                                        IsBrowserBlockTarget(block.AppName))
                                    {
                                        var installers = new[] { "msiexec","setup","install","installer",
                                                                  "chrome_installer","chrome_setup","edgemsi" };
                                        var procLower = exeBase.ToLowerInvariant();
                                        foreach (var ins in installers)
                                        {
                                            if (procLower.Contains(ins) ||
                                                proc.ProcessName.IndexOf(ins, StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                try { proc.Kill(); Switch.Service.Logger.Log($"[SwitchBlocker] Killed installer {proc.ProcessName}"); }
                                                catch { }
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Switch.Service.Logger.Log($"[SwitchBlocker] Loop error: {ex.Message}");
                }

                try { await Task.Delay(TimeSpan.FromSeconds(2), token); } catch { }
            }

            Switch.Service.Logger.Log("[SwitchBlocker] Enforcement loop exited");
        }

        // ── Win32 process name helper (no dep on ProcessHelper.cs) ──────────────

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(
            IntPtr hProcess, int flags, StringBuilder exeName, ref uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        private static string GetProcessExeName(Process p)
        {
            const uint QUERY = 0x1000;
            var h = OpenProcess(QUERY, false, p.Id);
            if (h == IntPtr.Zero) return p.ProcessName;
            try
            {
                var sb  = new StringBuilder(1024);
                uint sz = (uint)sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref sz))
                    return Path.GetFileNameWithoutExtension(sb.ToString());
            }
            finally { CloseHandle(h); }
            return p.ProcessName;
        }
    }
}
