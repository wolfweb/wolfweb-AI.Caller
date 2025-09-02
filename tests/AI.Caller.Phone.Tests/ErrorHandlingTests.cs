using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AI.Caller.Phone.Services;
using AI.Caller.Phone.Data;
using SIPSorcery.SIP;
using AI.Caller.Core.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using System;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Tests
{
    public class ErrorHandlingTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly AppDbContext _context;
        private readonly SipService _sipService;

        public ErrorHandlingTests()
        {
            var services = new ServiceCollection();
            
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
            
            services.AddScoped<SipService>();
            services.AddSingleton<ApplicationContext>();
            services.AddLogging(builder => builder.AddConsole());
            
            _serviceProvider = services.BuildServiceProvider();
            _context = _serviceProvider.GetRequiredService<AppDbContext>();
            _sipService = _serviceProvider.GetRequiredService<SipService>();
        }

        [Fact]
        public async Task CleanupOrphanedSipClients_ShouldRemoveUnusedClients()
        {
            // Arrange
            var user = new User
            {
                Id = 1,
                Username = "testuser",
                SipUsername = "test@sip.com",
                SipPassword = "password"
            };
            
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var appContext = _serviceProvider.GetRequiredService<ApplicationContext>();
            
            // Simulate orphaned SipClient
            var sipClient = new SipClient("test@sip.com", "password", "sip.server.com");
            appContext.AddSipClient(1, sipClient);

            // Act
            await _sipService.CleanupOrphanedSipClientsAsync();

            // Assert
            var clients = appContext.GetAllSipClients();
            Assert.Empty(clients);
        }

        [Fact]
        public async Task HandleSipClientError_ShouldLogAndCleanup()
        {
            // Arrange
            var user = new User
            {
                Id = 1,
                Username = "testuser",
                SipUsername = "test@sip.com",
                SipPassword = "password"
            };
            
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var appContext = _serviceProvider.GetRequiredService<ApplicationContext>();
            var sipClient = new SipClient("test@sip.com", "password", "sip.server.com");
            appContext.AddSipClient(1, sipClient);

            var exception = new InvalidOperationException("Test error");

            // Act
            await _sipService.HandleSipClientErrorAsync(1, exception);

            // Assert
            var clients = appContext.GetAllSipClients();
            Assert.Empty(clients);
        }

        [Fact]
        public async Task RegisterUserAsync_WithInvalidData_ShouldThrowException()
        {
            // Arrange
            var user = new User
            {
                Id = 1,
                Username = "testuser",
                SipUsername = null, // Invalid
                SipPassword = "password"
            };
            
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sipService.RegisterUserAsync("testuser"));
        }

        [Fact]
        public async Task MakeCallAsync_WithUnregisteredUser_ShouldThrowException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sipService.MakeCallAsync("nonexistent@sip.com", "target@sip.com"));
        }

        public void Dispose()
        {
            _context?.Dispose();
            _serviceProvider?.Dispose();
        }
    }
}