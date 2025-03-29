using DAM.Models;
using Microsoft.EntityFrameworkCore;

namespace DAM
{
    public class DamDbContext : DbContext
    {
        public DamDbContext(DbContextOptions<DamDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Models.File> Files { get; set; }
        public DbSet<Folder> Folders { get; set; }
        public DbSet<PermissionFile> FilePermissions { get; set; }
        public DbSet<PermissionFolder> FolderPermissions { get; set; }
        public DbSet<AccessRequest> AccessRequests { get; set; }
        public DbSet<PublicShare> PublicShare { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AccessRequest>()
                .HasOne(ar => ar.Requester)
                .WithMany()
                .HasForeignKey(ar => ar.RequesterId)
                .OnDelete(DeleteBehavior.Restrict); // Prevents cascading delete

            modelBuilder.Entity<AccessRequest>()
                .HasOne(ar => ar.Owner)
                .WithMany()
                .HasForeignKey(ar => ar.OwnerId)
                .OnDelete(DeleteBehavior.Restrict); // Prevents cascading delete

            modelBuilder.Entity<AccessRequest>()
                .HasOne(ar => ar.Folder)
                .WithMany()
                .HasForeignKey(ar => ar.FolderId)
                .OnDelete(DeleteBehavior.Restrict); // Prevents cascading delete

            modelBuilder.Entity<AccessRequest>()
                .HasOne(ar => ar.File)
                .WithMany()
                .HasForeignKey(ar => ar.FileId)
                .OnDelete(DeleteBehavior.Restrict); // Prevents cascading delete

            modelBuilder.Entity<PublicShare>()
                .HasOne(ps => ps.Owner)
                .WithMany()
                .HasForeignKey(ps => ps.OwnerId)
                .OnDelete(DeleteBehavior.Restrict); // Prevents cascading delete

            modelBuilder.Entity<PublicShare>()
                .HasOne(ps => ps.Folder)
                .WithMany()
                .HasForeignKey(ps => ps.FolderId)
                .OnDelete(DeleteBehavior.Restrict); // Prevents cascading delete

            modelBuilder.Entity<PublicShare>()
                .HasOne(ps => ps.File)
                .WithMany()
                .HasForeignKey(ps => ps.FileId)
                .OnDelete(DeleteBehavior.Restrict); // Prevents cascading delete
        }
    }
}
