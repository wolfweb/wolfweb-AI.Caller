using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services;

/// <summary>
/// RecordingService 验证器 - 用于验证基本功能
/// </summary>
public static class RecordingServiceValidator
{
    /// <summary>
    /// 验证 RecordingService 的基本功能
    /// </summary>
    public static async Task<bool> ValidateBasicFunctionalityAsync(IRecordingService recordingService)
    {
        try
        {
            Console.WriteLine("开始验证 RecordingService 基本功能...");

            // 测试 1: 获取存储信息
            var storageInfo = await recordingService.GetStorageInfoAsync();
            Console.WriteLine($"存储信息获取成功 - 使用率: {storageInfo.UsagePercentage:F2}%");

            // 测试 2: 开始录音
            var startResult = await recordingService.StartRecordingAsync("test_call_123", 1, "1001", "1002");
            if (!startResult.Success)
            {
                Console.WriteLine($"开始录音失败: {startResult.Message}");
                return false;
            }
            Console.WriteLine($"开始录音成功 - CallId: {startResult.CallId}, RecordingId: {startResult.RecordingId}");

            // 测试 3: 获取录音状态
            var status = await recordingService.GetRecordingStatusAsync("test_call_123");
            if (status != RecordStatus.Recording)
            {
                Console.WriteLine($"录音状态不正确: {status}");
                return false;
            }
            Console.WriteLine($"录音状态正确: {status}");

            // 测试 4: 停止录音
            var stopResult = await recordingService.StopRecordingAsync("test_call_123");
            if (!stopResult.Success)
            {
                Console.WriteLine($"停止录音失败: {stopResult.Message}");
                return false;
            }
            Console.WriteLine($"停止录音成功 - Duration: {stopResult.Message}");

            // 测试 5: 获取录音记录
            if (startResult.RecordingId.HasValue)
            {
                var recording = await recordingService.GetRecordingAsync(startResult.RecordingId.Value);
                if (recording == null)
                {
                    Console.WriteLine("获取录音记录失败");
                    return false;
                }
                Console.WriteLine($"获取录音记录成功 - Status: {recording.Status}, Duration: {recording.Duration}");

                // 测试 6: 权限检查
                var hasPermission = await recordingService.HasRecordingPermissionAsync(1, startResult.RecordingId.Value);
                if (!hasPermission)
                {
                    Console.WriteLine("权限检查失败");
                    return false;
                }
                Console.WriteLine("权限检查成功");
            }

            // 测试 7: 获取录音列表
            var filter = new RecordingFilter { Page = 1, PageSize = 10 };
            var recordings = await recordingService.GetRecordingsAsync(1, filter);
            Console.WriteLine($"获取录音列表成功 - 总数: {recordings.TotalCount}, 当前页: {recordings.Items.Count()}");

            Console.WriteLine("所有基本功能验证通过！");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"验证过程中发生异常: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 验证错误处理
    /// </summary>
    public static async Task<bool> ValidateErrorHandlingAsync(IRecordingService recordingService)
    {
        try
        {
            Console.WriteLine("开始验证错误处理...");

            // 测试 1: 重复开始录音
            await recordingService.StartRecordingAsync("duplicate_call", 1, "1001", "1002");
            var duplicateResult = await recordingService.StartRecordingAsync("duplicate_call", 1, "1001", "1002");
            if (duplicateResult.Success)
            {
                Console.WriteLine("重复录音检查失败 - 应该返回失败");
                return false;
            }
            Console.WriteLine("重复录音检查通过");

            // 测试 2: 停止不存在的录音
            var stopNonExistentResult = await recordingService.StopRecordingAsync("non_existent_call");
            if (stopNonExistentResult.Success)
            {
                Console.WriteLine("停止不存在录音检查失败 - 应该返回失败");
                return false;
            }
            Console.WriteLine("停止不存在录音检查通过");

            // 测试 3: 获取不存在的录音
            var nonExistentRecording = await recordingService.GetRecordingAsync(99999);
            if (nonExistentRecording != null)
            {
                Console.WriteLine("获取不存在录音检查失败 - 应该返回null");
                return false;
            }
            Console.WriteLine("获取不存在录音检查通过");

            // 测试 4: 权限检查 - 无权限用户
            var noPermission = await recordingService.HasRecordingPermissionAsync(999, 1);
            if (noPermission)
            {
                Console.WriteLine("无权限用户检查失败 - 应该返回false");
                return false;
            }
            Console.WriteLine("无权限用户检查通过");

            Console.WriteLine("所有错误处理验证通过！");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误处理验证过程中发生异常: {ex.Message}");
            return false;
        }
    }
}