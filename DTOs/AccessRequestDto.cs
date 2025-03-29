using DAM.Models;
using System;
using System.ComponentModel.DataAnnotations;

namespace DAM.DTOs
{
    public class AccessRequestDto
    {
        public int Id { get; set; }
        public int RequesterId { get; set; }
        public string RequesterEmail { get; set; } = string.Empty;
        public string RequesterUsername { get; set; } = string.Empty;
        public int OwnerId { get; set; }
        public string OwnerEmail { get; set; } = string.Empty;
        public int? FolderId { get; set; }
        public int? FileId { get; set; }
        public string ResourceName { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public PermissionType RequestedPermissionType { get; set; }
        public string Message { get; set; } = string.Empty;
        public AccessRequestStatus Status { get; set; }
        public string? DenialReason { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
    
    public class CreateAccessRequestDto
    {
        [Required]
        public int RequesterId { get; set; }
        
        public int? FolderId { get; set; }
        
        public int? FileId { get; set; }
        
        [Required]
        public PermissionType RequestedPermissionType { get; set; }
        
        [StringLength(500)]
        public string Message { get; set; } = string.Empty;
    }
    
    public class UpdateAccessRequestDto
    {
        [Required]
        public AccessRequestStatus Status { get; set; }
        
        [StringLength(500)]
        public string? DenialReason { get; set; }
    }
    
    public class PublicShareDto
    {
        public string Token { get; set; } = string.Empty;
        public string ResourceType { get; set; } = string.Empty;
        public string ResourceName { get; set; } = string.Empty;
        public PermissionType PermissionType { get; set; }
        public string OwnerUsername { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
