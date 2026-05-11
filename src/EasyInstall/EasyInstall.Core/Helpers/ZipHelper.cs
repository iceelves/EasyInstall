using EasyInstall.Core.Compression;
using EasyInstall.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyInstall.Core.Helpers
{
    /// <summary>
    /// 文件打包/解包工具
    /// 数据格式：[1字节 CompressionType] [4字节条目数] ( [4字节路径长度][路径UTF8] [8字节文件大小][文件数据] ) * N
    /// 整体用指定算法压缩
    /// </summary>
    public static class ZipHelper
    {
        public static event Action<int> ProgressChanged;

        private class FileEntry
        {
            public string RelPath { get; set; }
            public string AbsPath { get; set; }
        }

        /// <summary>
        /// 将多个源路径流式压缩并直接写入目标流，全程不在内存中缓冲整个数据集。
        /// 对于 Store（无压缩）模式，数据从磁盘直接流向目标流，内存占用恒定（约 80KB 缓冲区）。
        /// 对于 LZMA/GZip/Deflate 等压缩算法，原始数据通过管道流送入压缩器，
        /// 压缩器边读边压缩边写出，避免同时在内存中保留原始数据和压缩结果。
        /// </summary>
        public static void CompressPathsToStream(List<PackageFile> files, string baseDir,
            Stream outputStream, CompressionType compressionType = CompressionType.Lzma)
        {
            var entries = CollectEntries(files, baseDir);
            int count = entries.Count;

            // 写 1 字节压缩类型头
            outputStream.WriteByte((byte)compressionType);

            var compressor = CompressorFactory.Create(compressionType);

            // 用 EntryFeedStream 作为压缩器的输入源：
            // 它实现 Stream.Read，内部按格式逐条喂出文件数据，
            // 每次只在内存中保留一个 80KB 的读取缓冲区。
            using (var feeder = new EntryFeedStream(entries, pct =>
                ProgressChanged?.Invoke(pct)))
            {
                compressor.Compress(feeder, outputStream, pct =>
                {
                    // 压缩进度作为补充（部分压缩器会报告）
                });
            }

            ProgressChanged?.Invoke(100);
        }

        /// <summary>
        /// 将多个源路径压缩为字节数组（兼容旧接口，内部调用流式版本）。
        /// 注意：对于超大文件集合，建议直接使用 CompressPathsToStream 以避免内存压力。
        /// </summary>
        public static byte[] CompressPaths(List<PackageFile> files, string baseDir,
            CompressionType compressionType = CompressionType.Lzma)
        {
            using (var outMs = new MemoryStream())
            {
                CompressPathsToStream(files, baseDir, outMs, compressionType);
                return outMs.ToArray();
            }
        }

        /// <summary>
        /// 流式条目喂入器：实现 Stream.Read，按格式逐条输出文件头和文件内容，
        /// 每次只在内存中保留一个读取缓冲区，不预先加载任何文件。
        /// 格式：[4字节条目数] ( [4字节路径长度][路径UTF8] [8字节文件大小][文件数据] ) * N
        /// 支持 Length 属性（预先计算总字节数），以便 LZMA 等需要知道输入大小的压缩器正常工作。
        /// </summary>
        private sealed class EntryFeedStream : Stream
        {
            private enum State { WriteHeader, WritePathLen, WritePath, WriteFileSize, WriteFileData, Done }

            private readonly List<FileEntry> _entries;
            private readonly Action<int> _progress;
            private State _state = State.WriteHeader;

            // 当前条目索引
            private int _entryIndex = -1;

            // 小字段缓冲（头部字节）
            private byte[] _headerBuf;
            private int _headerPos;

            // 当前文件流
            private FileStream _currentFile;
            private long _fileRemaining;
            // 当前条目文件大小（WriteFileSize 完成后赋值，避免重新读 _headerBuf）
            private long _currentFileSize;

            // 预计算的总字节数（供 LZMA 等压缩器使用）
            private readonly long _totalLength;

            // 读取缓冲区（复用，避免每次分配）
            private readonly byte[] _copyBuf = new byte[81920];

            public EntryFeedStream(List<FileEntry> entries, Action<int> progress)
            {
                _entries = entries;
                _progress = progress;
                // 初始化：准备写入条目总数（4字节）
                _headerBuf = BitConverter.GetBytes(entries.Count);
                _headerPos = 0;
                // 预计算总字节数：4（条目数）+ 每条目（4+路径字节数+8+文件大小）
                _totalLength = CalculateTotalLength(entries);
            }

            /// <summary>
            /// 预计算流的总字节数，不读取文件内容，只查询文件大小。
            /// </summary>
            private static long CalculateTotalLength(List<FileEntry> entries)
            {
                long total = 4; // 4字节条目数
                foreach (var e in entries)
                {
                    byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(e.RelPath);
                    long fileSize = new FileInfo(e.AbsPath).Length;
                    total += 4;             // 路径长度字段
                    total += pathBytes.Length; // 路径内容
                    total += 8;             // 文件大小字段
                    total += fileSize;      // 文件内容
                }
                return total;
            }

            // CanSeek = true，Length 返回预计算值，供 LZMA 编码器使用
            public override bool CanRead  => true;
            public override bool CanSeek  => true;
            public override bool CanWrite => false;
            public override long Length   => _totalLength;
            // Position 只读（不支持随机定位，但 LZMA 只读 Length 不调用 Seek）
            private long _position;
            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException("EntryFeedStream does not support seeking.");
            }
            public override void Flush() { }
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin)        => throw new NotSupportedException("EntryFeedStream does not support seeking.");
            public override void SetLength(long value)                       => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_state == State.Done) return 0;

                int totalRead = 0;
                while (totalRead < count && _state != State.Done)
                {
                    int read = ReadChunk(buffer, offset + totalRead, count - totalRead);
                    if (read == 0 && _state != State.Done) break;
                    totalRead += read;
                }
                _position += totalRead;
                return totalRead;
            }

            private int ReadChunk(byte[] buf, int offset, int count)
            {
                switch (_state)
                {
                    case State.WriteHeader:
                    {
                        int take = Math.Min(count, _headerBuf.Length - _headerPos);
                        Array.Copy(_headerBuf, _headerPos, buf, offset, take);
                        _headerPos += take;
                        if (_headerPos == _headerBuf.Length)
                            AdvanceToNextEntry();
                        return take;
                    }
                    case State.WritePathLen:
                    {
                        int take = Math.Min(count, _headerBuf.Length - _headerPos);
                        Array.Copy(_headerBuf, _headerPos, buf, offset, take);
                        _headerPos += take;
                        if (_headerPos == _headerBuf.Length)
                        {
                            // 准备写路径字节
                            _headerBuf = System.Text.Encoding.UTF8.GetBytes(_entries[_entryIndex].RelPath);
                            _headerPos = 0;
                            _state = State.WritePath;
                        }
                        return take;
                    }
                    case State.WritePath:
                    {
                        int take = Math.Min(count, _headerBuf.Length - _headerPos);
                        Array.Copy(_headerBuf, _headerPos, buf, offset, take);
                        _headerPos += take;
                        if (_headerPos == _headerBuf.Length)
                        {
                            // 准备写文件大小（8字节）
                            _currentFileSize = new FileInfo(_entries[_entryIndex].AbsPath).Length;
                            _headerBuf = BitConverter.GetBytes(_currentFileSize);
                            _headerPos = 0;
                            _state = State.WriteFileSize;
                        }
                        return take;
                    }
                    case State.WriteFileSize:
                    {
                        int take = Math.Min(count, _headerBuf.Length - _headerPos);
                        Array.Copy(_headerBuf, _headerPos, buf, offset, take);
                        _headerPos += take;
                        if (_headerPos == _headerBuf.Length)
                        {
                            // 使用已保存的 _currentFileSize，不从 _headerBuf 重新解析
                            _fileRemaining = _currentFileSize;
                            if (_currentFileSize > 0)
                            {
                                _currentFile = new FileStream(
                                    _entries[_entryIndex].AbsPath,
                                    FileMode.Open, FileAccess.Read, FileShare.Read,
                                    81920, FileOptions.SequentialScan);
                                _state = State.WriteFileData;
                            }
                            else
                            {
                                // 空文件，直接进入下一条目
                                ReportProgress();
                                AdvanceToNextEntry();
                            }
                        }
                        return take;
                    }
                    case State.WriteFileData:
                    {
                        // 从文件流读取，最多读 count 字节，但不超过剩余文件大小
                        int toRead = (int)Math.Min(count, _fileRemaining);
                        int read = _currentFile.Read(buf, offset, toRead);
                        if (read > 0)
                        {
                            _fileRemaining -= read;
                            if (_fileRemaining == 0)
                            {
                                _currentFile.Dispose();
                                _currentFile = null;
                                ReportProgress();
                                AdvanceToNextEntry();
                            }
                        }
                        return read;
                    }
                    default:
                        return 0;
                }
            }

            private void ReportProgress()
            {
                int done = _entryIndex + 1;
                int total = _entries.Count;
                if (total > 0)
                    _progress?.Invoke(done * 100 / total);
            }

            private void AdvanceToNextEntry()
            {
                _entryIndex++;
                if (_entryIndex >= _entries.Count)
                {
                    _state = State.Done;
                    return;
                }
                // 准备写路径长度（4字节）
                byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(_entries[_entryIndex].RelPath);
                _headerBuf = BitConverter.GetBytes(pathBytes.Length);
                _headerPos = 0;
                _state = State.WritePathLen;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _currentFile?.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// 解压字节数组到目标目录。
        /// 解压流直接接到文件写入，不在内存中缓冲整个解压结果，避免大包 OOM。
        /// </summary>
        public static void Decompress(byte[] data, string targetDir, Action<int> progress = null)
        {
            using (var inMs = new MemoryStream(data))
            {
                // 读 1 字节类型头
                int typeByte = inMs.ReadByte();
                if (typeByte < 0) throw new InvalidDataException("Empty compressed data.");
                var compressionType = (CompressionType)(byte)typeByte;

                // 用 EntryDispatchStream 作为解压目标：
                // 它实现 Stream.Write，内部维护一个状态机，
                // 按照 [4字节路径长度][路径][8字节文件大小][文件数据] 的格式
                // 边接收字节边直接写到对应的目标文件，全程无大块内存缓冲。
                using (var dispatcher = new EntryDispatchStream(targetDir, progress))
                {
                    var compressor = CompressorFactory.Create(compressionType);
                    // uncompressedSize 传 -1，进度由 dispatcher 内部按文件数量汇报
                    compressor.Decompress(inMs, dispatcher, -1, null);
                }
            }
            progress?.Invoke(100);
        }

        /// <summary>
        /// 流式条目分发器：接收解压后的原始字节流，
        /// 按格式解析并直接写入目标文件，不在内存中积累整个解压结果。
        /// </summary>
        private sealed class EntryDispatchStream : Stream
        {
            // ── 状态机 ────────────────────────────────────────────
            private enum State { ReadEntryCount, ReadPathLen, ReadPath, ReadFileSize, WriteFile }

            private State _state = State.ReadEntryCount;
            private readonly string _targetDir;
            private readonly Action<int> _progress;

            // 小字段缓冲（读取定长头部用）
            private readonly byte[] _headerBuf = new byte[8];
            private int _headerNeeded;
            private int _headerFilled;

            // 条目元数据
            private int _totalEntries;
            private int _doneEntries;
            private int _pathLen;
            private byte[] _pathBuf;
            private int _pathFilled;
            private long _fileSize;
            private long _fileRemaining;
            private FileStream _currentFile;

            public EntryDispatchStream(string targetDir, Action<int> progress)
            {
                _targetDir = targetDir;
                _progress = progress;
                // 初始：读 4 字节条目数
                _headerNeeded = 4;
                _headerFilled = 0;
            }

            public override bool CanRead  => false;
            public override bool CanSeek  => false;
            public override bool CanWrite => true;
            public override long Length   => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { _currentFile?.Flush(); }
            public override int  Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin)       => throw new NotSupportedException();
            public override void SetLength(long value)                      => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                int end = offset + count;
                int pos = offset;
                while (pos < end)
                    pos += ProcessBytes(buffer, pos, end - pos);
            }

            private int ProcessBytes(byte[] buf, int offset, int available)
            {
                switch (_state)
                {
                    case State.ReadEntryCount:
                    {
                        int need = _headerNeeded - _headerFilled;
                        int take = Math.Min(need, available);
                        Array.Copy(buf, offset, _headerBuf, _headerFilled, take);
                        _headerFilled += take;
                        if (_headerFilled == _headerNeeded)
                        {
                            _totalEntries = BitConverter.ToInt32(_headerBuf, 0);
                            _doneEntries  = 0;
                            _state        = State.ReadPathLen;
                            _headerNeeded = 4;
                            _headerFilled = 0;
                        }
                        return take;
                    }
                    case State.ReadPathLen:
                    {
                        int need = _headerNeeded - _headerFilled;
                        int take = Math.Min(need, available);
                        Array.Copy(buf, offset, _headerBuf, _headerFilled, take);
                        _headerFilled += take;
                        if (_headerFilled == _headerNeeded)
                        {
                            _pathLen   = BitConverter.ToInt32(_headerBuf, 0);
                            _pathBuf   = new byte[_pathLen];
                            _pathFilled = 0;
                            _state     = State.ReadPath;
                        }
                        return take;
                    }
                    case State.ReadPath:
                    {
                        int need = _pathLen - _pathFilled;
                        int take = Math.Min(need, available);
                        Array.Copy(buf, offset, _pathBuf, _pathFilled, take);
                        _pathFilled += take;
                        if (_pathFilled == _pathLen)
                        {
                            _state        = State.ReadFileSize;
                            _headerNeeded = 8;
                            _headerFilled = 0;
                        }
                        return take;
                    }
                    case State.ReadFileSize:
                    {
                        int need = _headerNeeded - _headerFilled;
                        int take = Math.Min(need, available);
                        Array.Copy(buf, offset, _headerBuf, _headerFilled, take);
                        _headerFilled += take;
                        if (_headerFilled == _headerNeeded)
                        {
                            _fileSize      = BitConverter.ToInt64(_headerBuf, 0);
                            _fileRemaining = _fileSize;
                            // 打开目标文件
                            string relPath = System.Text.Encoding.UTF8.GetString(_pathBuf);
                            string dest    = Path.Combine(_targetDir, relPath);
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            _currentFile = new FileStream(dest, FileMode.Create, FileAccess.Write,
                                                          FileShare.None, 65536);
                            _state = State.WriteFile;
                        }
                        return take;
                    }
                    case State.WriteFile:
                    {
                        int take = (int)Math.Min(available, _fileRemaining);
                        _currentFile.Write(buf, offset, take);
                        _fileRemaining -= take;
                        if (_fileRemaining == 0)
                        {
                            _currentFile.Dispose();
                            _currentFile = null;
                            _doneEntries++;
                            if (_totalEntries > 0)
                                _progress?.Invoke(_doneEntries * 100 / _totalEntries);
                            // 下一条目
                            _state        = State.ReadPathLen;
                            _headerNeeded = 4;
                            _headerFilled = 0;
                        }
                        return take;
                    }
                    default:
                        return available; // 不应到达
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _currentFile?.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// 读取压缩包中所有文件的原始大小之和（不解压文件数据，流式扫描）
        /// </summary>
        public static long GetUncompressedSize(byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            try
            {
                using (var inMs = new MemoryStream(data))
                {
                    int typeByte = inMs.ReadByte();
                    if (typeByte < 0) return 0;
                    var compressionType = (CompressionType)(byte)typeByte;

                    // 用 SizeCountStream 流式扫描，不把整个解压结果放入内存
                    using (var counter = new SizeCountStream())
                    {
                        var compressor = CompressorFactory.Create(compressionType);
                        compressor.Decompress(inMs, counter, -1, null);
                        return counter.TotalUncompressedSize;
                    }
                }
            }
            catch { return 0; }
        }

        /// <summary>
        /// 流式扫描解压数据，只统计文件总大小，不写磁盘也不缓冲文件内容。
        /// </summary>
        private sealed class SizeCountStream : Stream
        {
            private enum State { ReadEntryCount, ReadPathLen, ReadPath, ReadFileSize, SkipFile }

            private State _state = State.ReadEntryCount;
            private readonly byte[] _headerBuf = new byte[8];
            private int _headerNeeded = 4, _headerFilled;
            private int _pathLen, _pathFilled;
            private long _fileRemaining;
            public long TotalUncompressedSize { get; private set; }

            public override bool CanRead  => false;
            public override bool CanSeek  => false;
            public override bool CanWrite => true;
            public override long Length   => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int  Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin)       => throw new NotSupportedException();
            public override void SetLength(long value)                      => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                int end = offset + count;
                int pos = offset;
                while (pos < end)
                    pos += ProcessBytes(buffer, pos, end - pos);
            }

            private int ProcessBytes(byte[] buf, int offset, int available)
            {
                switch (_state)
                {
                    case State.ReadEntryCount:
                    {
                        int take = Math.Min(_headerNeeded - _headerFilled, available);
                        Array.Copy(buf, offset, _headerBuf, _headerFilled, take);
                        _headerFilled += take;
                        if (_headerFilled == _headerNeeded)
                        { _state = State.ReadPathLen; _headerNeeded = 4; _headerFilled = 0; }
                        return take;
                    }
                    case State.ReadPathLen:
                    {
                        int take = Math.Min(_headerNeeded - _headerFilled, available);
                        Array.Copy(buf, offset, _headerBuf, _headerFilled, take);
                        _headerFilled += take;
                        if (_headerFilled == _headerNeeded)
                        { _pathLen = BitConverter.ToInt32(_headerBuf, 0); _pathFilled = 0; _state = State.ReadPath; }
                        return take;
                    }
                    case State.ReadPath:
                    {
                        int take = Math.Min(_pathLen - _pathFilled, available);
                        _pathFilled += take; // 只跳过，不存储
                        if (_pathFilled == _pathLen)
                        { _state = State.ReadFileSize; _headerNeeded = 8; _headerFilled = 0; }
                        return take;
                    }
                    case State.ReadFileSize:
                    {
                        int take = Math.Min(_headerNeeded - _headerFilled, available);
                        Array.Copy(buf, offset, _headerBuf, _headerFilled, take);
                        _headerFilled += take;
                        if (_headerFilled == _headerNeeded)
                        {
                            _fileRemaining = BitConverter.ToInt64(_headerBuf, 0);
                            TotalUncompressedSize += _fileRemaining;
                            _state = State.SkipFile;
                        }
                        return take;
                    }
                    case State.SkipFile:
                    {
                        int take = (int)Math.Min(available, _fileRemaining);
                        _fileRemaining -= take;
                        if (_fileRemaining == 0)
                        { _state = State.ReadPathLen; _headerNeeded = 4; _headerFilled = 0; }
                        return take;
                    }
                    default: return available;
                }
            }
        }

        // ── 内部辅助 ──────────────────────────────────────────────

        private static List<FileEntry> CollectEntries(List<PackageFile> files, string baseDir)
        {
            var entries = new List<FileEntry>();
            foreach (var pf in files)
            {
                string src = Path.IsPathRooted(pf.Source)
                    ? pf.Source
                    : Path.Combine(baseDir, pf.Source);

                string rootPrefix = string.IsNullOrEmpty(pf.TreeRootPath)
                    ? ""
                    : Path.GetFileName(pf.TreeRootPath.TrimEnd('\\', '/'));

                if (Directory.Exists(src))
                {
                    foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(src.TrimEnd('\\', '/').Length + 1);
                        entries.Add(new FileEntry { RelPath = rel, AbsPath = f });
                    }
                }
                else if (File.Exists(src))
                {
                    string subPath = Path.GetFileName(src);
                    if (!string.IsNullOrEmpty(pf.TreeRootPath))
                    {
                        string fileDir = Path.GetDirectoryName(src) ?? "";
                        string rootPath = pf.TreeRootPath.TrimEnd('\\', '/');
                        if (fileDir.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
                            && fileDir.Length > rootPath.Length)
                        {
                            string sub = fileDir.Substring(rootPath.Length).TrimStart('\\', '/');
                            subPath = Path.Combine(sub, Path.GetFileName(src));
                        }
                        subPath = Path.Combine(rootPrefix, subPath);
                    }
                    entries.Add(new FileEntry { RelPath = subPath, AbsPath = src });
                }
            }
            return entries;
        }
    }
}
