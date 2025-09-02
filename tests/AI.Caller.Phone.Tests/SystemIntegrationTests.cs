using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Xunit;
using AI.Caller.Phone.Services;
using AI.Caller.Phone.Data;
using SIPSorcery.SIP;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Hubs;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AI.Caller.Phone.Tests
{
    public class SystemIntegrationTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly AppDbContext _context;
        private readonly SipService _sipService;
        private readonly ApplicationContext _appContext;
        private readonly DataMigrationService _migrationService;

        public SystemIntegrationTests()
        {
            var services = new ServiceCollection();
            
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
            
            services.AddScoped<SipService>();
            services.AddScoped<DataMigrationService>();
            services.AddSingleton<ApplicationContext>();
            services.AddLogging(builder => builder.AddConsole());
            
            _serviceProvider = services.BuildServiceProvider();
            _context = _serviceProvider.GetRequiredService<AppDbContext>();
            _sipService = _serviceProvider.GetRequiredService<SipService>();
            _appContext = _serviceProvider.GetRequiredService<ApplicationContext>();
            _migrationService = _serviceProvider.GetRequiredService<DataMigrationService>();
        }

        [Fact]
        public async Task EndToEndWorkflow_MultipleUsersSharedSipAccount_ShouldWork()
        {
            // Arrange - 创建共享的SIP账户
            var sipAccount = new SipAccount
            {
                Id = 1,
                SipUsername = "shared@sip.com",
                SipPassword = "password123",
                SipServer = "sip.server.com",
                IsActive = true
            };
            _context.SipAccounts.Add(sipAccount);

            // 创建两个用户共享同一个SIP账户
            var user1 = new User
            {
                Id = 1,
                Username = "user1",
                SipAccountId = 1,
                SipUsername = "shared@sip.com", // 向后兼容字段
                SipPassword = "password123"
            };

            var user2 = new User
            {
                Id = 2,
                Username = "user2",
                SipAccountId = 1,
                SipUsername = "shared@sip.com", // 向后兼容字段
                SipPassword = "password123"
            };

            _context.Users.AddRange(user1, user2);
            await _context.SaveChangesAsync();

            // Act & Assert - 测试用户注册
            await _sipService.RegisterUserAsync("user1");
            await _sipService.RegisterUserAsync("user2");

            // 验证两个用户都成功注册，但共享同一个SIP连接
            var sipClients = _appContext.GetAllSipClients();
            Assert.Equal(2, sipClients.Count); // 两个用户各有一个SipClient实例
            
            // 验证用户可以通过用户名查找
            var client1 = _appContext.GetSipClientByUserId(1);
            var client2 = _appContext.GetSipClientByUserId(2);
            Assert.NotNull(client1);
            Assert.NotNull(client2);

            // 测试向后兼容的查找方式
            var clientByUsername = _appContext.GetSipClient("shared@sip.com");
            Assert.NotNull(clientByUsername);
        }

        [Fact]
        public async Task DataMigration_LegacyToNewModel_ShouldPreserveData()
        {
            // Arrange - 模拟旧数据结构
            var legacyUser = new User
            {
                Id = 1,
                Username = "legacy_user",
                SipUsername = "legacy@sip.com",
                SipPassword = "legacy_pass",
                SipAccountId = null // 旧数据没有SipAccountId
            };
            _context.Users.Add(legacyUser);
            await _context.SaveChangesAsync();

            // Act - 执行数据迁移
            await _migrationService.MigrateLegacySipDataAsync();

            // Assert - 验证迁移结果
            var migratedUser = await _context.Users
                .Include(u => u.SipAccount)
                .FirstAsync(u => u.Id == 1);

            Assert.NotNull(migratedUser.SipAccount);
            Assert.Equal("legacy@sip.com", migratedUser.SipAccount.SipUsername);
            Assert.Equal("legacy_pass", migratedUser.SipAccount.SipPassword);
            Assert.Equal("legacy@sip.com", migratedUser.SipUsername); // 保持向后兼容
        }

        [Fact]
        public async Task ConcurrentOperations_MultipleUsersSharedAccount_ShouldBeThreadSafe()
        {
            // Arrange
            var sipAccount = new SipAccount
            {
                Id = 1,
                SipUsername = "concurrent@sip.com",
                SipPassword = "password",
                SipServer = "sip.server.com",
                IsActive = true
            };
            _context.SipAccounts.Add(sipAccount);

            var users = new List<User>();
            for (int i = 1; i <= 5; i++)
            {
                users.Add(new User
                {
                    Id = i,
                    Username = $"user{i}",
                    SipAccountId = 1,
                    SipUsername = "concurrent@sip.com",
                    SipPassword = "password"
                });
            }
            _context.Users.AddRange(users);
            await _context.SaveChangesAsync();

            // Act - 并发注册多个用户
            var tasks = users.Select(u => _sipService.RegisterUserAsync(u.Username)).ToArray();
            await Task.WhenAll(tasks);

            // Assert - 验证所有用户都成功注册
            var sipClients = _appContext.GetAllSipClients();
            Assert.Equal(5, sipClients.Count);

            // 验证并发安全性 - 所有用户都应该能找到自己的SipClient
            for (int i = 1; i <= 5; i++)
            {
                var client = _appContext.GetSipClientByUserId(i);
                Assert.NotNull(client);
            }
        }

        [Fact]
        public async Task BackwardCompatibility_LegacyApiCalls_ShouldWork()
        {
            // Arrange
            var sipAccount = new SipAccount
            {
                Id = 1,
                SipUsername = "compat@sip.com",
                SipPassword = "password",
                SipServer = "sip.server.com",
                IsActive = true
            };
            _context.SipAccounts.Add(sipAccount);

            var user = new User
            {
                Id = 1,
                Username = "compat_user",
                SipAccountId = 1,
                SipUsername = "compat@sip.com",
                SipPassword = "password"
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Act - 使用旧的API调用方式
            await _sipService.RegisterUserAsync("compat_user");

            // 使用SipUsername进行呼叫（旧API）
            try
            {
                await _sipService.MakeCallAsync("compat@sip.com", "target@sip.com");
            }
            catch (InvalidOperationException)
            {
                // 预期异常，因为没有真实的SIP服务器
            }

            // Assert - 验证用户已注册
            var client = _appContext.GetSipClient("compat@sip.com");
            Assert.NotNull(client);
        }

        [Fact]
        public async Task ResourceCleanup_UserLogout_ShouldCleanupResources()
        {
            // Arrange
            var sipAccount = new SipAccount
            {
                Id = 1,
                SipUsername = "cleanup@sip.com",
                SipPassword = "password",
                SipServer = "sip.server.com",
                IsActive = true
            };
            _context.SipAccounts.Add(sipAccount);

            var user = new User
            {
                Id = 1,
                Username = "cleanup_user",
                SipAccountId = 1,
                SipUsername = "cleanup@sip.com",
                SipPassword = "password"
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Act - 注册然后注销用户
            await _sipService.RegisterUserAsync("cleanup_user");
            
            // 验证用户已注册
            var clientBefore = _appContext.GetSipClientByUserId(1);
            Assert.NotNull(clientBefore);

            // 注销用户
            await _sipService.UnregisterUserAsync(1);

            // Assert - 验证资源已清理
            var clientAfter = _appContext.GetSipClientByUserId(1);
            Assert.Null(clientAfter);
        }

        [Fact]
        public async Task PerformanceBaseline_MultipleOperations_ShouldMeetPerformanceRequirements()
        {
            // Arrange
            var sipAccount = new SipAccount
            {
                Id = 1,
                SipUsername = "perf@sip.com",
                SipPassword = "password",
                SipServer = "sip.server.com",
                IsActive = true
            };
            _context.SipAccounts.Add(sipAccount);

            var users = new List<User>();
            for (int i = 1; i <= 100; i++)
            {
                users.Add(new User
                {
                    Id = i,
                    Username = $"perfuser{i}",
                    SipAccountId = 1,
                    SipUsername = "perf@sip.com",
                    SipPassword = "password"
                });
            }
            _context.Users.AddRange(users);
            await _context.SaveChangesAsync();

            // Act - 测量性能
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var tasks = users.Select(u => _sipService.RegisterUserAsync(u.Username)).ToArray();
            await Task.WhenAll(tasks);
            
            stopwatch.Stop();

            // Assert - 验证性能要求（100个用户注册应在5秒内完成）
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
                $"Performance test failed: {stopwatch.ElapsedMilliseconds}ms > 5000ms");

            // 验证所有用户都成功注册
            var sipClients = _appContext.GetAllSipClients();
            Assert.Equal(100, sipClients.Count);
        }

        public void Dispose()
        {
            _context?.Dispose();
            _serviceProvider?.Dispose();
        }
    }
}