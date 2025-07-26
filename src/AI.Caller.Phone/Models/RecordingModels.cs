using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Models;

/// <summary>
/// 录音操作结果
/// </summary>
public class RecordingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? CallId { get; set; }
    public int? RecordingId { get; set; }

    public static RecordingResult CreateSuccess(string callId, int recordingId, string message) =>
        new() { Success = true, CallId = callId, RecordingId = recordingId, Message = message };

    public static RecordingResult CreateFailure(string callId, string message) =>
        new() { Success = false, CallId = callId, Message = message };
}

/// <summary>
/// 录音查询过滤器
/// </summary>
public class RecordingFilter
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? CallerNumber { get; set; }
    public string? CalleeNumber { get; set; }
    public RecordingStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>
/// 分页结果
/// </summary>
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

/// <summary>
/// 存储信息
/// </summary>
public class StorageInfo
{
    public long TotalSizeMB { get; set; }
    public long UsedSizeMB { get; set; }
    public long AvailableSizeMB { get; set; }
    public int RecordingCount { get; set; }
    public double UsagePercentage => TotalSizeMB > 0 ? (double)UsedSizeMB / TotalSizeMB * 100 : 0;
}