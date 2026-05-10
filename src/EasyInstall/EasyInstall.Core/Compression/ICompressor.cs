using System;
using System.IO;

namespace EasyInstall.Core.Compression
{
    public interface ICompressor
    {
        /// <summary>压缩：将 input 流写入 output 流，progress 回调 0-100</summary>
        void Compress(Stream input, Stream output, Action<int> progress = null);
        /// <summary>解压：将 input 流解压写入 output 流，uncompressedSize 用于进度计算</summary>
        void Decompress(Stream input, Stream output, long uncompressedSize, Action<int> progress = null);
    }
}
