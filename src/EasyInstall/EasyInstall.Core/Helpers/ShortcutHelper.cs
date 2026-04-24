using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;

namespace EasyInstall.Core.Helpers
{
    /// <summary>
    /// 快捷方式帮助类
    /// </summary>
    public static class ShortcutHelper
    {
        /// <summary>
        /// 创建桌面快捷方式
        /// </summary>
        /// <param name="appName"></param>
        /// <param name="targetExe"></param>
        /// <param name="workDir"></param>
        public static void CreateDesktopShortcut(string appName, string targetExe, string workDir)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            CreateShortcut(Path.Combine(desktop, appName + ".lnk"), targetExe, workDir);
        }

        /// <summary>
        /// 创建开始菜单快捷方式
        /// </summary>
        /// <param name="appName"></param>
        /// <param name="targetExe"></param>
        /// <param name="workDir"></param>
        public static void CreateStartMenuShortcut(string appName, string targetExe, string workDir)
        {
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), appName);
            Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, appName + ".lnk"), targetExe, workDir);
        }

        /// <summary>
        /// 删除桌面快捷方式
        /// </summary>
        /// <param name="appName"></param>
        public static void RemoveDesktopShortcut(string appName)
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                appName + ".lnk");
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// 删除开始菜单快捷方式目录
        /// </summary>
        /// <param name="appName"></param>
        public static void RemoveStartMenuShortcut(string appName)
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), appName);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        /// <summary>
        /// 通过 IShellLink COM 接口创建 .lnk 快捷方式
        /// </summary>
        /// <param name="lnkPath"></param>
        /// <param name="targetExe"></param>
        /// <param name="workDir"></param>
        private static void CreateShortcut(string lnkPath, string targetExe, string workDir)
        {
            var shellLink = (IShellLink)new ShellLink();
            shellLink.SetPath(targetExe);
            shellLink.SetWorkingDirectory(workDir);
            shellLink.SetDescription("");
            shellLink.SetShowCmd(1); // SW_SHOWNORMAL

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(lnkPath, false);

            Marshal.ReleaseComObject(shellLink);
        }

        // ── COM 接口定义 ──────────────────────────────────────────

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
                int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
                int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
                int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
