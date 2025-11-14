using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;

namespace 翻译工具.Models
{
    // 翻译条目状态
    public enum TranslationItemStatus
    {
        Untranslated,
        Translated,
        Approved
    }

    public class TranslationEntry
    {
        public string OriginalText { get; set; } = "";
        public string Translation { get; set; } = "";
        public TranslationItemStatus Status { get; set; } = TranslationItemStatus.Untranslated;
        public List<string> Comment { get; set; } = new();
    }

    // 任务列表数据文件结构
    public class TranslationInfoFile
    {
        public string? ExportTime { get; set; }
        public int TotalMods { get; set; }
        public List<TranslationInfoRecord>? Translations { get; set; }
    }

    public class TranslationInfoRecord
    {
        public string ModId { get; set; } = string.Empty;
        public string ModTitle { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public int TotalEntries { get; set; }
        public int UntranslatedEntries { get; set; }
        public int TranslatedEntries { get; set; }
        public int ApprovedEntries { get; set; }
        public bool IsLocked { get; set; }
        public string LockedBy { get; set; } = string.Empty;
        public DateTime LockTime { get; set; }
        public DateTime ExpireTime { get; set; }
        public bool IsCIPassed { get; set; }
        public int ApprovalCount { get; set; }
        public string PRReviewState { get; set; } = string.Empty; // PR 状态
        public DateTime RefreshTime { get; set; }
    }

    public class ModItemView : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private static readonly DispatcherTimer _refreshTimer = new();
        private static readonly List<ModItemView> _allInstances = new();

        static ModItemView()
        {
            // 全局定时器：每秒刷新一次各行的过期状态
            _refreshTimer.Interval = TimeSpan.FromSeconds(1);
            _refreshTimer.Tick += (s, e) =>
            {
                foreach (var item in _allInstances)
                {
                    item.UpdateExpiredStatus();
                }
            };
            _refreshTimer.Start();
        }

        public ModItemView(TranslationInfoRecord r, string currentUser)
        {
            ModId = r.ModId;
            ModTitle = r.ModTitle;
            Language = r.Language;
            TotalEntries = r.TotalEntries;
            UntranslatedEntries = r.UntranslatedEntries;
            TranslatedEntries = r.TranslatedEntries;
            ApprovedEntries = r.ApprovedEntries;
            IsLocked = r.IsLocked;
            LockedBy = r.LockedBy ?? string.Empty;
            LockTime = r.LockTime;
            ExpireTime = r.ExpireTime;
            IsCIPassed = r.IsCIPassed;
            ApprovalCount = r.ApprovalCount;
            PRReviewState = r.PRReviewState ?? string.Empty;
            RefreshTime = r.RefreshTime;
            _currentUser = currentUser ?? string.Empty;
            _isCheckBoxEnabled = true; // 默认允许复选

            UpdateExpiredStatus();
            _allInstances.Add(this);
        }

        private void UpdateExpiredStatus()
        {
            var newExpiredStatus = IsLocked && ExpireTime != default && ExpireTime < DateTime.Now;
            if (newExpiredStatus != _isExpired)
            {
                _isExpired = newExpiredStatus;
                OnPropertyChanged(nameof(IsExpired));
            }
        }

        private readonly string _currentUser;

        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); OnSelectionChanged(); } }
        private bool _isSelected;

        private void OnSelectionChanged()
        {
            // 通知主窗口更新按钮状态
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.OnModSelectionChanged();
                }
            }));
        }

        public bool IsCheckBoxEnabled { get => _isCheckBoxEnabled; set { _isCheckBoxEnabled = value; OnPropertyChanged(nameof(IsCheckBoxEnabled)); } }
        private bool _isCheckBoxEnabled;

        public string ModId { get; }
        public string ModTitle { get; }
        public string Language { get; }
        public int TotalEntries { get; }
        public int UntranslatedEntries { get; }
        public int TranslatedEntries { get; }
        public int ApprovedEntries { get; }
        public bool IsLocked { get; }
        public string LockedBy { get; }
        public DateTime LockTime { get; }
        public DateTime ExpireTime { get; }
        public bool IsCIPassed { get; }
        public int ApprovalCount { get; }
        public string PRReviewState { get; }
        public DateTime RefreshTime { get; }

        // 计算属性
        public bool IsExpired => _isExpired;
        private bool _isExpired;

        public bool IsLockedByMe => IsLocked && !string.IsNullOrWhiteSpace(_currentUser) && string.Equals(LockedBy, _currentUser, StringComparison.OrdinalIgnoreCase);
        public bool IsLockedByOthers => IsLocked && !IsLockedByMe;

        // 任务状态（基于 PRReviewState 与审批数）
        public string TaskStatus
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PRReviewState)) return string.Empty;
                var norm = NormalizePrState(PRReviewState);
                if (norm == "draft") return "草稿中";
                if (norm == "readyforreview")
                {
                    return ApprovalCount > 0 ? "已批准" : "待审核";
                }
                if (norm == "approved") return "已批准";
                return "待审核";
            }
        }

        private static string NormalizePrState(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                if (ch != ' ' && ch != '_' && ch != '-') sb.Append(ch);
            }
            return sb.ToString().ToLowerInvariant();
        }
    }
}
