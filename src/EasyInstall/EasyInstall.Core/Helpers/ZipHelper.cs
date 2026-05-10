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
        /// 将多个源路径压缩为字节数组
        /// </summary>
        public static byte[] CompressPaths(List<PackageFile> files, string baseDir,
            CompressionType compressionType = CompressionType.Lzma)
        {
            var entries = CollectEntries(files, baseDir);

            // 先把原始数据序列化到内存流
            using (var rawMs = new MemoryStream())
            using (var bw = new BinaryWriter(rawMs))
            {
                bw.Write(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    byte[] data = File.ReadAllBytes(entries[i].AbsPath);
                    byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(entries[i].RelPath);
                    bw.Write(pathBytes.Length);
                    bw.Write(pathBytes);
                    bw.Write((long)data.Length);
                    bw.Write(data);
                    // 序列化阶段占总进度 0-50%
                    ProgressChanged?.Invoke((i + 1) * 50 / entries.Count);
                }
                bw.Flush();
                rawMs.Position = 0;

                // 压缩阶段占总进度 50-100%
                var compressor = CompressorFactory.Create(compressionType);
                using (var outMs = new MemoryStream())
                {
                    // 写 1 字节类型头
                    outMs.WriteByte((byte)compressionType);

                    compressor.Compress(rawMs, outMs, pct =>
                        ProgressChanged?.Invoke(50 + pct / 2));

                    ProgressChanged?.Invoke(100);
                    return outMs.ToArray();
                }
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
