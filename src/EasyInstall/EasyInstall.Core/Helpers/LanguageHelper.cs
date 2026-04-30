using System;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace EasyInstall.Core.Helpers
{
    /// <summary>
    /// 运行时语言切换。
    /// 支持的语言代码：null/"" = 跟随系统，"zh-CN" = 简体中文，"en-US" = 英文
    /// </summary>
    public static class LanguageHelper
    {
        private const string ZhCnUri = "pack://application:,,,/EasyInstall.Core;component/Language/zh-CN.xaml";
        private const string EnUsUri = "pack://application:,,,/EasyInstall.Core;component/Language/en-US.xaml";

        /// <summary>
        /// 根据配置的语言代码切换应用程序语言资源字典。
        /// 必须在 Application.Resources 已加载 Generic.xaml 之后调用。
        /// </summary>
        /// <param name="languageCode">null/"" 跟随系统，"zh-CN" 中文，"en-US" 英文</param>
        public static void Apply(string languageCode)
        {
            // 解析目标语言
            string target = Resolve(languageCode);

            string targetUri = target == "zh-CN" ? ZhCnUri : EnUsUri;
            string removeUri = target == "zh-CN" ? EnUsUri : ZhCnUri;

            var merged = Application.Current.Resources.MergedDictionaries;

            // 移除另一种语言的字典
            var toRemove = merged
                .Where(d => d.Source != null &&
                            d.Source.OriginalString.Equals(removeUri,
                                StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var d in toRemove)
                merged.Remove(d);

            // 如果目标语言字典不存在则添加
            bool exists = merged.Any(d =>
                d.Source != null &&
                d.Source.OriginalString.Equals(targetUri,
                    StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                merged.Add(new ResourceDictionary
                {
                    Source = new Uri(targetUri, UriKind.Absolute)
                });
            }
        }

        /// <summary>
        /// 将配置中的语言代码解析为实际使用的语言代码（zh-CN 或 en-US）
        /// </summary>
        public static string Resolve(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                // 跟随系统：检测当前 UI 文化
                string culture = CultureInfo.CurrentUICulture.Name;
                return culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                    ? "zh-CN"
                    : "en-US";
            }

            if (languageCode.Equals("zh-CN", StringComparison.OrdinalIgnoreCase))
                return "zh-CN";

            return "en-US";
        }
    }
}
