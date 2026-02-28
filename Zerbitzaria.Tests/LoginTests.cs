using System;
using System.Linq;
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
        private readonly HttpClient _client;

        public LoginTests(ApiFactory factory)
        {
            _client = factory.CreateClient();
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
        public async Task Login_ReturnsOk_ForRegisteredUser()
        {
            var register = await _client.PostAsJsonAsync("/api/register", new { username = "test-user", password = "test-pass" });
            register.EnsureSuccessStatusCode();

            var response = await _client.PostAsJsonAsync("/api/login", new { username = "test-user", password = "test-pass" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
            Assert.Equal("test-user", payload?.Username);
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
                });
            }
        }
    }
}
