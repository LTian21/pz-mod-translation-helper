namespace TranslatorGUI.Models
{
    public class Config
    {
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public string? LocalPath { get; set; }
        public string? LanguageSuffix { get; set; }

        // 是否跳过丢弃提示
        public bool SkipDiscardPrompt { get; set; }

        // 丢弃提示时是否自动选择“继续”（true 继续，false 取消）
        public bool SkipDiscardPromptProceed { get; set; }

        // 记住是否在首次下载时使用镜像站
        public bool UseMirrorSiteFirstDownload { get; set; }
    }
}
