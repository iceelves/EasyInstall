namespace EasyInstall.Server.Services
{
    /// <summary>
    /// 面向客户端软件的安装包信息（版本信息 / 版本判断接口的返回体）。
    /// downloadUrl 为相对路径，客户端拼接服务基地址后直接下载。
    /// </summary>
    public record PackageInfo
    {
        public string AppKey { get; init; } = "";
        public string Version { get; init; } = "";
        public string FileName { get; init; } = "";
        public long FileSize { get; init; }
        public string Sha256 { get; init; } = "";
        public string? ChangeLog { get; init; }
        public DateTime PublishedAt { get; init; }
        public string DownloadUrl { get; init; } = "";
    }
}
