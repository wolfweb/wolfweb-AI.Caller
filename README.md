# AI.Caller

一个基于 .NET 8 的智能 SIP 电话系统，集成了 AI 驱动的呼叫任务管理和自动应答功能。

## 项目概述

AI.Caller 是一个现代化的 SIP 电话解决方案，它不仅提供标准的 Web 界面进行电话呼叫管理，还引入了 AI 能力来自动化呼出任务和应答来电。系统采用模块化架构，分为核心 SIP 功能库和 Web 应用程序两个主要组件，并通过后台任务队列实现高效的异步处理。

## 技术栈

- **.NET 8.0** - 主要开发框架
- **ASP.NET Core MVC** - Web 应用框架
- **Entity Framework Core** & **SQLite** - 数据持久化
- **SignalR** - 实时通信
- **SIPSorcery** - 核心 SIP 协议栈
- **ExcelMapper & NPOI** - Excel 文件处理
- **IHostedService & Channels** - 后台异步任务队列

## 主要功能

### 核心 SIP 功能
- **标准 SIP 通话** - 支持通过 WebRTC 客户端或外部设备进行语音通话。
- **通话管理与监控** - 提供通话建立、应答、挂断等基本控制，并可监控活跃通话。
- **通话录音** - 支持对通话进行录音。

### AI 驱动的业务功能
- **AI 自动应答 (来电)**
  - 当外部电话呼入时，可自动应答并根据预设的 TTS (文本转语音) 模板播放欢迎语或通知。

- **AI 外呼任务管理 (去电)**
  - **统一任务视图**: 在 Web 界面集中查看和管理所有外呼任务的状态（排队中、进行中、已完成、失败）。
  - **单个任务创建**: 通过表单快速创建一个单呼任务，手动填写电话号码和模板变量。
  - **批量任务创建**:
    - **动态模板下载**: 根据选定的 TTS 模板，动态生成并下载包含正确表头（如：电话号码、姓名、订单号等）的 Excel 模板文件。
    - **批量上传**: 用户填写模板后上传，系统会自动解析文件并为每一行数据创建一个独立的呼叫任务。
  - **后台异步处理**: 所有外呼任务均进入后台队列，由 `IHostedService` 驱动的后台服务依次处理，不阻塞前台操作。
  - **动态坐席调度**: 后台服务在处理任务时，会自动从所有可用的 AI 坐席中选择一个空闲的来执行呼叫，实现资源的动态分配。

### 基础 Web 功能
- **用户认证** - 安全的用户登录系统。
- **SIP 账户管理** - 管理用于呼叫的 SIP 账户。
- **TTS 模板管理** - 创建和管理包含变量的 TTS 文本模板。

## 架构亮点

- **后台任务队列**: 基于 `System.Threading.Channels` 和 `IHostedService` 实现了一个轻量级、高效的内存后台任务队列，用于异步处理所有外呼任务。
- **服务分层清晰**:
  - `ICallManager`: 负责纯粹的通话“管道”管理。
  - `AICustomerServiceManager`: 负责在通话“管道”中注入 AI 能力（如播放 TTS）。
  - `CallProcessor`: 作为后台任务的“调度员”，粘合 `ICallManager` 和 `AICustomerServiceManager`，完成完整的外呼业务流程。
- **依赖倒置**: 通过 `IBackgroundTaskQueue` 等接口抽象，实现了业务逻辑与底层实现（如内存队列或未来的消息中间件）的解耦，提高了系统的可扩展性和可测试性。

## 未来展望 (简要)

当前系统已具备坚实的单向 AI 语音播放能力。下一步的自然演进方向是实现双向对话，主要包括：

- **语音识别 (ASR)**: 集成 ASR 服务，将用户的语音实时转换成文本。
- **自然语言理解 (NLU)**: 理解用户意图，并从对话中提取关键信息。
- **对话管理 (Dialogue Manager)**: 根据用户意图，进行多轮对话，实现真正的智能问答或业务办理。

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