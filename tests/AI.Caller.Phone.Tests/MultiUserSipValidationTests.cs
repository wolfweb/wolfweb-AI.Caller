using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Xunit;
using AI.Caller.Phone.Services;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using System;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Tests {
    public class MultiUserSipValidationTests : IDisposable {
        private readonly ServiceProvider _serviceProvider;
        private readonly AppDbContext _context;
        private readonly SipService _sipService;
        private readonly ApplicationContext _appContext;

        public MultiUserSipValidationTests() {
            var services = new ServiceCollection();

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

            services.AddLogging(builder => builder.AddConsole());

            services.AddSingleton<ApplicationContext>();
            services.AddScoped<SipService>();
            services.AddScoped<DataMigrationService>();

            _serviceProvider = services.BuildServiceProvider();
            _context = _serviceProvider.GetRequiredService<AppDbContext>();
            _sipService = _serviceProvider.GetRequiredService<SipService>();
            _appContext = _serviceProvider.GetRequiredService<ApplicationContext>();
        }

        [Fact]
        public async Task MultiUser_SipAccount_Should_Work() {
            var sipAccount = new SipAccount {
                SipUsername = "shared@test.com",
                SipPassword = "password123",
                SipServer = "sip.test.com",
                IsActive = true
            };

            _context.SipAccounts.Add(sipAccount);
            await _context.SaveChangesAsync();

            var user1 = new User {
                Username = "user1",
                SipAccountId = sipAccount.Id
            };

            var user2 = new User {
                Username = "user2",
                SipAccountId = sipAccount.Id
            };

            _context.Users.AddRange(user1, user2);
            await _context.SaveChangesAsync();

            await _sipService.RegisterUserAsync(user1);
            await _sipService.RegisterUserAsync(user2);

            var client1 = _appContext.GetSipClientByUserId(user1.Id);
            var client2 = _appContext.GetSipClientByUserId(user2.Id);

            Assert.NotNull(client1);
            Assert.NotNull(client2);
            Assert.NotEqual(client1, client2); 

            var allClients = _appContext.GetAllSipClients();
            Assert.Equal(2, allClients.Count);
        }

        [Fact]
        public async Task DataMigration_Should_Work() {
            var sipAccount = new SipAccount {
                SipUsername = "shared@sip.com",
                SipPassword = "sharedpass",
                SipServer = "sip.server.com",
                IsActive = true
            };

            _context.SipAccounts.Add(sipAccount);
            await _context.SaveChangesAsync();

            var user1 = new User {
                Username = "user1",
                SipAccountId = sipAccount.Id,
                SipAccount = sipAccount
            };

            var user2 = new User {
                Username = "user2",
                SipAccountId = sipAccount.Id,
                SipAccount = sipAccount
            };

            _context.Users.AddRange(user1, user2);
            await _context.SaveChangesAsync();

            Assert.Equal(sipAccount.Id, user1.SipAccountId);
            Assert.Equal(sipAccount.Id, user2.SipAccountId);
            Assert.Equal("shared@sip.com", user1.SipAccount?.SipUsername);
            Assert.Equal("shared@sip.com", user2.SipAccount?.SipUsername);
        }

        public void Dispose() {
            _context?.Dispose();
            _serviceProvider?.Dispose();
        }
    }
}