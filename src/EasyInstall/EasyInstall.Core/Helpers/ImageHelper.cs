using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
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
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
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

        /// <summary>
        /// 从 EasyInstall.Core 程序集的内嵌 WPF 资源中读取指定图片并转为 ICO 字节。
        /// resourcePath 为资源包内的路径，如 "images/install.png"（不区分大小写）。
        /// 读取失败时返回 null。
        /// </summary>
        public static byte[] GetEmbeddedIco(string resourcePath)
        {
            try
            {
                // WPF Resource 统一打包在 <AssemblyName>.g.resources 中
                var assembly = typeof(ImageHelper).Assembly;
                string resourcesName = assembly.GetName().Name + ".g.resources";

                using (var stream = assembly.GetManifestResourceStream(resourcesName))
                {
                    if (stream == null) return null;

                    using (var reader = new ResourceReader(stream))
                    {
                        foreach (System.Collections.DictionaryEntry entry in reader)
                        {
                            string key = entry.Key as string ?? "";
                            if (!key.Equals(resourcePath, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (entry.Value is Stream s)
                            {
                                using (var ms = new MemoryStream())
                                {
                                    s.CopyTo(ms);
                                    return PngToIco(ms.ToArray());
                                }
                            }
                            if (entry.Value is byte[] b)
                                return PngToIco(b);
                        }
                    }
                }
            }
            catch { /* 读取失败不阻断调用方流程 */ }

            return null;
        }

        /// <summary>
        /// 获取安装程序默认图标（内嵌 Install.png → ICO）
        /// </summary>
        public static byte[] GetDefaultInstallIco()
            => GetEmbeddedIco("images/install.png");

        /// <summary>
        /// 获取卸载程序默认图标（内嵌 Uninstall.png → ICO）
        /// </summary>
        public static byte[] GetDefaultUninstallIco()
            => GetEmbeddedIco("images/uninstall.png");
    }
}
