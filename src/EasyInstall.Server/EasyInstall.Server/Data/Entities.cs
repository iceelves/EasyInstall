using System.Text.Json.Serialization;

namespace EasyInstall.Server.Data
{
    /// <summary>
    /// 一个被更新服务管理的软件（服务），对应一个唯一标识 AppKey。
    /// </summary>
    public class AppEntity
    {
        public int Id { get; set; }

        /// <summary>
        /// 唯一标识符，客户端接口通过它定位软件，如 "CloudMusic"、"com.company.app"。
        /// </summary>
        public string AppKey { get; set; } = "";

        /// <summary>
        /// 显示名称。
        /// </summary>
        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public List<PackageEntity> Packages { get; set; } = new();
    }

    /// <summary>
    /// 管理后台登录账号。首次运行时通过设置密码界面创建，默认用户名 admin。
    /// </summary>
    public class UserEntity
    {
        public int Id { get; set; }

        public string Username { get; set; } = "";

        /// <summary>
        /// PBKDF2 密码哈希（见 Services/PasswordHasher），不存明文。
        /// </summary>
        public string PasswordHash { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 某个软件的一个安装包版本。
    /// </summary>
    public class PackageEntity
    {
        public int Id { get; set; }

        public int AppId { get; set; }

        [JsonIgnore]
        public AppEntity? App { get; set; }

        /// <summary>
        /// 版本号，1~4 段数字，如 "1.0.0.0"。
        /// </summary>
        public string Version { get; set; } = "";

        /// <summary>
        /// 上传时的原始文件名（下载时回显给客户端）。
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>
        /// 相对 PackageDirectory 的存储路径（{appKey}/{version}/{fileName}）。
        /// </summary>
        public string StoredPath { get; set; } = "";

        public long FileSize { get; set; }

        public string Sha256 { get; set; } = "";

        /// <summary>
        /// 更新日志。
        /// </summary>
        public string? ChangeLog { get; set; }

        public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    }
}
