using System;
using System.IO;
using System.Text.Json;

namespace 翻译工具.Models
{
    // 存储“下次不再提示”的选择，与当前翻译用户名绑定
    public class ConfirmDiscardSettings
    {
        public string? UserName { get; set; }
        public bool SkipDiscardPrompt { get; set; }
        public bool SkipDiscardPromptProceed { get; set; }

        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "confirm_discard_settings.json");

        public static ConfirmDiscardSettings LoadOrDefault()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new ConfirmDiscardSettings();
                }
                var json = File.ReadAllText(SettingsPath);
                var data = JsonSerializer.Deserialize<ConfirmDiscardSettings>(json);
                return data ?? new ConfirmDiscardSettings();
            }
            catch
            {
                return new ConfirmDiscardSettings();
            }
        }

        public static void Save(ConfirmDiscardSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // 忽略 IO/序列化错误，避免影响 UI 流程
            }
        }
    }
}
