using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace EasyInstall.Core.Helpers
{
    /// <summary>
    /// 图片帮助类
    /// </summary>
    public class ImageHelper
    {
        // ICO 文件魔数：前 4 字节为 00 00 01 00
        private static readonly byte[] IcoMagic = { 0x00, 0x00, 0x01, 0x00 };

        /// <summary>
        /// 将任意图片字节（ICO / PNG / JPG / GIF / BMP）转为合法的 ICO 字节。
        /// - 已经是 ICO：直接返回原始字节
        /// - PNG：用 PngToIco 包装成单图 ICO
        /// - JPG / GIF / BMP：先用 WPF BitmapImage 解码，再编码为 PNG，再包装成 ICO
        /// 转换失败时返回 null。
        /// </summary>
        public static byte[] ToIcoBytes(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length < 4) return null;

            // 已经是 ICO 格式，直接返回
            if (IsIco(imageBytes))
                return imageBytes;

            // PNG 格式（魔数 89 50 4E 47）
            if (IsPng(imageBytes))
                return PngToIco(imageBytes);

            // 其他格式（JPG / GIF / BMP 等）：通过 WPF 解码后重新编码为 PNG
            try
            {
                byte[] pngBytes = ConvertToPng(imageBytes);
                if (pngBytes != null)
                    return PngToIco(pngBytes);
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 判断字节是否为 ICO 格式（前 4 字节 = 00 00 01 00）
        /// </summary>
        private static bool IsIco(byte[] bytes)
            => bytes.Length >= 4
            && bytes[0] == 0x00 && bytes[1] == 0x00
            && bytes[2] == 0x01 && bytes[3] == 0x00;

        /// <summary>
        /// 判断字节是否为 PNG 格式（魔数 89 50 4E 47 0D 0A 1A 0A）
        /// </summary>
        private static bool IsPng(byte[] bytes)
            => bytes.Length >= 4
            && bytes[0] == 0x89 && bytes[1] == 0x50
            && bytes[2] == 0x4E && bytes[3] == 0x47;

        /// <summary>
        /// 使用 WPF BitmapImage 将任意图片格式解码后重新编码为 PNG 字节
        /// </summary>
        private static byte[] ConvertToPng(byte[] imageBytes)
        {
            using (var input = new MemoryStream(imageBytes))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = input;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));

                using (var output = new MemoryStream())
                {
                    encoder.Save(output);
                    return output.ToArray();
                }
            }
        }

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
