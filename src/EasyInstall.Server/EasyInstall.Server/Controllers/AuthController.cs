using EasyInstall.Server.Data;
using EasyInstall.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EasyInstall.Server.Controllers
{
    /// <summary>
    /// 管理后台登录：首次运行设置管理员密码 → 登录 → 退出。
    /// 管理类接口（上传/编辑/删除/应用管理）必须登录后才能调用；
    /// 客户端接口（/api/client/...，供各软件检查更新与下载）保持匿名。
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly ServerDbContext _db;

        public AuthController(ServerDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// 当前状态：initialized=是否已创建管理员账号（false 时前端显示设置密码界面）。
        /// </summary>
        [HttpGet("status")]
        [AllowAnonymous]
        public async Task<IActionResult> Status()
        {
            var initialized = await _db.Users.AnyAsync();
            return Ok(new
            {
                initialized,
                authenticated = User.Identity?.IsAuthenticated == true,
                username = User.Identity?.IsAuthenticated == true ? User.Identity!.Name : null
            });
        }

        /// <summary>
        /// 首次运行设置管理员账号（仅当还没有任何账号时可用，之后返回 409）。
        /// </summary>
        [HttpPost("setup")]
        [AllowAnonymous]
        public async Task<IActionResult> Setup([FromBody] CredentialsRequest request)
        {
            if (await _db.Users.AnyAsync())
                return Conflict(new { error = "管理员账号已存在，请直接登录" });

            var username = (request.Username ?? "").Trim();
            if (username.Length is 0 or > 64)
                return BadRequest(new { error = "用户名不能为空且不超过 64 个字符" });
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 6)
                return BadRequest(new { error = "密码至少需要 6 位" });

            var user = new UserEntity
            {
                Username = username,
                PasswordHash = PasswordHasher.Hash(request.Password),
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            await SignInAsync(user);
            return Ok(new { username = user.Username });
        }

        /// <summary>
        /// 登录，成功后写入会话 Cookie。
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] CredentialsRequest request)
        {
            var username = (request.Username ?? "").Trim();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || !PasswordHasher.Verify(request.Password ?? "", user.PasswordHash))
                return Unauthorized(new { error = "用户名或密码错误" });

            await SignInAsync(user);
            return Ok(new { username = user.Username });
        }

        /// <summary>
        /// 退出登录。
        /// </summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { ok = true });
        }

        private async Task SignInAsync(UserEntity user)
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, user.Username)],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                });
        }

        public record CredentialsRequest(string? Username, string? Password);
    }
}
