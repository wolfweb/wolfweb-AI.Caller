namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// 场景录音片段类型
    /// </summary>
    public enum SegmentType {
        /// <summary>
        /// 录音文件
        /// </summary>
        Recording = 1,

        /// <summary>
        /// TTS语音
        /// </summary>
        TTS = 2,

        /// <summary>
        /// DTMF输入
        /// </summary>
        DtmfInput = 3,

        /// <summary>
        /// 条件分支
        /// </summary>
        Condition = 4,

        /// <summary>
        /// 静音
        /// </summary>
        Silence = 5
    }
}
