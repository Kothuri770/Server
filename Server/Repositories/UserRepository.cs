using Dapper;
using Server.Models;
using System.Security.Cryptography;
using System.Text;

namespace Server.Repositories
{
    public interface IUserRepository
    {
        Task<UserDto?> ValidateCredentialsAsync(string userName, string password);
        Task<bool> ChangePasswordAsync(string userName, string oldPassword, string newPassword);
        Task<IEnumerable<UserDto>> GetUsersAsync();
        Task<int> CreateUserAsync(CreateUserRequest request);
        Task UpdateUserAsync(UpdateUserRequest request);
        Task DeleteUserAsync(string userName);
    }
    public class UserRepository : BaseRepository, IUserRepository
    {
        public UserRepository(string connectionString, string provider) : base(connectionString, provider) { }

        public async Task<UserDto?> ValidateCredentialsAsync(string userName, string password)
        {
            using var conn = CreateConnection();
            var user = await conn.QuerySingleOrDefaultAsync<UserDto>(
                "SELECT UserName, UserType, Password, CreatedOn, ViewLimit, IsEnabled FROM UsersCredentials WHERE UserName = @UserName",
                new { UserName = userName });

            if (user == null) return null;

            return VerifyPassword(password, user.Password) ? user : null;
        }

        public async Task<bool> ChangePasswordAsync(string userName, string oldPassword, string newPassword)
        {
            using var conn = CreateConnection();
            var user = await ValidateCredentialsAsync(userName, oldPassword);
            if (user == null) return false;

            var hashedPassword = HashPassword(newPassword);
            await conn.ExecuteAsync("UPDATE UsersCredentials SET Password = @Password WHERE UserName = @UserName",
                new { Password = hashedPassword, UserName = userName });

            return true;
        }

        public async Task<IEnumerable<UserDto>> GetUsersAsync()
        {
            using var conn = CreateConnection();
            
            var users = await conn.QueryAsync<UserDto>(
                "SELECT UserName, UserType, CreatedOn, ViewLimit, IsEnabled FROM UsersCredentials ORDER BY UserName");
            
            var userRoleAssignments = await conn.QueryAsync<dynamic>(
                "SELECT UserName, RoleId FROM UserRoleAssignments");
            
            var allRoles = await conn.QueryAsync<dynamic>("SELECT RoleId, RoleName FROM UserRoles");
            
            // Group role assignments by username, filtering out null RoleIds
            var roleAssignments = userRoleAssignments
                .Where(r => r.RoleId != null)
                .GroupBy(r => r.UserName)
                .ToDictionary(g => g.Key, g => g.Select(r => (int)r.RoleId).ToList());
            
            // Assign role IDs to each user
            var userList = users.ToList();
            foreach (var user in userList)
            {
                // Initialize RoleIds for each user
                user.RoleIds = new List<int>();
                
                if (roleAssignments.ContainsKey(user.UserName))
                {
                    user.RoleIds = roleAssignments[user.UserName];
                }
            }
            
            return userList;
        }

        public async Task<int> CreateUserAsync(CreateUserRequest request)
        {
            var userType = string.IsNullOrWhiteSpace(request.UserType) ? "user" : request.UserType;
            using var conn = CreateConnection();
            var hashedPassword = HashPassword(request.Password);
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"INSERT INTO UsersCredentials (UserName, Password, UserType, ViewLimit, IsEnabled, CreatedOn) 
                        OUTPUT 1
                        VALUES (@UserName, @Password, @UserType, @ViewLimit, @IsEnabled, CURRENT_TIMESTAMP)";
            }
            else
            {
                sql = @"INSERT INTO UsersCredentials (UserName, Password, UserType, ViewLimit, IsEnabled, CreatedOn) 
                        VALUES (@UserName, @Password, @UserType, @ViewLimit, @IsEnabled, CURRENT_TIMESTAMP)
                        RETURNING 1";
            }
            return await conn.ExecuteScalarAsync<int>(sql, new { request.UserName, Password = hashedPassword, UserType = userType, request.ViewLimit, request.IsEnabled });
        }

        public async Task UpdateUserAsync(UpdateUserRequest request)
        {
            var userType = string.IsNullOrWhiteSpace(request.UserType) ? "user" : request.UserType;
            using var conn = CreateConnection();
            await conn.ExecuteAsync("UPDATE UsersCredentials SET UserType = @UserType, ViewLimit = @ViewLimit, IsEnabled = @IsEnabled WHERE UserName = @UserName",
                new { request.UserName, UserType = userType, request.ViewLimit, request.IsEnabled });
        }

        public async Task DeleteUserAsync(string userName)
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync("DELETE FROM UsersCredentials WHERE UserName = @UserName", new { UserName = userName });
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        private bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }
    }
}
