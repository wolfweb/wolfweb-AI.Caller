namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// 播放状态
    /// </summary>
    public enum PlaybackState {
        /// <summary>
        /// 未开始
        /// </summary>
        NotStarted = 0,

        /// <summary>
        /// 播放中
        /// </summary>
        Playing = 1,

        /// <summary>
        /// 已暂停
        /// </summary>
        Paused = 2,

        /// <summary>
        /// 已中断（人工接入）
        /// </summary>
        Interrupted = 3,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed = 4,

        /// <summary>
        /// 失败
        /// </summary>
        Failed = 5
    }
}
