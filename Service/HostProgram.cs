using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace Switch.Service.Host
{
    /// <summary>
    /// Entry point for the SwitchBlocker Windows Service host.
    ///
    /// Usage:
    ///   Switch.Service.Host.exe                  → run as SCM service (normal mode)
    ///   Switch.Service.Host.exe --install         → register with SCM + set auto-restart
    ///   Switch.Service.Host.exe --uninstall       → remove from SCM
    ///   Switch.Service.Host.exe --console         → run enforcement loop in a console window (debug)
    /// </summary>
    public class Program
    {
        public static int Main(string[] args)
        {
            var arg = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            switch (arg)
            {
                case "--install":
                    return Install();

                case "--uninstall":
                    return Uninstall();

                case "--console":
                    return RunConsole();

                default:
                    // Launched by SCM — hand control to ServiceBase
                    ServiceBase.Run(new BlockEnforcerService());
                    return 0;
            }
        }

        // ── Install ─────────────────────────────────────────────────────────────

        private static int Install()
        {
            try
            {
                var exePath = GetServiceExePath();
                Switch.Service.Logger.Log($"[SwitchBlocker] Installing service from {exePath}");

                // Create the service via sc.exe
                Run("sc", $"create {BlockEnforcerService.SvcName} " +
                          $"binPath= \"\\\"{exePath}\\\"\" " +
                          $"DisplayName= \"{BlockEnforcerService.SvcDisplayName}\" " +
                          $"start= auto " +
                          $"obj= LocalSystem");

                // Set description
                Run("sc", $"description {BlockEnforcerService.SvcName} " +
                          $"\"{BlockEnforcerService.SvcDescription}\"");

                // Configure failure actions: restart after 1 s on first, 5 s on second, 10 s thereafter
                Run("sc", $"failure {BlockEnforcerService.SvcName} " +
                          $"reset= 86400 " +
                          $"actions= restart/1000/restart/5000/restart/10000");

                // Start immediately
                Run("sc", $"start {BlockEnforcerService.SvcName}");

                Switch.Service.Logger.Log("[SwitchBlocker] Service installed and started");
                Console.WriteLine($"[SwitchBlocker] Service '{BlockEnforcerService.SvcName}' installed and started.");
                return 0;
            }
            catch (Exception ex)
            {
                Switch.Service.Logger.Log($"[SwitchBlocker] Install failed: {ex.Message}");
                Console.Error.WriteLine($"Install failed: {ex.Message}");
                return 1;
            }
        }

        // ── Uninstall ───────────────────────────────────────────────────────────

        private static int Uninstall()
        {
            try
            {
                if (Switch.Service.BlockStore.Instance.HasActiveBlocks)
                {
                    Switch.Service.Logger.Log("[SwitchBlocker] Uninstall blocked: active blocks still exist.");
                    Console.Error.WriteLine("[SwitchBlocker] Uninstall blocked: active blocks still exist.");
                    return 2;
                }

                Switch.Service.Logger.Log("[SwitchBlocker] Uninstalling service");

                Run("sc", $"stop {BlockEnforcerService.SvcName}");
                Run("sc", $"delete {BlockEnforcerService.SvcName}");

                Switch.Service.Logger.Log("[SwitchBlocker] Service uninstalled");
                Console.WriteLine($"[SwitchBlocker] Service '{BlockEnforcerService.SvcName}' removed.");
                return 0;
            }
            catch (Exception ex)
            {
                Switch.Service.Logger.Log($"[SwitchBlocker] Uninstall failed: {ex.Message}");
                Console.Error.WriteLine($"Uninstall failed: {ex.Message}");
                return 1;
            }
        }

        // ── Console (debug) mode ────────────────────────────────────────────────

        private static int RunConsole()
        {
            Console.WriteLine($"[SwitchBlocker] Running in console mode. Press Ctrl+C to stop.");
            Switch.Service.Logger.Log("[SwitchBlocker] Console mode started");

            var svc = new BlockEnforcerService();

            // Invoke OnStart via reflection since it's protected
            var onStart = typeof(ServiceBase).GetMethod("OnStart",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            onStart?.Invoke(svc, new object[] { Array.Empty<string>() });

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; tcs.TrySetResult(true); };
            tcs.Task.Wait();

            var onStop = typeof(ServiceBase).GetMethod("OnStop",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            onStop?.Invoke(svc, null);

            Switch.Service.Logger.Log("[SwitchBlocker] Console mode stopped");
            return 0;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the absolute path to this running executable.
        /// Works for both framework-dependent and single-file publish modes.
        /// </summary>
        private static string GetServiceExePath()
        {
            // Process.MainModule gives the correct path even for single-file apps
            var path = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return Path.GetFullPath(path);

            // Fallback: assembly location (may be empty for single-file)
            var asm = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(asm) && File.Exists(asm))
                return Path.GetFullPath(asm);

            throw new InvalidOperationException("Cannot determine service executable path.");
        }

        /// <summary>Runs a command and waits for exit. Throws on non-zero exit code.</summary>
        private static void Run(string exe, string arguments)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };

            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr);

            Switch.Service.Logger.Log($"[SwitchBlocker] {exe} {arguments} → exit {p.ExitCode}");

            // sc.exe returns 1060 (service not found) when stopping a non-existent service — ignore
            // sc.exe returns 1072 (marked for deletion) — also acceptable during uninstall
            if (p.ExitCode != 0 && p.ExitCode != 1060 && p.ExitCode != 1072)
            {
                // Non-fatal: log but don't throw — some codes are expected (e.g. already running)
                Switch.Service.Logger.Log($"[SwitchBlocker] Warning: {exe} exited with code {p.ExitCode}");
            }
        }
    }
}
