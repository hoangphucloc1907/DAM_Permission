
namespace DAM.Models
{
    public class PermissionFile
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int FileId { get; set; }

        public File File { get; set; } = null!;

        public PermissionType PermissionType { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
    }

    public enum PermissionType
    {
        Admin = 0,
        Contributor = 1,
        Reader = 2
    }
}
