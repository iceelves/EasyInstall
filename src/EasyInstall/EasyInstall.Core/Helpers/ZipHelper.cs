using EasyInstall.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyInstall.Core.Helpers
{
    /// <summary>
    /// 基于 GZip 的文件打包/解包工具（兼容 .NET 4.5.2，不使用 ValueTuple）
    /// 格式：[4字节条目数] ( [4字节路径长度][路径UTF8] [8字节文件大小][文件数据] ) * N
    /// 整体用 GZip 压缩
    /// </summary>
    public static class ZipHelper
    {
        public static event Action<int> ProgressChanged;

        private class FileEntry
        {
            public string RelPath { get; set; }
            public string AbsPath { get; set; }
        }

        /// <summary>
        /// 将多个源路径（文件或目录）压缩为字节数组
        /// </summary>
        /// <param name="files"></param>
        /// <param name="baseDir"></param>
        /// <returns></returns>
        public static byte[] CompressPaths(List<PackageFile> files, string baseDir)
        {
            var entries = new List<FileEntry>();

            foreach (var pf in files)
            {
                string src = Path.IsPathRooted(pf.Source)
                    ? pf.Source
                    : Path.Combine(baseDir, pf.Source);

                // 计算该文件在安装目录下的相对路径：
                //   TreeRootPath 有值 → 以根节点目录名为前缀，再拼文件相对根节点的子路径
                //   TreeRootPath 为空 → 独立文件，直接放安装根目录
                string rootPrefix = string.IsNullOrEmpty(pf.TreeRootPath)
                    ? ""
                    : Path.GetFileName(pf.TreeRootPath.TrimEnd('\\', '/'));

                if (Directory.Exists(src))
                {
                    foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(src.TrimEnd('\\', '/').Length + 1);
                        entries.Add(new FileEntry { RelPath = rel, AbsPath = f });
                    }
                }
                else if (File.Exists(src))
                {
                    // 计算文件相对于根节点的子路径
                    string subPath = Path.GetFileName(src);
                    if (!string.IsNullOrEmpty(pf.TreeRootPath))
                    {
                        string fileDir = Path.GetDirectoryName(src) ?? "";
                        string rootPath = pf.TreeRootPath.TrimEnd('\\', '/');
                        if (fileDir.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
                            && fileDir.Length > rootPath.Length)
                        {
                            string sub = fileDir.Substring(rootPath.Length).TrimStart('\\', '/');
                            subPath = Path.Combine(sub, Path.GetFileName(src));
                        }
                        subPath = Path.Combine(rootPrefix, subPath);
                    }

                    entries.Add(new FileEntry { RelPath = subPath, AbsPath = src });
                }
            }

            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
                using (var bw = new BinaryWriter(gz))
                {
                    bw.Write(entries.Count);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        byte[] data = File.ReadAllBytes(entries[i].AbsPath);
                        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(entries[i].RelPath);
                        bw.Write(pathBytes.Length);
                        bw.Write(pathBytes);
                        bw.Write((long)data.Length);
                        bw.Write(data);
                        ProgressChanged?.Invoke((i + 1) * 100 / entries.Count);
                    }
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// 解压字节数组到目标目录
        /// </summary>
        public static void Decompress(byte[] data, string targetDir, Action<int> progress = null)
        {
            using (var ms = new MemoryStream(data))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var br = new BinaryReader(gz))
            {
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int pathLen = br.ReadInt32();
                    string relPath = System.Text.Encoding.UTF8.GetString(br.ReadBytes(pathLen));
                    long fileSize = br.ReadInt64();
                    byte[] fileData = br.ReadBytes((int)fileSize);

                    string dest = Path.Combine(targetDir, relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, fileData);

                    progress?.Invoke((i + 1) * 100 / count);
                }
            }
        }

        /// <summary>
        /// 读取压缩包中所有文件的原始大小之和，不解压文件数据。
        /// 用于在安装前显示"所需磁盘空间"。
        /// </summary>
        public static long GetUncompressedSize(byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            try
            {
                long total = 0;
                using (var ms = new MemoryStream(data))
                using (var gz = new GZipStream(ms, CompressionMode.Decompress))
                using (var br = new BinaryReader(gz))
                {
                    int count = br.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        int pathLen = br.ReadInt32();
                        br.ReadBytes(pathLen);          // 跳过路径
                        long fileSize = br.ReadInt64();
                        total += fileSize;
                        // 跳过文件数据，避免读入内存
                        long remaining = fileSize;
                        var skip = new byte[81920];
                        while (remaining > 0)
                        {
                            int read = br.Read(skip, 0, (int)Math.Min(skip.Length, remaining));
                            if (read == 0) break;
                            remaining -= read;
                        }
                    }
                }
                return total;
            }
            catch { return 0; }
        }
    }
}
