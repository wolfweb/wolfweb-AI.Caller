/**
 * 性能监控工具
 * 监控HangupHandler和相关UI组件的性能
 */

class PerformanceMonitor {
    constructor() {
        this.metrics = {
            hangupOperations: [],
            uiUpdates: [],
            memoryUsage: [],
            eventListeners: new Set()
        };
        
        this.isMonitoring = false;
        this.monitoringInterval = null;
    }

    /**
     * 开始性能监控
     */
    startMonitoring() {
        if (this.isMonitoring) {
            console.warn('性能监控已在运行');
            return;
        }

        this.isMonitoring = true;
        console.log('开始性能监控...');

        // 监控内存使用情况
        this.monitoringInterval = setInterval(() => {
            this.recordMemoryUsage();
        }, 5000); // 每5秒记录一次

        // 监控DOM变化
        this.setupDOMObserver();

        // 监控事件监听器
        this.monitorEventListeners();
    }

    /**
     * 停止性能监控
     */
    stopMonitoring() {
        if (!this.isMonitoring) {
            console.warn('性能监控未在运行');
            return;
        }

        this.isMonitoring = false;
        console.log('停止性能监控');

        if (this.monitoringInterval) {
            clearInterval(this.monitoringInterval);
            this.monitoringInterval = null;
        }

        if (this.domObserver) {
            this.domObserver.disconnect();
            this.domObserver = null;
        }
    }

    /**
     * 记录挂断操作性能
     */
    recordHangupOperation(operationType, startTime, endTime, success = true) {
        const duration = endTime - startTime;
        const record = {
            type: operationType,
            duration: duration,
            success: success,
            timestamp: new Date().toISOString()
        };

        this.metrics.hangupOperations.push(record);

        // 只保留最近的100条记录
        if (this.metrics.hangupOperations.length > 100) {
            this.metrics.hangupOperations.shift();
        }

        console.log(`挂断操作性能: ${operationType} - ${duration}ms - ${success ? '成功' : '失败'}`);

        // 如果操作时间过长，发出警告
        if (duration > 1000) {
            console.warn(`挂断操作 ${operationType} 耗时过长: ${duration}ms`);
        }
    }

    /**
     * 记录UI更新性能
     */
    recordUIUpdate(updateType, startTime, endTime) {
        const duration = endTime - startTime;
        const record = {
            type: updateType,
            duration: duration,
            timestamp: new Date().toISOString()
        };

        this.metrics.uiUpdates.push(record);

        // 只保留最近的200条记录
        if (this.metrics.uiUpdates.length > 200) {
            this.metrics.uiUpdates.shift();
        }

        // 如果UI更新时间过长，发出警告
        if (duration > 16) { // 60fps = 16.67ms per frame
            console.warn(`UI更新 ${updateType} 耗时过长: ${duration}ms`);
        }
    }

    /**
     * 记录内存使用情况
     */
    recordMemoryUsage() {
        if (!performance.memory) {
            return; // 某些浏览器不支持
        }

        const memoryInfo = {
            used: performance.memory.usedJSHeapSize,
            total: performance.memory.totalJSHeapSize,
            limit: performance.memory.jsHeapSizeLimit,
            timestamp: new Date().toISOString()
        };

        this.metrics.memoryUsage.push(memoryInfo);

        // 只保留最近的50条记录
        if (this.metrics.memoryUsage.length > 50) {
            this.metrics.memoryUsage.shift();
        }

        // 检查内存使用是否过高
        const usagePercent = (memoryInfo.used / memoryInfo.limit) * 100;
        if (usagePercent > 80) {
            console.warn(`内存使用率过高: ${usagePercent.toFixed(1)}%`);
        }
    }

    /**
     * 设置DOM观察器
     */
    setupDOMObserver() {
        if (!window.MutationObserver) {
            return; // 不支持MutationObserver
        }

        this.domObserver = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                if (mutation.type === 'childList' && mutation.addedNodes.length > 0) {
                    // 检查是否有大量DOM节点被添加
                    if (mutation.addedNodes.length > 10) {
                        console.warn(`一次性添加了大量DOM节点: ${mutation.addedNodes.length}`);
                    }
                }
            });
        });

        // 观察整个文档的变化
        this.domObserver.observe(document.body, {
            childList: true,
            subtree: true
        });
    }

    /**
     * 监控事件监听器
     */
    monitorEventListeners() {
        // 重写addEventListener来跟踪事件监听器
        const originalAddEventListener = EventTarget.prototype.addEventListener;
        const originalRemoveEventListener = EventTarget.prototype.removeEventListener;
        const monitor = this;

        EventTarget.prototype.addEventListener = function(type, listener, options) {
            const listenerInfo = {
                target: this,
                type: type,
                listener: listener,
                timestamp: Date.now()
            };
            
            monitor.metrics.eventListeners.add(listenerInfo);
            return originalAddEventListener.call(this, type, listener, options);
        };

        EventTarget.prototype.removeEventListener = function(type, listener, options) {
            // 查找并移除对应的监听器记录
            for (const listenerInfo of monitor.metrics.eventListeners) {
                if (listenerInfo.target === this && 
                    listenerInfo.type === type && 
                    listenerInfo.listener === listener) {
                    monitor.metrics.eventListeners.delete(listenerInfo);
                    break;
                }
            }
            
            return originalRemoveEventListener.call(this, type, listener, options);
        };
    }

    /**
     * 获取性能报告
     */
    getPerformanceReport() {
        const report = {
            hangupOperations: {
                total: this.metrics.hangupOperations.length,
                averageDuration: this.calculateAverageDuration(this.metrics.hangupOperations),
                successRate: this.calculateSuccessRate(this.metrics.hangupOperations)
            },
            uiUpdates: {
                total: this.metrics.uiUpdates.length,
                averageDuration: this.calculateAverageDuration(this.metrics.uiUpdates),
                slowUpdates: this.metrics.uiUpdates.filter(u => u.duration > 16).length
            },
            memory: {
                current: this.metrics.memoryUsage.length > 0 ? 
                    this.metrics.memoryUsage[this.metrics.memoryUsage.length - 1] : null,
                peak: this.calculatePeakMemoryUsage()
            },
            eventListeners: {
                active: this.metrics.eventListeners.size,
                types: this.getEventListenerTypes()
            }
        };

        return report;
    }

    /**
     * 计算平均持续时间
     */
    calculateAverageDuration(operations) {
        if (operations.length === 0) return 0;
        
        const totalDuration = operations.reduce((sum, op) => sum + op.duration, 0);
        return totalDuration / operations.length;
    }

    /**
     * 计算成功率
     */
    calculateSuccessRate(operations) {
        if (operations.length === 0) return 0;
        
        const successCount = operations.filter(op => op.success).length;
        return (successCount / operations.length) * 100;
    }

    /**
     * 计算峰值内存使用
     */
    calculatePeakMemoryUsage() {
        if (this.metrics.memoryUsage.length === 0) return null;
        
        return this.metrics.memoryUsage.reduce((peak, current) => 
            current.used > peak.used ? current : peak
        );
    }

    /**
     * 获取事件监听器类型统计
     */
    getEventListenerTypes() {
        const types = {};
        for (const listener of this.metrics.eventListeners) {
            types[listener.type] = (types[listener.type] || 0) + 1;
        }
        return types;
    }

    /**
     * 打印性能报告
     */
    printPerformanceReport() {
        const report = this.getPerformanceReport();
        
        console.log('\n=== 性能监控报告 ===');
        console.log('挂断操作:');
        console.log(`  总数: ${report.hangupOperations.total}`);
        console.log(`  平均耗时: ${report.hangupOperations.averageDuration.toFixed(2)}ms`);
        console.log(`  成功率: ${report.hangupOperations.successRate.toFixed(1)}%`);
        
        console.log('UI更新:');
        console.log(`  总数: ${report.uiUpdates.total}`);
        console.log(`  平均耗时: ${report.uiUpdates.averageDuration.toFixed(2)}ms`);
        console.log(`  慢更新数: ${report.uiUpdates.slowUpdates}`);
        
        if (report.memory.current) {
            console.log('内存使用:');
            console.log(`  当前: ${(report.memory.current.used / 1024 / 1024).toFixed(2)}MB`);
            console.log(`  峰值: ${(report.memory.peak.used / 1024 / 1024).toFixed(2)}MB`);
        }
        
        console.log('事件监听器:');
        console.log(`  活跃数量: ${report.eventListeners.active}`);
        console.log(`  类型分布:`, report.eventListeners.types);
        
        console.log('=== 报告结束 ===\n');
    }

    /**
     * 检查潜在的性能问题
     */
    checkPerformanceIssues() {
        const issues = [];
        const report = this.getPerformanceReport();

        // 检查挂断操作性能
        if (report.hangupOperations.averageDuration > 500) {
            issues.push('挂断操作平均耗时过长');
        }

        if (report.hangupOperations.successRate < 95) {
            issues.push('挂断操作成功率偏低');
        }

        // 检查UI更新性能
        if (report.uiUpdates.averageDuration > 16) {
            issues.push('UI更新平均耗时过长，可能影响流畅度');
        }

        // 检查内存使用
        if (report.memory.current) {
            const usagePercent = (report.memory.current.used / report.memory.current.limit) * 100;
            if (usagePercent > 70) {
                issues.push('内存使用率较高');
            }
        }

        // 检查事件监听器
        if (report.eventListeners.active > 50) {
            issues.push('活跃事件监听器数量较多，可能存在内存泄漏');
        }

        return issues;
    }
}

// 导出到全局作用域
window.PerformanceMonitor = PerformanceMonitor;

// 创建全局性能监控实例
window.performanceMonitor = new PerformanceMonitor();

console.log('性能监控工具已加载。使用 performanceMonitor.startMonitoring() 开始监控。');