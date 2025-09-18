using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services {
    public interface ITtsCallDocumentService {
        /// <summary>
        /// 生成TTS外呼模板文件
        /// </summary>
        Task<byte[]> GenerateTemplateAsync();
        
        /// <summary>
        /// 上传并解析TTS外呼文档
        /// </summary>
        Task<TtsCallDocument> UploadDocumentAsync(IFormFile file, int userId);
        
        /// <summary>
        /// 启动TTS外呼任务
        /// </summary>
        Task<bool> StartCallTaskAsync(int documentId);
        
        /// <summary>
        /// 暂停TTS外呼任务
        /// </summary>
        Task<bool> PauseCallTaskAsync(int documentId);
        
        /// <summary>
        /// 恢复TTS外呼任务
        /// </summary>
        Task<bool> ResumeCallTaskAsync(int documentId);
        
        /// <summary>
        /// 停止TTS外呼任务
        /// </summary>
        Task<bool> StopCallTaskAsync(int documentId);
        
        /// <summary>
        /// 获取用户的TTS外呼文档列表
        /// </summary>
        Task<List<TtsCallDocument>> GetUserDocumentsAsync(int userId);
        
        /// <summary>
        /// 获取TTS外呼文档详情
        /// </summary>
        Task<TtsCallDocument?> GetDocumentAsync(int documentId);
        
        /// <summary>
        /// 删除TTS外呼文档
        /// </summary>
        Task<bool> DeleteDocumentAsync(int documentId);
    }
}