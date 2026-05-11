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
    /// Overlay 打包：将压缩数据附加到 EXE 末尾
    /// 完整安装包格式：[EXE原始内容] [压缩数据] [JSON配置UTF8] [4字节JSON长度] [8字节数据长度] [8字节魔数]
    /// 卸载程序格式：  [EXE原始内容] [空数据(0字节)] [JSON配置UTF8] [4字节JSON长度] [8字节0] [8字节魔数]
    /// </summary>
    public static class OverlayHelper
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("EASYINST");

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

            // 解析 ICO 格式
            // ICO header: reserved(2) type(2) count(2)
            int count = BitConverter.ToUInt16(icoBytes, 4);
            if (count == 0) return;

            // 构建 GRPICONDIR 用于 RT_GROUP_ICON
            // GRPICONDIR = WORD reserved, WORD type, WORD count, GRPICONDIRENTRY[count]
            // GRPICONDIRENTRY = BYTE width, BYTE height, BYTE colorCount, BYTE reserved,
            //                   WORD planes, WORD bitCount, DWORD bytesInRes, WORD id
            int grpSize = 6 + count * 14;
            byte[] grpData = new byte[grpSize];
            // header
            grpData[0] = 0; grpData[1] = 0;   // reserved
            grpData[2] = 1; grpData[3] = 0;   // type = 1 (icon)
            grpData[4] = (byte)count; grpData[5] = 0;

            IntPtr hUpdate = BeginUpdateResource(exePath, false);
            if (hUpdate == IntPtr.Zero) return;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    int entryOffset = 6 + i * 16; // ICONDIRENTRY is 16 bytes
                    byte width = icoBytes[entryOffset];
                    byte height = icoBytes[entryOffset + 1];
                    byte colorCount = icoBytes[entryOffset + 2];
                    byte reserved = icoBytes[entryOffset + 3];
                    ushort planes = BitConverter.ToUInt16(icoBytes, entryOffset + 4);
                    ushort bitCount = BitConverter.ToUInt16(icoBytes, entryOffset + 6);
                    int dataSize = BitConverter.ToInt32(icoBytes, entryOffset + 8);
                    int dataOffset = BitConverter.ToInt32(icoBytes, entryOffset + 12);

                    // 提取单个图标数据
                    byte[] iconData = new byte[dataSize];
                    Array.Copy(icoBytes, dataOffset, iconData, 0, dataSize);

                    ushort iconId = (ushort)(i + 1);

                    // 写 RT_ICON
                    UpdateResource(hUpdate, RT_ICON, new IntPtr(iconId), 0, iconData, (uint)iconData.Length);

                    // 填 GRPICONDIRENTRY（14 bytes）
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

                // 写 RT_GROUP_ICON（id=1）
                UpdateResource(hUpdate, RT_GROUP_ICON, new IntPtr(1), 0, grpData, (uint)grpData.Length);
                EndUpdateResource(hUpdate, false);
            }
            catch
            {
                EndUpdateResource(hUpdate, true); // discard on error
            }
        }

        /// <summary>
        /// 从带 Overlay 的安装包中提取原始 EXE 字节（不含 overlay 部分）。
        /// 用于将纯 EXE 写入目标路径，后续再替换图标和追加 overlay。
        /// </summary>
        /// <param name="exePath">含完整 Overlay 的安装包路径</param>
        /// <returns>原始 EXE 字节（不含 overlay）</returns>
        public static byte[] ReadExeBytes(string exePath)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // 尾部结构：[JSON][jsonLen(4)][dataLen(8)][Magic(8)]
                fs.Seek(-(Magic.Length + 8 + 4), SeekOrigin.End);
                int jsonLen = br.ReadInt32();
                long dataLen = br.ReadInt64();

                // 纯 EXE 长度 = 总长度 - dataLen - jsonLen - 4 - 8 - Magic.Length
                long exeLen = fs.Length - dataLen - jsonLen - 4 - 8 - Magic.Length;
                fs.Seek(0, SeekOrigin.Begin);
                return br.ReadBytes((int)exeLen);
            }
        }

        /// <summary>
        /// 将卸载 overlay（JSON 配置 + 元数据）直接追加到已存在的 EXE 文件末尾。
        /// 调用前该文件必须已完成图标替换且不含 overlay。
        /// </summary>
        /// <param name="exePath">目标 EXE 路径（原地追加）</param>
        /// <param name="overlaySourcePath">含完整 Overlay 的安装包路径（用于读取 JSON 配置）</param>
        public static void AppendUninstallOverlay(string exePath, string overlaySourcePath)
        {
            string json = ReadConfig(overlaySourcePath);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int jsonLen = jsonBytes.Length;

            using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(jsonBytes);
                bw.Write(jsonLen);       // 4 bytes：JSON 长度
                bw.Write((long)0);       // 8 bytes：压缩数据长度为 0
                bw.Write(Magic);         // 8 bytes：魔数
            }
        }

        /// <summary>
        /// 流式压缩并直接追加到 EXE 末尾，全程无临时文件、无内存缓冲。
        /// 调用前必须已完成图标替换（BeginUpdateResource 会截断末尾数据）。
        /// 内部以 FileMode.Append 打开 EXE，边压缩边写入，完成后回填尾部元数据。
        /// </summary>
        /// <param name="exePath">目标 EXE 路径（已完成图标替换）</param>
        /// <param name="files">待打包文件列表</param>
        /// <param name="baseDir">文件相对路径基准目录</param>
        /// <param name="compressionType">压缩算法</param>
        /// <param name="configJson">JSON 配置字符串</param>
        /// <param name="progress">压缩进度回调（0-100）</param>
        public static void AppendOverlayStreaming(
            string exePath,
            List<Core.Models.PackageFile> files,
            string baseDir,
            Core.Compression.CompressionType compressionType,
            string configJson,
            Action<int> progress = null)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(configJson);

            using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write, FileShare.None, 65536))
            {
                // 记录压缩数据起始偏移（相对于当前文件末尾，即 Append 后的起始位置）
                // FileMode.Append 打开后 Position = Length，直接记录即可
                long dataStartPos = fs.Position;

                // 流式压缩，直接写入 EXE，内存恒定 ~80KB
                ZipHelper.CompressPathsToStream(files, baseDir, fs, compressionType, progress);

                long dataLen = fs.Position - dataStartPos;

                // 追加 JSON + 尾部元数据
                using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
                {
                    bw.Write(jsonBytes);
                    bw.Write(jsonBytes.Length);  // 4 bytes：JSON 长度
                    bw.Write(dataLen);           // 8 bytes：压缩数据长度
                    bw.Write(Magic);             // 8 bytes：魔数
                }
            }
        }

        /// <summary>
        /// 将压缩数据流和 JSON 配置追加到已存在的 EXE 文件末尾。
        /// </summary>
        public static void AppendOverlay(string exePath, Stream compressedStream, string configJson)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(configJson);

            if (compressedStream.CanSeek)
            {
                long dataLen = compressedStream.Length - compressedStream.Position;
                using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write, FileShare.None, 65536))
                {
                    CopyStream(compressedStream, fs);
                    using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
                    {
                        bw.Write(jsonBytes);
                        bw.Write(jsonBytes.Length);
                        bw.Write(dataLen);
                        bw.Write(Magic);
                    }
                }
            }
            else
            {
                long dataLen = 0;
                using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write, FileShare.None, 65536))
                {
                    dataLen = CopyStreamCounted(compressedStream, fs);
                    using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
                    {
                        bw.Write(jsonBytes);
                        bw.Write(jsonBytes.Length);
                        bw.Write(dataLen);
                        bw.Write(Magic);
                    }
                }
            }
        }

        private static void CopyStream(Stream src, Stream dst)
        {
            var buf = new byte[81920];
            int read;
            while ((read = src.Read(buf, 0, buf.Length)) > 0)
                dst.Write(buf, 0, read);
        }

        private static long CopyStreamCounted(Stream src, Stream dst)
        {
            var buf = new byte[81920];
            long total = 0;
            int read;
            while ((read = src.Read(buf, 0, buf.Length)) > 0)
            {
                dst.Write(buf, 0, read);
                total += read;
            }
            return total;
        }

        /// <summary>
        /// 检测当前运行的 EXE 是否包含 Overlay 数据
        /// </summary>
        /// <param name="exePath"></param>
        /// <returns></returns>
        public static bool HasOverlay(string exePath)
        {
            try
            {
                using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < Magic.Length + 12) return false;
                    fs.Seek(-(Magic.Length), SeekOrigin.End);
                    byte[] tail = new byte[Magic.Length];
                    fs.Read(tail, 0, tail.Length);
                    return BytesEqual(tail, Magic);
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// 从 EXE 中读取 JSON 配置
        /// </summary>
        /// <param name="exePath"></param>
        /// <returns></returns>
        public static string ReadConfig(string exePath)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // 读尾部：Magic(8) + dataLen(8) + jsonLen(4)
                fs.Seek(-(Magic.Length + 8 + 4), SeekOrigin.End);
                int jsonLen = br.ReadInt32();
                long dataLen = br.ReadInt64();
                // 跳过 Magic
                fs.Seek(-(Magic.Length + 8 + 4 + jsonLen), SeekOrigin.End);
                byte[] jsonBytes = br.ReadBytes(jsonLen);
                return Encoding.UTF8.GetString(jsonBytes);
            }
        }

        /// <summary>
        /// 打开一个定位到压缩数据起始位置的 FileStream，用于流式解压。
        /// 调用方负责 Dispose 该流。
        /// </summary>
        /// <param name="exePath">EXE 文件路径</param>
        /// <returns>定位到压缩数据起始位置的 FileStream，若无压缩数据则返回 null</returns>
        public static FileStream OpenDataStream(string exePath)
        {
            var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using (var br = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    fs.Seek(-(Magic.Length + 8 + 4), SeekOrigin.End);
                    int jsonLen = br.ReadInt32();
                    long dataLen = br.ReadInt64();

                    if (dataLen == 0)
                    {
                        fs.Dispose();
                        return null; // 卸载程序无压缩数据
                    }

                    // 定位到压缩数据起始位置
                    fs.Seek(-(Magic.Length + 8 + 4 + jsonLen + dataLen), SeekOrigin.End);
                    return fs;
                }
            }
            catch
            {
                fs.Dispose();
                throw;
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
