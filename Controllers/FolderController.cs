using DAM.DTOs;
using DAM.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DAM.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FolderController : ControllerBase
    {
        private readonly IFolderService _folderService;

        public FolderController(IFolderService folderService)
        {
            _folderService = folderService;
        }


        [HttpGet("{folderId}/shared-users")]
        public async Task<ActionResult<List<FolderPermissionDto>>> GetSharedUsers(int folderId)
        {

            var sharedUsers = await _folderService.GetSharedUsersAsync(folderId);
            return Ok(sharedUsers);
        }

        [HttpGet("{folderId}/owner")]
        public async Task<ActionResult<UserDto>> GetOwner(int folderId)
        {
            var owner = await _folderService.GetOwnerAsync(folderId);
            if (owner == null)
            {
                return NotFound();
            }

            return Ok(owner);
        }
    }
}
