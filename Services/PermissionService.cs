using AutoMapper;
using DAM.DTOs;
using DAM.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DAM.Services
{
    public interface IPermissionService
    {
        Task<IEnumerable<PermissionDto>> GetUserPermissions(int userId);
        Task<GrantPermissionResultDto> GrantPermission(int userId, int? folderId, int? fileId, PermissionType permissionType, string recipientEmail);
        Task<bool> RevokePermission(int permissionId, bool isFilePermission);
        Task<bool> HasPermission(int userId, int? folderId, int? fileId, string requiredPermission);
        Task<GrantPermissionResultDto> ShareResource(int userId, int? folderId, int? fileId, PermissionType permissionType, string recipientEmail);
        Task<string> GeneratePublicLink(int userId, int? folderId, int? fileId, PermissionType permissionType);
        Task<GrantPermissionResultDto> ShareWithPublicLink(string shareToken, string userEmail);
    }
    
    public class PermissionService : IPermissionService
    {
        private readonly DamDbContext _dbContext;
        private readonly IEmailService _emailService;
        private readonly IProducerService _producerService;
        private readonly IMapper _mapper;
        private const string TOPIC = "email_topic";
        
        public PermissionService(DamDbContext dbContext, IEmailService emailService, IProducerService producerService, IMapper mapper)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _emailService = emailService;
            _producerService = producerService;
            _mapper = mapper;
        }

        public async Task<IEnumerable<PermissionDto>> GetUserPermissions(int userId)
        {
            var filePermissions = await GetFilePermissions(userId);
            var folderPermissions = await GetFolderPermissions(userId);
            var ownedFiles = await GetOwnedFiles(userId, filePermissions);
            var ownedFolders = await GetOwnedFolders(userId, folderPermissions);

            return filePermissions.Concat(folderPermissions).Concat(ownedFiles).Concat(ownedFolders);
        }

        private async Task<IEnumerable<PermissionDto>> GetFilePermissions(int userId)
        {
            var filePermissions = await _dbContext.Set<PermissionFile>()
                .Where(p => p.UserId == userId)
                .Include(p => p.File)
                .ToListAsync();

            return _mapper.Map<IEnumerable<PermissionDto>>(filePermissions);
        }

        private async Task<IEnumerable<PermissionDto>> GetFolderPermissions(int userId)
        {
            var folderPermissions = await _dbContext.Set<PermissionFolder>()
                .Where(p => p.UserId == userId)
                .Include(p => p.Folder)
                .ToListAsync();

            return _mapper.Map<IEnumerable<PermissionDto>>(folderPermissions);
        }

        private async Task<IEnumerable<PermissionDto>> GetOwnedFiles(int userId, IEnumerable<PermissionDto> filePermissions)
        {
            var ownedFiles = await _dbContext.Set<Models.File>()
                .Where(f => f.OwnerId == userId)
                .ToListAsync();

            return ownedFiles
                .Where(file => !filePermissions.Any(p => p.ResourceId == file.Id))
                .Select(file => new PermissionDto
                {
                    Id = 0,
                    UserId = userId,
                    ResourceType = "File",
                    ResourceId = file.Id,
                    ResourceName = file.Name,
                    PermissionType = PermissionType.Admin,
                    CreatedAt = file.CreatedAt,
                    IsOwner = true
                });
        }

        private async Task<IEnumerable<PermissionDto>> GetOwnedFolders(int userId, IEnumerable<PermissionDto> folderPermissions)
        {
            var ownedFolders = await _dbContext.Set<Folder>()
                .Where(f => f.OwnerId == userId)
                .ToListAsync();

            return ownedFolders
                .Where(folder => !folderPermissions.Any(p => p.ResourceId == folder.Id))
                .Select(folder => new PermissionDto
                {
                    Id = 0,
                    UserId = userId,
                    ResourceType = "Folder",
                    ResourceId = folder.Id,
                    ResourceName = folder.Name,
                    PermissionType = PermissionType.Admin,
                    CreatedAt = folder.CreatedAt,
                    IsOwner = true
                });
        }
        private void ValidateResourceIds(int? folderId, int? fileId)
        {
            if (!folderId.HasValue && !fileId.HasValue)
                throw new ArgumentException("Either folder ID or file ID must be provided");

            if (folderId.HasValue && fileId.HasValue)
                throw new ArgumentException("Cannot specify both folder ID and file ID");
        }

        public async Task<GrantPermissionResultDto> GrantPermission(int userId, int? folderId, int? fileId, PermissionType permissionType, string recipientEmail)
        {
            ValidateResourceIds(folderId, fileId);

            if (!await CanModifyResource(userId, folderId, fileId))
                throw new UnauthorizedAccessException("User does not have permission to modify this resource");

            return folderId.HasValue
                ? await HandleFolderPermission(userId, folderId.Value, permissionType, recipientEmail)
                : await HandleFilePermission(userId, fileId!.Value, permissionType, recipientEmail);
        }

        private async Task<GrantPermissionResultDto> HandleFolderPermission(int userId, int folderId, PermissionType permissionType, string recipientEmail)
        {
            var folder = await _dbContext.Set<Folder>().FindAsync(folderId) ?? throw new ArgumentException($"Folder with ID {folderId} not found");

            var recipientUser = await _dbContext.Set<User>().FirstOrDefaultAsync(u => u.Email == recipientEmail) ?? throw new ArgumentException($"User with email {recipientEmail} not found");
            var recipientId = recipientUser.Id;

            var permission = await _dbContext.Set<PermissionFolder>()
                .FirstOrDefaultAsync(p => p.UserId == userId && p.FolderId == folderId);

            if (permission == null)
            {
                permission = new PermissionFolder
                {
                    UserId = recipientId,
                    FolderId = folderId,
                    PermissionType = permissionType,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _dbContext.Set<PermissionFolder>().Add(permission);
            }
            else
            {
                permission.PermissionType = permissionType;
                _dbContext.Set<PermissionFolder>().Update(permission);
            }

            await _dbContext.SaveChangesAsync();

            var permissionDto = _mapper.Map<PermissionDto>(permission);

            bool notificationSent = await SendNotification(recipientEmail, permissionType, folder.Name);

            await GrantPermissionsToSubfoldersAndFiles(userId, folderId, permissionType, recipientEmail);

            return new GrantPermissionResultDto
            {
                Permission = permissionDto,
                NotificationSent = notificationSent,
                ResourceType = "Folder"
            };
        }

        private async Task<GrantPermissionResultDto> HandleFilePermission(int userId, int fileId, PermissionType permissionType, string recipientEmail)
        {
            var file = await _dbContext.Set<Models.File>().FindAsync(fileId) ?? throw new ArgumentException($"File with ID {fileId} not found");

            var recipientUser = await _dbContext.Set<User>().FirstOrDefaultAsync(u => u.Email == recipientEmail) ?? throw new ArgumentException($"User with email {recipientEmail} not found");
            var recipientId = recipientUser.Id; 

            var permission = await _dbContext.Set<PermissionFile>()
                .FirstOrDefaultAsync(p => p.UserId == userId && p.FileId == fileId);

            if (permission == null)
            {
                permission = new PermissionFile
                {
                    UserId = recipientId,
                    FileId = fileId,
                    PermissionType = permissionType,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _dbContext.Set<PermissionFile>().Add(permission);
            }
            else
            {
                permission.PermissionType = permissionType;
                _dbContext.Set<PermissionFile>().Update(permission);
            }

            await _dbContext.SaveChangesAsync();

            var permissionDto = _mapper.Map<PermissionDto>(permission);

            bool notificationSent = await SendNotification(recipientEmail, permissionType, file.Name);

            return new GrantPermissionResultDto
            {
                Permission = permissionDto,
                NotificationSent = notificationSent,
                ResourceType = "File"
            };
        }

        private async Task<bool> SendNotification(string recipientEmail, PermissionType permissionType, string resourceName)
        {
            if (string.IsNullOrEmpty(recipientEmail))
                return false;

            await _emailService.SendEmail(
                recipientEmail,
                $"You have been granted {permissionType} access to the {resourceName}.",
                $"New {permissionType} Permission Granted");

            return true;
        }

        private async Task GrantPermissionsToSubfoldersAndFiles(int userId, int folderId, PermissionType permissionType, string recipientEmail)
        {
            var subfolders = await _dbContext.Set<Folder>()
                .Where(f => f.ParentFolderId == folderId)
                .ToListAsync();

            foreach (var subfolder in subfolders)
            {
                await HandleFolderPermission(userId, subfolder.Id, permissionType, recipientEmail);
            }

            var files = await _dbContext.Set<Models.File>()
                .Where(f => f.FolderId == folderId)
                .ToListAsync();

            foreach (var file in files)
            {
                await HandleFilePermission(userId, file.Id, permissionType, recipientEmail);
            }
        }

        public async Task<bool> RevokePermission(int permissionId, bool isFilePermission)
        {
            if (permissionId <= 0)
                throw new ArgumentException("Permission ID must be greater than zero.", nameof(permissionId));

            if (isFilePermission)
            {
                var permission = await _dbContext.Set<PermissionFile>().FindAsync(permissionId);
                if (permission == null)
                    return false;

                _dbContext.Set<PermissionFile>().Remove(permission);
            }
            else
            {
                var permission = await _dbContext.Set<PermissionFolder>().FindAsync(permissionId);
                if (permission == null)
                    return false;

                _dbContext.Set<PermissionFolder>().Remove(permission);
            }

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> HasPermission(int userId, int? folderId, int? fileId, string requiredPermission)
        {
            ValidateUserId(userId);
            ValidateResourceIds(folderId, fileId);

            if (!Enum.TryParse<PermissionType>(requiredPermission, true, out var requiredPermissionType))
                throw new ArgumentException($"Invalid permission type: {requiredPermission}");

            if (await IsResourceOwner(userId, folderId, fileId))
                return true;

            return folderId.HasValue
                ? await HasFolderPermission(userId, folderId.Value, requiredPermissionType)
                : await HasFilePermission(userId, fileId!.Value, requiredPermissionType);
        }

        private void ValidateUserId(int userId)
        {
            if (userId <= 0)
                throw new ArgumentException("User ID must be greater than zero.", nameof(userId));
        }

        private async Task<bool> HasFolderPermission(int userId, int folderId, PermissionType requiredPermissionType)
        {
            var permissions = await _dbContext.Set<PermissionFolder>()
                .Where(p => p.UserId == userId && p.FolderId == folderId)
                .ToListAsync();

            return permissions.Any(p => IsRoleSufficient(p.PermissionType, requiredPermissionType));
        }

        private async Task<bool> HasFilePermission(int userId, int fileId, PermissionType requiredPermissionType)
        {
            var permissions = await _dbContext.Set<PermissionFile>()
                .Where(p => p.UserId == userId && p.FileId == fileId)
                .ToListAsync();

            return permissions.Any(p => IsRoleSufficient(p.PermissionType, requiredPermissionType));
        }

        private async Task<bool> IsResourceOwner(int userId, int? folderId, int? fileId)
        {
            ValidateUserId(userId);
            ValidateResourceIds(folderId, fileId);

            if (folderId.HasValue)
            {
                var folder = await _dbContext.Set<Folder>().FindAsync(folderId.Value);
                return folder != null && folder.OwnerId == userId;
            }
            else if (fileId.HasValue)
            {
                var file = await _dbContext.Set<Models.File>().FindAsync(fileId.Value);
                return file != null && file.OwnerId == userId;
            }

            return false;
        }

        private async Task<bool> CanModifyResource(int userId, int? folderId, int? fileId)
        {
            if (await IsResourceOwner(userId, folderId, fileId))
                return true;

            return folderId.HasValue
                ? await CanModifyFolder(userId, folderId.Value)
                : await CanModifyFile(userId, fileId!.Value);
        }

        private async Task<bool> CanModifyFolder(int userId, int folderId)
        {
            var permissions = await _dbContext.Set<PermissionFolder>()
                .Where(p => p.UserId == userId && p.FolderId == folderId)
                .ToListAsync();

            return permissions.Any(p => p.PermissionType == PermissionType.Admin || p.PermissionType == PermissionType.Contributor);
        }

        private async Task<bool> CanModifyFile(int userId, int fileId)
        {
            var permissions = await _dbContext.Set<PermissionFile>()
                .Where(p => p.UserId == userId && p.FileId == fileId)
                .ToListAsync();

            return permissions.Any(p => p.PermissionType == PermissionType.Admin || p.PermissionType == PermissionType.Contributor);
        }

        public async Task<GrantPermissionResultDto> ShareResource(int userId, int? folderId, int? fileId, PermissionType permissionType, string recipientEmail)
        {
            if (!await CanModifyResource(userId, folderId, fileId))
                throw new UnauthorizedAccessException("User does not have permission to share this resource");

            var grantResult = await GrantPermission(userId, folderId, fileId, permissionType, recipientEmail);

            var message = new
            {
                EventType = "ResourceShared",
                Timestamp = DateTimeOffset.UtcNow,
                SharerUserId = userId,
                FolderId = folderId,
                FileId = fileId,
                PermissionType = permissionType,
                RecipientEmail = recipientEmail,
                ResourceType = grantResult.ResourceType,
                ResourceName = grantResult.Permission.ResourceName
            };

            var messageJson = JsonSerializer.Serialize(message);
            await _producerService.ProduceAsync(TOPIC, messageJson);

            return grantResult;
        }

        public async Task<string> GeneratePublicLink(int userId, int? folderId, int? fileId, PermissionType permissionType)
        {
            ValidateResourceIds(folderId, fileId);

            if (!await CanModifyResource(userId, folderId, fileId))
                throw new UnauthorizedAccessException("User does not have permission to share this resource");

            // Generate a unique token for this shared resource
            var shareToken = Guid.NewGuid().ToString("N");
            
            var publicShare = new PublicShare
            {
                Token = shareToken,
                OwnerId = userId,
                FolderId = folderId,
                FileId = fileId,
                PermissionType = permissionType,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7) // Default 7-day expiration
            };
            
            _dbContext.Set<PublicShare>().Add(publicShare);
            await _dbContext.SaveChangesAsync();
            
            var resourceType = folderId.HasValue ? "Folder" : "File";
            var resourceId = folderId.HasValue ? folderId.Value : fileId!.Value;
            var resourceName = folderId.HasValue 
                ? (await _dbContext.Set<Folder>().FindAsync(folderId.Value))?.Name 
                : (await _dbContext.Set<Models.File>().FindAsync(fileId!.Value))?.Name;
            
            // Publish event to Kafka topic
            var message = new
            {
                EventType = "PublicLinkGenerated",
                Timestamp = DateTimeOffset.UtcNow,
                SharerUserId = userId,
                FolderId = folderId,
                FileId = fileId,
                PermissionType = permissionType,
                ResourceType = resourceType,
                ResourceName = resourceName,
                ShareToken = shareToken,
                ExpiresAt = publicShare.ExpiresAt
            };

            var messageJson = JsonSerializer.Serialize(message);
            await _producerService.ProduceAsync(TOPIC, messageJson);
            
            return shareToken;
        }
        
        public async Task<GrantPermissionResultDto> ShareWithPublicLink(string shareToken, string userEmail)
        {
            if (string.IsNullOrEmpty(shareToken))
                throw new ArgumentException("Share token cannot be empty", nameof(shareToken));
            
            var publicShare = await _dbContext.Set<PublicShare>()
                .FirstOrDefaultAsync(ps => ps.Token == shareToken);
                
            if (publicShare == null)
                throw new ArgumentException("Invalid or expired share token", nameof(shareToken));
                
            if (publicShare.ExpiresAt < DateTimeOffset.UtcNow)
            {
                // Remove expired share
                _dbContext.Set<PublicShare>().Remove(publicShare);
                await _dbContext.SaveChangesAsync();
                throw new ArgumentException("Share link has expired", nameof(shareToken));
            }
            
            // Find or create user by email
            var user = await _dbContext.Set<User>().FirstOrDefaultAsync(u => u.Email == userEmail);
            if (user == null)
                throw new ArgumentException($"User with email {userEmail} not found", nameof(userEmail));
                
            // Get owner
            var ownerId = publicShare.OwnerId;
            
            // Grant permission using owner's identity to the requesting user
            return await GrantPermission(
                ownerId, 
                publicShare.FolderId, 
                publicShare.FileId, 
                publicShare.PermissionType, 
                userEmail);
        }
        
        private bool IsRoleSufficient(PermissionType actualPermission, PermissionType requiredPermission)
        {
            return actualPermission >= requiredPermission;
        }
    }
}
