namespace DAM.Models
{
    public class AccessRequest
    {
        public int Id { get; set; }
        public int RequesterId { get; set; }
        public User Requester { get; set; } = null!;
        public int OwnerId { get; set; }
        public User Owner { get; set; } = null!;
        public int? FolderId { get; set; }
        public Folder? Folder { get; set; }
        public int? FileId { get; set; }
        public File? File { get; set; }
        public PermissionType RequestedPermissionType { get; set; }
        public string Message { get; set; } = string.Empty;
        public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Pending;
        public string? DenialReason { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }
    
    public enum AccessRequestStatus
    {
        Pending,
        Approved,
        Denied
    }
    
    public class PublicShare
    {
        public int Id { get; set; }
        public string Token { get; set; } = string.Empty;
        public int OwnerId { get; set; }
        public User Owner { get; set; } = null!;
        public int? FolderId { get; set; }
        public Folder? Folder { get; set; }
        public int? FileId { get; set; }
        public File? File { get; set; }
        public PermissionType PermissionType { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
