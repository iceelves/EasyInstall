using EasyInstall.Core.Compression;
using EasyInstall.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace EasyInstall.Core.Helpers
{
    /// <summary>
    /// Overlay 打包：将压缩数据附加到 EXE 末尾，或拆分为外部 .eidat 数据文件。
    ///
    /// 内嵌格式（压缩数据 ≤ 阈值）：
    ///   [EXE原始内容] [压缩数据] [JSON配置UTF8] [4字节JSON长度] [8字节数据长度(>0)] [8字节魔数]
    ///
    /// 拆分格式（压缩数据 > 阈值）：
    ///   EXE  ：[EXE原始内容] [JSON配置UTF8] [4字节JSON长度] [8字节 -1L] [8字节魔数]
    ///   .eidat：[压缩数据]（纯压缩流，无额外头部）
    ///
    /// 卸载程序格式（始终内嵌，无压缩数据）：
    ///   [EXE原始内容] [JSON配置UTF8] [4字节JSON长度] [8字节0] [8字节魔数]
    /// </summary>
    public static class OverlayHelper
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("EASYINST");

        /// <summary>
        /// dataLen 写入 -1L 表示压缩数据存放在外部 .eidat 文件中
        /// </summary>
        private const long ExternalDataFlag = -1L;

        /// <summary>
        /// 拆分阈值：32位进程按 1.9 GB，64位进程按 3.9 GB。
        /// 超过此阈值时将压缩数据从 EXE 中剥离为外部 .eidat 文件。
        /// </summary>
        private static long SplitThreshold =>
            IntPtr.Size == 4
                ? 1_900_000_000L   // 32位：1.9 GB
                : 3_900_000_000L;  // 64位：3.9 GB

        /// <summary>
        /// 根据安装包 EXE 路径推算对应的 .eidat 文件路径
        /// </summary>
        public static string GetEidatPath(string exePath) => Path.ChangeExtension(exePath, ".eidat");

        // ── Win32 图标替换 API ────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName,
            ushort wLanguage, byte[] lpData, uint cbData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

        private static readonly IntPtr RT_ICON = new IntPtr(3);
        private static readonly IntPtr RT_GROUP_ICON = new IntPtr(14);

        /// <summary>
        /// 将 ICO 文件字节写入 EXE 的图标资源（替换第一个图标组）
        /// </summary>
        public static void SetExeIcon(string exePath, byte[] icoBytes)
        {
            if (icoBytes == null || icoBytes.Length < 6) return;

            int count = BitConverter.ToUInt16(icoBytes, 4);
            if (count == 0) return;

            int grpSize = 6 + count * 14;
            byte[] grpData = new byte[grpSize];
            grpData[0] = 0; grpData[1] = 0;
            grpData[2] = 1; grpData[3] = 0;
            grpData[4] = (byte)count; grpData[5] = 0;

            IntPtr hUpdate = BeginUpdateResource(exePath, false);
            if (hUpdate == IntPtr.Zero) return;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    int entryOffset = 6 + i * 16;
                    byte width = icoBytes[entryOffset];
                    byte height = icoBytes[entryOffset + 1];
                    byte colorCount = icoBytes[entryOffset + 2];
                    byte reserved = icoBytes[entryOffset + 3];
                    ushort planes = BitConverter.ToUInt16(icoBytes, entryOffset + 4);
                    ushort bitCount = BitConverter.ToUInt16(icoBytes, entryOffset + 6);
                    int dataSize = BitConverter.ToInt32(icoBytes, entryOffset + 8);
                    int dataOffset = BitConverter.ToInt32(icoBytes, entryOffset + 12);

                    byte[] iconData = new byte[dataSize];
                    Array.Copy(icoBytes, dataOffset, iconData, 0, dataSize);

                    ushort iconId = (ushort)(i + 1);
                    UpdateResource(hUpdate, RT_ICON, new IntPtr(iconId), 0, iconData, (uint)iconData.Length);

                    int grpEntry = 6 + i * 14;
                    grpData[grpEntry] = width;
                    grpData[grpEntry + 1] = height;
                    grpData[grpEntry + 2] = colorCount;
                    grpData[grpEntry + 3] = reserved;
                    grpData[grpEntry + 4] = (byte)(planes & 0xFF);
                    grpData[grpEntry + 5] = (byte)(planes >> 8);
                    grpData[grpEntry + 6] = (byte)(bitCount & 0xFF);
                    grpData[grpEntry + 7] = (byte)(bitCount >> 8);
                    grpData[grpEntry + 8] = (byte)(dataSize & 0xFF);
                    grpData[grpEntry + 9] = (byte)((dataSize >> 8) & 0xFF);
                    grpData[grpEntry + 10] = (byte)((dataSize >> 16) & 0xFF);
                    grpData[grpEntry + 11] = (byte)((dataSize >> 24) & 0xFF);
                    grpData[grpEntry + 12] = (byte)(iconId & 0xFF);
                    grpData[grpEntry + 13] = (byte)(iconId >> 8);
                }

                UpdateResource(hUpdate, RT_GROUP_ICON, new IntPtr(1), 0, grpData, (uint)grpData.Length);
                EndUpdateResource(hUpdate, false);
            }
            catch
            {
                EndUpdateResource(hUpdate, true);
            }
        }

        /// <summary>
        /// 将安装包中的原始 EXE 部分（不含 overlay）流式复制到目标文件。
        /// 支持超过 2GB 的文件，全程无大块内存分配。
        /// </summary>
        public static void CopyExeOnly(string exePath, string destPath)
        {
            using (var src = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
            using (var br = new BinaryReader(src, Encoding.UTF8, leaveOpen: true))
            {
                src.Seek(-(Magic.Length + 8 + 4), SeekOrigin.End);
                int jsonLen = br.ReadInt32();
                long dataLen = br.ReadInt64(); // -1 = 外部 .eidat；0 = 卸载程序；>0 = 内嵌数据

                long embedLen = dataLen > 0 ? dataLen : 0L;
                long exeLen = src.Length - embedLen - jsonLen - 4 - 8 - Magic.Length;

                src.Seek(0, SeekOrigin.Begin);
                using (var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                    CopyStreamExact(src, dst, exeLen);
            }
        }

        /// <summary>
        /// 将卸载 overlay（JSON 配置 + 元数据）直接追加到已存在的 EXE 文件末尾。
        /// 调用前该文件必须已完成图标替换且不含 overlay。
        /// </summary>
        public static void AppendUninstallOverlay(string exePath, string overlaySourcePath)
        {
            string json = ReadConfig(overlaySourcePath);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(jsonBytes);
                bw.Write(jsonBytes.Length); // 4 bytes：JSON 长度
                bw.Write((long)0);          // 8 bytes：压缩数据长度为 0（卸载程序无数据）
                bw.Write(Magic);            // 8 bytes：魔数
            }
        }

        /// <summary>
        /// 流式压缩并追加到 EXE 末尾，支持大文件自动拆分为外部 .eidat 文件。
        ///
        /// 压缩数据直接流式写入 EXE，完成后判断数据量：
        ///   未超过阈值 → 写入 JSON + 元数据，完成（内嵌模式，无临时文件）。
        ///   超过阈值   → 将 EXE 末尾的压缩数据段流式复制到 .eidat，
        ///                截断 EXE 至原始长度，写入 JSON + 元数据（dataLen = -1L）。
        ///
        /// 调用前必须已完成图标替换（BeginUpdateResource 会截断末尾数据）。
        /// </summary>
        /// <returns>true = 生成了外部 .eidat 文件；false = 数据内嵌在 EXE 中</returns>
        public static bool AppendOverlayStreaming(
            string exePath,
            List<PackageFile> files,
            string baseDir,
            CompressionType compressionType,
            string configJson,
            Action<int> progress = null)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(configJson);
            long dataStartPos = 0;
            long dataLen = 0;

            // ── 阶段 1：流式压缩，直接写入 EXE ──────────────────────
            // 用 Append 模式打开，压缩完成后记录数据长度，然后关闭流。
            // 关闭后文件结构：[EXE原始内容][压缩数据]（尚无 JSON/元数据）
            using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write, FileShare.None, 65536))
            {
                dataStartPos = fs.Position; // EXE 原始末尾位置
                ZipHelper.CompressPathsToStream(files, baseDir, fs, compressionType, progress);
                dataLen = fs.Position - dataStartPos;
            }

            // ── 阶段 2：判断是否需要拆分 ─────────────────────────────
            if (dataLen <= SplitThreshold)
            {
                // 内嵌模式：直接追加 JSON + 元数据
                using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write, FileShare.None, 65536))
                using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
                {
                    bw.Write(jsonBytes);
                    bw.Write(jsonBytes.Length); // 4 bytes：JSON 长度
                    bw.Write(dataLen);          // 8 bytes：压缩数据长度
                    bw.Write(Magic);            // 8 bytes：魔数
                }
                return false;
            }

            // 拆分模式：
            //   1. 以 ReadWrite 模式打开 EXE，将压缩数据段流式复制到 .eidat
            //   2. 截断 EXE 至 dataStartPos（移除压缩数据）
            //   3. 追加 JSON + 元数据（dataLen = -1L）
            string eidatPath = GetEidatPath(exePath);
            if (File.Exists(eidatPath)) File.Delete(eidatPath);

            using (var exeFs = new FileStream(exePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 65536))
            using (var eidatFs = new FileStream(eidatPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
            {
                // 定位到压缩数据起始位置，流式复制到 .eidat
                exeFs.Seek(dataStartPos, SeekOrigin.Begin);
                CopyStreamExact(exeFs, eidatFs, dataLen);
            }

            // 截断 EXE，移除压缩数据段
            using (var exeFs = new FileStream(exePath, FileMode.Open, FileAccess.Write, FileShare.None, 65536))
                exeFs.SetLength(dataStartPos);

            // 追加 JSON + 元数据（dataLen = -1L 标记外部文件）
            using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write, FileShare.None, 65536))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(jsonBytes);
                bw.Write(jsonBytes.Length); // 4 bytes：JSON 长度
                bw.Write(ExternalDataFlag); // 8 bytes：-1L 表示外部文件
                bw.Write(Magic);            // 8 bytes：魔数
            }
            return true;
        }

        /// <summary>
        /// 检测 EXE 是否包含 Overlay 数据（魔数校验）
        /// </summary>
        public static bool HasOverlay(string exePath)
        {
            try
            {
                using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < Magic.Length + 12) return false;
                    fs.Seek(-Magic.Length, SeekOrigin.End);
                    byte[] tail = new byte[Magic.Length];
                    fs.Read(tail, 0, tail.Length);
                    return BytesEqual(tail, Magic);
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// 从 EXE 中读取 JSON 配置（内嵌和拆分模式均适用）
        /// </summary>
        public static string ReadConfig(string exePath)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                fs.Seek(-(Magic.Length + 8 + 4), SeekOrigin.End);
                int jsonLen = br.ReadInt32();
                long dataLen = br.ReadInt64();
                fs.Seek(-(Magic.Length + 8 + 4 + jsonLen), SeekOrigin.End);
                byte[] jsonBytes = br.ReadBytes(jsonLen);
                return Encoding.UTF8.GetString(jsonBytes);
            }
        }

        /// <summary>
        /// 打开压缩数据流，用于流式解压。调用方负责 Dispose。
        ///   内嵌模式（dataLen > 0）：返回定位到 EXE 内压缩数据起始位置的 FileStream
        ///   拆分模式（dataLen == -1）：返回同目录 .eidat 文件的 FileStream（从头开始）
        ///   卸载程序（dataLen == 0）：返回 null
        /// </summary>
        public static Stream OpenDataStream(string exePath)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true))
            {
                fs.Seek(-(Magic.Length + 8 + 4), SeekOrigin.End);
                int jsonLen = br.ReadInt32();
                long dataLen = br.ReadInt64();

                if (dataLen == 0)
                    return null;

                if (dataLen == ExternalDataFlag)
                {
                    string eidatPath = GetEidatPath(exePath);
                    if (!File.Exists(eidatPath))
                        throw new FileNotFoundException(
                            $"安装包数据文件缺失，请确保 \"{Path.GetFileName(eidatPath)}\" 与安装程序位于同一目录。",
                            eidatPath);
                    return new FileStream(eidatPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                }

                // 内嵌模式
                var dataFs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                try
                {
                    dataFs.Seek(-(Magic.Length + 8 + 4 + jsonLen + dataLen), SeekOrigin.End);
                    return dataFs;
                }
                catch
                {
                    dataFs.Dispose();
                    throw;
                }
            }
        }

        // ── 内部辅助 ──────────────────────────────────────────────

        /// <summary>
        /// 从 src 精确复制 count 字节到 dst，使用固定缓冲区。
        /// </summary>
        private static void CopyStreamExact(Stream src, Stream dst, long count)
        {
            var buf = new byte[65536];
            long remaining = count;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buf.Length, remaining);
                int read = src.Read(buf, 0, toRead);
                if (read == 0) break;
                dst.Write(buf, 0, read);
                remaining -= read;
            }
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
