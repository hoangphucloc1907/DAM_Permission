namespace DAM.Models
{
    public class PermissionFolder
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int FolderId { get; set; }

        public Folder Folder { get; set; } = null!;

        public PermissionType PermissionType { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }
}
