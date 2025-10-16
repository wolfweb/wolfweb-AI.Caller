# AI.Caller

一个基于 .NET 8 的智能 SIP 电话系统，集成了 AI 驱动的呼叫任务管理、自动应答和实时语音处理功能。

## 项目概述

AI.Caller 是一个现代化的企业级 SIP 电话解决方案，它不仅提供标准的 Web 界面进行电话呼叫管理，还引入了 AI 能力来自动化呼出任务和应答来电。系统采用模块化架构，分为核心 SIP 功能库（AI.Caller.Core）和 Web 应用程序（AI.Caller.Phone）两个主要组件，并通过后台任务队列实现高效的异步处理。

## 技术栈

### 核心框架
- **.NET 8.0** - 主要开发框架
- **ASP.NET Core MVC** - Web 应用框架
- **Entity Framework Core** & **SQLite** - 数据持久化
- **SignalR** - 实时双向通信

### 通信协议
- **SIPSorcery** - 核心 SIP/RTP 协议栈
- **WebRTC** - 浏览器端实时音视频通信
- **G.711 (PCMA/PCMU)** - 音频编解码

### AI 与音频处理
- **Sherpa-ONNX** - 语音活动检测 (VAD) 和语音识别 (ASR)
- **FFmpeg** - 音频格式转换和处理
- **自定义 TTS 引擎** - 文本转语音合成

### 数据处理
- **ExcelMapper & NPOI & ClosedXML** - Excel 文件处理
- **System.Threading.Channels** - 高性能异步数据流
- **IHostedService** - 后台任务队列

## 主要功能

### 核心通信功能
- **多场景 SIP 通话支持**
  - Web 到 Web：浏览器之间的 WebRTC 通话
  - Web 到移动端：浏览器呼叫外部 SIP 设备
  - 移动端到 Web：外部 SIP 设备呼入浏览器
  - 服务器到 Web/移动端：AI 坐席自动外呼
- **通话管理与监控** - 实时通话状态监控、通话建立、应答、挂断、DTMF 发送等完整控制
- **通话录音** - 支持对通话进行实时录音和回放
- **SIP 客户端池管理** - 高效的 SIP 客户端资源池，支持动态分配和复用

### AI 驱动的智能功能
- **AI 自动应答（来电处理）**
  - 智能来电路由：根据被叫号码自动路由到对应坐席或 AI 客服
  - 自动应答：外部电话呼入时自动应答并播放预设的 TTS 语音
  - 语音活动检测（VAD）：实时检测用户语音，智能暂停/恢复 AI 播报
  - 双向音频桥接：支持 SIP 和 WebRTC 之间的音频流转换

- **AI 外呼任务管理（去电处理）**
  - **统一任务视图**：Web 界面集中管理所有外呼任务状态（排队中、进行中、已完成、失败）
  - **单个任务创建**：快速创建单个外呼任务，支持自定义 TTS 脚本和变量替换
  - **批量任务创建**：
    - 动态模板下载：根据 TTS 模板自动生成 Excel 模板（包含电话号码、姓名、订单号等字段）
    - 批量上传：解析 Excel 文件，为每行数据创建独立的呼叫任务
  - **后台异步处理**：基于 `System.Threading.Channels` 的高性能任务队列
  - **动态坐席调度**：自动从可用 AI 坐席池中选择空闲坐席执行任务
  - **智能重试机制**：支持失败任务的自动重试

- **实时音频处理**
  - TTS 流式合成：支持流式文本转语音，降低延迟
  - 音频重采样：自动处理不同采样率的音频转换（8kHz ↔ 16kHz ↔ 24kHz）
  - G.711 编解码：支持 PCMA 和 PCMU 编码
  - Jitter Buffer：抖动缓冲区确保音频播放流畅
  - 自适应播放：根据网络状况动态调整音频播放速率

### 基础管理功能
- **用户认证与授权** - 基于 Cookie 的安全认证系统
- **SIP 账户管理** - 管理 SIP 服务器、用户名、密码等配置
- **TTS 模板管理** - 创建和管理支持变量替换的 TTS 文本模板

## 项目结构

```
AI.Caller/
├── src/
│   ├── AI.Caller.Core/              # 核心 SIP 和音频处理库
│   │   ├── CallAutomation/          # AI 自动应答核心逻辑
│   │   │   └── AIAutoResponder.cs   # AI 自动应答器（TTS 播放、VAD 检测）
│   │   ├── Media/                   # 音频处理模块
│   │   │   ├── AudioBridge.cs       # 音频桥接（SIP ↔ AI）
│   │   │   ├── AudioResampler.cs    # 音频重采样
│   │   │   ├── Encoders/            # G.711 编解码器
│   │   │   ├── Vad/                 # 语音活动检测
│   │   │   └── Asr/                 # 语音识别（预留）
│   │   ├── Network/                 # 网络监控服务
│   │   ├── SIPClient.cs             # SIP 客户端封装
│   │   ├── SIPClientPoolManager.cs  # SIP 客户端池管理
│   │   ├── MediaSessionManager.cs   # 媒体会话管理（RTP/WebRTC）
│   │   └── SIPTransportManager.cs   # SIP 传输层管理
│   │
│   └── AI.Caller.Phone/             # Web 应用程序
│       ├── Controllers/             # MVC 控制器
│       ├── Services/                # 业务服务层
│       │   ├── ICallManager.cs      # 通话管理服务（含实现）
│       │   ├── AICustomerServiceManager.cs  # AI 客服管理
│       │   ├── CallProcessor.cs     # 外呼任务处理器
│       │   └── BackgroundTaskQueue.cs       # 后台任务队列
│       ├── CallRouting/             # 来电路由服务
│       ├── BackgroundTask/          # 后台服务
│       ├── Hubs/                    # SignalR 实时通信
│       ├── Entities/                # 数据模型
│       └── Views/                   # MVC 视图
│
├── tests/                           # 单元测试项目
└── deploy/                          # 部署文件
```

## 架构设计

### 核心架构原则

1. **分层架构**
   - **表示层**：ASP.NET Core MVC + SignalR（实时通信）
   - **业务逻辑层**：服务层（CallManager、AICustomerServiceManager 等）
   - **数据访问层**：Entity Framework Core + Repository 模式
   - **基础设施层**：SIP 协议栈、音频处理、网络监控

2. **模块化设计**
   - **AI.Caller.Core**：独立的核心库，可被其他项目引用
   - **AI.Caller.Phone**：Web 应用，依赖核心库
   - 清晰的模块边界，便于维护和扩展

3. **依赖注入与控制反转**
   - 所有服务通过 DI 容器管理生命周期
   - 接口抽象（如 `ICallManager`、`IBackgroundTaskQueue`）实现松耦合
   - 便于单元测试和功能替换

### 关键组件说明

#### 1. 媒体会话管理（MediaSessionManager）
- 管理 VoIPMediaSession（SIP/RTP）和 RTCPeerConnection（WebRTC）
- 支持 SIP 和 WebRTC 之间的音频桥接
- 处理 SDP 协商、ICE 候选交换
- 支持动态启用/禁用 WebRTC 桥接

#### 2. SIP 客户端池（SIPClientPoolManager）
- 维护 SIP 客户端连接池，提高资源利用率
- 支持客户端的获取、释放和复用
- 自动管理客户端生命周期
- 支持多 SIP 服务器配置

#### 3. AI 自动应答器（AIAutoResponder）
- 流式 TTS 音频生成和播放
- 实时语音活动检测（VAD）
- Jitter Buffer 缓冲区管理
- 自适应音频播放速率
- 支持音频重采样和编解码

#### 4. 音频桥接（AudioBridge）
- 连接 SIP 音频流和 AI 处理模块
- 处理音频格式转换（G.711 ↔ PCM）
- 支持不同采样率的音频重采样
- 双向音频流处理

#### 5. 通话场景管理（Call Scenarios）
- **WebToWebScenario**：浏览器到浏览器通话
- **WebToMobileScenario**：浏览器到外部设备通话
- **MobileToWebScenario**：外部设备到浏览器通话
- **ServerToWebScenario**：AI 坐席到浏览器通话
- **ServerToMobileScenario**：AI 坐席到外部设备通话
- 每种场景独立处理 SDP 协商和媒体流

#### 6. 后台任务队列（BackgroundTaskQueue）
- 基于 `System.Threading.Channels` 的高性能队列
- 支持任务优先级和重试机制
- 通过 `IHostedService` 实现后台持续处理
- 解耦任务提交和执行，提高系统响应性

#### 7. 来电路由服务（CallRoutingService）
- 智能路由来电到合适的坐席或 AI 客服
- 支持基于被叫号码的路由策略
- 动态选择空闲坐席
- 支持 AI 客服和人工坐席混合模式

### 数据流示例

#### 外呼流程
```
用户创建任务 → BackgroundTaskQueue → CallProcessor 
→ CallManager.MakeCallAsync → SIPClient.CallAsync 
→ MediaSessionManager → SIP/RTP 协议栈 → 外部电话
→ AICustomerServiceManager.StartAI → AIAutoResponder 
→ TTS 生成音频 → AudioBridge → MediaSessionManager.SendAudioFrame 
→ 发送到对方
```

#### 来电流程
```
外部电话 → SIP 服务器 → SIPTransportManager 
→ CallRoutingService（路由决策） → CallManager.IncomingCallAsync 
→ 选择场景（MobileToWebScenario/MobileToServerScenario）
→ 如果是 AI 客服：AICustomerServiceManager.StartAI 
→ AIAutoResponder 播放欢迎语 → VAD 检测用户语音 
→ 智能暂停/恢复播放
```

## 架构亮点

- **高性能异步处理**：基于 `System.Threading.Channels` 和 `IHostedService` 实现轻量级、高效的内存任务队列
- **服务分层清晰**：
  - `ICallManager`：负责通话"管道"管理（建立、维护、销毁）
  - `AICustomerServiceManager`：负责在通话中注入 AI 能力（TTS 播放、VAD 检测）
  - `CallProcessor`：作为后台任务调度员，协调 CallManager 和 AICustomerServiceManager
- **依赖倒置原则**：通过接口抽象（`IBackgroundTaskQueue`、`ICallManager` 等）实现业务逻辑与底层实现解耦
- **资源池化管理**：SIP 客户端池化复用，减少连接建立开销
- **实时音频优化**：
  - Jitter Buffer 缓冲区平滑音频播放
  - 自适应播放速率应对网络抖动
  - 并行音频编码提高处理效率
  - 内存池（ArrayPool）减少 GC 压力
- **灵活的场景模式**：通过策略模式实现不同通话场景的独立处理逻辑
- **可扩展性设计**：预留 ASR、NLU 等模块接口，便于未来扩展双向对话能力

## 未来展望

当前系统已具备坚实的单向 AI 语音播放能力。下一步的自然演进方向是实现双向对话，主要包括：

- **语音识别 (ASR)**：集成 ASR 服务，将用户的语音实时转换成文本
- **自然语言理解 (NLU)**：理解用户意图，并从对话中提取关键信息
- **对话管理 (Dialogue Manager)**：根据用户意图，进行多轮对话，实现真正的智能问答或业务办理

## 快速开始

### 环境要求

- .NET 8.0 SDK
- Visual Studio 2022 或 VS Code

### 安装与运行

1. **克隆项目**
   ```bash
   git clone https://github.com/wolfweb/wolfweb-AI.Caller.git
   cd AI.Caller
   ```

2. **还原依赖包**
   ```bash
   dotnet restore
   ```

3. **配置 `appsettings.json`**
   
   在 `src/AI.Caller.Phone/appsettings.json` 中，至少需要配置默认管理员账户。SIP 账户等信息可以通过 Web 界面进行配置。
   ```json
   {
     "UserSettings": {
       "DefaultUser": {
         "Username": "admin",
         "Password": "your-strong-password"
       }
     }
   }
   ```

4. **初始化并运行**
   ```bash
   # 切换到 Web 项目目录
   cd src/AI.Caller.Phone
   # 自动应用数据库迁移并运行
   dotnet run
   ```

5. **访问应用**
   
   打开浏览器访问 `https://localhost:5001` (或启动时终端提示的地址)，使用您在 `appsettings.json` 中配置的账户登录。

## 贡献指南

1. Fork 项目
2. 创建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交更改 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 创建 Pull Request

## 许可证

本项目采用 MIT 许可证。
