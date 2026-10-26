# PCLCE Assistant：会话与聊天界面设计

## 设计参考

借鉴 Cursor / VS Code Agent 的会话原则，而不引入其复杂的多工作区或多 Agent 能力：

- 一个聊天工作区只有一个聊天画面和一个输入框。
- 历史列表中的每条记录代表独立会话；选择历史只切换会话数据，不切换右侧页面实例。
- “新对话”创建临时草稿；第一条用户消息发送时才将它提升为历史记录，因此连续点新对话不会生成一堆空记录。若切换到历史时草稿里已有未发送文字，它会显示在独立的“未发送草稿”区域，可返回继续编辑，也可单独删除。
- 会话切换不应取消或重定向其他会话的流式回答。每个请求捕获它启动时的会话对象和取消令牌；UI 只绘制当前选中会话的内容。
- 删除当前会话后，聊天区回到空白草稿；删除其他会话不能改变当前对话。
- 重启时恢复最近活跃的有效会话；旧版本留下的空会话不再显示。

参考文档：

- VS Code Agent sessions：<https://code.visualstudio.com/docs/agents/concepts/sessions>
- VS Code Manage agent sessions：<https://code.visualstudio.com/docs/agents/run/sessions/manage-sessions>
- Cursor Chat History：<https://docs.cursor.com/en/agent/chat/history>

## 状态所有权

`AssistantSessionStore` 是会话数据的唯一所有者：

- `Current` 是当前展示的会话，允许指向尚未持久化的草稿。
- `CurrentChanged` 只表示当前会话切换；聊天页面收到事件后重绘当前会话。
- `SessionsChanged` 表示历史数据更改；左侧历史列表据此刷新。
- `Sessions` 只包含至少有一条消息的持久化会话；`Drafts` 保存暂未发送的草稿，二者在左侧分区显示且只有 `Sessions` 写入历史 JSON。
- `Title` 先由首条用户消息截断得到；一轮回答正常结束后 `AssistantTitles` 会再问一次模型要短标题，成功才写回并置 `TitleIsAi`，失败就保留截断原文。`TitleIsAi` 保证一次对话只花这一回钱，改名也不 bump `UpdatedAt`（否则列表顺序会跳）。
- `UpdatedAt` 用于最近活跃排序；`UpdatedAt == 0` 的旧记录回退到 `CreatedAt`。

`PageChatRight` 是唯一聊天画面。`AssistantHome` 旧枚举值仍为兼容而存在，但被映射到同一个 `PageChatRight`，不得再创建第二个聊天页面或第二个输入框。

## 落盘边界

`Save()` 走"写 `.tmp` → 改名替换"，改名被挡时退回原地重写，且**任何异常都不允许逃出 `Save()`**。原因是 2026-10-25 03:27 的一次事故：连点删除会话时 `File.Move` 抛 `UnauthorizedAccessException`（目标文件被一个不给 `FILE_SHARE_DELETE` 的读句柄开着，索引器/杀软/编辑器都算），当时只 `catch (IOException)`，异常一路穿过 `DeleteSession` → `MyIconButton.Button_MouseUp` 落到 UI 线程上，窗口就此停住并占着单例锁，表现成"启动器崩溃"。

启动时 `Load()` 会先处理 `.tmp` 残骸：比正式文件新就补位（否则上次没落地的删除会"复活"），更旧就直接丢掉。

## 左侧列表的右键菜单

历史条目与草稿都支持右键：打开所在位置（资源管理器里选中 `assistant-sessions.json`）、修改名称（`MyMsgBoxInput`，改完即视为标题已定，AI 不会再覆盖）、导出（`SystemDialogs.SelectSaveFile` + `AssistantExport.ToMarkdown`，Markdown 里保留玩家/助手发言、诊断提示与工具痕迹）、删除（与条目上的垃圾桶按钮同一条路径，都会先弹确认）。菜单用上游的 `MyMenuItem` + `ContextMenu`，跟实例列表那些右键菜单一个样式；草稿没有标题可改、空草稿没有内容可导出，这两项按状态禁用而不是藏起来。

## 流式请求隔离

每个请求在开始时捕获具体 `AssistantSession`、响应消息索引和自己的 `CancellationTokenSource`。增量内容首先更新会话数据；只有当该会话仍为当前会话且消息气泡仍挂在当前视觉树中时，才更新可见文本框。切换到其他会话不会让旧请求将内容画到新会话上。停止按钮只取消当前所选会话的请求。

删除正在生成的会话会先取消该会话的请求；请求结束后不能把已删除会话重新加入历史。

## 权限与执行

三档权限存在 `Config.Assistant.PermissionMode`：`read`（只读）/ `ask`（需要审批，默认）/ `full`（完全控制），聊天底部模型框右边那个按钮就是它，点一下循环一档。工具分两类：`AssistantToolKind.Read` 与 `Action`。

- 只读档：Action 工具**根本不挂载**，主提示词里"只能读取和分析"那句此时是准确的。
- 需要审批：Action 工具挂上，每执行一个都先弹上游 `ModMain.MyMsgBox`，把动作名和参数摊开给人看；拒绝就把"用户不同意，不要重试"作为工具结果回给模型。
- 完全控制：不弹确认，改过什么只记在回答左下角的痕迹里。

执行类动作的实现集中在 `AssistantActions`，`AssistantToolset` 只管挂载、审批与痕迹。写文件一律走 `AssistantLocalWriter`（文本扩展名白名单 + 覆盖前备份到实例下的 `.pclce-assistant-backup`），改设置一律走 `AssistantConfigKeys` 的白名单——白名单是具体键名而不是前缀，否则 `Assistant*` 会被一起放进来，等于让模型自己改自己的权限与脱敏开关。跑 Python 按 `py -3 → python → python3` 探测，脚本落在系统临时目录、工作目录是当前实例，管道强制 UTF-8（中文 Windows 上 Python 默认按 GBK 写管道）。

## 版本与加载器

`download_resource` 挑文件必须同时满足游戏版本与加载器：模型传了 `loader` / `version` 就用它，没传就用当前实例装的加载器和 `Info.VanillaName`。这里有个真实事故：上游 `IsInstanceSuitableForFile` 在 `allowedLoaders` 为空时**直接返回 true**（表示"不筛加载器"），所以早期实现传空列表时，Fabric 实例会拿到 NeoForge 版的 Sodium——Modrinth 上同一个游戏版本的两条记录是交替排的，第一条往往就是错的那个。现在实例一个加载器都没有时不再猜，直接让模型去问玩家。

卡片链接支持 `?mc=1.21.1&loader=fabric`（`version` / `modloader` 也认）：`AssistantProjectLinks.Parse` 把它留在 `AssistantProjectLink` 上，点开详情页时作为 `TargetVersion` / `TargetLoader` 传给上游，玩家看到的就是筛好的那一版；链接没带参数就退回当前选中的实例。

## 环境简报

每次发送前，`BuildSystemPrompt` 会拼一段【当前环境】给模型：当前实例的名字 / 游戏版本 / 加载器 / 是否版本隔离 / 模组文件数、权限档位、**这一轮真的挂载给它的工具名清单**、开了哪些站点。它之前会说自己"装不了光影包"，就是因为这些只能凭训练记忆猜。非只读档还额外给一段【玩家的说法对应哪个工具】，把"帮我安装光影包""建个新实例"这类口语直接对上 `download_resource` / `create_instance` 等工具名。

工具清单必须来自 `toolset.Definitions` 而不是手写字符串——档位、CurseForge 密钥、站点开关都会改变实际挂载的工具，写死的清单会教模型去调一个没有的工具。

## 渲染窗口

切会话是重建整个消息区：每条消息一个 `MyCard` + 一次 Markdig 解析。所以 `RenderCurrentSession` 一次只画最近 `RenderWindow`（24）条，顶部放一个"显示更早的 N 条消息"按钮，点一次往前再放 24 条并把原来最先看到的那条滚回视野；换会话时窗口回到尾部。入场动画同理只给最靠近输入框的 8 个控件做（复位仍然覆盖全部，否则上次动画做到一半的条目会永远停在半透明）。这条只影响画多少，不改发给模型的上下文——历史仍然全量发送。

## 手工回归矩阵

1. 启动时有历史：恢复最近活跃会话，并显示完整消息；无历史：显示空状态，不创建持久化空记录。
2. 连点“新对话”多次：不会新增空历史；有未发送内容的旧草稿会进入独立草稿区域，而不是串进历史会话。
3. 草稿发送第一条消息：新建一条历史记录，标题由首条用户消息前 24 个字符生成，用户消息先落盘；该草稿从独立草稿区消失。
4. 新建 A、发送消息；新建 B、发送消息；在 A/B 间切换至少 10 次：消息、角色、顺序与标题始终对应各自会话。
5. 在 A 生成中切换到 B：A 的流式内容继续写入 A；B 的界面不出现 A 的内容。切回 A 能看到已生成的内容。
6. 在 A 生成中删除 A：A 从历史移除且不会在请求结束后复活；聊天区为空白。
7. 删除非当前会话：当前会话和当前滚动位置不被替换。
8. 关闭重开：所有已持久化消息可恢复，旧的空会话不会回来。未发送草稿目前只保存在内存中，关闭应用后不承诺恢复。
9. 未启用助手或未配置模型：错误提示作为当前会话中的 assistant 消息保存，不改变当前会话。
10. 构建、`assistant/check-hooks.ps1` 与 Windows 沙箱 UI 测试全部通过后，才可以认定完成；静态检查不得标记为 UI 实测。
