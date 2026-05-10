using System;
using System.IO;
using System.IO.Compression;

namespace EasyInstall.Core.Compression
{
    public class DeflateCompressor : ICompressor
    {
        public void Compress(Stream input, Stream output, Action<int> progress = null)
        {
            long total = input.CanSeek ? input.Length : -1;
            using (var df = new DeflateStream(output, CompressionMode.Compress, leaveOpen: true))
            {
                CopyWithProgress(input, df, total, progress);
            }
        }

        public void Decompress(Stream input, Stream output, long uncompressedSize, Action<int> progress = null)
        {
            using (var df = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true))
            {
                CopyWithProgress(df, output, uncompressedSize, progress);
            }
        }

        private static void CopyWithProgress(Stream src, Stream dst, long total, Action<int> progress)
        {
            var buf = new byte[81920];
            long written = 0;
            int read;
            while ((read = src.Read(buf, 0, buf.Length)) > 0)
            {
                dst.Write(buf, 0, read);
                written += read;
                if (progress != null && total > 0)
                    progress((int)(written * 100 / total));
            }
            progress?.Invoke(100);
        }
    }
}
