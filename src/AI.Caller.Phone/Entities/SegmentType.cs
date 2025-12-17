namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// 场景录音片段类型
    /// </summary>
    public enum SegmentType {
        /// <summary>
        /// 录音文件
        /// </summary>
        Recording ,

        /// <summary>
        /// TTS语音
        /// </summary>
        TTS ,

        /// <summary>
        /// DTMF输入
        /// </summary>
        DtmfInput,

        /// <summary>
        /// 条件分支
        /// </summary>
        Condition ,

        /// <summary>
        /// 静音
        /// </summary>
        Silence
    }
}
