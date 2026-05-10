using System.Runtime.Serialization;

namespace EasyInstall.Core.Compression
{
    /// <summary>
    /// 压缩算法类型，写入数据头第 1 字节
    /// </summary>
    public enum CompressionType : byte
    {
        GZip    = 0,
        Deflate = 1,
        Store   = 2,
        Lzma    = 3,
    }
}
