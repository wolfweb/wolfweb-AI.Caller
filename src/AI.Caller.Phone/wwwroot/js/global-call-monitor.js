/**
 * 简化版WebRTC管理器 - 专用于全局监控
 */
class GlobalWebRTCManager {
    constructor() {
        this.pc = null;
        this.localStream = null;
        this.isConnected = false;
    }

    async createAnswerConnection(offerSdp) {
        try {
            console.log('创建全局WebRTC连接...');
            
            // 创建RTCPeerConnection
            this.pc = new RTCPeerConnection({
                iceServers: [
                    { urls: 'stun:stun.l.google.com:19302' },
                    { urls: 'stun:stun1.l.google.com:19302' }
                ]
            });

            // 设置ICE候选事件
            this.pc.onicecandidate = (event) => {
                if (event.candidate && window.globalSignalRManager) {
                    window.globalSignalRManager.invoke("SendIceCandidateAsync", {
                        callId: window.globalCallMonitor.currentIncomingCall?.callId,
                        iceCandidate: JSON.stringify(event.candidate)
                    }).catch(error => {
                        console.error('发送ICE候选失败:', error);
                    });
                }
            };

            // 设置远程流事件
            this.pc.ontrack = (event) => {
                console.log('收到远程音频流');
                const remoteAudio = document.getElementById('global-remote-audio');
                if (remoteAudio) {
                    remoteAudio.srcObject = event.streams[0];
                }
            };

            // 设置远程描述
            await this.pc.setRemoteDescription(offerSdp);

            // 获取用户媒体
            this.localStream = await navigator.mediaDevices.getUserMedia({
                audio: true,
                video: false
            });

            // 添加本地流到连接
            this.localStream.getTracks().forEach(track => {
                this.pc.addTrack(track, this.localStream);
            });

            // 创建应答
            const answer = await this.pc.createAnswer();
            await this.pc.setLocalDescription(answer);

            this.isConnected = true;
            console.log('全局WebRTC连接创建成功');
            
            return answer;

        } catch (error) {
            console.error('创建全局WebRTC连接失败:', error);
            this.cleanup();
            throw error;
        }
    }

    toggleMute() {
        if (this.localStream) {
            const audioTrack = this.localStream.getAudioTracks()[0];
            if (audioTrack) {
                audioTrack.enabled = !audioTrack.enabled;
                return !audioTrack.enabled; // 返回是否静音
            }
        }
        return false;
    }

    cleanup() {
        console.log('清理全局WebRTC资源...');
        
        if (this.localStream) {
            this.localStream.getTracks().forEach(track => {
                track.stop();
            });
            this.localStream = null;
        }

        if (this.pc) {
            this.pc.close();
            this.pc = null;
        }

        this.isConnected = false;
        console.log('全局WebRTC资源清理完成');
    }
}

/**
 * 全局通话监控系统
 * 功能：全局来电监听 + 通话监控集成 + 全局接听/拒接
 */
class GlobalCallMonitor {
    constructor() {
        this.isActive = false;
        this.activeCalls = new Map();
        this.monitoringData = {
            incomingCalls: [],
            activeSessions: [],
            callEvents: []
        };
        
        // 监控UI元素
        this.ui = null;
        this.isUIVisible = false;
        this.isUserManuallyHidden = false; // 用户是否手动隐藏了面板
        
        // SignalR连接
        this.signalRConnection = null;
        this.isUsingSharedConnection = false;
        
        // 页面信息
        this.currentPage = this.detectCurrentPage();
        
        // 当前来电和通话状态
        this.currentIncomingCall = null;
        this.isIncomingCallActive = false;
        this.isInCall = false;
        this.callStartTime = null;
        this.callTimerInterval = null;
        
        // WebRTC管理器
        this.webrtcManager = new GlobalWebRTCManager();
        
        // 拖动相关
        this.isDragging = false;
        this.dragOffset = { x: 0, y: 0 };
        this.boundHandleDrag = null;
        this.boundHandleDragEnd = null;
        this.boundHandleTouchMove = null;
        this.boundHandleTouchEnd = null;
        
        // 初始化
        this.init();
    }

    /**
     * 检测当前页面类型
     */
    detectCurrentPage() {
        const path = window.location.pathname.toLowerCase();
        
        if (path === '/' || path === '/home' || path.includes('/home/')) {
            return 'home';
        } else if (path.includes('/monitoring/')) {
            return 'monitoring';
        } else if (path.includes('/account/')) {
            return 'account';
        } else {
            return 'other';
        }
    }

    /**
     * 检查是否在Home页面
     */
    isOnHomePage() {
        return this.currentPage === 'home' || 
               window.location.pathname.toLowerCase().includes('/home');
    }

    /**
     * 初始化全局监控
     */
    async init() {
        console.log('初始化全局通话监控系统...');
        
        // 创建监控UI
        this.createMonitoringUI();
        
        // 监听现有系统事件
        this.setupEventListeners();
        
        // 延迟设置SignalR监听，等待PhoneApp初始化
        setTimeout(async () => {
            await this.setupGlobalSignalR();
        }, 2000);
        
        // 定期获取活跃通话
        this.startActiveCallsPolling();
        
        this.isActive = true;
        console.log('全局通话监控系统已启动');
    }

    /**
     * 创建监控UI
     */
    createMonitoringUI() {
        // 创建悬浮监控面板
        const monitorPanel = document.createElement('div');
        monitorPanel.id = 'global-monitor-panel';
        monitorPanel.innerHTML = `
            <div class="monitor-header">
                <span class="monitor-title">📞 全局通话监控</span>
                <div class="monitor-controls">
                    <button id="monitor-minimize" class="btn-control">−</button>
                    <button id="monitor-close" class="btn-control">×</button>
                </div>
            </div>
            <div class="monitor-content">
                <!-- 来电监听区域 -->
                <div class="monitor-section">
                    <h4>📥 来电监听</h4>
                    <div class="incoming-status">
                        <span class="status-dot" id="incoming-status-dot"></span>
                        <span id="incoming-status-text">等待来电...</span>
                    </div>
                    <div id="incoming-calls-list" class="calls-list">
                        <!-- 来电列表 -->
                    </div>
                </div>

                <!-- 活跃通话监控 -->
                <div class="monitor-section">
                    <h4>🔊 活跃通话 (<span id="active-calls-count">0</span>)</h4>
                    <div id="active-calls-list" class="calls-list">
                        <!-- 活跃通话列表 -->
                    </div>
                </div>

                <!-- 快速操作 -->
                <div class="monitor-section">
                    <h4>⚡ 快速操作</h4>
                    <div class="quick-actions">
                        <button id="refresh-calls" class="btn-action">刷新通话</button>
                        <button id="view-all-monitoring" class="btn-action">查看监控页面</button>
                        <button id="export-logs" class="btn-action">导出日志</button>
                    </div>
                </div>

                <!-- 事件日志 -->
                <div class="monitor-section">
                    <h4>📋 事件日志</h4>
                    <div id="event-logs" class="event-logs">
                        <!-- 事件日志 -->
                    </div>
                </div>
            </div>
        `;

        // 添加样式
        const style = document.createElement('style');
        style.textContent = `
            #global-monitor-panel {
                position: fixed;
                top: 20px;
                right: 20px;
                width: 350px;
                max-height: 80vh;
                background: linear-gradient(145deg, #1a1a1a, #2d2d2d);
                border-radius: 12px;
                box-shadow: 0 8px 32px rgba(0,0,0,0.4);
                color: white;
                font-family: 'Segoe UI', sans-serif;
                z-index: 10000;
                overflow: hidden;
                display: none;
                border: 1px solid rgba(255,255,255,0.1);
            }
            
            #global-monitor-panel.dragging {
                box-shadow: 0 12px 48px rgba(0,0,0,0.6);
                z-index: 10001; /* 确保拖动时在最上层 */
            }
            
            .monitor-header {
                background: linear-gradient(135deg, #667eea, #764ba2);
                padding: 12px 16px;
                display: flex;
                justify-content: space-between;
                align-items: center;
                cursor: move;
                /* 拖动优化 - 简化版本 */
                touch-action: none;
                user-select: none;
                /* 平滑过渡 */
                transition: background 0.2s ease;
            }
            
            .monitor-header:hover {
                background: linear-gradient(135deg, #7c8df0, #8a5cb8);
            }
            
            .monitor-header:active {
                background: linear-gradient(135deg, #5a6fd8, #6a4a98);
            }
            
            .monitor-title {
                font-weight: 600;
                font-size: 14px;
            }
            
            .monitor-controls {
                display: flex;
                gap: 8px;
            }
            
            .btn-control {
                width: 24px;
                height: 24px;
                border: none;
                border-radius: 4px;
                background: rgba(255,255,255,0.2);
                color: white;
                cursor: pointer;
                font-size: 12px;
                font-weight: bold;
            }
            
            .btn-control:hover {
                background: rgba(255,255,255,0.3);
            }
            
            .monitor-content {
                padding: 16px;
                max-height: calc(80vh - 60px);
                overflow-y: auto;
            }
            
            .monitor-section {
                margin-bottom: 16px;
                padding: 12px;
                background: rgba(255,255,255,0.05);
                border-radius: 8px;
                border: 1px solid rgba(255,255,255,0.1);
            }
            
            .monitor-section h4 {
                margin: 0 0 8px 0;
                font-size: 12px;
                color: #4CAF50;
                font-weight: 500;
            }
            
            .incoming-status {
                display: flex;
                align-items: center;
                gap: 8px;
                margin-bottom: 8px;
            }
            
            .status-dot {
                width: 8px;
                height: 8px;
                border-radius: 50%;
                background: #666;
            }
            
            .status-dot.waiting { background: #666; }
            .status-dot.incoming { background: #FF9800; animation: pulse 1s infinite; }
            .status-dot.active { background: #4CAF50; }
            
            @keyframes pulse {
                0%, 100% { opacity: 1; }
                50% { opacity: 0.5; }
            }
            
            .calls-list {
                max-height: 120px;
                overflow-y: auto;
            }
            
            .call-item {
                padding: 8px;
                margin-bottom: 4px;
                background: rgba(255,255,255,0.1);
                border-radius: 4px;
                font-size: 11px;
                border-left: 3px solid #4CAF50;
            }
            
            .call-item.incoming {
                border-left-color: #FF9800;
                background: rgba(255,152,0,0.1);
            }
            
            .call-item-header {
                display: flex;
                justify-content: space-between;
                align-items: center;
                margin-bottom: 4px;
            }
            
            .call-item-user {
                font-weight: 500;
                color: #fff;
            }
            
            .call-item-time {
                color: #aaa;
                font-size: 10px;
            }
            
            .call-item-actions {
                display: flex;
                gap: 4px;
                margin-top: 4px;
            }
            
            .btn-small {
                padding: 2px 6px;
                font-size: 10px;
                border: none;
                border-radius: 3px;
                cursor: pointer;
                color: white;
            }
            
            .btn-monitor { background: #2196F3; }
            .btn-answer { background: #4CAF50; }
            .btn-reject { background: #F44336; }
            
            .quick-actions {
                display: flex;
                flex-wrap: wrap;
                gap: 6px;
            }
            
            .btn-action {
                padding: 6px 10px;
                font-size: 11px;
                border: none;
                border-radius: 4px;
                background: linear-gradient(135deg, #4CAF50, #45a049);
                color: white;
                cursor: pointer;
                flex: 1;
                min-width: 80px;
            }
            
            .btn-action:hover {
                transform: translateY(-1px);
            }
            
            .event-logs {
                max-height: 100px;
                overflow-y: auto;
                background: rgba(0,0,0,0.3);
                border-radius: 4px;
                padding: 8px;
            }
            
            .log-entry {
                font-size: 10px;
                margin-bottom: 4px;
                padding: 4px;
                border-radius: 2px;
                border-left: 2px solid #4CAF50;
            }
            
            .log-entry.warning { border-left-color: #FF9800; }
            .log-entry.error { border-left-color: #F44336; }
            
            .log-time {
                color: #888;
                margin-right: 8px;
            }
            
            /* 悬浮按钮 - 右下角圆形 */
            #monitor-toggle-btn {
                position: fixed;
                bottom: 30px;
                right: 30px;
                width: 60px;
                height: 60px;
                background: linear-gradient(135deg, #667eea, #764ba2);
                border-radius: 50% !important;
                display: flex;
                align-items: center;
                justify-content: center;
                cursor: pointer;
                z-index: 9999;
                box-shadow: 0 6px 20px rgba(0,0,0,0.3);
                color: white;
                font-size: 24px;
                transition: all 0.3s ease;
                border: 3px solid rgba(255,255,255,0.2);
                /* 确保圆形 */
                min-width: 60px;
                min-height: 60px;
                max-width: 60px;
                max-height: 60px;
                overflow: hidden;
            }
            
            #monitor-toggle-btn:hover {
                transform: scale(1.1);
                box-shadow: 0 8px 25px rgba(0,0,0,0.4);
                border-color: rgba(255,255,255,0.4);
            }
            
            #monitor-toggle-btn:active {
                transform: scale(0.95);
            }
            
            /* 悬浮按钮响应式设计 */
            @media (max-width: 768px) {
                #monitor-toggle-btn {
                    bottom: 20px;
                    right: 20px;
                    width: 55px;
                    height: 55px;
                    font-size: 22px;
                    /* 移动端也确保圆形 */
                    min-width: 55px;
                    min-height: 55px;
                    max-width: 55px;
                    max-height: 55px;
                    border-radius: 50% !important;
                }
            }
            
            /* 通话控制区域 */
            .call-control-section {
                border: 2px solid #4CAF50;
                border-radius: 8px;
                margin-bottom: 16px;
                animation: callPulse 2s infinite;
            }
            
            @keyframes callPulse {
                0%, 100% { border-color: #4CAF50; }
                50% { border-color: #81C784; }
            }
            
            .call-controls {
                display: flex;
                gap: 8px;
                margin-bottom: 8px;
            }
            
            .btn-control-call {
                padding: 8px 12px;
                font-size: 12px;
                border: none;
                border-radius: 4px;
                cursor: pointer;
                color: white;
                flex: 1;
            }
            
            .btn-control-call.btn-mute {
                background: #FF9800;
            }
            
            .btn-control-call.btn-mute.muted {
                background: #F44336;
            }
            
            .btn-control-call.btn-danger {
                background: #F44336;
            }
            
            .btn-control-call:hover {
                opacity: 0.8;
            }
            
            .call-timer {
                text-align: center;
                font-size: 14px;
                font-weight: bold;
                color: #4CAF50;
            }
            
            .current-call {
                border-left-color: #4CAF50 !important;
                background: rgba(76, 175, 80, 0.1) !important;
            }
            
            .btn-answer {
                background: #4CAF50 !important;
            }
            
            .btn-reject {
                background: #F44336 !important;
            }
            
            .btn-answer:hover, .btn-reject:hover {
                opacity: 0.8;
            }
            
            /* 隐藏音频元素 */
            #global-remote-audio {
                display: none;
            }
        `;
        
        document.head.appendChild(style);
        document.body.appendChild(monitorPanel);
        
        // 创建右下角悬浮按钮
        const toggleBtn = document.createElement('div');
        toggleBtn.id = 'monitor-toggle-btn';
        toggleBtn.innerHTML = `
            <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor">
                <path d="M6.62 10.79c1.44 2.83 3.76 5.14 6.59 6.59l2.2-2.2c.27-.27.67-.36 1.02-.24 1.12.37 2.33.57 3.57.57.55 0 1 .45 1 1V20c0 .55-.45 1-1 1-9.39 0-17-7.61-17-17 0-.55.45-1 1-1h3.5c.55 0 1 .45 1 1 0 1.25.2 2.45.57 3.57.11.35.03.74-.25 1.02l-2.2 2.2z"/>
            </svg>
        `;
        toggleBtn.title = '全局通话监控 - 点击打开/关闭';
        toggleBtn.setAttribute('role', 'button');
        toggleBtn.setAttribute('tabindex', '0');
        document.body.appendChild(toggleBtn);
        
        // 添加隐藏的音频元素
        const remoteAudio = document.createElement('audio');
        remoteAudio.id = 'global-remote-audio';
        remoteAudio.autoplay = true;
        document.body.appendChild(remoteAudio);
        
        // 绑定事件
        this.bindUIEvents();
        this.setupDragFunctionality();
        
        // 恢复上次保存的位置
        setTimeout(() => {
            this.restorePanelPosition();
        }, 100);
        
        this.ui = monitorPanel;
    }

    /**
     * 绑定UI事件
     */
    bindUIEvents() {
        // 切换显示
        document.getElementById('monitor-toggle-btn').addEventListener('click', () => {
            this.toggleUI();
            // 用户主动点击切换时，重置手动隐藏状态
            if (this.isUIVisible) {
                this.isUserManuallyHidden = false;
                this.addLog('用户主动显示面板', 'info');
            } else {
                this.isUserManuallyHidden = true;
                this.addLog('用户主动隐藏面板', 'info');
            }
        });
        
        // 最小化
        document.getElementById('monitor-minimize').addEventListener('click', () => {
            this.hideUI();
            this.isUserManuallyHidden = true; // 记录用户手动隐藏
            this.addLog('用户手动最小化面板', 'info');
        });
        
        // 关闭
        document.getElementById('monitor-close').addEventListener('click', () => {
            this.hideUI();
            this.isUserManuallyHidden = true; // 记录用户手动隐藏
            this.addLog('用户手动关闭面板', 'info');
        });
        
        // 刷新通话
        document.getElementById('refresh-calls').addEventListener('click', () => {
            this.refreshActiveCalls();
        });
        
        // 查看监控页面
        document.getElementById('view-all-monitoring').addEventListener('click', () => {
            window.open('/Monitoring/ActiveCalls', '_blank');
        });
        
        // 导出日志
        document.getElementById('export-logs').addEventListener('click', () => {
            this.exportLogs();
        });
    }

    /**
     * 设置窗口拖动功能 - 简化版本，专注于可靠性
     */
    setupDragFunctionality() {
        const header = document.querySelector('.monitor-header');
        if (!header) {
            console.error('监控面板标题栏未找到，无法设置拖动功能');
            return;
        }
        
        // 预绑定事件处理函数，避免内存泄漏
        this.boundHandleDrag = this.handleDrag.bind(this);
        this.boundHandleDragEnd = this.handleDragEnd.bind(this);
        this.boundHandleTouchMove = this.handleTouchMove.bind(this);
        this.boundHandleTouchEnd = this.handleTouchEnd.bind(this);
        
        // 鼠标拖动
        header.addEventListener('mousedown', (e) => {
            this.startDrag(e, 'mouse');
        });
        
        // 触摸拖动
        header.addEventListener('touchstart', (e) => {
            this.startDrag(e, 'touch');
        }, { passive: false });
    }

    /**
     * 开始拖动操作
     */
    startDrag(e, type) {
        // 只响应左键点击（鼠标）或单点触摸
        if (type === 'mouse' && e.button !== 0) return;
        if (type === 'touch' && e.touches.length !== 1) return;
        
        // 防止默认行为
        e.preventDefault();
        e.stopPropagation();
        
        // 获取初始位置
        const clientX = type === 'mouse' ? e.clientX : e.touches[0].clientX;
        const clientY = type === 'mouse' ? e.clientY : e.touches[0].clientY;
        
        const rect = this.ui.getBoundingClientRect();
        this.dragOffset.x = clientX - rect.left;
        this.dragOffset.y = clientY - rect.top;
        
        // 设置拖动状态
        this.isDragging = true;
        this.dragType = type;
        
        // 应用拖动样式
        this.applyDragStyles(true);
        
        // 添加事件监听器
        if (type === 'mouse') {
            document.addEventListener('mousemove', this.boundHandleDrag);
            document.addEventListener('mouseup', this.boundHandleDragEnd);
        } else {
            document.addEventListener('touchmove', this.boundHandleTouchMove, { passive: false });
            document.addEventListener('touchend', this.boundHandleTouchEnd);
        }
        
        console.log('开始拖动操作:', type);
    }

    /**
     * 应用或移除拖动样式
     */
    applyDragStyles(isDragging) {
        if (isDragging) {
            // 应用拖动样式
            this.ui.classList.add('dragging');
            this.ui.style.transition = 'none';
            this.ui.style.userSelect = 'none';
            this.ui.style.pointerEvents = 'auto'; // 确保面板可以接收事件
            document.body.style.userSelect = 'none';
            document.body.style.cursor = 'move';
        } else {
            // 移除拖动样式
            this.ui.classList.remove('dragging');
            this.ui.style.transition = '';
            this.ui.style.userSelect = '';
            this.ui.style.pointerEvents = '';
            document.body.style.userSelect = '';
            document.body.style.cursor = '';
        }
    }

    /**
     * 处理鼠标拖动
     */
    handleDrag(e) {
        if (!this.isDragging || this.dragType !== 'mouse') return;
        
        this.updatePosition(e.clientX, e.clientY);
    }

    /**
     * 处理触摸拖动
     */
    handleTouchMove(e) {
        if (!this.isDragging || this.dragType !== 'touch' || e.touches.length !== 1) return;
        
        e.preventDefault(); // 防止页面滚动
        const touch = e.touches[0];
        this.updatePosition(touch.clientX, touch.clientY);
    }

    /**
     * 更新面板位置
     */
    updatePosition(clientX, clientY) {
        const newX = clientX - this.dragOffset.x;
        const newY = clientY - this.dragOffset.y;
        
        // 获取视口尺寸和面板尺寸
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        const panelWidth = this.ui.offsetWidth;
        const panelHeight = this.ui.offsetHeight;
        
        // 边界检查 - 确保面板完全在视口内
        const minX = 0;
        const minY = 0;
        const maxX = Math.max(0, viewportWidth - panelWidth);
        const maxY = Math.max(0, viewportHeight - panelHeight);
        
        const boundedX = Math.max(minX, Math.min(newX, maxX));
        const boundedY = Math.max(minY, Math.min(newY, maxY));
        
        // 更新位置 - 使用left/top而不是transform，避免层叠上下文问题
        this.ui.style.left = boundedX + 'px';
        this.ui.style.top = boundedY + 'px';
        this.ui.style.right = 'auto'; // 清除right定位
        this.ui.style.bottom = 'auto'; // 清除bottom定位
    }

    /**
     * 结束鼠标拖动
     */
    handleDragEnd(e) {
        if (!this.isDragging || this.dragType !== 'mouse') return;
        
        this.endDrag();
        
        // 移除鼠标事件监听器
        document.removeEventListener('mousemove', this.boundHandleDrag);
        document.removeEventListener('mouseup', this.boundHandleDragEnd);
    }

    /**
     * 结束触摸拖动
     */
    handleTouchEnd(e) {
        if (!this.isDragging || this.dragType !== 'touch') return;
        
        this.endDrag();
        
        // 移除触摸事件监听器
        document.removeEventListener('touchmove', this.boundHandleTouchMove);
        document.removeEventListener('touchend', this.boundHandleTouchEnd);
    }

    /**
     * 结束拖动操作
     */
    endDrag() {
        console.log('结束拖动操作:', this.dragType);
        
        // 重置拖动状态
        this.isDragging = false;
        this.dragType = null;
        
        // 移除拖动样式
        this.applyDragStyles(false);
        
        // 保存位置（可选，用于记住用户偏好）
        this.savePanelPosition();
    }

    /**
     * 保存面板位置到本地存储
     */
    savePanelPosition() {
        try {
            const position = {
                left: this.ui.style.left,
                top: this.ui.style.top
            };
            localStorage.setItem('globalMonitorPosition', JSON.stringify(position));
        } catch (error) {
            console.warn('保存面板位置失败:', error);
        }
    }

    /**
     * 恢复面板位置
     */
    restorePanelPosition() {
        try {
            const savedPosition = localStorage.getItem('globalMonitorPosition');
            if (savedPosition) {
                const position = JSON.parse(savedPosition);
                if (position.left && position.top) {
                    this.ui.style.left = position.left;
                    this.ui.style.top = position.top;
                    this.ui.style.right = 'auto';
                    console.log('已恢复面板位置:', position);
                }
            }
        } catch (error) {
            console.warn('恢复面板位置失败:', error);
        }
    }

    /**
     * 更新悬浮按钮状态（来电提示）
     */
    updateToggleButtonForIncomingCall() {
        const toggleBtn = document.getElementById('monitor-toggle-btn');
        if (toggleBtn) {
            // 添加来电提示样式
            toggleBtn.style.animation = 'pulse 1s infinite, glow 2s infinite';
            toggleBtn.style.background = 'linear-gradient(135deg, #FF9800, #F57C00)';
            toggleBtn.innerHTML = `
                <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M6.62 10.79c1.44 2.83 3.76 5.14 6.59 6.59l2.2-2.2c.27-.27.67-.36 1.02-.24 1.12.37 2.33.57 3.57.57.55 0 1 .45 1 1V20c0 .55-.45 1-1 1-9.39 0-17-7.61-17-17 0-.55.45-1 1-1h3.5c.55 0 1 .45 1 1 0 1.25.2 2.45.57 3.57.11.35.03.74-.25 1.02l-2.2 2.2z"/>
                </svg>
            `;
            toggleBtn.title = '🔥 有新来电！点击查看监控面板';
            toggleBtn.classList.add('incoming-call');
            
            // 添加增强动画CSS（如果还没有）
            if (!document.getElementById('incoming-call-animation')) {
                const style = document.createElement('style');
                style.id = 'incoming-call-animation';
                style.textContent = `
                    @keyframes pulse {
                        0%, 100% { transform: scale(1); }
                        50% { transform: scale(1.15); }
                    }
                    @keyframes glow {
                        0%, 100% { box-shadow: 0 6px 20px rgba(255,152,0,0.3); }
                        50% { box-shadow: 0 6px 30px rgba(255,152,0,0.6), 0 0 20px rgba(255,152,0,0.4); }
                    }
                    .incoming-call {
                        border-color: rgba(255,193,7,0.8) !important;
                    }
                    #monitor-toggle-btn svg {
                        transition: all 0.3s ease;
                    }
                    #monitor-toggle-btn:hover svg {
                        transform: rotate(15deg);
                    }
                `;
                document.head.appendChild(style);
            }
        }
    }

    /**
     * 重置悬浮按钮状态
     */
    resetToggleButtonState() {
        const toggleBtn = document.getElementById('monitor-toggle-btn');
        if (toggleBtn) {
            toggleBtn.style.animation = '';
            toggleBtn.style.background = 'linear-gradient(135deg, #667eea, #764ba2)';
            toggleBtn.innerHTML = `
                <svg width="28" height="28" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M6.62 10.79c1.44 2.83 3.76 5.14 6.59 6.59l2.2-2.2c.27-.27.67-.36 1.02-.24 1.12.37 2.33.57 3.57.57.55 0 1 .45 1 1V20c0 .55-.45 1-1 1-9.39 0-17-7.61-17-17 0-.55.45-1 1-1h3.5c.55 0 1 .45 1 1 0 1.25.2 2.45.57 3.57.11.35.03.74-.25 1.02l-2.2 2.2z"/>
                </svg>
            `;
            toggleBtn.title = '全局通话监控 - 点击打开/关闭';
            toggleBtn.classList.remove('incoming-call');
        }
    }

    /**
     * 重置用户手动隐藏状态（可在页面刷新或长时间无活动后调用）
     */
    resetUserManuallyHiddenState() {
        this.isUserManuallyHidden = false;
        this.addLog('已重置用户手动隐藏状态', 'info');
    }

    /**
     * Home页面智能避让重要按钮
     */
    avoidHomePageButtons() {
        if (!this.isOnHomePage()) return;
        
        // 查找Home页面的重要按钮
        const importantButtons = this.findHomePageButtons();
        
        if (importantButtons.length === 0) {
            console.log('未找到Home页面按钮，使用默认位置');
            return;
        }
        
        // 计算安全位置
        const safePosition = this.calculateSafePosition(importantButtons);
        
        if (safePosition) {
            this.ui.style.left = safePosition.left + 'px';
            this.ui.style.top = safePosition.top + 'px';
            this.ui.style.right = 'auto';
            
            console.log('已调整面板位置避让Home按钮:', safePosition);
            this.addLog('已调整位置避让Home页面按钮', 'info');
        }
    }

    /**
     * 查找Home页面的重要按钮
     */
    findHomePageButtons() {
        const buttonSelectors = [
            '#answerButton',      // 接听按钮
            '#hangupButton',      // 挂断按钮
            '#callButton',        // 呼叫按钮
            '.call-controls',     // 通话控制区域
            '.incoming-call',     // 来电区域
            '.call-info'          // 通话信息区域
        ];
        
        const foundButtons = [];
        
        buttonSelectors.forEach(selector => {
            const element = document.querySelector(selector);
            if (element && this.isElementVisible(element)) {
                const rect = element.getBoundingClientRect();
                foundButtons.push({
                    element: element,
                    selector: selector,
                    rect: rect,
                    area: rect.width * rect.height
                });
            }
        });
        
        // 按重要性排序（面积大的优先避让）
        foundButtons.sort((a, b) => b.area - a.area);
        
        console.log('找到Home页面重要元素:', foundButtons.map(b => b.selector));
        return foundButtons;
    }

    /**
     * 检查元素是否可见
     */
    isElementVisible(element) {
        if (!element) return false;
        
        const style = window.getComputedStyle(element);
        return style.display !== 'none' && 
               style.visibility !== 'hidden' && 
               style.opacity !== '0' &&
               element.offsetWidth > 0 && 
               element.offsetHeight > 0;
    }

    /**
     * 计算安全位置（避开重要按钮）
     */
    calculateSafePosition(importantButtons) {
        const panelWidth = this.ui.offsetWidth || 350;
        const panelHeight = this.ui.offsetHeight || 400;
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        const margin = 20; // 安全边距
        
        // 候选位置（优先级从高到低）
        const candidatePositions = [
            // 左上角
            { left: margin, top: margin, priority: 1 },
            // 左下角
            { left: margin, top: viewportHeight - panelHeight - margin, priority: 2 },
            // 右下角
            { left: viewportWidth - panelWidth - margin, top: viewportHeight - panelHeight - margin, priority: 3 },
            // 右上角（默认位置，优先级最低）
            { left: viewportWidth - panelWidth - margin, top: margin, priority: 4 },
            // 中间左侧
            { left: margin, top: (viewportHeight - panelHeight) / 2, priority: 5 },
            // 中间右侧
            { left: viewportWidth - panelWidth - margin, top: (viewportHeight - panelHeight) / 2, priority: 6 }
        ];
        
        // 检查每个候选位置是否与重要按钮冲突
        for (const position of candidatePositions) {
            const panelRect = {
                left: position.left,
                top: position.top,
                right: position.left + panelWidth,
                bottom: position.top + panelHeight
            };
            
            let hasConflict = false;
            
            // 检查是否与任何重要按钮重叠
            for (const button of importantButtons) {
                if (this.isRectOverlap(panelRect, button.rect)) {
                    hasConflict = true;
                    break;
                }
            }
            
            if (!hasConflict) {
                console.log(`选择安全位置: 优先级${position.priority}`, position);
                return position;
            }
        }
        
        // 如果所有位置都有冲突，选择冲突最小的位置
        console.log('所有位置都有冲突，选择冲突最小的位置');
        return this.findLeastConflictPosition(candidatePositions, importantButtons, panelWidth, panelHeight);
    }

    /**
     * 检查两个矩形是否重叠
     */
    isRectOverlap(rect1, rect2) {
        return !(rect1.right < rect2.left || 
                rect1.left > rect2.right || 
                rect1.bottom < rect2.top || 
                rect1.top > rect2.bottom);
    }

    /**
     * 找到冲突最小的位置
     */
    findLeastConflictPosition(positions, buttons, panelWidth, panelHeight) {
        let bestPosition = positions[0];
        let minConflictArea = Infinity;
        
        for (const position of positions) {
            const panelRect = {
                left: position.left,
                top: position.top,
                right: position.left + panelWidth,
                bottom: position.top + panelHeight
            };
            
            let totalConflictArea = 0;
            
            for (const button of buttons) {
                if (this.isRectOverlap(panelRect, button.rect)) {
                    // 计算重叠面积
                    const overlapArea = this.calculateOverlapArea(panelRect, button.rect);
                    totalConflictArea += overlapArea;
                }
            }
            
            if (totalConflictArea < minConflictArea) {
                minConflictArea = totalConflictArea;
                bestPosition = position;
            }
        }
        
        console.log(`选择冲突最小位置，冲突面积: ${minConflictArea}`, bestPosition);
        return bestPosition;
    }

    /**
     * 计算两个矩形的重叠面积
     */
    calculateOverlapArea(rect1, rect2) {
        const overlapLeft = Math.max(rect1.left, rect2.left);
        const overlapTop = Math.max(rect1.top, rect2.top);
        const overlapRight = Math.min(rect1.right, rect2.right);
        const overlapBottom = Math.min(rect1.bottom, rect2.bottom);
        
        if (overlapLeft < overlapRight && overlapTop < overlapBottom) {
            return (overlapRight - overlapLeft) * (overlapBottom - overlapTop);
        }
        
        return 0;
    }

    /**
     * 设置全局SignalR监听 - 使用全局SignalR管理器
     */
    async setupGlobalSignalR() {
        try {
            // 等待全局SignalR管理器初始化
            await this.waitForGlobalSignalRManager();
            
            if (window.globalSignalRManager) {
                // 使用全局SignalR连接
                this.signalRConnection = window.globalSignalRManager.connection;
                this.isUsingSharedConnection = true;
                this.addLog('使用全局SignalR连接进行监听', 'info');
                
                // 注册监控事件处理器
                this.registerMonitoringHandlers();
            } else {
                this.addLog('全局SignalR管理器未找到', 'error');
            }
            
        } catch (error) {
            console.error('设置全局SignalR监听失败:', error);
            this.addLog('SignalR监听设置失败: ' + error.message, 'error');
        }
    }

    /**
     * 等待全局SignalR管理器初始化
     */
    async waitForGlobalSignalRManager() {
        return new Promise((resolve) => {
            let attempts = 0;
            const maxAttempts = 20; // 最多等待10秒
            
            const checkGlobalSignalR = () => {
                attempts++;
                
                if (window.globalSignalRManager && window.globalSignalRManager.isConnected) {
                    console.log('检测到全局SignalR管理器已就绪');
                    resolve(true);
                } else if (attempts >= maxAttempts) {
                    console.log('等待全局SignalR管理器超时');
                    resolve(false);
                } else {
                    setTimeout(checkGlobalSignalR, 500);
                }
            };
            checkGlobalSignalR();
        });
    }

    /**
     * 注册监控事件处理器
     */
    registerMonitoringHandlers() {
        const handlerId = 'global-monitor';
        
        // 注册来电监听
        window.globalSignalRManager.registerEventHandler(handlerId, 'inCalling', (callData) => {
            this.handleIncomingCall(callData);
        });
        
        // 注册通话状态监听
        window.globalSignalRManager.registerEventHandler(handlerId, 'callAnswered', (data) => {
            this.handleCallAnswered(data);
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'callEnded', (data) => {
            this.handleCallEnded(data);
        });
        
        window.globalSignalRManager.registerEventHandler(handlerId, 'callTimeout', (data) => {
            this.handleCallTimeout(data);
        });
        
        console.log('全局监控事件处理器已注册');
        
        // 页面卸载时注销处理器
        window.addEventListener('beforeunload', () => {
            if (window.globalSignalRManager) {
                window.globalSignalRManager.unregisterEventHandler(handlerId);
            }
        });
    }

    /**
     * 等待PhoneApp初始化完成（有超时机制）
     */
    async waitForPhoneApp() {
        return new Promise((resolve) => {
            let attempts = 0;
            const maxAttempts = 10; // 最多等待5秒 (10 * 500ms)
            
            const checkPhoneApp = () => {
                attempts++;
                
                if (window.phoneApp && window.phoneApp.isInitialized) {
                    console.log('检测到PhoneApp已初始化，将复用SignalR连接');
                    resolve(true); // 找到PhoneApp
                } else if (attempts >= maxAttempts) {
                    console.log('未检测到PhoneApp，将创建独立SignalR连接');
                    resolve(false); // 超时，没有PhoneApp
                } else {
                    setTimeout(checkPhoneApp, 500);
                }
            };
            checkPhoneApp();
        });
    }

    /**
     * 附加全局事件处理器到现有连接（已废弃 - 现在使用全局SignalR管理器）
     */
    attachGlobalEventHandlers() {
        console.warn('attachGlobalEventHandlers已废弃，现在使用全局SignalR管理器');
        // 此方法已不再使用，保留仅为兼容性
    }

    /**
     * 创建独立的SignalR连接（备用方案）
     */
    async createIndependentSignalR() {
        console.log('创建独立的全局SignalR连接...');
        
        this.signalRConnection = new signalR.HubConnectionBuilder()
            .withUrl("/webrtc")
            .configureLogging(signalR.LogLevel.Information)
            .withAutomaticReconnect()
            .build();

        // 监听来电事件
        this.signalRConnection.on("inCalling", (callData) => {
            this.handleIncomingCall(callData);
        });

        // 监听通话状态变化
        this.signalRConnection.on("callAnswered", (data) => {
            this.handleCallAnswered(data);
        });

        this.signalRConnection.on("callEnded", (data) => {
            this.handleCallEnded(data);
        });

        this.signalRConnection.on("callTimeout", (data) => {
            this.handleCallTimeout(data);
        });

        // 启动连接
        await this.signalRConnection.start();
        this.addLog('独立SignalR全局监听已启动', 'info');
    }

    /**
     * 监听现有系统事件
     */
    setupEventListeners() {
        // 监听页面通话事件
        document.addEventListener('callEnded', () => {
            this.addLog('检测到通话结束事件', 'info');
        });

        document.addEventListener('remoteHangup', () => {
            this.addLog('检测到对方挂断事件', 'warning');
        });

        // 监听网络状态
        window.addEventListener('online', () => {
            this.addLog('网络连接已恢复', 'info');
        });

        window.addEventListener('offline', () => {
            this.addLog('网络连接已断开', 'error');
        });
    }

    /**
     * 处理来电
     */
    handleIncomingCall(callData) {
        console.log('全局监听到来电:', callData);
        
        // 设置当前来电
        this.currentIncomingCall = callData;
        this.isIncomingCallActive = true;
        
        // 更新状态
        this.updateIncomingStatus('incoming', '检测到来电');
        
        // 记录来电到历史
        const incomingCall = {
            callId: callData.callId,
            caller: callData.caller,
            callee: callData.callee,
            timestamp: new Date(),
            isExternal: callData.isExternal || false
        };
        
        this.monitoringData.incomingCalls.push(incomingCall);
        
        // 根据页面类型更新UI
        if (this.isOnHomePage()) {
            this.showMonitoringMode(callData);
        } else {
            this.showFullControlMode(callData);
        }
        
        // 添加日志
        const callerName = callData.caller?.sipUsername || callData.caller?.userId || '未知';
        this.addLog(`来电: ${callerName}`, 'warning');
        
        // 显示监控面板（如果隐藏且用户未手动隐藏）
        if (!this.isUIVisible && !this.isUserManuallyHidden) {
            this.showUI();
            this.addLog('来电自动显示面板', 'info');
        } else if (this.isUserManuallyHidden) {
            this.addLog('用户已手动隐藏面板，不自动显示', 'info');
            // 但是要更新悬浮按钮状态，提示有新来电
            this.updateToggleButtonForIncomingCall();
        }
    }

    /**
     * 显示监控模式（Home页面）
     */
    showMonitoringMode(callData) {
        this.updateIncomingCallsUI(false); // 不显示操作按钮
        this.addLog('在Home页面，由Home系统处理来电', 'info');
        
        // 在Home页面智能避让重要按钮
        this.avoidHomePageButtons();
    }

    /**
     * 显示完整控制模式（其他页面）
     */
    showFullControlMode(callData) {
        this.updateIncomingCallsUI(true); // 显示操作按钮
        this.addLog('显示全局接听控制', 'info');
    }

    /**
     * 接听通话
     */
    async answerCall(callId) {
        try {
            if (this.isOnHomePage()) {
                // 在Home页面：通知Home系统处理
                this.notifyHomePageAnswer(callId);
                return;
            }

            // 在其他页面：直接处理
            await this.performGlobalAnswer(callId);

        } catch (error) {
            console.error('接听失败:', error);
            this.addLog(`接听失败: ${error.message}`, 'error');
        }
    }

    /**
     * 执行全局接听
     */
    async performGlobalAnswer(callId) {
        if (!this.currentIncomingCall) {
            throw new Error('没有当前来电信息');
        }

        this.addLog('正在接听来电...', 'info');

        try {
            // 1. 创建WebRTC连接
            const offerSdp = this.parseOfferSdp(this.currentIncomingCall.offerSdp);
            const answerSdp = await this.webrtcManager.createAnswerConnection(offerSdp);

            // 2. 调用后端接听接口
            await window.globalSignalRManager.invoke("AnswerAsync", {
                CallId: callId,
                AnswerSdp: JSON.stringify(answerSdp)
            });

            // 3. 更新状态
            this.isInCall = true;
            this.isIncomingCallActive = false;
            this.updateIncomingStatus('active', '通话进行中');
            this.addLog('通话已接听', 'success');

            // 4. 显示通话控制界面
            this.showCallControlUI();

        } catch (error) {
            // 清理WebRTC资源
            this.webrtcManager.cleanup();
            throw error;
        }
    }

    /**
     * 拒接通话
     */
    async rejectCall(callId) {
        try {
            await window.globalSignalRManager.invoke("HangupCallAsync", {
                CallId: callId,
                Reason: "Rejected"
            });

            this.addLog('已拒接来电', 'info');
            this.clearCurrentCall();

        } catch (error) {
            console.error('拒接失败:', error);
            this.addLog(`拒接失败: ${error.message}`, 'error');
        }
    }

    /**
     * 通知Home页面接听
     */
    notifyHomePageAnswer(callId) {
        // 通过自定义事件通知Home页面
        const event = new CustomEvent('globalAnswerRequest', {
            detail: { callId: callId }
        });
        document.dispatchEvent(event);

        this.addLog('已通知Home页面处理接听', 'info');
    }

    /**
     * 解析Offer SDP
     */
    parseOfferSdp(offerSdp) {
        if (typeof offerSdp === 'string') {
            return JSON.parse(offerSdp);
        } else if (typeof offerSdp === 'object') {
            return offerSdp;
        } else {
            throw new Error("Invalid offer SDP type: " + typeof offerSdp);
        }
    }

    /**
     * 显示通话控制UI
     */
    showCallControlUI() {
        // 移除现有的通话控制区域
        const existingControl = document.querySelector('.call-control-section');
        if (existingControl) {
            existingControl.remove();
        }

        // 创建通话控制区域
        const controlSection = document.createElement('div');
        controlSection.className = 'call-control-section';
        controlSection.innerHTML = `
            <div class="monitor-section">
                <h4>📞 通话中</h4>
                <div class="call-controls">
                    <button id="global-mute-btn" class="btn-control-call btn-mute">🔇 静音</button>
                    <button id="global-hangup-btn" class="btn-control-call btn-danger">📞 挂断</button>
                </div>
                <div class="call-timer" id="global-call-timer">00:00</div>
            </div>
        `;

        // 插入到监控面板顶部
        const monitorContent = document.querySelector('.monitor-content');
        monitorContent.insertBefore(controlSection, monitorContent.firstChild);

        // 绑定事件
        document.getElementById('global-mute-btn').addEventListener('click', () => {
            this.toggleMute();
        });

        document.getElementById('global-hangup-btn').addEventListener('click', () => {
            this.hangupCall();
        });

        // 启动通话计时器
        this.startCallTimer();
    }

    /**
     * 切换静音
     */
    toggleMute() {
        const isMuted = this.webrtcManager.toggleMute();
        const muteBtn = document.getElementById('global-mute-btn');
        
        if (muteBtn) {
            if (isMuted) {
                muteBtn.textContent = '🔊 取消静音';
                muteBtn.classList.add('muted');
            } else {
                muteBtn.textContent = '🔇 静音';
                muteBtn.classList.remove('muted');
            }
        }

        this.addLog(isMuted ? '已静音' : '已取消静音', 'info');
    }

    /**
     * 挂断通话
     */
    async hangupCall() {
        try {
            const callId = this.currentIncomingCall?.callId;
            if (callId) {
                await window.globalSignalRManager.invoke("HangupCallAsync", {
                    CallId: callId,
                    Reason: "UserHangup"
                });
            }

            this.endCall();

        } catch (error) {
            console.error('挂断失败:', error);
            this.addLog(`挂断失败: ${error.message}`, 'error');
        }
    }

    /**
     * 结束通话
     */
    endCall() {
        // 清理WebRTC连接
        this.webrtcManager.cleanup();

        // 清理UI
        const controlSection = document.querySelector('.call-control-section');
        if (controlSection) {
            controlSection.remove();
        }

        // 停止计时器
        this.stopCallTimer();

        // 重置状态
        this.clearCurrentCall();
        this.addLog('通话已结束', 'info');
    }

    /**
     * 启动通话计时器
     */
    startCallTimer() {
        this.callStartTime = new Date();
        const timerElement = document.getElementById('global-call-timer');
        
        if (timerElement) {
            timerElement.textContent = "00:00";
        }

        this.callTimerInterval = setInterval(() => {
            if (timerElement) {
                const now = new Date();
                const diff = new Date(now - this.callStartTime);
                const minutes = diff.getUTCMinutes().toString().padStart(2, '0');
                const seconds = diff.getUTCSeconds().toString().padStart(2, '0');
                timerElement.textContent = `${minutes}:${seconds}`;
            }
        }, 1000);
    }

    /**
     * 停止通话计时器
     */
    stopCallTimer() {
        if (this.callTimerInterval) {
            clearInterval(this.callTimerInterval);
            this.callTimerInterval = null;
        }
    }

    /**
     * 清理当前通话
     */
    clearCurrentCall() {
        this.currentIncomingCall = null;
        this.isIncomingCallActive = false;
        this.isInCall = false;
        this.callStartTime = null;
        this.updateIncomingStatus('waiting', '等待来电...');
        this.updateIncomingCallsUI(false);
        
        // 重置悬浮按钮状态
        this.resetToggleButtonState();
    }

    /**
     * 处理通话接听（SignalR事件）
     */
    handleCallAnswered(data) {
        if (!this.isInCall) {
            this.updateIncomingStatus('active', '通话进行中');
            this.addLog('通话已接听', 'info');
        }
        this.refreshActiveCalls();
    }

    /**
     * 处理通话结束（SignalR事件）
     */
    handleCallEnded(data) {
        if (this.isInCall) {
            this.endCall();
        } else {
            this.clearCurrentCall();
        }
        this.refreshActiveCalls();
    }

    /**
     * 处理通话超时（SignalR事件）
     */
    handleCallTimeout(data) {
        this.clearCurrentCall();
        this.addLog('来电超时未接听', 'warning');
    }

    /**
     * 开始活跃通话轮询
     */
    startActiveCallsPolling() {
        // 立即获取一次
        this.refreshActiveCalls();
        
        // 每10秒刷新一次
        this.activeCallsPollingInterval = setInterval(() => {
            this.refreshActiveCalls();
        }, 10000);
    }

    /**
     * 刷新活跃通话
     */
    async refreshActiveCalls() {
        try {
            const response = await fetch('/Monitoring/GetActiveCalls');
            const data = await response.json();
            
            if (data.success) {
                this.monitoringData.activeSessions = data.sessions;
                this.updateActiveCallsUI();
                
                // 更新计数
                document.getElementById('active-calls-count').textContent = data.sessions.length;
            }
        } catch (error) {
            console.error('获取活跃通话失败:', error);
            this.addLog('获取活跃通话失败', 'error');
        }
    }

    /**
     * 更新来电状态
     */
    updateIncomingStatus(status, text) {
        const dot = document.getElementById('incoming-status-dot');
        const statusText = document.getElementById('incoming-status-text');
        
        dot.className = `status-dot ${status}`;
        statusText.textContent = text;
    }

    /**
     * 更新来电列表UI
     */
    updateIncomingCallsUI(showButtons = false) {
        const container = document.getElementById('incoming-calls-list');
        
        // 如果有当前来电，显示当前来电
        if (this.currentIncomingCall && this.isIncomingCallActive) {
            const call = this.currentIncomingCall;
            const callerName = call.caller?.sipUsername || call.caller?.userId || '未知来电';
            
            const buttonsHtml = showButtons ? `
                <div class="call-item-actions">
                    <button class="btn-small btn-answer" onclick="window.globalCallMonitor.answerCall('${call.callId}')">
                        📞 接听
                    </button>
                    <button class="btn-small btn-reject" onclick="window.globalCallMonitor.rejectCall('${call.callId}')">
                        ❌ 拒接
                    </button>
                </div>
            ` : '';
            
            container.innerHTML = `
                <div class="call-item incoming current-call">
                    <div class="call-item-header">
                        <span class="call-item-user">${callerName}</span>
                        <span class="call-status">来电中...</span>
                    </div>
                    <div>CallID: ${call.callId}</div>
                    ${call.isExternal ? '<div style="color: #FF9800;">外部来电</div>' : ''}
                    ${buttonsHtml}
                </div>
            `;
            return;
        }
        
        // 显示历史来电
        const recentCalls = this.monitoringData.incomingCalls.slice(-3); // 显示最近3个历史来电
        
        if (recentCalls.length === 0) {
            container.innerHTML = '<div style="text-align: center; color: #666; padding: 20px;">暂无来电</div>';
            return;
        }
        
        container.innerHTML = recentCalls.map(call => {
            const callerName = call.caller?.sipUsername || call.caller?.userId || '未知来电';
            const timeStr = call.timestamp.toLocaleTimeString();
            
            return `
                <div class="call-item">
                    <div class="call-item-header">
                        <span class="call-item-user">${callerName}</span>
                        <span class="call-item-time">${timeStr}</span>
                    </div>
                    <div>CallID: ${call.callId}</div>
                    ${call.isExternal ? '<div style="color: #FF9800;">外部来电</div>' : ''}
                </div>
            `;
        }).join('');
    }

    /**
     * 更新活跃通话UI
     */
    updateActiveCallsUI() {
        const container = document.getElementById('active-calls-list');
        
        if (this.monitoringData.activeSessions.length === 0) {
            container.innerHTML = '<div style="text-align: center; color: #666; padding: 20px;">暂无活跃通话</div>';
            return;
        }
        
        container.innerHTML = this.monitoringData.activeSessions.map(session => {
            const duration = Math.floor(session.duration);
            const minutes = Math.floor(duration / 60);
            const seconds = duration % 60;
            const durationStr = `${minutes}:${seconds.toString().padStart(2, '0')}`;
            
            return `
                <div class="call-item">
                    <div class="call-item-header">
                        <span class="call-item-user">${session.userName || '未知用户'}</span>
                        <span class="call-item-time">${durationStr}</span>
                    </div>
                    <div>用户ID: ${session.userId}</div>
                    <div>通话ID: ${session.callId}</div>
                    <div class="call-item-actions">
                        <button class="btn-small btn-monitor" onclick="window.open('/Monitoring/Monitor?userId=${session.userId}&callId=${session.callId}', '_blank')">
                            监听
                        </button>
                    </div>
                </div>
            `;
        }).join('');
    }

    /**
     * 添加日志
     */
    addLog(message, type = 'info') {
        const container = document.getElementById('event-logs');
        const time = new Date().toLocaleTimeString();
        
        const logEntry = document.createElement('div');
        logEntry.className = `log-entry ${type}`;
        logEntry.innerHTML = `
            <span class="log-time">${time}</span>
            <span>${message}</span>
        `;
        
        container.insertBefore(logEntry, container.firstChild);
        
        // 保持最多50条日志
        while (container.children.length > 50) {
            container.removeChild(container.lastChild);
        }
    }

    /**
     * 切换UI显示
     */
    toggleUI() {
        if (this.isUIVisible) {
            this.hideUI();
        } else {
            this.showUI();
        }
    }

    /**
     * 显示UI
     */
    showUI() {
        this.ui.style.display = 'block';
        this.isUIVisible = true;
        this.refreshActiveCalls(); // 显示时刷新数据
        
        // 如果在Home页面，检查是否需要避让
        if (this.isOnHomePage()) {
            setTimeout(() => {
                this.avoidHomePageButtons();
            }, 100); // 延迟执行，确保DOM已更新
        }
    }

    /**
     * 隐藏UI
     */
    hideUI() {
        this.ui.style.display = 'none';
        this.isUIVisible = false;
    }

    /**
     * 导出日志
     */
    exportLogs() {
        const logs = {
            timestamp: new Date().toISOString(),
            incomingCalls: this.monitoringData.incomingCalls,
            activeSessions: this.monitoringData.activeSessions,
            callEvents: this.monitoringData.callEvents
        };
        
        const blob = new Blob([JSON.stringify(logs, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        
        const a = document.createElement('a');
        a.href = url;
        a.download = `call-monitor-logs-${new Date().toISOString().slice(0, 19)}.json`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        
        this.addLog('日志已导出', 'info');
    }

    /**
     * 获取监控状态
     */
    getStatus() {
        return {
            isActive: this.isActive,
            incomingCallsCount: this.monitoringData.incomingCalls.length,
            activeSessionsCount: this.monitoringData.activeSessions.length,
            signalRConnected: this.signalRConnection?.state === 'Connected',
            usingSharedConnection: this.signalRConnection === window.phoneApp?.signalRManager?.connection
        };
    }

    /**
     * 清理资源
     */
    cleanup() {
        console.log('清理全局监控资源...');
        
        // 停止轮询
        if (this.activeCallsPollingInterval) {
            clearInterval(this.activeCallsPollingInterval);
        }
        
        // 停止通话计时器
        this.stopCallTimer();
        
        // 移除拖动事件监听器
        if (this.boundHandleDrag) {
            document.removeEventListener('mousemove', this.boundHandleDrag);
        }
        if (this.boundHandleDragEnd) {
            document.removeEventListener('mouseup', this.boundHandleDragEnd);
        }
        if (this.boundHandleTouchMove) {
            document.removeEventListener('touchmove', this.boundHandleTouchMove);
        }
        if (this.boundHandleTouchEnd) {
            document.removeEventListener('touchend', this.boundHandleTouchEnd);
        }
        
        // 确保拖动状态被重置
    
        
        // 恢复body样式
        document.body.style.userSelect = '';
        document.body.style.cursor = '';
        
        // 清理WebRTC资源
        this.webrtcManager.cleanup();
        
        // 如果使用的是独立连接，才关闭SignalR连接
        if (this.signalRConnection && 
            this.signalRConnection !== window.phoneApp?.signalRManager?.connection) {
            this.signalRConnection.stop();
            console.log('独立SignalR连接已关闭');
        } else {
            console.log('使用共享SignalR连接，不关闭连接');
        }
        
        // 隐藏UI
        if (this.ui) {
            this.ui.style.display = 'none';
        }
        
        // 重置状态
        this.clearCurrentCall();
        
        this.isActive = false;
        console.log('全局监控资源清理完成');
    }
}

// 创建全局实例
window.GlobalCallMonitor = GlobalCallMonitor;

// 页面加载完成后自动启动
document.addEventListener('DOMContentLoaded', () => {
    window.globalCallMonitor = new GlobalCallMonitor();
    console.log('全局通话监控系统已加载');
});

// 调试函数
window.debugGlobalMonitor = () => {
    if (window.globalCallMonitor) {
        console.log('全局监控状态:', window.globalCallMonitor.getStatus());
        console.log('监控数据:', window.globalCallMonitor.monitoringData);
    }
};

// 系统完整性检查函数
window.checkGlobalMonitorSystem = () => {
    const results = {
        timestamp: new Date().toISOString(),
        components: {},
        issues: [],
        recommendations: []
    };
    
    // 检查全局SignalR管理器
    if (window.globalSignalRManager) {
        results.components.globalSignalR = {
            loaded: true,
            connected: window.globalSignalRManager.isConnected,
            state: window.globalSignalRManager.connection?.state || 'unknown',
            handlersCount: window.globalSignalRManager.eventHandlers?.size || 0
        };
        
        if (!window.globalSignalRManager.isConnected) {
            results.issues.push('全局SignalR连接未建立');
            results.recommendations.push('检查网络连接和服务器状态');
        }
    } else {
        results.components.globalSignalR = { loaded: false };
        results.issues.push('全局SignalR管理器未加载');
        results.recommendations.push('检查global-signalr-manager.js是否正确加载');
    }
    
    // 检查全局监控
    if (window.globalCallMonitor) {
        const status = window.globalCallMonitor.getStatus();
        results.components.globalMonitor = {
            loaded: true,
            active: status.isActive,
            incomingCalls: status.incomingCallsCount,
            activeSessions: status.activeSessionsCount,
            signalRConnected: status.signalRConnected,
            usingSharedConnection: status.usingSharedConnection
        };
        
        if (!status.isActive) {
            results.issues.push('全局监控未激活');
            results.recommendations.push('检查监控初始化过程');
        }
    } else {
        results.components.globalMonitor = { loaded: false };
        results.issues.push('全局监控未加载');
        results.recommendations.push('检查global-call-monitor.js是否正确加载');
    }
    
    // 检查UI组件
    const monitorPanel = document.getElementById('global-monitor-panel');
    const toggleBtn = document.getElementById('monitor-toggle-btn');
    
    results.components.ui = {
        monitorPanel: !!monitorPanel,
        toggleButton: !!toggleBtn,
        panelVisible: monitorPanel?.style.display !== 'none'
    };
    
    if (!monitorPanel || !toggleBtn) {
        results.issues.push('监控UI组件缺失');
        results.recommendations.push('检查UI创建过程');
    }
    
    // 检查Home页面集成（如果在Home页面）
    if (window.phoneApp) {
        results.components.homeIntegration = {
            phoneAppLoaded: true,
            initialized: window.phoneApp.isInitialized,
            signalRManager: !!window.phoneApp.signalRManager,
            globalEventListeners: true // 假设已设置
        };
    } else {
        results.components.homeIntegration = {
            phoneAppLoaded: false,
            note: '不在Home页面或PhoneApp未加载'
        };
    }
    
    // 生成总结
    results.summary = {
        totalIssues: results.issues.length,
        systemHealth: results.issues.length === 0 ? 'healthy' : 
                     results.issues.length <= 2 ? 'warning' : 'critical',
        readyForUse: results.components.globalSignalR?.connected && 
                    results.components.globalMonitor?.active
    };
    
    console.log('=== 全局监控系统检查报告 ===');
    console.log(results);
    console.log('=== 检查报告结束 ===');
    
    return results;
};