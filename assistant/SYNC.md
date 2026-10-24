# 与上游 PCL-CE 同步

上游仓库：`https://github.com/PCL-Community/PCL-CE`（默认分支 `dev`）
基线：`v2.15.1` / `8f0beaf`

## 分支模型

| 分支 | 作用 |
|---|---|
| `main` | 上游代码 + fork 功能，日常开发从这里开分支 |
| `assistant/<feature>` | 功能分支 |

私有代码全部位于：

- `PCL.Core/Assistant/`
- `PCL.Core.Test/Assistant/`
- `Plain Craft Launcher 2/Assistant/`
- `assistant/`（本文档与工具）

根目录另有一个新增文件 `Directory.Build.targets`：上游 csproj 把 `DebugType` 写死为 `full`，而 VS Code 的 vsdbg 只接受 `portable`，本地 Debug 构建在此覆盖（MSBuild 在项目体之后导入，能盖住 csproj；不碰上游文件）。

上游项目使用通配编译项（`Compile Include="**\*.cs"`、`Page Include="**\*.xaml"`），新增文件与目录**不需要改 csproj**。

## 上游改动清单（哨兵 `[ASSISTANT]`）

只有 7 个上游文件被改动，共 12 处，每次同步后必须逐条复核：

| 文件 | 处数 | 内容 |
|---|---|---|
| `README.md` | 1 | fork 说明块 |
| `Plain Craft Launcher 2/Program.cs` | 1 | 调用 `AssistantHooks.Install()` |
| `Plain Craft Launcher 2/Modules/Minecraft/RestrictedFeaturePolicy.cs` | 2 | `RegionOverride` 字段 + `Assess()` 优先取 override |
| `Plain Craft Launcher 2/FormMain.xaml` | 1 | 顶层导航按钮（下标 4） |
| `Plain Craft Launcher 2/FormMain.xaml.cs` | 5 | `PageType.Assistant = 4`、`PageSubType.SetupAssistant = 12`、`PageChangeActual` 的 case |
| `Plain Craft Launcher 2/Pages/PageSetup/PageSetupLeft.xaml` | 1 | 设置左栏条目（Tag=12） |
| `Plain Craft Launcher 2/Pages/PageSetup/PageSetupLeft.xaml.cs` | 1 | `PageGet` 的 case |

校验脚本会统计每个文件的哨兵数量，缺失或多余都会失败：

```powershell
powershell -NoProfile -File assistant/check-hooks.ps1
```

脚本还有第二道检查：拿 `upstream/dev` 做基线，把私有目录以外被改动的文件列出来，
只要有一个不在上表（登记表）里就直接失败——防止"改了上游文件但忘了打哨兵"，
那种改动在下次 merge 时会静默丢掉。新增文件（`A`）单独计数、不算失败，因为多一个文件不影响合并。
输出里的 `parity` 一行就是当前相对上游的落地面积，目标是它长期停在 `7 个 / 12 处`。

> 本脚本必须存成 **UTF-8 with BOM**：Windows PowerShell 5.1 在没有 BOM 时按 GBK 解码，
> 中文字符串会把引号吃掉导致 `UnexpectedToken`（`pwsh` 7 不会）。

## 加东西之前的规矩

- 新功能一律新增文件，不改上游文件；确实要改的，只允许一行哨兵式的注入点（字段 + 一次调用），并同步登记到上表与本脚本。
- 不重写上游方法体、不挪上游代码块、不格式化上游文件——diff 越干净，`git merge upstream/dev` 越接近自动完成。
- 配置项走上游的 `ConfigGenerator`（`[ConfigGroup]` + `[ConfigItem]` / `[AnyConfigItem]`），声明在 `PCL.Core/Assistant/AssistantConfig.cs`，不要往 `PCL.Core/App/Config.cs` 里加。
- 字符串走 `AssistantHooks` 里的 fork 私有字典，不动上游的语言文件。
- UI 复用上游控件与度量（`MyCard` / `MyCheckBox` / `MyIconButton` / `MyListItem` / `MyHint`、28+9 的表单行高、`SharedSizeGroup="Name"`），图标名必须带 `lucide/` 前缀。

## 同步步骤

```bash
git fetch upstream
git checkout main
git merge upstream/dev
```

冲突预期只出现在上表 7 个文件。处理原则：

1. 先接受上游版本（`git checkout --theirs <file>`），再按上表把哨兵行重新加回去。
2. 重跑 `assistant/check-hooks.ps1`。
3. 若上游删除或重命名了 `RestrictedFeaturePolicy`，脚本会通过哨兵缺失报错——此时区域限制的旁路点需要重新选，不要静默改成删除限制。

同步完成后：

```bash
git merge --no-edit upstream/dev   # 已在上面完成
powershell -NoProfile -File assistant/check-hooks.ps1
dotnet build "Plain Craft Launcher 2.slnx" -c Debug
```

## 上游移除区域限制后

上游 PR #3677 是一份待合并的回滚。若它最终合并：把 `Config.Assistant.AllowRestrictedFeatures` 的默认值改为 `true` 之外的行为已无必要，可直接删除 `PCL.Core/Assistant/AssistantRegionBypass.cs` 与 `RestrictedFeaturePolicy.cs` 上的两处哨兵。
