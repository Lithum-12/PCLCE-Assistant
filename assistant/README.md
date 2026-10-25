# PCLCE-Assistant

[PCL-Community/PCL-CE](https://github.com/PCL-Community/PCL-CE) 的私有 fork，在不打补丁上游代码的前提下追加 AI 助手能力，并放行上游自 2.15.1 起按区域禁用的下载/安装功能。

上游没有插件机制（页面、导航全部硬编码），因此本 fork 的分离策略是：**功能代码放进上游通配编译项自动收录的新目录，上游文件只留极少量带 `[ASSISTANT]` 哨兵的钩子行**。清单与同步流程见 [assistant/SYNC.md](assistant/SYNC.md)。

## 相对上游的差异

| 位置 | 内容 |
|---|---|
| `Plain Craft Launcher 2/Assistant/Pages/PageChat*` | 顶层「聊天」页：消息流 + 会话切换 + 输入框 |
| `Plain Craft Launcher 2/Assistant/Pages/PageSetupAssistant.*` | 设置 → AI 助手：启用开关、服务地址、模型名、API 密钥、工具与隐私开关 |
| `Plain Craft Launcher 2/Assistant/Controls/AssistantInputBar.*` | 双卡片输入栏：输入框 + 实例选择 + 模型切换 |
| `Plain Craft Launcher 2/Assistant/AssistantToolset.cs` | 暴露给模型的只读工具（崩溃报告 / latest.log / mods / Modrinth / CurseForge） |
| `Plain Craft Launcher 2/Assistant/AssistantHooks.cs` | 启动注入：区域判定覆盖 + fork 私有字符串资源 |
| `PCL.Core/Assistant/AssistantClient.cs` | OpenAI 兼容 `/chat/completions`：流式增量 + function calling，服务商拒绝 tools 时自动降级 |
| `PCL.Core/Assistant/AssistantStream.cs` | SSE 分片累积：正文按序拼接，`tool_calls` 按 index 归并 |
| `PCL.Core/Assistant/AssistantLocalReader.cs` | 实例文件提取：崩溃报告取头尾、日志只取尾部、mods 清单 |
| `PCL.Core/Assistant/AssistantRedactor.cs` | 外发前脱敏：用户名与完整路径、IP、UUID、坐标、种子、账号名 |
| `PCL.Core/Assistant/AssistantPermissions.cs` | 权限档位（需要审批 / 完全控制）的取值与判定，工具集按工具自己的 RequiresApproval 决定要不要弹确认 |
| `PCL.Core/Assistant/AssistantSites.cs` | AI 可联网查阅的站点白名单与站内相对路径校验 |
| `PCL.Core/Assistant/AssistantErrors.cs` | 把服务商错误归成"密钥无效 / 余额不足 / 网络 / 服务商挂了"等可提示类别 |
| `PCL.Core/Assistant/AssistantTitles.cs` | 会话短标题：首次回答结束后再问一次模型，把左侧列表里截断的原文换成一句话 |
| `PCL.Core/Assistant/AssistantProjectLinks.cs` | 识别回答里的 Modrinth / CurseForge 链接并查回工程信息 |
| `PCL.Core/Assistant/AssistantConfig.cs` | `Config.Assistant.*` 配置项，密钥走 `ConfigSource.SharedEncrypt` 加密存储 |
| `PCL.Core/Assistant/AssistantRegionBypass.cs` | 区域判定覆盖的实现，读 `AllowRestrictedFeatures` |

助手气泡按 Markdown 渲染，直接复用上游已有的 `Markdig.Wpf.MarkdownViewer`；每条消息卡片用 `MyCard` 自带的 `CanSwap` 折叠，右上角另有复制按钮。

## 构建

需要 .NET 10 SDK（`net10.0-windows`，WPF）。Visual Studio 不是必需：

```powershell
dotnet build "Plain Craft Launcher 2.slnx" -c Debug
dotnet run --project "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Debug
```

上游的 CurseForge API Key 等在 CI 里以构建期 secret 注入，本地 Debug 构建读到的是空串。`EnvironmentInterop.GetSecret` 在 `#if DEBUG` 下会改读 `PCL_CURSEFORGE_API_KEY` 环境变量（已实测：设了它 `Secrets.CurseForgeAPIKey` 就有值），所以本地想用 CurseForge 设这个变量即可，不在设置页里另开一格密钥。没有密钥时助手不硬闯：`search_curseforge` 不挂载、CurseForge 链接不生成卡片，`read_site` 与系统提示词一起改口成"这台机器查不到 CurseForge，先改用 Modrinth"，而不是丢给模型一个 403 让它自己编。`api.curseforge.com` 本机可直连（不带 key 与带假 key 都是 403 `Forbidden: API Key missing or invalid`），缺的只有 key。

## 区域限制说明

上游 `v2.15.1`（PR #3673）依据 GeoName / 设备安装区域 GeoID / 时区+locale 判定中国大陆，禁用客户端下载、加载器安装、补全、整合包安装，并在缺文件时阻断启动；其待合并回滚为 PR #3677。本 fork 通过 `RestrictedFeaturePolicy.Assess()` 的注入点覆盖判定结果，**上游 `RegionPolicy` 与其单测保持原样**。

这个 fork 仅供自己与受影响的玩家使用。上游的限制是对合规压力的回应，请勿把解除限制后的构建物作为“去限制版”公开分发，也不要引导用户下载第三方改 exe 的 patcher。

## 里程碑

- **M1**：fork 骨架与同步机制、区域限制旁路、聊天页与设置子页骨架。
- **M2（已用真实密钥验证）**：`AssistantClient` 走 OpenAI 兼容 `/chat/completions` 流式读取；`AssistantProviders` 内置 DeepSeek / Kimi / 智谱 / 通义 / 豆包 / MiniMax / 硅基流动 / 自定义预设；设置页可选服务商，聊天页左下选实例、右下切模型。
- **M3（已用真实密钥跑通工具往返与资源卡片）**：只读工具集 `read_crash_report` / `read_latest_log` / `list_mods` / `list_instance_files` / `read_instance_file` / `search_modrinth`（有 CurseForge 密钥时再加 `search_curseforge`），另有 `read_site` 走站点白名单（MC 百科、GitHub、ModpackIndex、Mojang 版本库、BMC API、Fabric、Forge、NeoForge、Quilt、LiteLoader、OptiFine、Modrinth、CurseForge），设置 → AI 助手 里逐个开关。页面最多 4 轮工具往返、本地内容 24000 字预算，回答末尾附一行"本次工具调用"痕迹说明实际外发了多少内容。脱敏默认开启，可在 工具与隐私 关闭。
- **对话里的资源卡片**：模型写出的 Modrinth / CurseForge 链接会被换成可点开的卡片（图标、下载量、更新时间、来源），点击进启动器自己的资源详情页。刻意没复用下载页的 `MyCompItem`——它内部点击逻辑要读 `pageCurrent.additional.Value`，从别的页面点会抛异常。
- **配置与请求失败**改用上游 `MyHint` 警告框画在对话流里（`role=="hint"`，不进模型上下文），错误由 `AssistantErrors.Classify` 归成密钥无效 / 余额不足 / 频率额度 / 服务商错误 / 网络 / 请求被拒。
- **依赖冲突分析**尚未做：M3 只把崩溃报告、日志与模组清单交给模型，本地没有做模组依赖/版本冲突的规则判定。

## Chat UX architecture

会话隔离、单一聊天画面与手工回归矩阵见 [CHAT-UX-ARCHITECTURE.md](CHAT-UX-ARCHITECTURE.md)。
