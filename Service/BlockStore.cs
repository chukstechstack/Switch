using Switch.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;

namespace Switch.Service
{
    public class BlockStore
    {
        // SINGLETON - One shoe box for whole app

        private static BlockStore? _instance; // Private storage for fridge

        // PUBLIC DOOR - with full if else!
        public static BlockStore Instance
        {
            get
            {
                // IF _instance is empty (null)
                if (_instance == null)
                {
                    // THEN create brand new fridge from blueprint!
                    _instance = new BlockStore();
                }
                else
                {
                    // ELSE fridge already exists! Do nothing!
                    // Keep using same old fridge!
                }

                // FINALLY return the fridge (new or old, same one!)
                return _instance;
            }
        }

        // Internal list protected by lock for thread-safety
        private readonly List<BlockItem> _blocks = new List<BlockItem>();
        private readonly object _lock = new object();
        public IReadOnlyList<BlockItem> Blocks
        {
            get
            {
                lock (_lock) { return _blocks.ToList().AsReadOnly(); }
            }
        }

        private string SavePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Switch", "blocks.json");

        public BlockStore()
        {
            Load();
        }

        public void AddBlock(BlockItem block)
        {
            // Preserve StartTime/EndTime if caller provided them (e.g., Create page selects exact end datetime).
            if (block.StartTime == default) block.StartTime = DateTime.Now;

            if (block.EndTime == default || block.EndTime <= block.StartTime)
            {
                // Fallback to DurationDays when EndTime isn't set
                block.EndTime = block.StartTime.AddDays(block.DurationDays);
            }

            if (block.IsFolder && !string.IsNullOrWhiteSpace(block.FolderPath) && Directory.Exists(block.FolderPath))
            {
                if (FolderLocker.TryLockFolder(block.FolderPath, block.Id, out var lockError))
                {
                    block.FolderLocked = true;
                    Logger.Log($"[BlockStore] Immediate folder lock applied for {block.FolderPath}");
                }
                else
                {
                    Logger.Log($"[BlockStore] Immediate folder lock failed for {block.FolderPath}: {lockError}");
                }
            }

            lock (_lock)
            {
                _blocks.Add(block);
                Save();
            }
        }

        // Update an existing block by Id. If not found, adds it.
        public void UpdateBlock(BlockItem block)
        {
            if (block.IsFolder && !string.IsNullOrWhiteSpace(block.FolderPath) && Directory.Exists(block.FolderPath) && block.IsLocked)
            {
                if (FolderLocker.TryLockFolder(block.FolderPath, block.Id, out var lockError))
                {
                    block.FolderLocked = true;
                    Logger.Log($"[BlockStore] Folder lock reapplied for {block.FolderPath}: {lockError}");
                }
                else
                {
                    Logger.Log($"[BlockStore] Folder lock reapply failed for {block.FolderPath}: {lockError}");
                }
            }

            lock (_lock)
            {
                var idx = _blocks.FindIndex(b => b.Id == block.Id);
                if (idx >= 0)
                {
                    _blocks[idx] = block;
                }
                else
                {
                    _blocks.Add(block);
                }
                Save();
            }
        }

        public bool TryLockBlockFolder(BlockItem block)
        {
            if (!block.IsFolder || string.IsNullOrWhiteSpace(block.FolderPath) || !Directory.Exists(block.FolderPath))
                return false;

            var ok = FolderLocker.TryLockFolder(block.FolderPath, block.Id, out _);
            if (ok)
            {
                block.FolderLocked = true;
            }
            return ok;
        }

        // Convenience overload used by UI: create and add BlockItem from app name + days
        public void AddBlock(string appName, int durationDays)
        {
            var block = new BlockItem
            {
                AppName = appName,
                BlockName = appName,
                DurationDays = durationDays
            };
            AddBlock(block);
        }

        // Keep original simple API
        public bool TryDeleteBlock(string idOrName, out string message)
        {
            return TryDeleteBlock(idOrName, false, out message);
        }

        // New overload: allow forced deletion of locked blocks when 'force' is true
        public bool TryDeleteBlock(string idOrName, bool force, out string message)
        {
            BlockItem? block = null;
            lock (_lock)
            {
                block = _blocks.FirstOrDefault(b => b.Id == idOrName || b.AppName == idOrName);
                if (block == null)
                {
                    message = $"Block not found! Looking for {idOrName}";
                    return false;
                }

                if (block.IsLocked && !force)
                {
                    message = $"LOCKED! Cannot delete. {block.CountdownText}";
                    return false;
                }

                _blocks.Remove(block);
                Save();
            }
            message = force ? "Deleted (forced)!" : "Deleted!";
            return true;
        }

        public void ForceDelete(string appName)
        {
            lock (_lock)
            {
                // If any removed block had its folder locked, attempt to restore ACL before removing
                var toRemove = _blocks.Where(b => b.AppName == appName || b.Id == appName).ToList();
                foreach (var b in toRemove)
                {
                    try
                    {
                        if (b.IsFolder && b.FolderLocked && !string.IsNullOrEmpty(b.FolderPath))
                        {
                            Switch.Service.FolderLocker.TryRestoreFolder(b.FolderPath, b.Id, out _);
                        }
                    }
                    catch { }
                }
                _blocks.RemoveAll(b => b.AppName == appName || b.Id == appName);
                Save();
            }
        }

        public string GetFilePath() => SavePath;

        public bool HasActiveBlocks
        {
            get
            {
                lock (_lock)
                {
                    return _blocks.Any(b => b.IsLocked || b.IsPendingDelete);
                }
            }
        }

        public bool CanUninstallApp()
        {
            return !HasActiveBlocks;
        }

        public bool CanModifyBlocks()
        {
            return !HasActiveBlocks;
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                var json = JsonSerializer.Serialize(Blocks, new JsonSerializerOptions { WriteIndented = true });
                // Write atomically: write to temp file then replace
                var tmp = SavePath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                // Replace or Move
                if (File.Exists(SavePath))
                {
                    File.Replace(tmp, SavePath, null);
                }
                else
                {
                    File.Move(tmp, SavePath);
                }
            }
            catch (Exception ex)
            {
                // Log and swallow to avoid crashing UI
                Logger.Log($"Failed to save blocks: {ex}");
            }
        }

        // Public save for callers
        public void SaveChanges()
        {
            Save();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    var json = File.ReadAllText(SavePath);
                    var list = JsonSerializer.Deserialize<List<BlockItem>>(json) ?? new List<BlockItem>();
                    lock (_lock)
                    {
                        _blocks.Clear();
                        _blocks.AddRange(list);
                    }
                }
            }
            catch
            {
                lock (_lock) { _blocks.Clear(); }
            }
        }
    }
}