using System;
using System.Collections.Generic;

// 数据模型
class TranslationInfo
{
    public string ModId { get; set; } = "";
    public string ModTitle { get; set; } = "";
    public string Language { get; set; } = "SChinese";
    public int TotalEntries { get; set; } = 0;
    public int UntranslatedEntries { get; set; } = 0;
    public int TranslatedEntries { get; set; } = 0;
    public int ApprovedEntries { get; set; } = 0;
    public bool IsLocked { get; set; } = false;
    public string LockedBy { get; set; } = "";
    public DateTime LockTime { get; set; } = DateTime.MinValue;
    public DateTime ExpireTime { get; set; } = DateTime.MinValue;
    public bool IsCIPassed { get; set; } = false;
    public int ApprovalCount { get; set; } = 0;
    public string PRReviewState { get; set; } = "";
    public DateTime RefreshTime { get; set; } = DateTime.MinValue;
}

class TranslationEntry
{
    public string OriginalText { get; set; } = "";
    public string SChinese { get; set; } = "";
    public TranslationStatus SChineseStatus { get; set; } = TranslationStatus.Untranslated;
    public List<string> Comment { get; set; } = new();
}

enum TranslationStatus
{
    Untranslated,
    Translated,
    Approved
}

class AppConfig
{
    public required string RepoUrl { get; set; }
    public required string Key { get; set; }
    public required string UserName { get; set; }
    public required string UserEmail { get; set; }
    public required TranslationSystem.Language Language { get; set; }
    public required string Operation { get; set; }
    public required string CommitMessage { get; set; }
    public required string LocalPath { get; set; }
}
