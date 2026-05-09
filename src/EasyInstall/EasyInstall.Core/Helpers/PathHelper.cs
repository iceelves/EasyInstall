using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyInstall.Core.Helpers
{
    public static class PathHelper
    {
        /// <summary>
        /// 解析路径中的占位符，如 {ProgramFiles}、{ProgramFiles64}、{Company}、{AppName}
        /// </summary>
        public static string Resolve(string path, string company, string appName)
        {
            return path
                .Replace("{ProgramFiles}",   Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                .Replace("{ProgramFiles64}", Environment.GetEnvironmentVariable("ProgramW6432")
                                             ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                .Replace("{LocalAppData}",   Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                .Replace("{AppData}",        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
                .Replace("{Company}",        company ?? "Company")
                .Replace("{AppName}",        appName ?? "App");
        }

        /// <summary>
        /// 递归删除目录（卸载用）
        /// </summary>
        /// <param name="dir"></param>
        public static void DeleteDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(f, FileAttributes.Normal);
                File.Delete(f);
            }
            Directory.Delete(dir, true);
        }
    }
}
