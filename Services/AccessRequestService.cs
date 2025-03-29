using AutoMapper;
using DAM.DTOs;
using DAM.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace DAM.Services
{
    public interface IAccessRequestService
    {
        Task<AccessRequestDto> CreateAccessRequest(CreateAccessRequestDto request);
        Task<AccessRequestDto> GetAccessRequest(int requestId);
        Task<IEnumerable<AccessRequestDto>> GetAccessRequestsByUser(int userId);
        Task<IEnumerable<AccessRequestDto>> GetAccessRequestsForOwner(int ownerId);
        Task<AccessRequestDto> ApproveAccessRequest(int requestId, int reviewerId);
        Task<AccessRequestDto> DenyAccessRequest(int requestId, int reviewerId, string denialReason);
    }
    
    public class AccessRequestService : IAccessRequestService
    {
        private readonly DamDbContext _dbContext;
        private readonly IPermissionService _permissionService;
        private readonly IProducerService _producerService;
        private readonly IMapper _mapper;
        private const string TOPIC = "email_topic";
        
        public AccessRequestService(
            DamDbContext dbContext,
            IPermissionService permissionService,
            IProducerService producerService,
            IMapper mapper)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
            _producerService = producerService ?? throw new ArgumentNullException(nameof(producerService));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }
        
        public async Task<AccessRequestDto> CreateAccessRequest(CreateAccessRequestDto requestDto)
        {
            // Validate that either folder ID or file ID is provided, but not both
            if ((!requestDto.FolderId.HasValue && !requestDto.FileId.HasValue) ||
                (requestDto.FolderId.HasValue && requestDto.FileId.HasValue))
            {
                throw new ArgumentException("Either folder ID or file ID must be provided, but not both");
            }
            
            // Determine the owner ID of the resource
            int ownerId;
            string resourceName;
            string resourceType;
            
            if (requestDto.FolderId.HasValue)
            {
                var folder = await _dbContext.Set<Folder>().FindAsync(requestDto.FolderId.Value)
                    ?? throw new ArgumentException($"Folder with ID {requestDto.FolderId.Value} not found");
                
                ownerId = folder.OwnerId;
                resourceName = folder.Name;
                resourceType = "Folder";
            }
            else
            {
                var file = await _dbContext.Set<Models.File>().FindAsync(requestDto.FileId.Value)
                    ?? throw new ArgumentException($"File with ID {requestDto.FileId.Value} not found");
                
                ownerId = file.OwnerId;
                resourceName = file.Name;
                resourceType = "File";
            }
            
            // Check if user already has the requested permission
            var hasPermission = await _permissionService.HasPermission(
                requestDto.RequesterId,
                requestDto.FolderId,
                requestDto.FileId,
                requestDto.RequestedPermissionType.ToString());
                
            if (hasPermission)
            {
                throw new InvalidOperationException("User already has the requested permission level or higher");
            }
            
            // Check if there's already a pending request for this resource
            var existingRequest = await _dbContext.Set<AccessRequest>()
                .FirstOrDefaultAsync(ar => 
                    ar.RequesterId == requestDto.RequesterId &&
                    ar.FolderId == requestDto.FolderId && 
                    ar.FileId == requestDto.FileId &&
                    ar.Status == AccessRequestStatus.Pending);
                    
            if (existingRequest != null)
            {
                throw new InvalidOperationException("A pending request for this resource already exists");
            }
            
            // Create the access request
            var request = new AccessRequest
            {
                RequesterId = requestDto.RequesterId,
                OwnerId = ownerId,
                FolderId = requestDto.FolderId,
                FileId = requestDto.FileId,
                RequestedPermissionType = requestDto.RequestedPermissionType,
                Message = requestDto.Message,
                Status = AccessRequestStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };
            
            _dbContext.Set<AccessRequest>().Add(request);
            await _dbContext.SaveChangesAsync();
            
            // Get requester and owner data for notification
            var requester = await _dbContext.Set<User>().FindAsync(requestDto.RequesterId);
            var owner = await _dbContext.Set<User>().FindAsync(ownerId);
            
            // Publish event to Kafka
            var message = new
            {
                EventType = "AccessRequested",
                Timestamp = DateTimeOffset.UtcNow,
                RequestId = request.Id,
                RequesterId = request.RequesterId,
                RequesterEmail = requester?.Email,
                RequesterUsername = requester?.Username,
                OwnerId = request.OwnerId,
                OwnerEmail = owner?.Email,
                ResourceId = requestDto.FolderId ?? requestDto.FileId,
                ResourceType = resourceType,
                ResourceName = resourceName,
                RequestedPermissionType = request.RequestedPermissionType.ToString(),
                Message = request.Message
            };
            
            var messageJson = JsonSerializer.Serialize(message);
            await _producerService.ProduceAsync(TOPIC, messageJson);
            
            // Return the DTO
            var result = _mapper.Map<AccessRequestDto>(request);
            result.ResourceType = resourceType;
            result.ResourceName = resourceName;
            result.RequesterEmail = requester?.Email ?? string.Empty;
            result.RequesterUsername = requester?.Username ?? string.Empty;
            result.OwnerEmail = owner?.Email ?? string.Empty;
            
            return result;
        }
        
        public async Task<AccessRequestDto> GetAccessRequest(int requestId)
        {
            var request = await _dbContext.Set<AccessRequest>()
                .Include(ar => ar.Requester)
                .Include(ar => ar.Owner)
                .FirstOrDefaultAsync(ar => ar.Id == requestId);
                
            if (request == null)
                throw new ArgumentException($"Access request with ID {requestId} not found");
                
            var dto = _mapper.Map<AccessRequestDto>(request);
            
            // Add resource name and type
            if (request.FolderId.HasValue)
            {
                var folder = await _dbContext.Set<Folder>().FindAsync(request.FolderId.Value);
                if (folder != null)
                {
                    dto.ResourceName = folder.Name;
                    dto.ResourceType = "Folder";
                }
            }
            else if (request.FileId.HasValue)
            {
                var file = await _dbContext.Set<Models.File>().FindAsync(request.FileId.Value);
                if (file != null)
                {
                    dto.ResourceName = file.Name;
                    dto.ResourceType = "File";
                }
            }
            
            return dto;
        }
        
        public async Task<IEnumerable<AccessRequestDto>> GetAccessRequestsByUser(int userId)
        {
            var requests = await _dbContext.Set<AccessRequest>()
                .Include(ar => ar.Requester)
                .Include(ar => ar.Owner)
                .Where(ar => ar.RequesterId == userId)
                .ToListAsync();
                
            var dtos = _mapper.Map<IEnumerable<AccessRequestDto>>(requests);
            
            // Enrich with resource names and types
            foreach (var dto in dtos)
            {
                await EnrichAccessRequestDto(dto);
            }
            
            return dtos;
        }
        
        public async Task<IEnumerable<AccessRequestDto>> GetAccessRequestsForOwner(int ownerId)
        {
            var requests = await _dbContext.Set<AccessRequest>()
                .Include(ar => ar.Requester)
                .Include(ar => ar.Owner)
                .Where(ar => ar.OwnerId == ownerId)
                .ToListAsync();
                
            var dtos = _mapper.Map<IEnumerable<AccessRequestDto>>(requests);
            
            // Enrich with resource names and types
            foreach (var dto in dtos)
            {
                await EnrichAccessRequestDto(dto);
            }
            
            return dtos;
        }
        
        public async Task<AccessRequestDto> ApproveAccessRequest(int requestId, int reviewerId)
        {
            var request = await _dbContext.Set<AccessRequest>().FindAsync(requestId)
                ?? throw new ArgumentException($"Access request with ID {requestId} not found");
                
            // Check if the reviewer is the owner of the resource
            if (request.OwnerId != reviewerId)
                throw new UnauthorizedAccessException("Only the resource owner can approve or deny access requests");
                
            // Check if request is already processed
            if (request.Status != AccessRequestStatus.Pending)
                throw new InvalidOperationException($"This request has already been {request.Status.ToString().ToLower()}");
                
            // Update request status
            request.Status = AccessRequestStatus.Approved;
            request.UpdatedAt = DateTimeOffset.UtcNow;
            
            _dbContext.Set<AccessRequest>().Update(request);
            await _dbContext.SaveChangesAsync();
            
            // Grant the permission
            var grantResult = await _permissionService.GrantPermission(
                reviewerId,
                request.FolderId,
                request.FileId,
                request.RequestedPermissionType,
                (await _dbContext.Set<User>().FindAsync(request.RequesterId))?.Email ?? string.Empty);
                
            // Prepare resource info for notification
            string resourceType = request.FolderId.HasValue ? "Folder" : "File";
            string resourceName = grantResult.Permission.ResourceName;
            int resourceId = grantResult.Permission.ResourceId;
            
            // Get requester email for notification
            var requester = await _dbContext.Set<User>().FindAsync(request.RequesterId);
            
            // Publish event to Kafka
            var message = new
            {
                EventType = "AccessRequestApproved",
                Timestamp = DateTimeOffset.UtcNow,
                RequestId = request.Id,
                RequesterId = request.RequesterId,
                RequesterEmail = requester?.Email,
                OwnerId = request.OwnerId,
                ResourceId = resourceId,
                ResourceType = resourceType,
                ResourceName = resourceName,
                GrantedPermissionType = request.RequestedPermissionType.ToString()
            };
            
            var messageJson = JsonSerializer.Serialize(message);
            await _producerService.ProduceAsync(TOPIC, messageJson);
            
            // Return the updated request
            var result = _mapper.Map<AccessRequestDto>(request);
            result.ResourceType = resourceType;
            result.ResourceName = resourceName;
            result.RequesterEmail = requester?.Email ?? string.Empty;
            
            return result;
        }
        
        public async Task<AccessRequestDto> DenyAccessRequest(int requestId, int reviewerId, string denialReason)
        {
            var request = await _dbContext.Set<AccessRequest>().FindAsync(requestId)
                ?? throw new ArgumentException($"Access request with ID {requestId} not found");
                
            // Check if the reviewer is the owner of the resource
            if (request.OwnerId != reviewerId)
                throw new UnauthorizedAccessException("Only the resource owner can approve or deny access requests");
                
            // Check if request is already processed
            if (request.Status != AccessRequestStatus.Pending)
                throw new InvalidOperationException($"This request has already been {request.Status.ToString().ToLower()}");
                
            // Update request status
            request.Status = AccessRequestStatus.Denied;
            request.DenialReason = denialReason;
            request.UpdatedAt = DateTimeOffset.UtcNow;
            
            _dbContext.Set<AccessRequest>().Update(request);
            await _dbContext.SaveChangesAsync();
            
            // Prepare resource info for notification
            string resourceType;
            string resourceName;
            
            if (request.FolderId.HasValue)
            {
                var folder = await _dbContext.Set<Folder>().FindAsync(request.FolderId.Value);
                resourceType = "Folder";
                resourceName = folder?.Name ?? "Unknown Folder";
            }
            else
            {
                var file = await _dbContext.Set<Models.File>().FindAsync(request.FileId!.Value);
                resourceType = "File";
                resourceName = file?.Name ?? "Unknown File";
            }
            
            // Get requester email for notification
            var requester = await _dbContext.Set<User>().FindAsync(request.RequesterId);
            
            // Publish event to Kafka
            var message = new
            {
                EventType = "AccessRequestDenied",
                Timestamp = DateTimeOffset.UtcNow,
                RequestId = request.Id,
                RequesterId = request.RequesterId,
                RequesterEmail = requester?.Email,
                OwnerId = request.OwnerId,
                ResourceType = resourceType,
                ResourceName = resourceName,
                RequestedPermissionType = request.RequestedPermissionType.ToString(),
                DenialReason = denialReason
            };
            
            var messageJson = JsonSerializer.Serialize(message);
            await _producerService.ProduceAsync(TOPIC, messageJson);
            
            // Return the updated request
            var result = _mapper.Map<AccessRequestDto>(request);
            result.ResourceType = resourceType;
            result.ResourceName = resourceName;
            result.RequesterEmail = requester?.Email ?? string.Empty;
            
            return result;
        }
        
        private async Task EnrichAccessRequestDto(AccessRequestDto dto)
        {
            if (dto.FolderId.HasValue)
            {
                var folder = await _dbContext.Set<Folder>().FindAsync(dto.FolderId.Value);
                if (folder != null)
                {
                    dto.ResourceName = folder.Name;
                    dto.ResourceType = "Folder";
                }
            }
            else if (dto.FileId.HasValue)
            {
                var file = await _dbContext.Set<Models.File>().FindAsync(dto.FileId.Value);
                if (file != null)
                {
                    dto.ResourceName = file.Name;
                    dto.ResourceType = "File";
                }
            }
            
            var requester = await _dbContext.Set<User>().FindAsync(dto.RequesterId);
            if (requester != null)
            {
                dto.RequesterEmail = requester.Email;
                dto.RequesterUsername = requester.Username;
            }
            
            var owner = await _dbContext.Set<User>().FindAsync(dto.OwnerId);
            if (owner != null)
            {
                dto.OwnerEmail = owner.Email;
            }
        }
    }
}
