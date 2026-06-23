# AutoModSubscriber 实施工作记忆

> 实现期工作笔记。设计见 `docs/DESIGN.md`，计划见 `docs/PLAN.md`。

## 关键路径

- 主源码：`K:\杀戮尖塔mod制作\STS2_mod\AutoModSubscriber\`
- 部署：`K:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\AutoModSubscriber\`
- 远端：`https://github.com/Jianbao233/AutoModSubscriber`（独立仓，未创建）
- 工坊 workspace：`STS2_mod/_workshop_workspaces/AutoModSubscriber/`（未创建）

## 依赖

- TargetFramework: net9.0（与 MultiplayerTools / ModListHider 一致）
- Godot.NET.Sdk 4.5.1
- Lib.Harmony 2.3.3 (NuGet)
- 直接 ref：`sts2.dll`、`0Harmony.dll`、`GodotSharp.dll`、`Steamworks.NET.dll`（均位于游戏 `data_sts2_windows_x86_64/`）

## 阶段进度

- [x] 阶段 1：仓骨架（csproj、manifest、project.godot、export_presets.cfg、build.ps1、.gitignore、docs）
- [ ] 阶段 2：协议层 sidecar
- [ ] 阶段 3：订阅子系统
- [ ] 阶段 4：UI 层
- [ ] 阶段 5：本地化与边界
- [ ] 阶段 6：发布上工坊

## 待办事项 / 已知 TODO

- 远端 GitHub 仓未创建（先做本地实现，发布前补）
- 工坊 workspace 未创建（首次发布前补）
- 本地化 json 当前为空 `{}`，阶段 5 填充
- 阶段 4 UI 用纯代码构造 Control 树，颜色字体阶段 4 期间从 `SL2/scenes/ui/error_popup.tscn` 取参考