using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace EasyInstall.Server.Services
{
    /// <summary>
    /// 版本号解析与比较。版本号为 1~4 段纯数字（与 EasyInstall 打包器的 AppVersion 一致），
    /// 逐段比较：1.2 &lt; 1.2.0.1 &lt; 1.10。
    /// </summary>
    public partial class VersionHelper : DbContext
    {
        /// <summary>
        /// 从文件名中识别版本号，兼容 EasyInstall 输出命名
        /// （{应用名}_{版本号}_{yyyyMMddHHmmss}_Install.exe，版本号可带 v 前缀也可缺省）。
        /// </summary>
        [GeneratedRegex(@"(?:^|[^0-9.])v?(?<v>\d{1,5}(?:\.\d{1,5}){1,3})(?:[^0-9.]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex FileNameVersionRegex();

        public static bool TryParse(string? text, out Version version)
        {
            version = new Version();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();
            if (text.StartsWith('v') || text.StartsWith('V'))
                text = text[1..];

            // System.Version 仅接受 1~4 段数字
            return Version.TryParse(text, out version!) &&
                   !string.IsNullOrWhiteSpace(version.ToString());
        }

        /// <summary>
        /// 比较版本：大于 0 表示 left 更新。
        /// </summary>
        public static int Compare(Version left, Version right) => left.CompareTo(right);

        /// <summary>
        /// 从安装包文件名识别版本号，如 "Demo_v1.2.0.0_20260908120000_Install.exe" → "1.2.0.0"。
        /// 识别失败返回 null。
        /// </summary>
        public static string? ParseFromFileName(string fileName)
        {
            var match = FileNameVersionRegex().Match(fileName);
            if (!match.Success)
                return null;

            var value = match.Groups["v"].Value;
            return TryParse(value, out _) ? value : null;
        }
    }
}
