using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyInstall.Core.Helpers
{
    /// <summary>
    /// Overlay 打包：将压缩数据附加到 EXE 末尾
    /// 格式：[EXE原始内容] [压缩数据] [JSON配置UTF8] [4字节JSON长度] [8字节数据长度] [8字节魔数]
    /// </summary>
    public static class OverlayHelper
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("EASYINST");

        /// <summary>将 Setup.exe + 压缩数据 + JSON配置 合并为单个 EXE</summary>
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

        /// <summary>检测当前运行的 EXE 是否包含 Overlay 数据</summary>
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

        /// <summary>从 EXE 中读取 JSON 配置</summary>
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

        /// <summary>从 EXE 中读取压缩数据</summary>
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
