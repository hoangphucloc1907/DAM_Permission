namespace DAM.DTOs
{
    public class FolderPermissionDto
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PermissionType { get; set; } = string.Empty;
    }
}
