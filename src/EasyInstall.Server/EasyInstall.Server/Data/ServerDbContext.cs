using Microsoft.EntityFrameworkCore;

namespace EasyInstall.Server.Data
{
    public class ServerDbContext : DbContext
    {
        public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options) { }

        public DbSet<AppEntity> Apps => Set<AppEntity>();
        public DbSet<PackageEntity> Packages => Set<PackageEntity>();
        public DbSet<UserEntity> Users => Set<UserEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AppEntity>(e =>
            {
                e.HasIndex(x => x.AppKey).IsUnique();
            });

            modelBuilder.Entity<UserEntity>(e =>
            {
                e.HasIndex(x => x.Username).IsUnique();
            });

            modelBuilder.Entity<PackageEntity>(e =>
            {
                // 同一软件同一版本只保留一个安装包
                e.HasIndex(x => new { x.AppId, x.Version }).IsUnique();
                e.HasOne(x => x.App)
                    .WithMany(a => a.Packages)
                    .HasForeignKey(x => x.AppId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
