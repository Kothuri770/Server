using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Repositories;
using Server.Services.Configuration;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IConfigurationService _configService;

        public UsersController(IUserRepository userRepository, IConfigurationService configService)
        {
            _userRepository = userRepository;
            _configService = configService;
        }

        [HttpGet]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
        {
            try
            {
                var users = await _userRepository.GetUsersAsync();
                var userList = users.ToList();

                // Get role assignments for each user
                foreach (var user in userList)
                {
                    user.RoleIds = (await _configService.GetUserRoleAssignmentsAsync(user.UserName))?.ToList() ?? new List<int>();
                }

                return Ok(userList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving users: {ex.Message}");
            }
        }

        [HttpGet("{username}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<UserDto>> GetUser(string username)
        {
            try
            {
                // Note: We can't get a single user by username from the current repository
                // So we get all users and filter
                var users = (await _userRepository.GetUsersAsync()).ToList();
                var user = users.FirstOrDefault(u => u.UserName.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (user == null)
                {
                    return NotFound($"User '{username}' not found");
                }

                // Get role assignments for the user
                user.RoleIds = (await _configService.GetUserRoleAssignmentsAsync(user.UserName))?.ToList() ?? new List<int>();

                return Ok(user);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving user: {ex.Message}");
            }
        }

        [HttpPost]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> CreateUser([FromBody] UserCreationRequest request)
        {
            if (string.IsNullOrEmpty(request.UserName) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest("Username and password are required");
            }

            try
            {
                // Create the user
                var createUserRequest = new CreateUserRequest
                {
                    UserName = request.UserName,
                    Password = request.Password,
                    UserType = request.UserType
                };

                await _userRepository.CreateUserAsync(createUserRequest);

                // Assign roles if specified
                if (request.RoleIds != null && request.RoleIds.Any())
                {
                    await _configService.AssignUserRolesAsync(request.UserName, request.RoleIds);
                }

                return Ok(new { message = "User created successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error creating user: {ex.Message}");
            }
        }

        [HttpPut("{username}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> UpdateUser(string username, [FromBody] UserDto user)
        {
            if (username != user.UserName)
            {
                return BadRequest("Username mismatch");
            }

            try
            {
                // Update basic user info
                var updateRequest = new UpdateUserRequest
                {
                    UserName = user.UserName,
                    UserType = user.UserType,
                    ViewLimit = user.ViewLimit,
                    IsEnabled = user.IsEnabled
                };
                
                await _userRepository.UpdateUserAsync(updateRequest);

                // Update role assignments if provided
                if (user.RoleIds != null)
                {
                    await _configService.AssignUserRolesAsync(user.UserName, user.RoleIds);
                }

                return Ok(new { message = "User updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating user: {ex.Message}");
            }
        }

        [HttpPut("{username}/details")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> UpdateUserDetails(string username, [FromBody] UserDto user)
        {
            if (username != user.UserName)
            {
                return BadRequest("Username mismatch");
            }

            try
            {
                // Update user details using repository method
                var updateRequest = new UpdateUserRequest
                {
                    UserName = user.UserName,
                    UserType = user.UserType,
                    ViewLimit = user.ViewLimit,
                    IsEnabled = user.IsEnabled
                };
                
                await _userRepository.UpdateUserAsync(updateRequest);

                // Update role assignments
                if (user.RoleIds != null)
                {
                    await _configService.AssignUserRolesAsync(user.UserName, user.RoleIds);
                }

                return Ok(new { message = "User details updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating user details: {ex.Message}");
            }
        }

        [HttpPut("{username}/password")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> ChangeUserPassword(string username, [FromBody] string newPassword)
        {
            try
            {
                var success = await _userRepository.ChangePasswordAsync(username, "", newPassword);
                if (!success)
                {
                    return BadRequest("Failed to change password");
                }

                return Ok(new { message = "Password updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error changing password: {ex.Message}");
            }
        }

        [HttpDelete("{username}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> DeleteUser(string username)
        {
            try
            {
                await _userRepository.DeleteUserAsync(username);
                return Ok(new { message = "User deleted successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting user: {ex.Message}");
            }
        }
    }
}
