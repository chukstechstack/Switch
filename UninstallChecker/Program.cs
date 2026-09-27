using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

// Console tool intended to be used as an installer "pre-uninstall" custom action.
// Exit codes:
// 0 - safe to uninstall (no active locks or pending deletes)
// 2 - uninstall blocked: there are active locks or pending emergency deletes

class BlockDto
{
    public string Id { get; set; }
    public string AppName { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime? PendingDeleteEnds { get; set; }
}

class Program
{
    static int Main(string[] args)
    {
        try
        {
            var savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Switch", "blocks.json");
            if (!File.Exists(savePath))
            {
                Console.WriteLine("No blocks file found. Safe to uninstall.");
                return 0;
            }

            var json = File.ReadAllText(savePath);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var list = JsonSerializer.Deserialize<List<BlockDto>>(json, opts) ?? new List<BlockDto>();

            var now = DateTime.Now;
            foreach (var b in list)
            {
                if (b == null) continue;
                if (b.EndTime.HasValue && b.EndTime.Value > now)
                {
                    Console.WriteLine($"Active block found: {b.AppName} ends {b.EndTime.Value}. Aborting uninstall.");
                    return 2;
                }
                if (b.PendingDeleteEnds.HasValue && b.PendingDeleteEnds.Value > now)
                {
                    Console.WriteLine($"Pending emergency delete active for: {b.AppName} until {b.PendingDeleteEnds.Value}. Aborting uninstall.");
                    return 2;
                }
            }

            Console.WriteLine("No active blocks or pending deletes. Safe to uninstall.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"UninstallChecker error: {ex}");
            // Fail-safe: prevent uninstall if checker fails
            return 2;
        }
    }
}
