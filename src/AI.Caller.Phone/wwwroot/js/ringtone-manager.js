/**
 * 铃音管理器 - 仅负责来电铃音
 * 回铃音由后端通过 SIP/RTP 发送，不在前端播放
 */
class RingtoneManager {
    constructor() {
        // 来电铃音
        this.incomingAudio = null;
        this.isIncomingPlaying = false;
        this.defaultRingtone = '/ringtones/default.mp3';

        console.log('RingtoneManager 初始化 (仅来电铃音，回铃音由后端 SIP/RTP 处理)');
    }

    /**
     * 初始化音频对象
     */
    initialize() {
        try {
            // 初始化来电铃音
            this.incomingAudio = new Audio(this.defaultRingtone);
            this.incomingAudio.loop = true;
            this.incomingAudio.volume = 0.7;
            this.incomingAudio.load();

            console.log('来电铃音音频对象已创建:', this.defaultRingtone);
            return true;
        } catch (error) {
            console.error('铃音初始化失败:', error);
            return false;
        }
    }

    /**
     * 播放来电铃音（被叫方）
     */
    play() {
        if (this.isIncomingPlaying) {
            console.warn('来电铃音已在播放中');
            return;
        }

        // 如果音频对象未初始化，先初始化
        if (!this.incomingAudio) {
            if (!this.initialize()) {
                console.error('无法初始化来电铃音');
                return;
            }
        }

        console.log('开始播放来电铃音');

        this.incomingAudio.play()
            .then(() => {
                this.isIncomingPlaying = true;
                console.log('来电铃音播放成功');
            })
            .catch(err => {
                console.error('来电铃音播放失败:', err);

                // 提示用户可能需要交互
                if (err.name === 'NotAllowedError') {
                    console.warn('浏览器阻止了自动播放，可能需要用户先与页面交互');
                    this.showPlaybackError('来电铃音');
                }
            });
    }

    /**
     * 停止来电铃音
     */
    stop() {
        if (!this.isIncomingPlaying) {
            console.log('来电铃音未在播放，无需停止');
            return;
        }

        if (this.incomingAudio) {
            console.log('停止来电铃音播放');
            this.incomingAudio.pause();
            this.incomingAudio.currentTime = 0;
            this.isIncomingPlaying = false;
        }
    }

    /**
     * 显示播放错误提示
     */
    showPlaybackError(type = '铃音') {
        // 可以在页面上显示一个提示
        const message = `${type}播放被浏览器阻止，请先点击页面任意位置以激活音频`;
        console.warn(message);

        // 尝试在页面上显示提示（如果有状态显示区域）
        if (typeof window.showNotification === 'function') {
            window.showNotification(message, 'warning');
        }
    }

    /**
     * 设置音量 (0-1)
     */
    setVolume(volume) {
        const vol = Math.max(0, Math.min(1, volume));
        if (this.incomingAudio) {
            this.incomingAudio.volume = vol;
        }
        console.log('来电铃音音量设置为:', vol);
    }

    /**
     * 检查来电铃音是否正在播放
     */
    isRinging() {
        return this.isIncomingPlaying;
    }
}

// 创建全局实例
window.ringtoneManager = new RingtoneManager();

console.log('RingtoneManager 已加载，全局实例: window.ringtoneManager');
