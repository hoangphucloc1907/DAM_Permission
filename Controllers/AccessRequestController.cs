using DAM.DTOs;
using DAM.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

namespace DAM.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces(MediaTypeNames.Application.Json)]
    [Consumes(MediaTypeNames.Application.Json)]
    public class AccessRequestController : ControllerBase
    {
        private readonly IAccessRequestService _accessRequestService;
        
        public AccessRequestController(IAccessRequestService accessRequestService)
        {
            _accessRequestService = accessRequestService ?? throw new ArgumentNullException(nameof(accessRequestService));
        }
        
        /// <summary>
        /// Request access to a file or folder
        /// </summary>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(AccessRequestDto))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AccessRequestDto>> CreateAccessRequest([FromBody] CreateAccessRequestDto requestDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
                
            try
            {
                var result = await _accessRequestService.CreateAccessRequest(requestDto);
                return CreatedAtAction(nameof(GetAccessRequest), new { id = result.Id }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while creating access request", error = ex.Message });
            }
        }
        
        /// <summary>
        /// Get a specific access request by ID
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AccessRequestDto))]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AccessRequestDto>> GetAccessRequest(int id)
        {
            try
            {
                var result = await _accessRequestService.GetAccessRequest(id);
                return Ok(result);
            }
            catch (ArgumentException)
            {
                return NotFound(new { message = $"Access request with ID {id} not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while retrieving access request", error = ex.Message });
            }
        }
        
        /// <summary>
        /// Get all access requests made by a user
        /// </summary>
        [HttpGet("user/{userId:int}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<AccessRequestDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<AccessRequestDto>>> GetUserAccessRequests(int userId)
        {
            try
            {
                var results = await _accessRequestService.GetAccessRequestsByUser(userId);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while retrieving access requests", error = ex.Message });
            }
        }
        
        /// <summary>
        /// Get all access requests for resources owned by a user
        /// </summary>
        [HttpGet("owner/{ownerId:int}")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<AccessRequestDto>))]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<AccessRequestDto>>> GetOwnerAccessRequests(int ownerId)
        {
            try
            {
                var results = await _accessRequestService.GetAccessRequestsForOwner(ownerId);
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while retrieving access requests", error = ex.Message });
            }
        }
        
        /// <summary>
        /// Approve an access request
        /// </summary>
        [HttpPost("{id:int}/approve")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AccessRequestDto))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AccessRequestDto>> ApproveAccessRequest(int id, [FromBody] ApproveRequestDto approveDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            approveDto.ReviewerId = 1;
            try
            {
                var result = await _accessRequestService.ApproveAccessRequest(id, approveDto.ReviewerId);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while approving access request", error = ex.Message });
            }
        }
        
        /// <summary>
        /// Deny an access request
        /// </summary>
        [HttpPost("{id:int}/deny")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AccessRequestDto))]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AccessRequestDto>> DenyAccessRequest(int id, [FromBody] DenyRequestDto denyDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            denyDto.ReviewerId = 1;
            denyDto.DenialReason = "No content";

            try
            {
                var result = await _accessRequestService.DenyAccessRequest(id, denyDto.ReviewerId, denyDto.DenialReason);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "An error occurred while denying access request", error = ex.Message });
            }
        }
        
        public class ApproveRequestDto
        {
            [Required]
            public int ReviewerId { get; set; }
        }
        
        public class DenyRequestDto
        {
            [Required]
            public int ReviewerId { get; set; }
            
            [Required]
            [StringLength(500)]
            public string DenialReason { get; set; } = string.Empty;
        }
    }
}
