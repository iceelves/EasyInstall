using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace EasyInstall.Core.Models
{
    /// <summary>
    /// 安装包配置文件（JSON 根节点）
    /// </summary>
    [DataContract]
    public class InstallConfig
    {
        // ── 基本信息 ──────────────────────────────────────────────
        [DataMember]
        public string AppName { get; set; }
        [DataMember]
        public string AppVersion { get; set; }
        [DataMember]
        public string Company { get; set; }
        [DataMember]
        public string CompanySimplify { get; set; }
        [DataMember]
        public string Website { get; set; }

        // ── 安装行为 ──────────────────────────────────────────────
        /// <summary>
        /// 默认安装路径，支持 {ProgramFiles}、{LocalAppData}、{Company}、{AppName} 占位符
        /// </summary>
        [DataMember]
        public string DefaultInstallDir { get; set; } = @"{ProgramFiles}\{Company}\{AppName}";

        /// <summary>
        /// 注册表卸载键名
        /// </summary>
        [DataMember]
        public string RegistryKey { get; set; }

        /// <summary>
        /// 安装完成后可启动的主程序（相对安装目录）
        /// </summary>
        [DataMember]
        public string MainExecutable { get; set; }

        // ── 协议 ──────────────────────────────────────────────────
        [DataMember]
        public string LicenseText { get; set; }

        // ── 文件列表 ──────────────────────────────────────────────
        [DataMember]
        public List<PackageFile> Files { get; set; } = new List<PackageFile>();

        // ── 快捷方式 ──────────────────────────────────────────────
        [DataMember]
        public bool CreateDesktopShortcut { get; set; } = true;
        [DataMember]
        public bool CreateStartMenuShortcut { get; set; } = true;
        [DataMember]
        public bool CreateAutoRun { get; set; } = false;

        // ── 界面定制 ──────────────────────────────────────────────
        [DataMember]
        public string BannerColor { get; set; } = "#2C3E50";
        [DataMember]
        public string AccentColor { get; set; } = "#3498DB";
        [DataMember]
        public string LogoBase64 { get; set; }
    }

    [DataContract]
    public class PackageFile
    {
        /// <summary>
        /// 源路径（绝对路径或相对于配置文件的路径）
        /// </summary>
        [DataMember]
        public string Source { get; set; }

        /// <summary>
        /// 安装目标子目录（相对安装根目录），空表示根目录
        /// </summary>
        [DataMember]
        public string TargetDir { get; set; } = "";
    }
}
