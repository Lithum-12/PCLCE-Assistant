# PCLCE-Assistant

[PCL-Community/PCL-CE](https://github.com/PCL-Community/PCL-CE) 的私有 fork，在不打补丁上游代码的前提下追加 AI 助手能力，并放行上游自 2.15.1 起按区域禁用的下载/安装功能。

上游没有插件机制（页面、导航全部硬编码），因此本 fork 的分离策略是：**功能代码放进上游通配编译项自动收录的新目录，上游文件只留极少量带 `[ASSISTANT]` 哨兵的钩子行**。清单与同步流程见 [assistant/SYNC.md](assistant/SYNC.md)。

## 相对上游的差异

| 位置 | 内容 |
|---|---|
| `Plain Craft Launcher 2/Assistant/Pages/PageChat*` | 顶层「聊天」页：消息流 + 输入框 |
| `Plain Craft Launcher 2/Assistant/Pages/PageSetupAssistant.*` | 设置 → AI 助手：启用开关、服务地址、模型名、API 密钥 |
| `Plain Craft Launcher 2/Assistant/AssistantHooks.cs` | 启动注入：区域判定覆盖 + fork 私有字符串资源 |
| `PCL.Core/Assistant/AssistantConfig.cs` | `Config.Assistant.*` 配置项，密钥走 `ConfigSource.SharedEncrypt` 加密存储 |
| `PCL.Core/Assistant/AssistantRegionBypass.cs` | 区域判定覆盖的实现，读 `AllowRestrictedFeatures` |

## 构建

需要 .NET 10 SDK（`net10.0-windows`，WPF）。Visual Studio 不是必需：

```powershell
dotnet build "Plain Craft Launcher 2.slnx" -c Debug
dotnet run --project "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Debug
```

上游的 CurseForge API Key 等在 CI 里以构建期 secret 注入；本地缺省时相关功能会静默降级，调试可用 `PCL_*` 环境变量提供。

## 区域限制说明

上游 `v2.15.1`（PR #3673）依据 GeoName / 设备安装区域 GeoID / 时区+locale 判定中国大陆，禁用客户端下载、加载器安装、补全、整合包安装，并在缺文件时阻断启动；其待合并回滚为 PR #3677。本 fork 通过 `RestrictedFeaturePolicy.Assess()` 的注入点覆盖判定结果，**上游 `RegionPolicy` 与其单测保持原样**。

这个 fork 仅供自己与受影响的玩家使用。上游的限制是对合规压力的回应，请勿把解除限制后的构建物作为“去限制版”公开分发，也不要引导用户下载第三方改 exe 的 patcher。

## 里程碑

- **M1（当前）**：fork 骨架与同步机制、区域限制旁路、聊天页与设置子页骨架。
- **M2**：OpenAI 兼容客户端（流式 SSE）+ 国产 Provider 预设（DeepSeek / Kimi / GLM / Qwen / Doubao / MiniMax / SiliconFlow / 自定义），把密钥、地址、模型接到设置页并跑通对话。
- **M3**：工具调用——读取当前实例的崩溃报告与日志、模组清单与依赖冲突分析、CurseForge / Modrinth 资源查询；外发前脱敏（玩家名、IP、坐标）。
