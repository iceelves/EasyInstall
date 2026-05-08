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

        // ── 语言 ──────────────────────────────────────────────────
        /// <summary>
        /// 安装/卸载界面语言。
        /// null 或 "" = 跟随系统；"zh-CN" = 简体中文；"en-US" = 英文
        /// </summary>
        [DataMember]
        public string Language { get; set; }

        // ── 文件列表 ──────────────────────────────────────────────
        [DataMember]
        public List<PackageFile> Files { get; set; } = new List<PackageFile>();

        // ── 快捷方式 ──────────────────────────────────────────────
        /// <summary>
        /// 桌面快捷方式
        /// </summary>
        [DataMember]
        public bool DesktopShortcut { get; set; } = true;

        /// <summary>
        /// 开始菜单快捷方式
        /// </summary>
        [DataMember]
        public bool StartMenuShortcut { get; set; } = true;

        /// <summary>
        /// 开机自启
        /// </summary>
        [DataMember]
        public bool StartWithWindows { get; set; } = false;

        // ── 图标 ──────────────────────────────────────────────────
        /// <summary>
        /// 安装程序图标（ICO Base64），为空时使用内置 Install.png
        /// </summary>
        [DataMember]
        public string InstallIconBase64 { get; set; }

        /// <summary>
        /// 卸载程序图标（ICO Base64），为空时使用内置 Uninstall.png
        /// </summary>
        [DataMember]
        public string UninstallIconBase64 { get; set; }

        // ── 样式配置 ──────────────────────────────────────────────
        [DataMember]
        public StyleConfig Style { get; set; } = new StyleConfig();
    }

    /// <summary>
    /// 安装/卸载界面样式配置
    /// </summary>
    [DataContract]
    public class StyleConfig
    {
        /// <summary>
        /// 是否隐藏左上角 Logo 与标题栏文字
        /// </summary>
        [DataMember]
        public bool HideTitleBar { get; set; } = false;

        // ── 安装阶段 ──────────────────────────────────────────────
        /// <summary>
        /// 安装阶段背景图（Base64 PNG/JPG），为空时使用内置 Background.jpg
        /// </summary>
        [DataMember]
        public string InstallBackgroundBase64 { get; set; }

        /// <summary>
        /// 安装阶段轮播图列表（Base64 PNG/JPG），有值时在安装进行页替换动画效果
        /// </summary>
        [DataMember]
        public List<string> InstallCarouselImages { get; set; } = new List<string>();

        /// <summary>
        /// 安装阶段按钮主色（十六进制，如 #4083FD），为空时使用默认色
        /// </summary>
        [DataMember]
        public string InstallButtonColor { get; set; }

        // ── 卸载阶段 ──────────────────────────────────────────────
        /// <summary>
        /// 卸载阶段背景图（Base64 PNG/JPG），为空时使用内置 Background.jpg
        /// </summary>
        [DataMember]
        public string UninstallBackgroundBase64 { get; set; }

        /// <summary>
        /// 卸载阶段轮播图列表（Base64 PNG/JPG），有值时在卸载进行页替换动画效果
        /// </summary>
        [DataMember]
        public List<string> UninstallCarouselImages { get; set; } = new List<string>();

        /// <summary>
        /// 卸载阶段按钮主色（十六进制，如 #4083FD），为空时使用默认色
        /// </summary>
        [DataMember]
        public string UninstallButtonColor { get; set; }

        // ── 通用控件颜色 ──────────────────────────────────────────
        /// <summary>
        /// CheckBox 勾选框颜色（十六进制），为空时使用默认色 #69AFE7
        /// </summary>
        [DataMember]
        public string CheckBoxColor { get; set; }

        /// <summary>
        /// 进度条颜色（十六进制），为空时使用默认色 #1E90FF
        /// </summary>
        [DataMember]
        public string ProgressBarColor { get; set; }
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
        /// 该文件在 Builder 文件树中所属的根节点完整路径。
        /// 为空时表示该文件是直接添加的独立文件（无目录根节点）。
        /// </summary>
        [DataMember]
        public string TreeRootPath { get; set; } = "";
    }
}
