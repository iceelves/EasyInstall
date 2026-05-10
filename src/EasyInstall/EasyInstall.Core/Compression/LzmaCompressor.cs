using System;
using System.IO;
using SevenZip.Compression.LZMA;

namespace EasyInstall.Core.Compression
{
    /// <summary>
    /// 基于 7-zip LZMA SDK 的压缩实现（public domain）
    /// 格式：[5字节 LZMA properties][8字节原始大小][LZMA压缩数据]
    /// </summary>
    public class LzmaCompressor : ICompressor
    {
        public void Compress(Stream input, Stream output, Action<int> progress = null)
        {
            long inSize = input.CanSeek ? input.Length : -1;

            var encoder = new Encoder();

            // 写入 5 字节 properties
            encoder.WriteCoderProperties(output);

            // 写入 8 字节原始大小（-1 表示未知）
            var sizeBytes = BitConverter.GetBytes(inSize);
            output.Write(sizeBytes, 0, 8);

            // 压缩，通过 ICodeProgress 回调进度
            long lastPct = -1;
            encoder.Code(input, output, inSize, -1, new LzmaProgress(inSize, pct =>
            {
                if (pct != lastPct) { lastPct = pct; progress?.Invoke((int)pct); }
            }));

            progress?.Invoke(100);
        }

        public void Decompress(Stream input, Stream output, long uncompressedSize, Action<int> progress = null)
        {
            // 读 5 字节 properties
            var props = new byte[5];
            if (input.Read(props, 0, 5) != 5)
                throw new InvalidDataException("Invalid LZMA stream: missing properties.");

            // 读 8 字节原始大小
            var sizeBytes = new byte[8];
            if (input.Read(sizeBytes, 0, 8) != 8)
                throw new InvalidDataException("Invalid LZMA stream: missing size.");
            long outSize = BitConverter.ToInt64(sizeBytes, 0);
            if (outSize < 0) outSize = uncompressedSize; // 兜底

            var decoder = new Decoder();
            decoder.SetDecoderProperties(props);

            // 用包装流跟踪写出字节数以报告进度
            // input 此时已读过 5+8=13 字节头，剩余全部是 LZMA 压缩数据
            // MemoryStream 可以用 Length-Position 得到剩余字节数，其他流传 -1 让 decoder 自行处理
            long remainingIn = input.CanSeek ? (input.Length - input.Position) : -1;
            var trackingOutput = new ProgressTrackingStream(output, outSize, progress);
            decoder.Code(input, trackingOutput, remainingIn, outSize, null);

            progress?.Invoke(100);
        }

        // ── 内部辅助类 ────────────────────────────────────────────

        private class LzmaProgress : SevenZip.ICodeProgress
        {
            private readonly long _total;
            private readonly Action<int> _callback;
            public LzmaProgress(long total, Action<int> callback) { _total = total; _callback = callback; }
            public void SetProgress(long inSize, long outSize)
            {
                if (_total > 0) _callback((int)(inSize * 100 / _total));
            }
        }

        private class ProgressTrackingStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _total;
            private readonly Action<int> _progress;
            private long _written;

            public ProgressTrackingStream(Stream inner, long total, Action<int> progress)
            { _inner = inner; _total = total; _progress = progress; }

            public override bool CanRead  => false;
            public override bool CanSeek  => false;
            public override bool CanWrite => true;
            public override long Length   => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush()  => _inner.Flush();
            public override int  Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin)       => throw new NotSupportedException();
            public override void SetLength(long value)                      => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                _inner.Write(buffer, offset, count);
                _written += count;
                if (_progress != null && _total > 0)
                    _progress((int)(_written * 100 / _total));
            }
        }
    }
}
