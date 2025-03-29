namespace DAM.Models
{
    public class Folder
    {
        public int Id { get; set; }

        public required string Name { get; set; } 

        public int OwnerId { get; set; }

        public int? ParentFolderId { get; set; }

        public Folder ParentFolder { get; set; } = null!;

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public ICollection<File> Files { get; set; } = [];
        public ICollection<Folder> Subfolders { get; set; } = [];

        public ICollection<PermissionFolder> FolderPermissions { get; set; } = new List<PermissionFolder>();

    }
}
