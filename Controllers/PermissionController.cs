using DAM.Models;
using DAM.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading.Tasks;

namespace DAM.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces(MediaTypeNames.Application.Json)]
    [Consumes(MediaTypeNames.Application.Json)]
    public class PermissionController : ControllerBase
    {
        private readonly IPermissionService _permissionService;
        private static readonly string ErrorMsgResource = "Either folder ID or file ID must be provided";
        private static readonly string ErrorMsgBoth = "Cannot specify both folder ID and file ID";

        public PermissionController(IPermissionService permissionService)
        {
            _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        }

        /// <summary>
        /// Get all permissions for a user
        /// </summary>
        [HttpGet("user/{userId:int}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PermissionDto>))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<PermissionDto>>> GetUserPermissions(int userId)
        {
            try
            {
                var permissions = await _permissionService.GetUserPermissions(userId);
                return Ok(permissions);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while retrieving permissions", error = ex.Message });
            }
        }

        /// <summary>
        /// Revoke a permission by ID
        /// </summary>
        [HttpDelete("{permissionId:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> RevokePermission(int permissionId, [FromQuery] bool isFilePermission = true)
        {
            try
            {
                var result = await _permissionService.RevokePermission(permissionId, isFilePermission);

                if (!result)
                    return NotFound(new { message = $"Permission with ID {permissionId} not found" });

                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while revoking permission", error = ex.Message });
            }
        }

        /// <summary>
        /// Check if a user has a specific permission level for a folder or file
        /// </summary>
        [HttpGet("check")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PermissionCheckResult))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<PermissionCheckResult>> CheckPermission(
            [Required] int userId,
            int? folderId,
            int? fileId,
            [Required] string requiredPermission)
        {
            if (string.IsNullOrWhiteSpace(requiredPermission))
                return BadRequest(new { message = "Required permission must be specified" });

            if (!ValidateResourceIds(folderId, fileId, out var errorMessage))
                return BadRequest(new { message = errorMessage });

            try
            {
                var hasPermission = await _permissionService.HasPermission(
                    userId,
                    folderId,
                    fileId,
                    requiredPermission);

                return Ok(new PermissionCheckResult
                {
                    UserId = userId,
                    FolderId = folderId,
                    FileId = fileId,
                    RequiredPermission = requiredPermission,
                    HasPermission = hasPermission
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while checking permission", error = ex.Message });
            }
        }

        /// <summary>
        /// Share a folder or file with another user via email and Kafka message
        /// </summary>
        [HttpPost("share")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ShareResult))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ShareResult>> ShareResource([FromBody] ShareRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!ValidateResourceIds(request.FolderId, request.FileId, out var errorMessage))
                return BadRequest(new { message = errorMessage });

            if (string.IsNullOrWhiteSpace(request.RecipientEmail))
                return BadRequest(new { message = "Recipient email is required" });

            try
            {
                var result = await _permissionService.ShareResource(
                    request.UserId,
                    request.FolderId,
                    request.FileId,
                    request.PermissionType,
                    request.RecipientEmail);

                return Ok(new ShareResult
                {
                    Success = true,
                    Message = $"Successfully shared {result.ResourceType.ToLower()} '{result.Permission.ResourceName}' with {request.RecipientEmail}",
                    PermissionId = result.Permission.Id,
                    ResourceName = result.Permission.ResourceName,
                    ResourceType = result.ResourceType
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while sharing resource", error = ex.Message });
            }
        }

        /// <summary>
        /// Generate a public link for a folder or file
        /// </summary>
        [HttpPost("public-link")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PublicLinkResult))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<PublicLinkResult>> GeneratePublicLink([FromBody] PublicLinkRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!ValidateResourceIds(request.FolderId, request.FileId, out var errorMessage))
                return BadRequest(new { message = errorMessage });

            try
            {
                var shareToken = await _permissionService.GeneratePublicLink(
                    request.UserId,
                    request.FolderId,
                    request.FileId,
                    request.PermissionType);

                // Get resource type and name
                string resourceType = request.FolderId.HasValue ? "Folder" : "File";
                string resourceName = string.Empty;

                return Ok(new PublicLinkResult
                {
                    Success = true,
                    Message = $"Successfully generated public link for {resourceType.ToLower()}",
                    ShareToken = shareToken,
                    ResourceType = resourceType
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while generating public link", error = ex.Message });
            }
        }

        /// <summary>
        /// Access a resource using a public link
        /// </summary>
        [HttpPost("public-link/access")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ShareResult))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<ShareResult>> AccessPublicLink([FromBody] PublicLinkAccessRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var result = await _permissionService.ShareWithPublicLink(
                    request.ShareToken,
                    request.UserEmail);

                return Ok(new ShareResult
                {
                    Success = true,
                    Message = $"Successfully granted access to {result.ResourceType.ToLower()} '{result.Permission.ResourceName}'",
                    PermissionId = result.Permission.Id,
                    ResourceName = result.Permission.ResourceName,
                    ResourceType = result.ResourceType
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while accessing public link", error = ex.Message });
            }
        }

        // Helper method to validate folder and file IDs
        private bool ValidateResourceIds(int? folderId, int? fileId, out string errorMessage)
        {
            if (!folderId.HasValue && !fileId.HasValue)
            {
                errorMessage = ErrorMsgResource;
                return false;
            }

            if (folderId.HasValue && fileId.HasValue)
            {
                errorMessage = ErrorMsgBoth;
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public class GrantPermissionRequest
        {
            [Required]
            public int UserId { get; set; }

            public int? FolderId { get; set; }

            public int? FileId { get; set; }

            [Required]
            public PermissionType PermissionType { get; set; }

            public string? RecipientEmail { get; set; }
        }

        public class ShareRequest
        {
            [Required]
            public int UserId { get; set; }

            public int? FolderId { get; set; }

            public int? FileId { get; set; }

            [Required]
            public PermissionType PermissionType { get; set; }

            [Required]
            [EmailAddress]
            public string RecipientEmail { get; set; } = string.Empty;
        }

        public class ShareResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public int PermissionId { get; set; }
            public string ResourceName { get; set; } = string.Empty;
            public string ResourceType { get; set; } = string.Empty;
        }

        public class PermissionCheckResult
        {
            public int UserId { get; set; }
            public int? FolderId { get; set; }
            public int? FileId { get; set; }
            public string RequiredPermission { get; set; } = string.Empty;
            public bool HasPermission { get; set; }
        }

        public class PublicLinkRequest
        {
            [Required]
            public int UserId { get; set; }

            public int? FolderId { get; set; }

            public int? FileId { get; set; }

            [Required]
            public PermissionType PermissionType { get; set; }
        }

        public class PublicLinkResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public string ShareToken { get; set; } = string.Empty;
            public string ResourceType { get; set; } = string.Empty;
        }

        public class PublicLinkAccessRequest
        {
            [Required]
            public string ShareToken { get; set; } = string.Empty;

            [Required]
            [EmailAddress]
            public string UserEmail { get; set; } = string.Empty;
        }
    }

}
