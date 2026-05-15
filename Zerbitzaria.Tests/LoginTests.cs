using System;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zerbitzaria.Data;
using Zerbitzaria.Dtos;
using Zerbitzaria.Services;
using Xunit;

namespace Zerbitzaria.Tests
{
    public sealed class LoginTests : IClassFixture<LoginTests.ApiFactory>
    {
        private readonly ApiFactory _factory;
        private readonly HttpClient _client;

        public LoginTests(ApiFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
        }

        [Fact]
        public void Database_IsInMemory_ForTests()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True(db.Database.IsInMemory());
        }

        [Fact]
        public async Task Login_ReturnsBadRequest_ForInvalidCredentials()
        {
            var response = await _client.PostAsJsonAsync("/api/login", new { username = "no-user", password = "bad" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<ErrorResponseDto>();
            Assert.Equal("invalid_credentials", error?.Error);
        }

        [Fact]
        public async Task Login_ReturnsOk_ForHttp()
        {
            var response = await _client.PostAsJsonAsync("/api/login", new { username = "user", password = "1234" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Login_ReturnsOk_ForUser()
        {
            var response = await _client.PostAsJsonAsync("/api/login", new { username = "user", password = "1234" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            Assert.Equal("user", payload?.Username);
            Assert.False(payload?.IsAdmin ?? true);
        }

        [Fact]
        public async Task Login_ReturnsOk_ForAdmin()
        {
            var response = await _client.PostAsJsonAsync("/api/login", new { username = "admin", password = "admin1234" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            Assert.Equal("admin", payload?.Username);
            Assert.True(payload?.IsAdmin ?? false);
        }

        public sealed class ApiFactory : WebApplicationFactory<Program>
        {
            private readonly string _dbName = Guid.NewGuid().ToString("N");

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureServices(services =>
                {
                    var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
                    if (dbDescriptor != null)
                    {
                        services.Remove(dbDescriptor);
                    }

                    services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(_dbName));

                    var hostedServices = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
                    foreach (var service in hostedServices)
                    {
                        if (service.ImplementationType == typeof(PriceUpdaterService) || service.ImplementationType == typeof(TcpServerHostedService))
                        {
                            services.Remove(service);
                        }
                    }

                    var provider = services.BuildServiceProvider();
                    using var scope = provider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    db.Database.EnsureCreated();
                    if (!db.Users.Any())
                    {
                        var adminPwd = BCrypt.Net.BCrypt.HashPassword("admin1234");
                        var userPwd = BCrypt.Net.BCrypt.HashPassword("1234");
                        db.Users.AddRange(
                            new Zerbitzaria.Models.User { Username = "admin", PasswordHash = adminPwd, Balance = 100000m },
                            new Zerbitzaria.Models.User { Username = "user", PasswordHash = userPwd, Balance = 5000m }
                        );
                        db.SaveChanges();
                    }
                });
            }
        }
    }
}
