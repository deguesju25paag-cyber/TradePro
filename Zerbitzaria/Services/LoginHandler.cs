using System;
using System.Threading.Tasks;
using Zerbitzaria.Dtos;

namespace Zerbitzaria.Services
{
    public class LoginHandler
    {
        private readonly IAuthService _authService;

        public LoginHandler(IAuthService authService)
        {
            _authService = authService;
        }

        public async Task<(LoginResponseDto? Response, ErrorResponseDto? Error)> HandleAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (null, new ErrorResponseDto("invalid_request", "Username and password required"));
            }

            try
            {
                var user = await _authService.ValidateUserAsync(username, password).ConfigureAwait(false);
                if (user == null)
                {
                    return (null, new ErrorResponseDto("invalid_credentials", "Erabiltzailea edo pasahitza okerra"));
                }

                var isAdmin = await _authService.IsAdminAsync(user.Username).ConfigureAwait(false);
                return (new LoginResponseDto(user.Username, user.Balance, user.Id, isAdmin), null);
            }
            catch (Exception ex)
            {
                return (null, new ErrorResponseDto("db_error", ex.Message));
            }
        }
    }
}
