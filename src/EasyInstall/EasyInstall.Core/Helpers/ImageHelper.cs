using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyInstall.Core.Helpers
{
    /// <summary>
    /// 图片帮助类
    /// </summary>
    public class ImageHelper
    {
        /// <summary>
        /// 将 PNG 字节包装成最简单的单图 ICO 格式（用于内置 PNG 回退）
        /// </summary>
        public static byte[] PngToIco(byte[] pngBytes)
        {
            // ICO with one 256x256 PNG entry
            using (var ms = new System.IO.MemoryStream())
            using (var bw = new System.IO.BinaryWriter(ms))
            {
                // ICONDIR
                bw.Write((ushort)0);   // reserved
                bw.Write((ushort)1);   // type = icon
                bw.Write((ushort)1);   // count = 1

                // ICONDIRENTRY (16 bytes)
                bw.Write((byte)0);     // width  0 = 256
                bw.Write((byte)0);     // height 0 = 256
                bw.Write((byte)0);     // color count
                bw.Write((byte)0);     // reserved
                bw.Write((ushort)1);   // planes
                bw.Write((ushort)32);  // bit count
                bw.Write((uint)pngBytes.Length);
                bw.Write((uint)22);    // offset = 6 + 16

                bw.Write(pngBytes);
                return ms.ToArray();
            }
        }
    }
}
