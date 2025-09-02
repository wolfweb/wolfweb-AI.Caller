using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AI.Caller.Phone.Tests {
    public class SipAccountTests : IDisposable {
        private readonly AppDbContext _dbContext;
        private readonly DataMigrationService _migrationService;
        private readonly Mock<ILogger<DataMigrationService>> _mockLogger;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly IServiceProvider _serviceProvider;

        public SipAccountTests() {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(_mockConfiguration.Object);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _mockLogger = new Mock<ILogger<DataMigrationService>>();
            _mockConfiguration = new Mock<IConfiguration>();
            var serviceProvider = services.BuildServiceProvider();
            _serviceProvider = serviceProvider;
            var serviceScopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            _dbContext = new AppDbContext(options, _mockConfiguration.Object);
            _migrationService = new DataMigrationService(_mockLogger.Object, serviceScopeFactory);
        }

        [Fact]
        public async Task SipAccount_CRUD_Operations_Should_Work() {
            var sipAccount = new SipAccount {
                SipUsername = "test@example.com",
                SipPassword = "password123",
                SipServer = "sip.example.com",
                IsActive = true
            };

            _dbContext.SipAccounts.Add(sipAccount);
            await _dbContext.SaveChangesAsync();

            Assert.True(sipAccount.Id > 0);
            Assert.Equal("test@example.com", sipAccount.SipUsername);

            var retrievedAccount = await _dbContext.SipAccounts
                .FirstOrDefaultAsync(s => s.SipUsername == "test@example.com");

            Assert.NotNull(retrievedAccount);
            Assert.Equal(sipAccount.Id, retrievedAccount.Id);

            retrievedAccount.SipServer = "updated.sip.server";
            await _dbContext.SaveChangesAsync();

            var updatedAccount = await _dbContext.SipAccounts.FindAsync(sipAccount.Id);
            Assert.Equal("updated.sip.server", updatedAccount?.SipServer);

            _dbContext.SipAccounts.Remove(updatedAccount!);
            await _dbContext.SaveChangesAsync();

            var deletedAccount = await _dbContext.SipAccounts.FindAsync(sipAccount.Id);
            Assert.Null(deletedAccount);
        }

        [Fact]
        public async Task User_SipAccount_Relationship_Should_Work() {
            var sipAccount1 = new SipAccount {
                SipUsername = "sip1@example.com",
                SipPassword = "sippass1",
                SipServer = "sip.server.com",
                IsActive = true
            };

            var sipAccount2 = new SipAccount {
                SipUsername = "sip2@example.com",
                SipPassword = "sippass2",
                SipServer = "sip.server.com",
                IsActive = true
            };

            _dbContext.SipAccounts.AddRange(sipAccount1, sipAccount2);
            await _dbContext.SaveChangesAsync();

            var users = new[]
            {
                new User
                {
                    Username = "user1",
                    Password = "pass1",
                    SipAccountId = sipAccount1.Id,
                    SipAccount = sipAccount1
                },
                new User
                {
                    Username = "user2",
                    Password = "pass2",
                    SipAccountId = sipAccount1.Id,
                    SipAccount = sipAccount1
                },
                new User
                {
                    Username = "user3",
                    Password = "pass3",
                    SipAccountId = sipAccount2.Id,
                    SipAccount = sipAccount2
                }
            };

            _dbContext.Users.AddRange(users);
            await _dbContext.SaveChangesAsync();

            var retrievedSipAccount = await _dbContext.SipAccounts
                .Include(s => s.Users)
                .FirstOrDefaultAsync(s => s.Id == sipAccount1.Id);

            Assert.NotNull(retrievedSipAccount);
            Assert.Equal(2, retrievedSipAccount.Users.Count);
            Assert.Contains(retrievedSipAccount.Users, u => u.Username == "user1");
            Assert.Contains(retrievedSipAccount.Users, u => u.Username == "user2");

            var user1 = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == "user1");
            var user2 = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == "user2");
            Assert.Equal(sipAccount1.Id, user1.SipAccountId);
            Assert.Equal(sipAccount1.Id, user2.SipAccountId);
        }

        [Fact]
        public async Task DataMigration_Should_Create_SipAccounts_From_Users() {            
            var sipAccount1 = new SipAccount {
                SipUsername = "sip1@example.com",
                SipPassword = "sippass1",
                SipServer = "sip.example.com",
                IsActive = true
            };

            var sipAccount2 = new SipAccount {
                SipUsername = "sip2@example.com",
                SipPassword = "sippass2",
                SipServer = "sip.example.com",
                IsActive = true
            };

            _dbContext.SipAccounts.AddRange(sipAccount1, sipAccount2);
            await _dbContext.SaveChangesAsync();

            var users = new[]
            {
                new User
                {
                    Username = "user1",
                    Password = "pass1",
                    SipAccountId = sipAccount1.Id,
                    SipAccount = sipAccount1
                },
                new User
                {
                    Username = "user2",
                    Password = "pass2",
                    SipAccountId = sipAccount1.Id,
                    SipAccount = sipAccount1
                },
                new User
                {
                    Username = "user3",
                    Password = "pass3",
                    SipAccountId = sipAccount2.Id,
                    SipAccount = sipAccount2
                }
            };

            _dbContext.Users.AddRange(users);
            await _dbContext.SaveChangesAsync();

            var result = await _migrationService.MigrateUserSipDataToSipAccountAsync();

            Assert.True(result);

            var sipAccounts = await _dbContext.SipAccounts.ToListAsync();
            Assert.Equal(2, sipAccounts.Count); 

            var migratedUsers = await _dbContext.Users
                .Include(u => u.SipAccount)
                .ToListAsync();

            Assert.All(migratedUsers, u => Assert.NotNull(u.SipAccount));

            var user1 = migratedUsers.First(u => u.Username == "user1");
            var user2 = migratedUsers.First(u => u.Username == "user2");
            Assert.Equal(user1.SipAccountId, user2.SipAccountId);
        }

        public void Dispose() {
            _dbContext.Dispose();
        }
    }
}