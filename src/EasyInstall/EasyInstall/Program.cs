using EasyInstall.Core.Helpers;
using EasyInstall.Core.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EasyInstall
{
    /// <summary>
    /// EasyInstall 命令行打包工具
    /// 用法：EasyInstall [选项] &lt;config.json&gt; [选项]
    /// </summary>
    internal class Program
    {
        // ── 版本信息 ──────────────────────────────────────────────
        private const string AppName = "EasyInstall";
        private const string AppVersion = "1.0.0";
        private const string AppDesc = "A command-line packaging tool that builds installers from JSON config files";

        // ── 退出码 ────────────────────────────────────────────────
        private const int ExitOk = 0;
        private const int ExitBadArgs = 1;
        private const int ExitConfigError = 2;
        private const int ExitBuildError = 3;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // 无参数时显示帮助
            if (args.Length == 0)
            {
                PrintHelp();
                return ExitOk;
            }

            // ── 解析参数 ──────────────────────────────────────────
            var parsed = ParseArgs(args);
            if (parsed == null)
                return ExitBadArgs;

            // 处理纯信息类参数（--help / --version），解析后立即响应
            if (parsed.ShowHelp) { PrintHelp(); return ExitOk; }
            if (parsed.ShowVersion) { PrintVersion(); return ExitOk; }

            // ── 校验必要参数 ──────────────────────────────────────
            if (string.IsNullOrEmpty(parsed.ConfigFile))
            {
                PrintError("Missing config file argument.");
                Console.WriteLine();
                PrintUsage();
                return ExitBadArgs;
            }

            if (!File.Exists(parsed.ConfigFile))
            {
                PrintError($"Config file not found: {parsed.ConfigFile}");
                return ExitConfigError;
            }

            // ── 确定 Setup.exe 路径 ───────────────────────────────
            string setupExe = parsed.SetupExe;
            if (string.IsNullOrEmpty(setupExe))
                setupExe = AutoDetectSetupExe();

            if (string.IsNullOrEmpty(setupExe) || !File.Exists(setupExe))
            {
                PrintError("Setup.exe not found. Specify its path with --setup.");
                Console.WriteLine("  Example: EasyInstall app.json --setup EasyInstall.Setup.exe");
                return ExitBadArgs;
            }

            // ── 读取并解析配置 ────────────────────────────────────
            InstallConfig config;
            try
            {
                string json = File.ReadAllText(parsed.ConfigFile, Encoding.UTF8);
                config = JsonConvert.DeserializeObject<InstallConfig>(json);
                if (config == null)
                    throw new InvalidOperationException("JSON deserialization returned null.");
            }
            catch (Exception ex)
            {
                PrintError($"Failed to read config file: {ex.Message}");
                return ExitConfigError;
            }

            // ── 确定输出路径 ──────────────────────────────────────
            string outputPath = parsed.OutputFile;
            if (string.IsNullOrEmpty(outputPath))
            {
                // 默认：{AppName}_{AppVersion}_{yyyyMMddHHmmss}_install.exe
                // AppName / AppVersion 取自配置，非法文件名字符替换为 _
                string appName = SanitizeFileName(config.AppName ?? "App");
                string appVer = SanitizeFileName(config.AppVersion ?? "1.0");
                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string outDir = Path.GetDirectoryName(Path.GetFullPath(parsed.ConfigFile));
                outputPath = Path.Combine(outDir, $"{appName}_{appVer}_{timestamp}_install.exe");
            }
            // ── 打印构建摘要 ──────────────────────────────────────
            PrintBanner();
            Console.WriteLine($"  Config    : {Path.GetFullPath(parsed.ConfigFile)}");
            Console.WriteLine($"  Setup.exe : {setupExe}");
            Console.WriteLine($"  Output    : {outputPath}");
            Console.WriteLine($"  App name  : {config.AppName}");
            Console.WriteLine($"  Version   : {config.AppVersion}");
            Console.WriteLine($"  Files     : {config.Files?.Count ?? 0}");
            Console.WriteLine();

            // ── 执行打包 ──────────────────────────────────────────
            return Build(config, parsed.ConfigFile, setupExe, outputPath, parsed.Verbose);
        }

        /// <summary>
        /// 打包核心逻辑
        /// </summary>
        /// <param name="config"></param>
        /// <param name="configFilePath"></param>
        /// <param name="setupExe"></param>
        /// <param name="outputPath"></param>
        /// <param name="verbose"></param>
        /// <returns></returns>
        private static int Build(InstallConfig config, string configFilePath, string setupExe, string outputPath, bool verbose)
        {
            try
            {
                // 确保输出目录存在
                string outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                // ── 校验文件列表 ──────────────────────────────────
                if (config.Files == null || config.Files.Count == 0)
                    throw new InvalidOperationException("The Files list in the config is empty — nothing to package.");

                // 配置文件所在目录作为相对路径基准
                string baseDir = Path.GetDirectoryName(Path.GetFullPath(configFilePath));

                // 检查所有源文件是否存在（支持相对路径）
                var missing = new List<string>();
                foreach (var pf in config.Files)
                {
                    string src = Path.IsPathRooted(pf.Source)
                        ? pf.Source
                        : Path.Combine(baseDir, pf.Source);
                    if (!File.Exists(src))
                        missing.Add(src);
                }
                if (missing.Count > 0)
                {
                    PrintError("The following source files do not exist:");
                    foreach (var m in missing.Take(10))
                        Console.WriteLine($"    {m}");
                    if (missing.Count > 10)
                        Console.WriteLine($"    ... and {missing.Count - 10} more");
                    return ExitBuildError;
                }

                // ── 阶段 1：复制 Setup.exe ────────────────────────
                PrintStep(1, "Copying Setup.exe...");
                File.Copy(setupExe, outputPath, overwrite: true);

                // ── 阶段 2：替换图标（必须在压缩追加之前）──────────
                byte[] icoBytes = null;
                if (!string.IsNullOrEmpty(config.InstallIconBase64))
                {
                    PrintStep(2, "Replacing installer icon...");
                    byte[] rawBytes = Convert.FromBase64String(config.InstallIconBase64);
                    icoBytes = ImageHelper.ToIcoBytes(rawBytes) ?? rawBytes;
                }
                else
                {
                    PrintStep(2, "Using default icon...");
                    icoBytes = ImageHelper.GetDefaultInstallIco();
                }
                if (icoBytes != null)
                    OverlayHelper.SetExeIcon(outputPath, icoBytes);

                // ── 阶段 3：计算解压大小 + 序列化 JSON ───────────────
                PrintStep(3, "Serializing config...");
                config.UncompressedSize = ZipHelper.CalculateUncompressedSize(config.Files, baseDir);
                string configJson = JsonConvert.SerializeObject(config, Formatting.Indented);
                if (verbose)
                    Console.WriteLine($"    JSON size: {FormatSize(Encoding.UTF8.GetByteCount(configJson))}");

                // ── 阶段 4：流式压缩直接追加到 EXE（进度 0-100%）────
                // 无临时文件，内存恒定 ~80KB，压缩完成后立即追加 JSON + 元数据
                PrintStep(4, "Compressing and appending data...");
                int lastPct = -1;
                Action<int> zipProgress = pct =>
                {
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        PrintProgress(pct);
                    }
                };
                ZipHelper.ProgressChanged += zipProgress;
                bool hasSplit;
                try
                {
                    hasSplit = OverlayHelper.AppendOverlayStreaming(
                        outputPath, config.Files, baseDir, config.CompressionMethod, configJson);
                }
                finally
                {
                    ZipHelper.ProgressChanged -= zipProgress;
                }
                Console.WriteLine(); // 进度条后换行

                if (verbose)
                    Console.WriteLine($"    Output size: {FormatSize(new FileInfo(outputPath).Length)}");

                // ── 完成 ──────────────────────────────────────────
                long outputSize = new FileInfo(outputPath).Length;
                Console.WriteLine();
                PrintSuccess("Build succeeded!");
                Console.WriteLine($"  Output : {outputPath}");
                Console.WriteLine($"  Size   : {FormatSize(outputSize)}");

                if (hasSplit)
                {
                    string eidatPath = OverlayHelper.GetEidatPath(outputPath);
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  ⚠ 安装包数据已拆分为外部文件：");
                    Console.ResetColor();
                    Console.WriteLine($"    Data : {eidatPath}");
                    Console.WriteLine($"    Size : {FormatSize(new FileInfo(eidatPath).Length)}");
                    Console.WriteLine("  分发时请将 .exe 与 .eidat 文件放在同一目录。");
                }

                Console.WriteLine();

                return ExitOk;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                PrintError($"Build failed: {ex.Message}");
                if (verbose)
                    Console.WriteLine(ex.StackTrace);
                return ExitBuildError;
            }
        }

        /// <summary>
        /// 参数解析
        /// </summary>
        private class ParsedArgs
        {
            public string ConfigFile { get; set; }
            public string OutputFile { get; set; }
            public string SetupExe { get; set; }
            public bool ShowHelp { get; set; }
            public bool ShowVersion { get; set; }
            public bool Verbose { get; set; }
        }

        /// <summary>
        /// 解析命令行参数，支持四种风格：/key value、--key value、-k value、/key:value
        /// </summary>
        private static ParsedArgs ParseArgs(string[] args)
        {
            var result = new ParsedArgs();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                // ── 帮助 ──────────────────────────────────────────
                if (IsFlag(arg, "help", "h", "?"))
                {
                    result.ShowHelp = true;
                    return result;
                }

                // ── 版本 ──────────────────────────────────────────
                if (IsFlag(arg, "version", "v", "ver"))
                {
                    result.ShowVersion = true;
                    return result;
                }

                // ── 详细输出 ──────────────────────────────────────
                if (IsFlag(arg, "verbose", "vv"))
                {
                    result.Verbose = true;
                    continue;
                }

                // ── 输出文件 ──────────────────────────────────────
                if (TryGetValue(args, ref i, arg, out string outVal,
                    "output", "out", "o"))
                {
                    result.OutputFile = outVal;
                    continue;
                }

                // ── Setup.exe 路径 ────────────────────────────────
                if (TryGetValue(args, ref i, arg, out string setupVal,
                    "setup", "s", "setupexe"))
                {
                    result.SetupExe = setupVal;
                    continue;
                }

                // ── 位置参数（配置文件）───────────────────────────
                if (!arg.StartsWith("-") && !arg.StartsWith("/"))
                {
                    if (string.IsNullOrEmpty(result.ConfigFile))
                    {
                        result.ConfigFile = arg;
                        continue;
                    }
                    // 多余的位置参数
                    PrintError($"Unknown argument: {arg}");
                    Console.WriteLine();
                    PrintUsage();
                    return null;
                }

                // ── 未知参数 ──────────────────────────────────────
                PrintError($"Unknown argument: {arg}");
                Console.WriteLine();
                PrintUsage();
                return null;
            }

            return result;
        }

        /// <summary>
        /// 判断参数是否匹配指定的标志名（支持 /name --name -name 三种前缀）
        /// </summary>
        private static bool IsFlag(string arg, params string[] names)
        {
            string stripped = StripPrefix(arg);
            if (stripped == null) return false;
            return names.Any(n => string.Equals(stripped, n, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 尝试从参数中提取键值对，支持：
        ///   /key value   --key value   -k value
        ///   /key:value   --key:value   /key=value   --key=value
        /// </summary>
        private static bool TryGetValue(
            string[] args, ref int i,
            string arg, out string value,
            params string[] names)
        {
            value = null;
            string stripped = StripPrefix(arg);
            if (stripped == null) return false;

            // 检查 key=value 或 key:value 内联形式
            int sep = stripped.IndexOfAny(new[] { '=', ':' });
            if (sep > 0)
            {
                string key = stripped.Substring(0, sep);
                if (names.Any(n => string.Equals(key, n, StringComparison.OrdinalIgnoreCase)))
                {
                    value = stripped.Substring(sep + 1);
                    return true;
                }
                return false;
            }

            // 检查 key 后跟下一个位置参数
            if (names.Any(n => string.Equals(stripped, n, StringComparison.OrdinalIgnoreCase)))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-") && !args[i + 1].StartsWith("/"))
                {
                    value = args[++i];
                    return true;
                }
                PrintError($"Option {arg} requires a value.");
                return false;
            }

            return false;
        }

        /// <summary>
        /// 去掉参数前缀（--、-、/），返回裸名称；不是参数则返回 null
        /// </summary>
        private static string StripPrefix(string arg)
        {
            if (arg.StartsWith("--")) return arg.Substring(2);
            if (arg.StartsWith("-")) return arg.Substring(1);
            if (arg.StartsWith("/")) return arg.Substring(1);
            return null;
        }

        /// <summary>
        /// 自动检测 Setup.exe
        /// </summary>
        /// <returns></returns>
        private static string AutoDetectSetupExe()
        {
            string selfDir = AppDomain.CurrentDomain.BaseDirectory;

            // 同目录查找
            string[] candidates =
            {
                Path.Combine(selfDir, "EasyInstall.Setup.exe"),
                Path.Combine(selfDir, "Setup.exe"),
            };
            foreach (string p in candidates)
                if (File.Exists(p)) return p;

            // 开发环境：向上找解决方案目录
            string solutionDir = FindSolutionDir(selfDir);
            if (solutionDir != null)
            {
                string[] devCandidates =
                {
                    Path.Combine(solutionDir, "EasyInstall.Setup", "bin", "Release", "EasyInstall.Setup.exe"),
                    Path.Combine(solutionDir, "EasyInstall.Setup", "bin", "Debug",   "EasyInstall.Setup.exe"),
                };
                foreach (string p in devCandidates)
                    if (File.Exists(p)) return p;
            }

            return null;
        }

        private static string FindSolutionDir(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (dir.GetFiles("*.sln").Length > 0) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════
        // 控制台输出辅助
        // ══════════════════════════════════════════════════════════

        private static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  {AppName} v{AppVersion}  —  {AppDesc}");
            Console.ResetColor();
            Console.WriteLine(new string('─', 60));
        }

        private static void PrintVersion()
        {
            Console.WriteLine($"{AppName} {AppVersion}");
            Console.WriteLine($".NET Framework {Environment.Version}");
        }

        private static void PrintHelp()
        {
            PrintVersion();
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  EasyInstall <config.json> [options]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  <config.json>              Path to the installer config file (required)");
            Console.WriteLine();
            Console.WriteLine("Options (each option accepts /opt, --opt, or -opt prefix;");
            Console.WriteLine("         values can be passed as --opt value or --opt=value):");
            Console.WriteLine();
            Console.WriteLine("  --output, --out, -o        Output installer path");
            Console.WriteLine("                             Default: <AppName>_<Version>_<yyyyMMddHHmmss>_install.exe");
            Console.WriteLine("  --setup, -s, --setupexe    Path to the Setup.exe template");
            Console.WriteLine("                             Default: auto-detected in the same directory");
            Console.WriteLine("  --verbose, --vv            Show detailed build output");
            Console.WriteLine("  --version, -v, --ver       Show version information");
            Console.WriteLine("  --help, -h, -?             Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  EasyInstall app.json");
            Console.WriteLine("  EasyInstall app.json --output myapp_install.exe");
            Console.WriteLine("  EasyInstall app.json /output myapp_install.exe");
            Console.WriteLine("  EasyInstall app.json -o myapp_install.exe");
            Console.WriteLine("  EasyInstall app.json --output=myapp_install.exe");
            Console.WriteLine("  EasyInstall app.json --setup EasyInstall.Setup.exe -o dist\\myapp.exe");
            Console.WriteLine("  EasyInstall app.json --verbose");
            Console.WriteLine();
            Console.WriteLine("Exit codes:");
            Console.WriteLine("  0  Success");
            Console.WriteLine("  1  Invalid arguments");
            Console.WriteLine("  2  Config file error");
            Console.WriteLine("  3  Build failed");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: EasyInstall <config.json> [--output <output.exe>] [--setup <Setup.exe>]");
            Console.WriteLine("       Run 'EasyInstall --help' for full usage information.");
        }

        private static void PrintStep(int step, string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"  [{step}/4] ");
            Console.ResetColor();
            Console.WriteLine(message);
        }

        private static void PrintProgress(int pct)
        {
            int width = 30;
            int filled = pct * width / 100;
            string bar = new string('█', filled) + new string('░', width - filled);
            Console.Write($"\r         [{bar}] {pct,3}%");
        }

        private static void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {message}");
            Console.ResetColor();
        }

        private static void PrintSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ {message}");
            Console.ResetColor();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        /// <summary>
        /// 将字符串中不合法的文件名字符替换为下划线，用于生成默认输出文件名
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }
    }
}
