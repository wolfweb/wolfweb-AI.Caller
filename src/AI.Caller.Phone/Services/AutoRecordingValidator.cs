using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 自动录音功能验证器
/// </summary>
public static class AutoRecordingValidator
{
    /// <summary>
    /// 验证默认自动录音设置
    /// </summary>
    public static async Task<bool> ValidateDefaultAutoRecordingAsync(AppDbContext dbContext, IRecordingService recordingService)
    {
        try
        {
            Console.WriteLine("开始验证默认自动录音设置...");

            // 创建测试用户
            var testUser = new User
            {
                Username = "test_auto_recording_user",
                SipUsername = "test_1001",
                SipPassword = "password123"
            };

            dbContext.Users.Add(testUser);
            await dbContext.SaveChangesAsync();

            Console.WriteLine($"创建测试用户: {testUser.Username}");

            // 模拟获取用户录音设置（这会触发默认设置的创建）
            var startResult = await recordingService.StartRecordingAsync("test_call_auto", testUser.Id, "1001", "1002");
            
            // 检查是否创建了默认设置
            var settings = await dbContext.RecordingSettings
                .FirstOrDefaultAsync(s => s.UserId == testUser.Id);

            if (settings == null)
            {
                Console.WriteLine("❌ 默认录音设置未创建");
                return false;
            }

            if (!settings.AutoRecording)
            {
                Console.WriteLine("❌ 默认自动录音设置为 false，应该为 true");
                return false;
            }

            Console.WriteLine("✅ 默认自动录音设置正确 (AutoRecording = true)");

            // 验证其他默认设置
            var expectedDefaults = new
            {
                StoragePath = "recordings",
                MaxRetentionDays = 30,
                MaxStorageSizeMB = 1024L,
                AudioFormat = "wav",
                AudioQuality = 44100,
                EnableCompression = true
            };

            var validationResults = new List<(string Property, bool IsValid)>
            {
                ("StoragePath", settings.StoragePath == expectedDefaults.StoragePath),
                ("MaxRetentionDays", settings.MaxRetentionDays == expectedDefaults.MaxRetentionDays),
                ("MaxStorageSizeMB", settings.MaxStorageSizeMB == expectedDefaults.MaxStorageSizeMB),
                ("AudioFormat", settings.AudioFormat == expectedDefaults.AudioFormat),
                ("AudioQuality", settings.AudioQuality == expectedDefaults.AudioQuality),
                ("EnableCompression", settings.EnableCompression == expectedDefaults.EnableCompression)
            };

            foreach (var (property, isValid) in validationResults)
            {
                if (isValid)
                {
                    Console.WriteLine($"✅ {property} 设置正确");
                }
                else
                {
                    Console.WriteLine($"❌ {property} 设置不正确");
                    return false;
                }
            }

            // 清理测试数据
            dbContext.Users.Remove(testUser);
            if (settings != null)
            {
                dbContext.RecordingSettings.Remove(settings);
            }
            await dbContext.SaveChangesAsync();

            Console.WriteLine("✅ 所有默认自动录音设置验证通过！");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 验证过程中发生异常: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 验证自动录音功能流程
    /// </summary>
    public static async Task<bool> ValidateAutoRecordingFlowAsync(SipService sipService)
    {
        try
        {
            Console.WriteLine("开始验证自动录音功能流程...");

            // 测试用户SIP用户名
            var testSipUsername = "test_1001";

            // 检查自动录音是否启用
            var isAutoRecordingEnabled = await sipService.IsAutoRecordingEnabledAsync(1);
            
            if (!isAutoRecordingEnabled)
            {
                Console.WriteLine("❌ 自动录音未启用");
                return false;
            }

            Console.WriteLine("✅ 自动录音已启用");

            // 测试录音状态获取
            var recordingStatus = await sipService.GetRecordingStatusAsync(testSipUsername);
            Console.WriteLine($"✅ 录音状态获取成功: {recordingStatus?.ToString() ?? "无状态"}");

            Console.WriteLine("✅ 自动录音功能流程验证通过！");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 自动录音功能流程验证失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 显示自动录音配置信息
    /// </summary>
    public static void DisplayAutoRecordingInfo()
    {
        Console.WriteLine("=== 自动录音配置信息 ===");
        Console.WriteLine("默认设置:");
        Console.WriteLine("  - 自动录音: 启用 (true)");
        Console.WriteLine("  - 存储路径: recordings");
        Console.WriteLine("  - 保留天数: 30 天");
        Console.WriteLine("  - 最大存储: 1024 MB");
        Console.WriteLine("  - 音频格式: wav");
        Console.WriteLine("  - 音频质量: 44100 Hz");
        Console.WriteLine("  - 启用压缩: 是");
        Console.WriteLine();
        Console.WriteLine("自动录音工作流程:");
        Console.WriteLine("  1. 用户发起通话");
        Console.WriteLine("  2. 系统检查用户自动录音设置");
        Console.WriteLine("  3. 如果启用，等待通话建立后自动开始录音");
        Console.WriteLine("  4. 通话结束时自动停止录音并保存文件");
        Console.WriteLine("  5. 通过 SignalR 实时通知前端录音状态");
        Console.WriteLine("========================");
    }
}