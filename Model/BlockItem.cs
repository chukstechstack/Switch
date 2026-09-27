using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Switch.Model
{
    public class BlockItem : INotifyPropertyChanged
    {
        // If this is a folder block, FolderPath contains the path to block
        public bool IsFolder { get; set; } = false;
        public string FolderPath { get; set; } = "";
        // FolderMode: currently only "ReadOnly" is supported. Stored as string for simple serialization.
        public string FolderMode { get; set; } = "ReadOnly";

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string BlockName { get; set; } = "";
        public string AppName { get; set; } = "";
        public int DurationDays { get; set; } = 7;
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime EndTime { get; set; }

        // If this block targets a folder, indicates whether we have applied an ACL lock
        public bool FolderLocked { get; set; } = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        void Notify([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // If locked, you CANNOT delete until time passes
        public bool IsLocked => DateTime.Now < EndTime;

        // Can delete only when lock expired
        public bool CanDelete => !IsLocked;

        // Time left as TimeSpan
        public TimeSpan TimeLeft => EndTime - DateTime.Now;

        // Live countdown text including seconds
        public string CountdownText
        {
            get
            {
                if (IsPendingDelete)
                {
                    var leftP = PendingDeleteEnds.HasValue ? (PendingDeleteEnds.Value - DateTime.Now) : TimeSpan.Zero;
                    if (leftP.TotalSeconds < 0) leftP = TimeSpan.Zero;
                    return $"Emergency delete in {leftP.Days}d {leftP.Hours}h {leftP.Minutes}m {leftP.Seconds}s";
                }

                if (!IsLocked) return "Unlocked - can delete";
                var left = TimeLeft;
                // Use absolute values to avoid negative display
                if (left.TotalSeconds < 0) left = TimeSpan.Zero;
                return $"{left.Days}d {left.Hours}h {left.Minutes}m {left.Seconds}s left";
            }
        }

        // Progress 0..1 of elapsed time
        public double Progress
        {
            get
            {
                var total = EndTime - StartTime;
                if (total.TotalSeconds <= 0) return 1.0;
                var elapsed = DateTime.Now - StartTime;
                var p = elapsed.TotalSeconds / total.TotalSeconds;
                if (p < 0) p = 0; if (p > 1) p = 1;
                return p;
            }
        }

        // Pending emergency delete
        public DateTime? PendingDeleteStart { get; set; }
        public DateTime? PendingDeleteEnds { get; set; }
        public bool IsPendingDelete => PendingDeleteEnds.HasValue && PendingDeleteEnds.Value > DateTime.Now;

        public void StartPendingDelete(TimeSpan duration)
        {
            PendingDeleteStart = DateTime.Now;
            PendingDeleteEnds = PendingDeleteStart.Value.Add(duration);
            Notify(nameof(PendingDeleteStart));
            Notify(nameof(PendingDeleteEnds));
            Notify(nameof(IsPendingDelete));
            Notify(nameof(CountdownText));
        }

        public void CancelPendingDelete()
        {
            PendingDeleteStart = null;
            PendingDeleteEnds = null;
            Notify(nameof(PendingDeleteStart));
            Notify(nameof(PendingDeleteEnds));
            Notify(nameof(IsPendingDelete));
            Notify(nameof(CountdownText));
        }

        // Call to refresh UI-bound properties
        public void Update()
        {
            Notify(nameof(CountdownText));
            Notify(nameof(IsLocked));
            Notify(nameof(CanDelete));
            Notify(nameof(TimeLeft));
            Notify(nameof(Progress));
        }
    }
}
