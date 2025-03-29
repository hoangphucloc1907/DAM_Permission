using DAM.DTOs;
using DAM.Models;
using Microsoft.EntityFrameworkCore;

namespace DAM.Services
{
    public interface IFolderService
    {
        Task<List<FolderPermissionDto>> GetSharedUsersAsync(int folderId);
        Task<UserDto?> GetOwnerAsync(int folderId);
    }
    public class FolderService : IFolderService
    {
        private readonly DamDbContext _dbContext;

        public FolderService(DamDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<List<FolderPermissionDto>> GetSharedUsersAsync(int folderId)
        {
            // Join PermissionFolder with Users to get user information
            var sharedUsers = await _dbContext.Set<PermissionFolder>()
                .Where(p => p.FolderId == folderId)
                .Join(
                    _dbContext.Set<User>(),
                    permission => permission.UserId,
                    user => user.Id,
                    (permission, user) => new FolderPermissionDto
                    {
                        UserId = permission.UserId,
                        UserName = user.Username,
                        Email = user.Email,
                        PermissionType = permission.PermissionType.ToString()
                    })
                .ToListAsync();

            return sharedUsers;
        }

        public async Task<UserDto?> GetOwnerAsync(int folderId)
        {
            var folder = await _dbContext.Set<Folder>()
                .Where(f => f.Id == folderId)
                .FirstOrDefaultAsync();

            if (folder == null)
                return null;

            // Get the owner information from Users table
            var owner = await _dbContext.Set<User>()
                .Where(u => u.Id == folder.OwnerId)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email
                })
                .FirstOrDefaultAsync();

            return owner;
        }
    }
}
