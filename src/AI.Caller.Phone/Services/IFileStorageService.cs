using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 文件存储服务接口
/// </summary>
public interface IFileStorageService {
    /// <summary>
    /// 创建录音文件
    /// </summary>
    /// <param name="callId">通话ID</param>
    /// <param name="audioFormat">音频格式</param>
    /// <returns>文件路径</returns>
    Task<string> CreateRecordingFileAsync(string callId, string audioFormat);

    /// <summary>
    /// 完成录音文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否成功</returns>
    Task<bool> FinalizeRecordingFileAsync(string filePath);

    /// <summary>
    /// 获取文件流
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文件流</returns>
    Task<Stream> GetFileStreamAsync(string filePath);

    /// <summary>
    /// 删除文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否成功</returns>
    Task<bool> DeleteFileAsync(string filePath);

    /// <summary>
    /// 获取文件大小
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文件大小（字节）</returns>
    Task<long> GetFileSizeAsync(string filePath);

    /// <summary>
    /// 获取存储信息
    /// </summary>
    /// <returns>存储使用情况</returns>
    Task<StorageInfo> GetStorageInfoAsync();

    /// <summary>
    /// 确保存储目录存在
    /// </summary>
    /// <param name="path">目录路径</param>
    /// <returns>是否成功</returns>
    Task<bool> EnsureDirectoryExistsAsync(string path);

    /// <summary>
    /// 保存上传的文件
    /// </summary>
    /// <param name="file">上传的文件</param>
    /// <param name="subDirectory">子目录</param>
    /// <returns>文件路径</returns>
    Task<string> SaveFileAsync(IFormFile file, string subDirectory);
}