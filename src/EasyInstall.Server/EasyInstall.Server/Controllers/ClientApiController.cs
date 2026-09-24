using EasyInstall.Server.Data;
using EasyInstall.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyInstall.Server.Controllers
{
    /// <summary>
    /// 供各软件（客户端）调用的公开接口：版本信息、版本判断、下载。
    /// 使用方式见管理后台页面中的「客户端接口调用示例」。
    /// </summary>
    [ApiController]
    [Route("api/client/{appKey}")]
    public class ClientApiController : ControllerBase
    {
        private readonly ServerDbContext _db;
        private readonly StoragePaths _storage;

        public ClientApiController(ServerDbContext db, StoragePaths storage)
        {
            _db = db;
            _storage = storage;
        }

        /// <summary>
        /// 版本信息接口：返回指定软件当前最新的安装包信息。
        /// </summary>
        [HttpGet("latest")]
        public async Task<IActionResult> Latest(string appKey)
        {
            var app = await _db.Apps.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppKey == appKey);
            if (app == null)
                return NotFound(new { error = $"未找到应用 {appKey}" });

            var latest = await LatestPackageAsync(app.Id);
            if (latest == null)
                return NotFound(new { error = $"应用 {appKey} 还没有上传过任何安装包" });

            return Ok(ToInfo(app.AppKey, latest));
        }

        /// <summary>
        /// 版本判断接口：比较客户端当前版本与服务端最新版本。
        /// currentVersion 为客户端软件自己的版本号（1~4 段数字，如 1.0.0.0）。
        /// </summary>
        [HttpGet("check")]
        public async Task<IActionResult> Check(string appKey, [FromQuery] string? currentVersion)
        {
            if (!VersionHelper.TryParse(currentVersion, out var current))
                return BadRequest(new
                {
                    error = "currentVersion 缺失或格式非法，应为 1~4 段数字，例如 ?currentVersion=1.0.0.0"
                });

            var app = await _db.Apps.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppKey == appKey);
            if (app == null)
                return NotFound(new { error = $"未找到应用 {appKey}" });

            var latest = await LatestPackageAsync(app.Id);

            var hasUpdate = latest != null &&
                            VersionHelper.Compare(Version.Parse(latest.Version), current) > 0;

            return Ok(new
            {
                appKey = app.AppKey,
                currentVersion = current.ToString(),
                hasUpdate,
                latestVersion = latest?.Version,
                latest?.FileName,
                latest?.FileSize,
                latest?.Sha256,
                latest?.ChangeLog,
                latest?.PublishedAt,
                downloadUrl = latest == null ? null : $"/api/client/{app.AppKey}/download"
            });
        }

        /// <summary>
        /// 返回该软件全部版本（含更新日志），客户端可用于展示历史版本。
        /// </summary>
        [HttpGet("versions")]
        public async Task<IActionResult> Versions(string appKey)
        {
            var app = await _db.Apps.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppKey == appKey);
            if (app == null)
                return NotFound(new { error = $"未找到应用 {appKey}" });

            var versions = await _db.Packages.AsNoTracking()
                .Where(p => p.AppId == app.Id)
                .OrderByDescending(p => p.PublishedAt)
                .ToListAsync();

            return Ok(versions.Select(p => ToInfo(app.AppKey, p)));
        }

        /// <summary>
        /// 下载接口：下载最新版本安装包。支持 Range 断点续传。
        /// </summary>
        [HttpGet("download")]
        public async Task<IActionResult> DownloadLatest(string appKey)
        {
            var app = await _db.Apps.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppKey == appKey);
            if (app == null)
                return NotFound(new { error = $"未找到应用 {appKey}" });

            var latest = await LatestPackageAsync(app.Id);
            if (latest == null)
                return NotFound(new { error = $"应用 {appKey} 还没有上传过任何安装包" });

            return Download(latest);
        }

        /// <summary>
        /// 下载指定版本安装包。
        /// </summary>
        [HttpGet("download/{version}")]
        public async Task<IActionResult> DownloadVersion(string appKey, string version)
        {
            var app = await _db.Apps.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AppKey == appKey);
            if (app == null)
                return NotFound(new { error = $"未找到应用 {appKey}" });

            var package = await _db.Packages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.AppId == app.Id && p.Version == version);
            if (package == null)
                return NotFound(new { error = $"应用 {appKey} 不存在版本 {version}" });

            return Download(package);
        }

        private IActionResult Download(PackageEntity package)
        {
            var path = _storage.Resolve(package.StoredPath);
            if (!System.IO.File.Exists(path))
                return NotFound(new { error = $"安装包文件丢失: {package.FileName}" });

            return PhysicalFile(path, "application/octet-stream", package.FileName,
                enableRangeProcessing: true);
        }

        private async Task<PackageEntity?> LatestPackageAsync(int appId)
        {
            // “最新”按版本号大小取最大值，而不是上传时间
            var packages = await _db.Packages.AsNoTracking()
                .Where(p => p.AppId == appId)
                .ToListAsync();

            return packages
                .OrderByDescending(p => VersionHelper.TryParse(p.Version, out var v) ? v : new Version())
                .FirstOrDefault();
        }

        private static PackageInfo ToInfo(string appKey, PackageEntity p) => new()
        {
            AppKey = appKey,
            Version = p.Version,
            FileName = p.FileName,
            FileSize = p.FileSize,
            Sha256 = p.Sha256,
            ChangeLog = p.ChangeLog,
            PublishedAt = p.PublishedAt,
            DownloadUrl = $"/api/client/{appKey}/download/{p.Version}"
        };
    }
}
