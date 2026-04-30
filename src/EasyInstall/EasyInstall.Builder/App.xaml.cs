using System;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace EasyInstall.Builder
{
    public partial class App : Application
    {
        // URI 常量
        private const string CoreZhCN    = "pack://application:,,,/EasyInstall.Core;component/Language/zh-CN.xaml";
        private const string CoreEnUS    = "pack://application:,,,/EasyInstall.Core;component/Language/en-US.xaml";
        private const string BuilderZhCN = "pack://application:,,,/EasyInstall.Builder;component/Language/zh-CN.xaml";
        private const string BuilderEnUS = "pack://application:,,,/EasyInstall.Builder;component/Language/en-US.xaml";

        /// <summary>
        /// 当前实际生效的语言代码（"zh-CN" 或 "en-US"，已解析跟随系统）
        /// </summary>
        public static string CurrentLanguage { get; private set; } = "zh-CN";

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 启动时跟随系统（Tag = ""）
            ApplyLanguage("");
            new MainWindow().Show();
        }

        /// <summary>
        /// 切换 Builder 界面语言，立即生效（DynamicResource 自动刷新）。
        /// lang = ""      → 跟随系统（检测 CurrentUICulture）
        /// lang = "zh-CN" → 简体中文
        /// lang = "en-US" → 英文
        /// </summary>
        public static void ApplyLanguage(string lang)
        {
            // 解析实际语言
            string resolved = Resolve(lang);
            CurrentLanguage = resolved;

            var merged = Current.Resources.MergedDictionaries;

            string coreTarget    = resolved == "zh-CN" ? CoreZhCN    : CoreEnUS;
            string builderTarget = resolved == "zh-CN" ? BuilderZhCN : BuilderEnUS;
            string coreRemove    = resolved == "zh-CN" ? CoreEnUS    : CoreZhCN;
            string builderRemove = resolved == "zh-CN" ? BuilderEnUS : BuilderZhCN;

            // 移除旧语言字典
            foreach (string uri in new[] { coreRemove, builderRemove })
            {
                var old = merged.FirstOrDefault(d =>
                    d.Source != null &&
                    d.Source.OriginalString.Equals(uri, StringComparison.OrdinalIgnoreCase));
                if (old != null) merged.Remove(old);
            }

            // 添加新语言字典（如果还没有）
            foreach (string uri in new[] { coreTarget, builderTarget })
            {
                bool exists = merged.Any(d =>
                    d.Source != null &&
                    d.Source.OriginalString.Equals(uri, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                    merged.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
            }
        }

        /// <summary>
        /// 将语言代码解析为实际语言（空 = 跟随系统）
        /// </summary>
        public static string Resolve(string lang)
        {
            if (string.IsNullOrEmpty(lang))
            {
                string culture = CultureInfo.CurrentUICulture.Name;
                return culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                    ? "zh-CN" : "en-US";
            }
            return lang.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
        }
    }
}
