using EasyInstall.Server.Data;
using EasyInstall.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace EasyInstall.Server.Controllers
{
    /// <summary>
    /// 应用（服务）管理接口，供管理页面使用，必须登录后才能调用。
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/apps")]
    public partial class AppsController : ControllerBase
    {
        private readonly ServerDbContext _db;
        private readonly StoragePaths _storage;

        public AppsController(ServerDbContext db, StoragePaths storage)
        {
            _db = db;
            _storage = storage;
        }

        /// <summary>
        /// AppKey 限制为字母、数字、点、下划线、连字符，作为客户端调用的唯一标识。
        /// </summary>
        [GeneratedRegex(@"^[A-Za-z0-9_\-\.]{1,64}$")]
        private static partial Regex AppKeyRegex();

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var apps = await _db.Apps.AsNoTracking()
                .Include(a => a.Packages)
                .OrderBy(a => a.Id)
                .ToListAsync();

            var result = apps.Select(a =>
            {
                var latest = a.Packages
                    .OrderByDescending(p => VersionHelper.TryParse(p.Version, out var v) ? v : new Version())
                    .FirstOrDefault();
                return new
                {
                    a.AppKey,
                    a.Name,
                    a.Description,
                    a.CreatedAt,
                    packageCount = a.Packages.Count,
                    latestVersion = latest?.Version,
                    latestPublishedAt = latest?.PublishedAt
                };
            });

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAppRequest request)
        {
            var appKey = request.AppKey?.Trim() ?? "";
            if (!AppKeyRegex().IsMatch(appKey))
                return BadRequest(new
                {
                    error = "AppKey 只能包含字母、数字、点、下划线、连字符，长度 1~64"
                });

            if (await _db.Apps.AnyAsync(a => a.AppKey == appKey))
                return Conflict(new { error = $"AppKey {appKey} 已存在" });

            var app = new AppEntity
            {
                AppKey = appKey,
                Name = string.IsNullOrWhiteSpace(request.Name) ? appKey : request.Name.Trim(),
                Description = request.Description?.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            _db.Apps.Add(app);
            await _db.SaveChangesAsync();

            return Ok(new { app.AppKey, app.Name, app.Description });
        }

        [HttpPut("{appKey}")]
        public async Task<IActionResult> Update(string appKey, [FromBody] CreateAppRequest request)
        {
            var app = await _db.Apps.FirstOrDefaultAsync(a => a.AppKey == appKey);
            if (app == null)
                return NotFound(new { error = $"未找到应用 {appKey}" });

            if (!string.IsNullOrWhiteSpace(request.Name))
                app.Name = request.Name.Trim();
            app.Description = request.Description?.Trim();
            await _db.SaveChangesAsync();

            return Ok(new { app.AppKey, app.Name, app.Description });
        }

        [HttpDelete("{appKey}")]
        public async Task<IActionResult> Delete(string appKey)
        {
            var app = await _db.Apps.FirstOrDefaultAsync(a => a.AppKey == appKey);
            if (app == null)
                return NotFound(new { error = $"未找到应用 {appKey}" });

            _db.Apps.Remove(app);
            await _db.SaveChangesAsync();

            // 数据库删除成功后清理磁盘上的安装包目录
            var dir = Path.Combine(_storage.PackageDirectory, appKey);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

            return Ok(new { deleted = appKey });
        }

        public record CreateAppRequest(string? AppKey, string? Name, string? Description);
    }
}
