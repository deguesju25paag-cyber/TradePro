using System;
using System.Threading.Tasks;
using Moq;
using Xunit;
using Zerbitzaria.Dtos;
using Zerbitzaria.Models;
using Zerbitzaria.Services;

namespace Zerbitzaria.UnitTests
{
    public class LoginHandlerTests
    {
        [Fact]
        public async Task Login_ReturnsInvalidRequest_WhenUsernameEmpty()
        {
            var auth = new Mock<IAuthService>();
            var handler = new LoginHandler(auth.Object);

            var result = await handler.HandleAsync(string.Empty, "pass");

            Assert.Null(result.Response);
            Assert.Equal("invalid_request", result.Error?.Error);
        }

        [Fact]
        public async Task Login_ReturnsInvalidRequest_WhenPasswordEmpty()
        {
            var auth = new Mock<IAuthService>();
            var handler = new LoginHandler(auth.Object);

            var result = await handler.HandleAsync("user", string.Empty);

            Assert.Null(result.Response);
            Assert.Equal("invalid_request", result.Error?.Error);
        }

        [Fact]
        public async Task Login_ReturnsInvalidCredentials_WhenUserNotFound()
        {
            var auth = new Mock<IAuthService>();
            auth.Setup(a => a.ValidateUserAsync("user", "bad")).ReturnsAsync((User?)null);
            var handler = new LoginHandler(auth.Object);

            var result = await handler.HandleAsync("user", "bad");

            Assert.Null(result.Response);
            Assert.Equal("invalid_credentials", result.Error?.Error);
        }

        [Fact]
        public async Task Login_ReturnsOk_ForNormalUser()
        {
            var auth = new Mock<IAuthService>();
            auth.Setup(a => a.ValidateUserAsync("user", "1234"))
                .ReturnsAsync(new User { Id = 2, Username = "user", Balance = 5000m, PasswordHash = "hash" });
            auth.Setup(a => a.IsAdminAsync("user")).ReturnsAsync(false);
            var handler = new LoginHandler(auth.Object);

            var result = await handler.HandleAsync("user", "1234");

            Assert.Null(result.Error);
            Assert.Equal("user", result.Response?.Username);
            Assert.False(result.Response?.IsAdmin ?? true);
        }

        [Fact]
        public async Task Login_ReturnsOk_ForAdmin()
        {
            var auth = new Mock<IAuthService>();
            auth.Setup(a => a.ValidateUserAsync("admin", "admin1234"))
                .ReturnsAsync(new User { Id = 1, Username = "admin", Balance = 100000m, PasswordHash = "hash" });
            auth.Setup(a => a.IsAdminAsync("admin")).ReturnsAsync(true);
            var handler = new LoginHandler(auth.Object);

            var result = await handler.HandleAsync("admin", "admin1234");

            Assert.Null(result.Error);
            Assert.Equal("admin", result.Response?.Username);
            Assert.True(result.Response?.IsAdmin ?? false);
        }

        [Fact]
        public async Task Login_ReturnsDbError_WhenServiceThrows()
        {
            var auth = new Mock<IAuthService>();
            auth.Setup(a => a.ValidateUserAsync("user", "pass"))
                .ThrowsAsync(new Exception("db failure"));
            var handler = new LoginHandler(auth.Object);

            var result = await handler.HandleAsync("user", "pass");

            Assert.Null(result.Response);
            Assert.Equal("db_error", result.Error?.Error);
        }
    }
}
