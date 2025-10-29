using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;

namespace 翻译工具.Models
{
    // ====== 翻译数据模型 ======
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

    // ====== 任务列表数据模型 ======
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
        public string PRReviewState { get; set; } = string.Empty; // 新增：PR 状态
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
            // 全局定时器：每秒刷新一次所有 ModItemView 实例的过期状态
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
            PRReviewState = r.PRReviewState ?? string.Empty; // 新增：保存 PR 状态
            RefreshTime = r.RefreshTime;
            _currentUser = currentUser ?? string.Empty;
            _isCheckBoxEnabled = true; // 默认启用复选框

            // 初始化过期状态
            UpdateExpiredStatus();

            // 注册到全局实例列表
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
            // 通知主窗口选择状态已改变
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
        public string PRReviewState { get; } // 新增：公开给绑定使用
        public DateTime RefreshTime { get; }

        // 过期状态（可观察属性）
        public bool IsExpired => _isExpired;
        private bool _isExpired;

        // 派生属性用于行样式
        public bool IsLockedByMe => IsLocked && !string.IsNullOrWhiteSpace(_currentUser) && string.Equals(LockedBy, _currentUser, StringComparison.OrdinalIgnoreCase);
        public bool IsLockedByOthers => IsLocked && !IsLockedByMe;

        // 新增：任务状态（根据 PRReviewState 决定）。没有 PR 则为空字符串。
        public string TaskStatus
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PRReviewState)) return string.Empty; // 没有 PR
                var norm = NormalizePrState(PRReviewState);
                if (norm == "draft") return "翻译中";
                if (norm == "readyforreview")
                {
                    return ApprovalCount > 0 ? "已批准" : "已提交";
                }
                if (norm == "approved") return "已批准";
                // 其他状态一律视为已提交
                return "已提交";
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
