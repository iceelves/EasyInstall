namespace EasyInstall.Server.Services
{
    /// <summary>
    /// 存储位置配置。DataDirectory 在启动时被解析为绝对路径，
    /// SQLite 数据库与安装包文件都存放在其下。
    /// </summary>
    public class StoragePaths
    {
        /// <summary>
        /// 数据根目录，来自 appsettings.json 的 Storage:DataDirectory，默认 "App_Data"。
        /// </summary>
        public string DataDirectory { get; set; } = "App_Data";

        public string DatabaseFile => Path.Combine(DataDirectory, "easyinstall.db");

        public string PackageDirectory => Path.Combine(DataDirectory, "packages");

        /// <summary>
        /// 安装包在磁盘上的绝对路径。storedPath 为库中保存的相对路径。
        /// </summary>
        public string Resolve(string storedPath)
        {
            var root = Path.GetFullPath(PackageDirectory);
            var full = Path.GetFullPath(Path.Combine(root, storedPath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"非法的存储路径: {storedPath}");
            return full;
        }
    }
}
