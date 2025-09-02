using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Phone.Services {
    public class DataMigrationService {
        private readonly ILogger<DataMigrationService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public DataMigrationService(
            ILogger<DataMigrationService> logger,
            IServiceScopeFactory serviceScopeFactory) {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task<bool> MigrateUserSipDataToSipAccountAsync() {
            try {
                _logger.LogInformation("开始迁移用户SIP数据到SipAccount表");

                using var scope = _serviceScopeFactory.CreateScope();
                var _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 检查是否还有未迁移的用户（通过SipAccountId为null来判断）
                var usersToMigrate = await _dbContext.Users
                    .Where(u => u.SipAccountId == null)
                    .ToListAsync();

                if (!usersToMigrate.Any()) {
                    _logger.LogInformation("没有需要迁移的用户数据");
                    return true;
                }

                _logger.LogInformation($"找到 {usersToMigrate.Count} 个用户需要关联SipAccount");

                // 为没有SipAccount的用户分配默认的SipAccount
                foreach (var user in usersToMigrate) {
                    // 查找是否有可用的SipAccount
                    var availableSipAccount = await _dbContext.SipAccounts
                        .Where(s => s.IsActive)
                        .FirstOrDefaultAsync();

                    if (availableSipAccount != null) {
                        user.SipAccountId = availableSipAccount.Id;
                        _logger.LogInformation($"用户 {user.Username} 已关联到SipAccount {availableSipAccount.SipUsername}");
                    } else {
                        _logger.LogWarning($"用户 {user.Username} 无法找到可用的SipAccount");
                    }
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("用户SIP数据迁移完成");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, "迁移用户SIP数据时发生错误");
                return false;
            }
        }

        public async Task<int> GetUnmigratedUserCountAsync() {
            using var scope = _serviceScopeFactory.CreateScope();
            var _dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var unmigrated = await _dbContext.Users
                .Where(u => u.SipAccountId == null)
                .CountAsync();

            return unmigrated;
        }

        /// <summary>
        /// 验证数据迁移是否成功
        /// </summary>
        public async Task<bool> ValidateMigrationAsync() {
            try {
                _logger.LogInformation("开始验证数据迁移");

                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // 检查是否还有未迁移的用户
                var unmigratedUsers = await dbContext.Users
                    .Where(u => u.SipAccountId == null)
                    .CountAsync();

                if (unmigratedUsers > 0) {
                    _logger.LogWarning($"仍有 {unmigratedUsers} 个用户未迁移");
                    return false;
                }

                // 检查用户是否正确关联到SipAccount
                var usersWithoutAccount = await dbContext.Users
                    .Where(u => u.SipAccountId.HasValue && u.SipAccount == null)
                    .CountAsync();

                if (usersWithoutAccount > 0) {
                    _logger.LogWarning($"有 {usersWithoutAccount} 个用户关联了无效的SipAccountId");
                    return false;
                }

                _logger.LogInformation("数据迁移验证通过");
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, "验证数据迁移时发生错误");
                return false;
            }
        }
    }
}