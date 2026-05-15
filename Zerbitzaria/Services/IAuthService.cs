using System.Threading.Tasks;
using Zerbitzaria.Models;

namespace Zerbitzaria.Services
{
    public interface IAuthService
    {
        Task<User?> ValidateUserAsync(string username, string password);
        Task<bool> IsAdminAsync(string username);
    }
}
