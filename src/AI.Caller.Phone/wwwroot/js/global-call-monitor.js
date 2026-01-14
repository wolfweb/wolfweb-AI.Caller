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
            this.pc = new RTCPeerConnection({
                iceServers: [
                    { urls: 'stun:stun.l.google.com:19302' },
                    { urls: 'stun:stun1.l.google.com:19302' }
                ]
            });

            this.pc.onicecandidate = (event) => {
                if (event.candidate && window.globalSignalRManager) {
                    window.globalSignalRManager.invoke("SendIceCandidateAsync", {
                        CallId: window.globalCallMonitor.currentIncomingCall?.callId,
                        iceCandidate: JSON.stringify(event.candidate)
                    }).catch(error => {
                        console.error('发送ICE候选失败:', error);
                    });
                }
            };

            // 设置远程流事件
            this.pc.ontrack = (event) => {
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

        // 🔧 新增：事件去重机制
        this.lastProcessedEvents = new Map(); // 存储最近处理的事件
        this.eventDeduplicationTimeout = 1000; // 1秒内的重复事件将被忽略

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
    }

    /**
     * 创建监控UI
     */
    createMonitoringUI() {
        // 创建悬浮监控面板
        const monitorPanel = document.createElement('div');
        monitorPanel.id = 'global-monitor-panel';
        monitorPanel.className = 'global-monitor-panel';
        monitorPanel.innerHTML = `
            <div class="monitor-header">
                <span class="monitor-title">📞 通话监控</span>
                <div class="monitor-controls">
                    <button id="monitor-minimize" class="monitor-btn-control">−</button>
                    <button id="monitor-close" class="monitor-btn-control">×</button>
                </div>
            </div>
            <div class="monitor-content">                
                <div class="monitor-section">
                    <h4>📥 来电</h4>
                    <div class="monitor-status">
                        <span class="monitor-status-dot" id="incoming-status-dot"></span>
                        <span id="incoming-status-text">等待来电...</span>
                    </div>
                    <div id="incoming-calls-list" class="monitor-calls-list">
                        <!-- 来电列表 -->
                    </div>
                </div>

                <div class="monitor-section">
                    <h4>🔊 通话 (<span id="active-calls-count">0</span>)</h4>
                    <div id="active-calls-list" class="monitor-calls-list">
                        <!-- 活跃通话列表 -->
                    </div>
                </div>

                <div class="monitor-section">
                    <h4>⚡ 操作</h4>
                    <div class="monitor-quick-actions">
                        <button id="refresh-calls" class="monitor-btn-action">刷新</button>
                        <button id="view-all-monitoring" class="monitor-btn-action">监控</button>
                        <button id="export-logs" class="monitor-btn-action">日志</button>
                    </div>
                </div>

                <div class="monitor-section">
                    <h4>📋 日志</h4>
                    <div id="event-logs" class="monitor-event-logs">
                        <!-- 事件日志 -->
                    </div>
                </div>
            </div>
        `;

        // 样式已移至 enterprise-theme.css，无需内联样式

        document.body.appendChild(monitorPanel);

        // 创建右下角悬浮按钮
        const toggleBtn = document.createElement('div');
        toggleBtn.id = 'monitor-toggle-btn';
        toggleBtn.className = 'global-monitor-toggle';
        toggleBtn.innerHTML = `
            <svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor">
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
            // 初始化后再次检查安全区域（防止CSS加载延迟导致的尺寸问题）
            setTimeout(() => {
                this.detectSplitScreenMode(); // 🔧 检测分屏模式
                this.ensureCurrentPositionSafe('初始化检查');
            }, 200);
        }, 100);

        // 监听窗口大小变化，确保面板始终在安全区域内
        this.setupWindowResizeHandler();

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

        // 🔧 正确修复：获取面板在viewport中的实际位置
        const rect = this.ui.getBoundingClientRect();

        // 🔧 正确修复：计算鼠标相对于面板左上角的偏移
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
     * 安全区域检查 - 确保面板位置在屏幕范围内
     */
    ensureSafePosition(x, y) {
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        const panelWidth = this.ui.offsetWidth || 280;
        const panelHeight = this.ui.offsetHeight || 320;
        const safeMargin = 10; // 安全边距

        // 🔧 修复：正确的边界计算
        // 如果viewport比面板小，使用紧急位置
        if (viewportWidth < panelWidth + 20 || viewportHeight < panelHeight + 20) {
            console.warn(`Viewport太小 (${viewportWidth}x${viewportHeight})，使用紧急位置`);
            return this.getEmergencyPosition(viewportWidth, viewportHeight, panelWidth, panelHeight);
        }

        // 计算有效的边界范围
        const minX = safeMargin;
        const minY = safeMargin;
        const maxX = viewportWidth - panelWidth - safeMargin;
        const maxY = viewportHeight - panelHeight - safeMargin;

        // 🔧 修复：确保边界有效
        if (maxX < minX || maxY < minY) {
            console.warn('计算的边界无效，使用紧急位置');
            return this.getEmergencyPosition(viewportWidth, viewportHeight, panelWidth, panelHeight);
        }

        // 应用边界限制
        const safeX = Math.max(minX, Math.min(x, maxX));
        const safeY = Math.max(minY, Math.min(y, maxY));

        return { x: safeX, y: safeY };
    }

    /**
     * 紧急位置计算 - 当viewport太小时使用
     */
    getEmergencyPosition(viewportWidth, viewportHeight, panelWidth, panelHeight) {
        // 🔧 修复：完善紧急位置计算
        let emergencyX, emergencyY;

        // X轴位置计算
        if (viewportWidth >= panelWidth + 10) {
            // 宽度足够，居中显示
            emergencyX = Math.max(5, (viewportWidth - panelWidth) / 2);
        } else if (viewportWidth >= panelWidth) {
            // 刚好够宽，左对齐
            emergencyX = Math.max(0, viewportWidth - panelWidth);
        } else {
            // 宽度不够，左对齐
            emergencyX = 0;
        }

        // Y轴位置计算
        if (viewportHeight >= panelHeight + 10) {
            // 高度足够，顶部显示
            emergencyY = 5;
        } else if (viewportHeight >= panelHeight) {
            // 刚好够高，顶部对齐
            emergencyY = Math.max(0, viewportHeight - panelHeight);
        } else {
            // 高度不够，顶部对齐
            emergencyY = 0;
        }

        return { x: emergencyX, y: emergencyY };
    }

    /**
     * 设置面板位置（统一的位置设置方法）
     */
    setPanelPosition(x, y) {
        const safePosition = this.ensureSafePosition(x, y);

        // 🔧 修复：只使用CSS变量，避免与transform冲突
        this.ui.style.setProperty('--panel-x', safePosition.x + 'px');
        this.ui.style.setProperty('--panel-y', safePosition.y + 'px');

        // 🔧 清除可能的left/top设置，避免冲突
        this.ui.style.left = '';
        this.ui.style.top = '';
        this.ui.style.right = '';
        this.ui.style.bottom = '';

        return safePosition;
    }

    /**
     * 更新面板位置
     */
    updatePosition(clientX, clientY) {
        const newX = clientX - this.dragOffset.x;
        const newY = clientY - this.dragOffset.y;

        // 使用统一的安全位置设置方法
        this.setPanelPosition(newX, newY);
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
            // 获取当前位置（优先使用CSS变量）
            const x = parseFloat(this.ui.style.getPropertyValue('--panel-x')) ||
                parseFloat(this.ui.style.left) || 20;
            const y = parseFloat(this.ui.style.getPropertyValue('--panel-y')) ||
                parseFloat(this.ui.style.top) || 20;

            const position = { x, y };
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

                // 支持新格式 {x, y} 和旧格式 {left, top}
                let x, y;
                if (position.x !== undefined && position.y !== undefined) {
                    x = position.x;
                    y = position.y;
                } else if (position.left && position.top) {
                    // 兼容旧格式
                    x = parseFloat(position.left);
                    y = parseFloat(position.top);
                } else {
                    return; // 无效的位置数据
                }

                // 使用安全位置设置
                const safePosition = this.setPanelPosition(x, y);
            } else {
                // 没有保存的位置，设置默认安全位置
                this.setDefaultPosition();
            }
        } catch (error) {
            console.warn('恢复面板位置失败:', error);
            // 出错时设置默认位置
            this.setDefaultPosition();
        }
    }

    /**
     * 设置默认安全位置
     */
    setDefaultPosition() {
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        const panelWidth = this.ui.offsetWidth || 280;
        const panelHeight = this.ui.offsetHeight || 320;

        // 🔧 修复：简化并修正默认位置逻辑
        let defaultX, defaultY;

        // X轴位置：优先右上角，其次居中，最后左上角
        if (viewportWidth >= panelWidth + 40) {
            // 有足够空间，放在右上角
            defaultX = viewportWidth - panelWidth - 20;
        } else if (viewportWidth >= panelWidth + 20) {
            // 空间较小，居中显示
            defaultX = (viewportWidth - panelWidth) / 2;
        } else {
            // 空间很小，左对齐
            defaultX = Math.max(0, Math.min(10, viewportWidth - panelWidth));
        }

        // Y轴位置：优先顶部，空间不够时调整
        if (viewportHeight >= panelHeight + 40) {
            defaultY = 20;
        } else if (viewportHeight >= panelHeight + 20) {
            defaultY = 10;
        } else {
            defaultY = Math.max(0, Math.min(5, viewportHeight - panelHeight));
        }

        // 🔧 最终边界检查
        defaultX = Math.max(0, Math.min(defaultX, Math.max(0, viewportWidth - panelWidth)));
        defaultY = Math.max(0, Math.min(defaultY, Math.max(0, viewportHeight - panelHeight)));

        this.setPanelPosition(defaultX, defaultY);
    }

    /**
     * 设置窗口大小变化处理
     */
    setupWindowResizeHandler() {
        // 防抖处理，避免频繁调用
        let resizeTimeout;

        const handleResize = () => {
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(() => {
                this.handleWindowResize();
            }, 250); // 250ms 防抖
        };

        window.addEventListener('resize', handleResize);

        // 保存引用以便清理
        this.windowResizeHandler = handleResize;
    }

    /**
     * 处理窗口大小变化
     */
    handleWindowResize() {
        if (!this.ui) return;

        // 🔧 检测分屏情况
        this.detectSplitScreenMode();

        this.ensureCurrentPositionSafe('窗口大小变化');
    }

    /**
     * 检测分屏模式并调整策略
     */
    detectSplitScreenMode() {
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        const screenWidth = window.screen.width;
        const screenHeight = window.screen.height;

        const isLikelySplitScreen =
            viewportWidth < screenWidth * 0.8 || // 宽度明显小于屏幕宽度
            viewportHeight < screenHeight * 0.8;  // 高度明显小于屏幕高度

        const isNarrowScreen = viewportWidth < 600; // 窄屏判断
        const isShortScreen = viewportHeight < 500; // 矮屏判断

        if (isLikelySplitScreen || isNarrowScreen || isShortScreen) {
            this.setDefaultPosition();
            this.addLog('检测到分屏模式，已调整面板位置', 'info');
        }
    }

    /**
     * 确保当前位置在安全区域内
     */
    ensureCurrentPositionSafe(reason = '位置检查') {
        if (!this.ui) return;

        const currentX = parseFloat(this.ui.style.getPropertyValue('--panel-x')) ||
            parseFloat(this.ui.style.left) || 20;
        const currentY = parseFloat(this.ui.style.getPropertyValue('--panel-y')) ||
            parseFloat(this.ui.style.top) || 20;

        const safePosition = this.setPanelPosition(currentX, currentY);

        if (Math.abs(safePosition.x - currentX) > 1 || Math.abs(safePosition.y - currentY) > 1) {
            this.addLog(`${reason}，已调整面板位置`, 'info');
        }

        return safePosition;
    }

    /**
     * 更新悬浮按钮状态（来电提示）
     */
    updateToggleButtonForIncomingCall() {
        const toggleBtn = document.getElementById('monitor-toggle-btn');
        if (toggleBtn) {
            // 使用CSS类管理来电状态
            toggleBtn.classList.add('incoming-call');
            toggleBtn.innerHTML = `
                <svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M6.62 10.79c1.44 2.83 3.76 5.14 6.59 6.59l2.2-2.2c.27-.27.67-.36 1.02-.24 1.12.37 2.33.57 3.57.57.55 0 1 .45 1 1V20c0 .55-.45 1-1 1-9.39 0-17-7.61-17-17 0-.55.45-1 1-1h3.5c.55 0 1 .45 1 1 0 1.25.2 2.45.57 3.57.11.35.03.74-.25 1.02l-2.2 2.2z"/>
                </svg>
            `;
            toggleBtn.title = '🔥 有新来电！点击查看监控面板';
        }
    }

    /**
     * 重置悬浮按钮状态
     */
    resetToggleButtonState() {
        const toggleBtn = document.getElementById('monitor-toggle-btn');
        if (toggleBtn) {
            // 使用CSS类管理状态
            toggleBtn.classList.remove('incoming-call');
            toggleBtn.innerHTML = `
                <svg width="24" height="24" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M6.62 10.79c1.44 2.83 3.76 5.14 6.59 6.59l2.2-2.2c.27-.27.67-.36 1.02-.24 1.12.37 2.33.57 3.57.57.55 0 1 .45 1 1V20c0 .55-.45 1-1 1-9.39 0-17-7.61-17-17 0-.55.45-1 1-1h3.5c.55 0 1 .45 1 1 0 1.25.2 2.45.57 3.57.11.35.03.74-.25 1.02l-2.2 2.2z"/>
                </svg>
            `;
            toggleBtn.title = '全局通话监控 - 点击打开/关闭';
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
            this.setDefaultPosition();
            return;
        }

        // 计算避让位置
        const avoidPosition = this.calculateSafePosition(importantButtons);

        if (avoidPosition) {
            // 使用统一的安全位置设置方法
            const finalPosition = this.setPanelPosition(avoidPosition.left, avoidPosition.top);
            this.addLog('已调整位置避让Home页面按钮', 'info');
        } else {
            // 如果无法找到合适的避让位置，使用默认安全位置
            this.setDefaultPosition();
            this.addLog('无法找到合适避让位置，使用默认位置', 'warning');
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
        const panelWidth = this.ui.offsetWidth || 280; // 🔧 从350减少到280
        const panelHeight = this.ui.offsetHeight || 320; // 🔧 从400减少到320
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
                return position;
            }
        }

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

                // 🔧 启用SignalR事件调试
                this.enableSignalRDebug();
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
                    resolve(true);
                } else if (attempts >= maxAttempts) {
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

        // 🔧 注册对方挂断事件
        window.globalSignalRManager.registerEventHandler(handlerId, 'remoteHangup', (data) => {
            this.handleRemoteHangup(data);
        });

        // 🔧 注册通话失败事件
        window.globalSignalRManager.registerEventHandler(handlerId, 'callFailed', (data) => {
            this.handleCallFailed(data);
        });

        // 🔧 注册网络断开事件
        window.globalSignalRManager.registerEventHandler(handlerId, 'connectionLost', (data) => {
            this.handleConnectionLost(data);
        });

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
                    resolve(true); // 找到PhoneApp
                } else if (attempts >= maxAttempts) {
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
        this.signalRConnection = new signalR.HubConnectionBuilder()
            .withUrl("/webrtc")
            .withHubProtocol(new signalR.protocols.msgpack.MessagePackHubProtocol()) 
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

        // 🔧 监听对方挂断事件
        this.signalRConnection.on("remoteHangup", (data) => {
            this.handleRemoteHangup(data);
        });

        // 🔧 监听其他挂断相关事件
        this.signalRConnection.on("callFailed", (data) => {
            this.handleCallFailed(data);
        });

        this.signalRConnection.on("connectionLost", (data) => {
            this.handleConnectionLost(data);
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
            this.handleCallEnded({ reason: 'local' });
        });

        document.addEventListener('remoteHangup', () => {
            this.addLog('检测到对方挂断事件', 'warning');
            this.handleCallEnded({ reason: 'remote' });
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
     * 事件去重检查
     */
    isDuplicateEvent(eventType, callId) {
        const eventKey = `${eventType}_${callId || 'unknown'}`;
        const now = Date.now();
        const lastProcessed = this.lastProcessedEvents.get(eventKey);

        if (lastProcessed && (now - lastProcessed) < this.eventDeduplicationTimeout) {
            console.log(`🔄 忽略重复事件: ${eventType} (${callId})`);
            return true;
        }

        this.lastProcessedEvents.set(eventKey, now);

        // 清理过期的事件记录
        for (const [key, timestamp] of this.lastProcessedEvents.entries()) {
            if (now - timestamp > this.eventDeduplicationTimeout * 2) {
                this.lastProcessedEvents.delete(key);
            }
        }

        return false;
    }

    /**
     * 处理通话接听（SignalR事件）
     */
    handleCallAnswered(data) {
        const callId = data?.callId || this.currentIncomingCall?.callId;
        if (this.isDuplicateEvent('callAnswered', callId)) return;

        if (!this.isInCall) {
            this.isInCall = true;
            this.isIncomingCallActive = false;
            this.updateIncomingStatus('active', '通话进行中');
            this.addLog('通话已接听', 'success');

            // 显示通话控制界面
            this.showCallControlUI();
        }

        // 刷新活跃通话列表
        this.refreshActiveCalls();
    }

    /**
     * 处理通话结束（SignalR事件）
     */
    handleCallEnded(data) {
        const callId = data?.callId || this.currentIncomingCall?.callId;
        if (this.isDuplicateEvent('callEnded', callId)) return;

        const reason = data?.reason || 'unknown';
        let logMessage = '通话已结束';

        // 根据结束原因显示不同的消息
        switch (reason) {
            case 'remote':
                logMessage = '对方已挂断';
                break;
            case 'local':
                logMessage = '本地挂断';
                break;
            case 'timeout':
                logMessage = '通话超时';
                break;
            case 'rejected':
                logMessage = '通话被拒绝';
                break;
            case 'network':
                logMessage = '网络断开';
                break;
            default:
                logMessage = `通话结束 (${reason})`;
        }

        this.addLog(logMessage, reason === 'remote' ? 'warning' : 'info');

        // 清理通话状态
        this.endCall();

        // 刷新活跃通话列表
        this.refreshActiveCalls();
    }

    /**
     * 处理通话超时（SignalR事件）
     */
    handleCallTimeout(data) {
        this.addLog('来电超时未接听', 'warning');

        // 清理当前通话状态
        this.clearCurrentCall();

        // 刷新活跃通话列表
        this.refreshActiveCalls();
    }

    /**
     * 处理对方挂断（SignalR事件）
     */
    handleRemoteHangup(data) {
        this.addLog('对方已挂断通话', 'warning');

        // 清理通话状态
        this.endCall();

        // 刷新活跃通话列表
        this.refreshActiveCalls();

        // 如果面板可见，显示提示
        if (this.isUIVisible) {
            this.updateIncomingStatus('ended', '对方已挂断');

            // 3秒后恢复到等待状态
            setTimeout(() => {
                if (!this.isIncomingCallActive && !this.isInCall) {
                    this.updateIncomingStatus('waiting', '等待来电...');
                }
            }, 3000);
        }
    }

    /**
     * 处理通话失败（SignalR事件）
     */
    handleCallFailed(data) {
        const reason = data?.reason || 'unknown';
        const errorMessage = data?.message || '通话连接失败';

        this.addLog(`通话失败: ${errorMessage}`, 'error');

        // 清理通话状态
        this.clearCurrentCall();

        // 显示错误状态
        if (this.isUIVisible) {
            this.updateIncomingStatus('error', `通话失败: ${reason}`);

            // 5秒后恢复到等待状态
            setTimeout(() => {
                if (!this.isIncomingCallActive && !this.isInCall) {
                    this.updateIncomingStatus('waiting', '等待来电...');
                }
            }, 5000);
        }

        // 刷新活跃通话列表
        this.refreshActiveCalls();
    }

    /**
     * 处理连接丢失（SignalR事件）
     */
    handleConnectionLost(data) {
        this.addLog('网络连接丢失，通话中断', 'error');

        // 清理通话状态
        this.endCall();

        // 显示连接丢失状态
        if (this.isUIVisible) {
            this.updateIncomingStatus('error', '网络连接丢失');

            // 5秒后恢复到等待状态
            setTimeout(() => {
                if (!this.isIncomingCallActive && !this.isInCall) {
                    this.updateIncomingStatus('waiting', '等待来电...');
                }
            }, 5000);
        }

        // 刷新活跃通话列表
        this.refreshActiveCalls();
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
                    <button id="global-mute-btn" class="btn btn-sm btn-control-call btn-warning">🔇 静音</button>
                    <button id="global-hangup-btn" class="btn btn-sm btn-control-call btn-danger">📞 挂断</button>
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
                    <button class="btn btn-sm btn-success" onclick="window.globalCallMonitor.answerCall('${call.callId}')">
                        📞 接听
                    </button>
                    <button class="btn btn-sm btn-danger" onclick="window.globalCallMonitor.rejectCall('${call.callId}')">
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
                        <button class="btn btn-sm btn-danger" onclick="window.open('/Monitoring/Monitor?userId=${session.userId}&callId=${session.callId}', '_blank')">
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
     * 公共方法：手动触发安全区域检查
     * 可以在页面布局变化后调用此方法确保面板位置安全
     */
    checkSafePosition() {
        return this.ensureCurrentPositionSafe('手动检查');
    }

    /**
     * 公共方法：重置面板到安全位置
     * 当面板超出可视范围时可以调用此方法
     */
    resetToSafePosition() {
        this.detectSplitScreenMode();
        this.setDefaultPosition();
        this.addLog('面板已重置到安全位置', 'info');

        // 如果面板隐藏，显示它
        if (!this.isUIVisible) {
            this.showUI();
        }

        return this.getStatus();
    }

    /**
     * 公共方法：启用SignalR事件调试（仅记录，不处理）
     */
    enableSignalRDebug() {
        console.log('启用SignalR事件调试模式');

        // 🔧 修复：只记录事件，不重复处理
        if (window.globalSignalRManager && window.globalSignalRManager.connection) {
            const connection = window.globalSignalRManager.connection;

            // 监听所有可能的事件，但只用于调试记录
            const possibleEvents = [
                'callEnded',        // 通话结束
                'callTimeout',      // 通话超时
                'remoteHangup',     // 对方挂断
                'callFailed',       // 通话失败
                'connectionLost'    // 连接丢失
            ];

            // 🔧 修复：使用不同的处理器ID避免冲突
            const debugHandlerId = 'global-monitor-debug';

            possibleEvents.forEach(eventName => {
                window.globalSignalRManager.registerEventHandler(debugHandlerId, eventName, (data) => {
                    console.log(`🔥 [DEBUG] 收到SignalR事件: ${eventName}`, data);
                    this.addLog(`[调试] 收到事件: ${eventName}`, 'info');
                    // 🔧 修复：不在这里处理事件，避免重复处理
                });
            });

            this.addLog('SignalR调试模式已启用', 'info');
        }
    }

    /**
     * 公共方法：获取当前viewport信息（调试用）
     */
    getViewportInfo() {
        const info = {
            viewport: {
                width: window.innerWidth,
                height: window.innerHeight
            },
            screen: {
                width: window.screen.width,
                height: window.screen.height
            },
            panel: {
                width: this.ui?.offsetWidth || 280,
                height: this.ui?.offsetHeight || 320,
                actualWidth: this.ui?.offsetWidth,
                actualHeight: this.ui?.offsetHeight
            },
            currentPosition: {
                x: parseFloat(this.ui?.style.getPropertyValue('--panel-x')) || 0,
                y: parseFloat(this.ui?.style.getPropertyValue('--panel-y')) || 0
            },
            dragState: {
                isDragging: this.isDragging,
                dragType: this.dragType,
                dragOffset: this.dragOffset
            },
            uiState: {
                isVisible: this.isUIVisible,
                isUserManuallyHidden: this.isUserManuallyHidden
            }
        };

        return info;
    }

    /**
     * 清理资源
     */
    cleanup() {
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

        // 移除窗口大小变化监听器
        if (this.windowResizeHandler) {
            window.removeEventListener('resize', this.windowResizeHandler);
            this.windowResizeHandler = null;
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
    }
}

// 创建全局实例
window.GlobalCallMonitor = GlobalCallMonitor;

// 页面加载完成后自动启动
document.addEventListener('DOMContentLoaded', () => {
    window.globalCallMonitor = new GlobalCallMonitor();
});

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

    return results;
};