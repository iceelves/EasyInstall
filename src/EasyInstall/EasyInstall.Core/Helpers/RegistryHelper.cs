using Microsoft.Win32;
using System;

namespace EasyInstall.Core.Helpers
{
    /// <summary>
    /// 注册表帮助类
    /// </summary>
    public static class RegistryHelper
    {
        private const string UninstallBase =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>
        /// 写入卸载注册表项
        /// </summary>
        /// <param name="appName">应用名称</param>
        /// <param name="regKey">注册表卸载键名</param>
        /// <param name="installDir">安装路径</param>
        /// <param name="mainExecutable">主程序名</param>
        /// <param name="version">版本号</param>
        /// <param name="company">公司</param>
        /// <param name="uninstallExe">卸载程序路径</param>
        /// <param name="website">官方页面或更多信息的链接</param>
        public static void RegisterUninstall(string appName, string regKey,
            string installDir, string mainExecutable, string version, string company,
            string uninstallExe, string website)
        {
            string keyPath = $@"{UninstallBase}\{regKey}";
            using (var key = Registry.LocalMachine.CreateSubKey(keyPath))
            {
                // 图标
                key.SetValue("DisplayIcon", $"{installDir}\\{mainExecutable}");
                // 应用名称
                key.SetValue("DisplayName", appName);
                // 版本号
                key.SetValue("DisplayVersion", version);
                // 安装路径
                key.SetValue("InstallLocation", installDir);
                // 公司
                key.SetValue("Publisher", company);
                // 卸载路径
                key.SetValue("UninstallString", $"\"{uninstallExe}\" /uninstall");
                // 官方页面或更多信息的链接
                key.SetValue("URLInfoAbout", website);
            }
        }

        /// <summary>
        /// 删除卸载注册表项
        /// </summary>
        public static void UnregisterUninstall(string regKey)
        {
            string keyPath = $@"{UninstallBase}\{regKey}";
            Registry.LocalMachine.DeleteSubKeyTree(keyPath, false);
        }

        /// <summary>
        /// 写入开机自启
        /// </summary>
        public static void SetAutoRun(string appName, string exePath, bool enable)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (enable)
                    key.SetValue(appName, $"\"{exePath}\"");
                else
                    key.DeleteValue(appName, false);
            }
        }

        /// <summary>
        /// 读取已安装路径
        /// </summary>
        public static string GetInstallLocation(string regKey)
        {
            string keyPath = $@"{UninstallBase}\{regKey}";
            using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
            {
                return key?.GetValue("InstallLocation") as string;
            }
        }

        /// <summary>
        /// 是否已安装
        /// </summary>
        public static bool IsInstalled(string regKey)
        {
            string keyPath = $@"{UninstallBase}\{regKey}";
            using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                return key != null;
        }
    }
}
