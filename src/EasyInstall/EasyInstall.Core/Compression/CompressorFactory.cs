namespace EasyInstall.Core.Compression
{
    public static class CompressorFactory
    {
        public static ICompressor Create(CompressionType type)
        {
            switch (type)
            {
                case CompressionType.Deflate: return new DeflateCompressor();
                case CompressionType.Store:   return new StoreCompressor();
                case CompressionType.Lzma:    return new LzmaCompressor();
                default:                      return new GZipCompressor();
            }
        }
    }
}
