# 任务：修复 PCLCE-Assistant fork 的聊天体验问题（协作交接）

你是接手一个进行中的 WPF 项目。用户（中文母语、Java/Python 熟、C# 新手）报告「问题还没解决」，但没有逐条复测清单——你的第一步是**在沙箱实例里逐条复现**下面「待解决问题」，确认哪些仍复现，再修。不要相信本文件之外的任何口头描述。

## 仓库与构建

- 工作副本：`C:\Users\35320\Documents\Qoder\2026-10-23\2046fff2\pclce`，分支 `main`，基线提交 `bfb5dc1`。
- 这是 `PCL-Community/PCL-CE`（默认分支 dev，基线 v2.15.1 / 8f0beaf）的 fork。remote：`upstream`=上游；`origin`=本仓库。
- 构建：`dotnet build "Plain Craft Launcher 2.slnx" -c Debug`（.NET 10 SDK 已装，net10.0-windows）。
  若报 `MSB3027 ... 被 "Plain Craft Launcher Community Edition (pid)" 锁定`：有启动器实例占着输出目录。用户自己的实例**不许 kill**；改用隔离输出：`dotnet build ... -o C:\Users\35320\Documents\Qoder\2026-10-23\2046fff2\tools\ui-verify`。
- 校验钩子：`powershell -NoProfile -File assistant\check-hooks.ps1` 必须绿。

## 分离约定（违反即破坏整个 fork 的可维护性）

- 私有代码只允许放在：`PCL.Core/Assistant/`、`Plain Craft Launcher 2/Assistant/`、`assistant/`。
- 上游文件只允许带 `[ASSISTANT]` 注释的钩子行（当前 12 处），清单在本目录 `SYNC.md`；新增上游改动必须同步更新 SYNC.md 与 check-hooks.ps1 的计数。
- 两个上游项目都用 `**/*.cs`、`**/*.xaml` 通配收录，**新增文件不需要改 csproj/slnx**。

## 运行与验证（强制流程）

- 沙箱实例（不碰用户数据/实例）：

  ```powershell
  $sb="...\tools\sandbox"; $d="...\tools\ui-verify"
  $env:PCL_DISABLE_DEBUG_HINT="1"; $env:PCL_PATH_LOCAL="$sb\local"; $env:PCL_PATH_SHARED="$sb\roaming"
  Start-Process "$d\Plain Craft Launcher 2.exe" -WorkingDirectory $d
  ```

  环境变量依据 `PCL.Core/App/Paths.cs:81-84`（仅 Debug 生效）。会话/配置落在沙箱目录，与用户的 `%APPDATA%\PCLCE_Debug` 完全隔离。
- 日志：`<exe目录>\PCL\Log\Launch-*.log`。**崩溃排查先看最新日志末尾**；若进程消失而无异常落盘，按栈溢出查事件环（本项目已发生过一次）。
- 量控件矩形（只读、不点击）：`tools\ui-measure.ps1`，按 `x:Name`（AutomationId）输出窗口相对坐标。DPI=144（1.5x）。
- **禁止**盲坐标点击用户的实时桌面、禁止按进程名 kill 用户实例。需要交互验证就在沙箱窗口里做，或请用户操作。

## 上游控件陷阱（每条都曾造成真实 bug，改 UI 前必读）

- `MyButton.Click` / `MyIconTextButton.Click` 委托是 `(object, ModBase.RouteEventArgs)`，不是 RoutedEventHandler；签名错=运行时 XamlParseException。
- `MyCheckBox` 用 `Change(object, bool user)`；`MyListItem` 用 `Check(object, RouteEventArgs)`。
- `SetChecked(...)` 会触发 `Check`，`e.raiseByMouse` 区分手动/程序化。**所有 Check handler 开头必须 `if (!e.raiseByMouse) return;`**，否则 SetChecked→Check→改状态→Changed→重建列表→SetChecked 同步递归栈溢出（历史真实崩溃）。
- `MyComboBox` 展开时执行 `Width = ActualWidth`（`Controls/MyComboBox.cs:187-189`），用过一次下拉宽度就冻结；必须 `DropDownWidthSync="False"`。
- 页面进入动画只作用于 `MyCard/MyHint/MyExtraTextButton/TextBlock/MyTextButton`（`Controls/MyPageRight.cs:660-682`）；想让新控件跟动画，就把它放进 MyCard 里。
- 设置页网格规范：两列 `Auto(SharedSizeGroup="Name")` + `1*`，行高固定 28、间隔行 5/9，标签 `Margin="0,0,25,0"`、`VerticalAlignment="Center"`。参照 `Pages/PageSetup/PageSetupLauncherMisc.xaml:12-27`。
- 配置：`[ConfigGroup]/[ConfigItem]` 声明在 PCL.Core 的新文件即可生成 `Config.Assistant.*`；密钥用 `ConfigSource.SharedEncrypt`（已验证落盘为密文）。
- 字符串：fork 自有字典在 `Plain Craft Launcher 2/Assistant/AssistantHooks.cs`（运行时注入 `Application.Resources`）；Debug 下缺 key 会显示 `!key!`，这是判断注入是否成功的快速信号。

## 现有架构（改动前先读这些文件）

- 会话模型与落盘：`PCL.Core/Assistant/AssistantSession.cs`（`AssistantSession` + `AssistantSessionStore`，json 在 `<Paths.Data>/assistant-sessions.json`，损坏当空列表）。
- 模型客户端：`PCL.Core/Assistant/AssistantClient.cs`（OpenAI 兼容 `/chat/completions`，SSE 逐行解析，`NetworkService.GetClient()` 复用代理/DoH/UA）、`AssistantProviders.cs`（国产服务商预设）。
- UI：`Assistant/Controls/AssistantInputBar.xaml(.cs)`（双 MyCard 输入栏，暴露 `SendRequested`/`StopRequested`/`SetBusy`）、`Pages/PageAssistantHome`（居中空状态卡+输入栏，发送即新建会话并转入对话页）、`Pages/PageChatRight`（会话渲染与发送）、`Pages/PageChatLeft`（首页/历史两组、历史删除按钮）、`Pages/PageSetupAssistant`（即时写入设置页）。
- 路由：`PageType.Assistant=4`、`PageSubType.AssistantHome=13/AssistantChat=14/SetupAssistant=12`；`FormMain.xaml.cs` 的 case 走 `left.PageGet(subType)`。

## 待解决问题（用户报告；先复现再修）

1. 聊天对话逻辑：历史消息曾全部堆在单一会话。验证：新建会话互不串、切换历史恢复对应消息、删除按钮（含删除当前会话后的落点）、重启后从 json 恢复、会话标题=首条用户消息前 24 字、消息顺序与角色正确。
2. 输入框单行文字垂直居中（现为 `VerticalContentAlignment="Center"`）；多行时的表现是否可接受。
3. Enter 只发送不换行、Shift+Enter 才换行（现为 `PreviewKeyDown`；曾误用 KeyDown 导致换行）。
4. 首页发送→自动新建会话→转入对话页→该消息出现在对话中，全链路。
5. 任何新崩溃：按上面日志流程定位；修事件环时优先加 `raiseByMouse` 守卫而非 try/catch 吞掉。

## 完成标准

- 每条问题在沙箱实例实测通过，证据写进提交信息（日志行/UIA 数值/截图路径）。
- `dotnet build` 0 错误，fork 文件零新增警告；`check-hooks.ps1` 绿。
- 不新增上游文件改动；每个修复独立提交，中文提交信息，风格参照 `git log`。
- 不向任何第三方推送或披露本仓库信息；不动用户实例与用户数据目录。
