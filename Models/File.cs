namespace DAM.Models
{
    public class File
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public required string Path { get; set; } 

        public int Size { get; set; }

        public int FolderId { get; set; }

        public Folder Folder { get; set; } = null!;

        public int OwnerId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public ICollection<PermissionFile> FilePermissions { get; set; } = new List<PermissionFile>();
    }
}
