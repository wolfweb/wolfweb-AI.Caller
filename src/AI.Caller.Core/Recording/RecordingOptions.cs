namespace AI.Caller.Core.Recording
{
    
    public class RecordingOptions
    {
        
        public AudioCodec Codec { get; set; } = AudioCodec.PCM_WAV;
        
        
        public int SampleRate { get; set; } = 8000;
        
        
        public int Channels { get; set; } = 1;
        
        
        public int BitRate { get; set; } = 64000;
        
        
        public string OutputDirectory { get; set; } = "./recordings";
        
        
        public string FileNameTemplate { get; set; } = "{timestamp}_{caller}_{duration}";
        
        
        public bool AutoStart { get; set; } = false;
        
        
        public bool RecordBothParties { get; set; } = true;
        
        
        public TimeSpan MaxDuration { get; set; } = TimeSpan.FromHours(2);
        
        
        public long MaxFileSize { get; set; } = 100 * 1024 * 1024; // 100MB
        
        
        public AudioQuality Quality { get; set; } = AudioQuality.Standard;
        
        
        public bool EnableNoiseReduction { get; set; } = false;
        
        
        public bool EnableVolumeNormalization { get; set; } = false;
        
        
        public ValidationResult Validate()
        {
            var errors = new List<string>();
            
            if (SampleRate <= 0 || SampleRate > 192000)
                errors.Add("采样率必须在1-192000Hz之间");
                
            if (Channels <= 0 || Channels > 8)
                errors.Add("声道数必须在1-8之间");
                
            if (BitRate <= 0 || BitRate > 320000)
                errors.Add("比特率必须在1-320000bps之间");
                
            if (string.IsNullOrWhiteSpace(OutputDirectory))
                errors.Add("输出目录不能为空");
                
            if (string.IsNullOrWhiteSpace(FileNameTemplate))
                errors.Add("文件名模板不能为空");
                
            if (MaxDuration <= TimeSpan.Zero)
                errors.Add("最大录音时长必须大于0");
                
            if (MaxFileSize <= 0)
                errors.Add("最大文件大小必须大于0");
            
            return new ValidationResult(errors.Count == 0, errors);
        }
    }    
    
    
    public class ValidationResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }
        
        public ValidationResult(bool isValid, IList<string> errors)
        {
            IsValid = isValid;
            Errors = errors.ToList().AsReadOnly();
        }
    }
}