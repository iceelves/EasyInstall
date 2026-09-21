using EasyInstall.Server.Data;
using EasyInstall.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace EasyInstall.Server.Controllers
{
    /// <summary>
    /// 安装包版本管理接口（上传、编辑更新日志、删除），供管理页面使用，必须登录后才能调用。
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api")]
    public class PackagesController : ControllerBase
    {
        private readonly ServerDbContext _db;
        private readonly StoragePaths _storage;

        public PackagesController(ServerDbContext db, StoragePaths storage)
        {
            _db = db;
            _storage = storage;
        }

        /// <summary>
        /// 列出某应用全部版本。
        /// </summary>
        [HttpGet("apps/{appKey}/packages")]
        public async Task<IActionResult> List(string appKey)
        {
            var app = await _db.Apps.AsNoTracking()
                .Include(a => a.Packages)
                .FirstOrDefaultAsync(a => a.AppKey == appKey);
            if (app == null)
                return NotFound(new { error = $"未找到应用 {appKey}" });

            var latest = LatestOf(app.Packages);
            var packages = app.Packages
                .OrderByDescending(p => VersionHelper.TryParse(p.Version, out var v) ? v : new Version())
                .Select(p => new
                {
                    p.Id,
                    p.Version,
                    p.FileName,
                    p.FileSize,
                    p.Sha256,
                    p.ChangeLog,
                    p.PublishedAt,
                    downloadUrl = $"/api/client/{app.AppKey}/download/{p.Version}",
                    isLatest = p == latest
                })
                .ToList();

            return Ok(packages);
        }

        /// <summary>
        /// 上传安装包（multipart/form-data）：
        ///   file      必填，安装包文件（EasyInstall 生成的 *Install.exe）
        ///   version   可选，1~4 段数字；缺省时自动从文件名识别
        ///   changeLog 可选，更新日志
        /// 同一应用同一版本重复上传时覆盖旧文件并更新信息。
        /// </summary>
        [HttpPost("apps/{appKey}/packages")]
        [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
        public async Task<IActionResult> Upload(string appKey, [FromForm] UploadRequest request)
        {
            var app = await _db.Apps.FirstOrDefaultAsync(a => a.AppKey == appKey);
            if (app == null)
                return NotFound(new { error = $"未找到应用 {appKey}，请先创建应用" });

            if (request.File == null || request.File.Length == 0)
                return BadRequest(new { error = "缺少安装包文件（form 字段名 file）" });

            // 版本号：显式提供优先，否则从文件名识别
            string version;
            if (!string.IsNullOrWhiteSpace(request.Version))
            {
                if (!VersionHelper.TryParse(request.Version, out var parsed))
                    return BadRequest(new
                    {
                        error = $"版本号 {request.Version} 非法，应为 1~4 段数字，如 1.0.0.0"
                    });
                version = parsed.ToString();
            }
            else
            {
                var fromName = VersionHelper.ParseFromFileName(request.File.FileName);
                if (fromName == null)
                    return BadRequest(new
                    {
                        error = $"无法从文件名 {request.File.FileName} 识别版本号，请在表单中显式填写 version"
                    });
                version = fromName;
            }

            // 同一版本重复上传 → 覆盖
            var package = await _db.Packages
                .FirstOrDefaultAsync(p => p.AppId == app.Id && p.Version == version);
            var isNew = package == null;
            if (package == null)
            {
                package = new PackageEntity { AppId = app.Id, Version = version };
                _db.Packages.Add(package);
            }
            else
            {
                var oldPath = _storage.Resolve(package.StoredPath);
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            var fileName = SanitizeFileName(request.File.FileName);
            var storedPath = Path.Combine(app.AppKey, version, fileName);
            var fullPath = _storage.Resolve(storedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            await using (var stream = System.IO.File.Create(fullPath))
            {
                await request.File.CopyToAsync(stream);
            }

            package.FileName = fileName;
            package.StoredPath = storedPath;
            package.FileSize = request.File.Length;
            package.Sha256 = await ComputeSha256Async(fullPath);
            package.ChangeLog = string.IsNullOrWhiteSpace(request.ChangeLog) ? null : request.ChangeLog.Trim();
            package.PublishedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                package.Id,
                app.AppKey,
                package.Version,
                package.FileName,
                package.FileSize,
                package.Sha256,
                package.ChangeLog,
                package.PublishedAt,
                overwritten = !isNew,
                downloadUrl = $"/api/client/{app.AppKey}/download/{package.Version}"
            });
        }

        /// <summary>
        /// 编辑版本信息（目前支持更新日志）。
        /// </summary>
        [HttpPut("packages/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdatePackageRequest request)
        {
            var package = await _db.Packages.Include(p => p.App)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (package == null)
                return NotFound(new { error = $"未找到安装包记录 #{id}" });

            package.ChangeLog = string.IsNullOrWhiteSpace(request.ChangeLog) ? null : request.ChangeLog.Trim();
            await _db.SaveChangesAsync();

            return Ok(new
            {
                package.Id,
                package.Version,
                package.ChangeLog,
                package.PublishedAt
            });
        }

        /// <summary>
        /// 删除一个版本（记录 + 磁盘文件）。
        /// </summary>
        [HttpDelete("packages/{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var package = await _db.Packages.FirstOrDefaultAsync(p => p.Id == id);
            if (package == null)
                return NotFound(new { error = $"未找到安装包记录 #{id}" });

            _db.Packages.Remove(package);
            await _db.SaveChangesAsync();

            var path = _storage.Resolve(package.StoredPath);
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);

            // 清理空目录：版本目录、应用目录
            RemoveEmptyDir(Path.GetDirectoryName(path));
            RemoveEmptyDir(Path.GetDirectoryName(Path.GetDirectoryName(path)));

            return Ok(new { deleted = new { package.Id, package.Version } });
        }

        private static PackageEntity? LatestOf(List<PackageEntity> packages) =>
            packages.OrderByDescending(p => VersionHelper.TryParse(p.Version, out var v) ? v : new Version())
                .FirstOrDefault();

        private static string SanitizeFileName(string fileName)
        {
            var name = Path.GetFileName(fileName);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? $"package_{DateTime.UtcNow:yyyyMMddHHmmss}.exe" : name;
        }

        private static async Task<string> ComputeSha256Async(string path)
        {
            await using var stream = System.IO.File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(stream);
            return Convert.ToHexString(hash);
        }

        private void RemoveEmptyDir(string? dir)
        {
            if (dir == null || !Directory.Exists(dir))
                return;
            if (dir.TrimEnd(Path.DirectorySeparatorChar).TrimEnd(Path.AltDirectorySeparatorChar)
                .Equals(Path.GetFullPath(_storage.PackageDirectory).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                return;
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }

        public record UploadRequest(IFormFile? File, string? Version, string? ChangeLog);

        public record UpdatePackageRequest(string? ChangeLog);
    }
}
