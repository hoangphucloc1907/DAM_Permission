using DAM.Models;

public class PermissionDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string ResourceType { get; set; } = "Unknown";
    public int ResourceId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public PermissionType PermissionType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsOwner { get; set; } = false;
}

public class GrantPermissionResultDto
{
    public PermissionDto Permission { get; set; } = null!;
    public bool NotificationSent { get; set; }
    public string ResourceType { get; set; } = "Unknown";
}
