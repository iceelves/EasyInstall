
using EasyInstall.Server.Data;
using EasyInstall.Server.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyInstall.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 上传的是几十上百 MB 的安装包，取消请求体大小限制
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = null;
                options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
                options.Limits.MinRequestBodyDataRate = null; // 慢速网络下大文件上传不被掐断
            });

            // 存储目录解析为绝对路径（相对程序运行目录）
            var storage = new StoragePaths
            {
                DataDirectory = Path.GetFullPath(Path.Combine(
                    builder.Environment.ContentRootPath,
                    builder.Configuration["Storage:DataDirectory"] ?? "data"))
            };
            Directory.CreateDirectory(storage.PackageDirectory);
            builder.Services.AddSingleton(storage);

            builder.Services.AddDbContext<ServerDbContext>(options =>
                options.UseSqlite($"Data Source={storage.DatabaseFile}"));

            builder.Services.AddControllers();
            // 参数绑定失败的错误也统一为 { error: "..." } 格式
            builder.Services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var message = string.Join("; ", context.ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "请求参数格式错误" : e.ErrorMessage));
                    return new BadRequestObjectResult(new { error = message });
                };
            });
            builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            // 管理后台 Cookie 登录：上传/编辑/删除类接口必须登录，客户端更新接口保持匿名
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.Cookie.Name = "EasyInstall.Admin";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                    // 管理页面是单页应用：未登录时不做 302 跳转，统一返回 401，由前端切换到登录界面
                    options.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };
                    options.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                });
            builder.Services.AddAuthorization();

            var app = builder.Build();

            // SQLite 建库建表（已存在则跳过）
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
                db.Database.EnsureCreated();

                // EnsureCreated 不会改动已存在的库：为旧版本创建的数据库补上 Users 表
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL,
                        PasswordHash TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    )
                    """);
                db.Database.ExecuteSqlRaw(
                    "CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Username ON Users (Username)");
            }

            app.UseDefaultFiles();  // wwwroot/index.html
            app.UseStaticFiles();
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}
