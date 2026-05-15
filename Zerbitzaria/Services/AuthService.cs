using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Zerbitzaria.Data;
using Zerbitzaria.Models;

namespace Zerbitzaria.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _db;

        public AuthService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<User?> ValidateUserAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == username).ConfigureAwait(false);
            if (user == null)
            {
                return null;
            }

            return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? user : null;
        }

        public async Task<bool> IsAdminAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

            return await _db.Users.AnyAsync(u => u.Username == username && u.Username == "admin").ConfigureAwait(false);
        }
    }
}
