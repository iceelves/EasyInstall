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
        /// 从带 Overlay 的安装包 EXE 中提取原始 EXE 字节，并将 JSON 配置重新附加，
        /// 生成一个不含压缩数据的卸载程序 EXE。
        /// 卸载程序可通过 HasOverlay / ReadConfig 正常读取配置，但 ReadData 返回空。
        /// </summary>
        /// <param name="exePath">带完整 Overlay 的安装包路径</param>
        /// <returns>可直接写入磁盘的卸载程序字节</returns>
        public static byte[] BuildUninstallExe(string exePath)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // 读取尾部元数据
                fs.Seek(-(Magic.Length + 8 + 4), SeekOrigin.End);
                int jsonLen = br.ReadInt32();
                long dataLen = br.ReadInt64();
                long overlayLen = dataLen + jsonLen + 4 + 8 + Magic.Length;
                long exeLen = fs.Length - overlayLen;

                // 读取原始 EXE
                fs.Seek(0, SeekOrigin.Begin);
                byte[] exeBytes = br.ReadBytes((int)exeLen);

                // 读取 JSON 配置
                fs.Seek(-(Magic.Length + 8 + 4 + jsonLen), SeekOrigin.End);
                byte[] jsonBytes = br.ReadBytes(jsonLen);

                // 重新打包：EXE + JSON + 元数据（dataLen = 0，无压缩数据）
                using (var ms = new MemoryStream(exeBytes.Length + jsonLen + 4 + 8 + Magic.Length))
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(exeBytes);
                    bw.Write(jsonBytes);
                    bw.Write(jsonLen);       // 4 bytes：JSON 长度
                    bw.Write((long)0);       // 8 bytes：压缩数据长度为 0
                    bw.Write(Magic);         // 8 bytes：魔数
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// 将 Setup.exe + 压缩数据 + JSON配置 合并为单个 EXE
        /// </summary>
        /// <param name="setupExePath"></param>
        /// <param name="compressedData"></param>
        /// <param name="configJson"></param>
        /// <param name="outputPath"></param>
        public static void Pack(string setupExePath, byte[] compressedData,
            string configJson, string outputPath)
        {
            byte[] exeBytes = File.ReadAllBytes(setupExePath);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(configJson);

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(exeBytes);
                bw.Write(compressedData);
                bw.Write(jsonBytes);
                bw.Write(jsonBytes.Length);          // 4 bytes
                bw.Write((long)compressedData.Length); // 8 bytes
                bw.Write(Magic);                     // 8 bytes
            }
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
        /// 从 EXE 中读取压缩数据
        /// </summary>
        /// <param name="exePath"></param>
        /// <returns></returns>
        public static byte[] ReadData(string exePath)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                fs.Seek(-(Magic.Length + 8 + 4), SeekOrigin.End);
                int jsonLen = br.ReadInt32();
                long dataLen = br.ReadInt64();

                fs.Seek(-(Magic.Length + 8 + 4 + jsonLen + dataLen), SeekOrigin.End);
                return br.ReadBytes((int)dataLen);
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
