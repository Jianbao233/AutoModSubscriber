# AutoModSubscriber 实施工作记忆

> 实现已完成并发布。设计见 `docs/DESIGN.md`（含早期方案保留），计划见 `docs/PLAN.md`。
> 本文件 2026-08-10 更新：此前版本停留在实现期，与现状严重不符（仓/workspace 已建、阶段全部完成、Harmony 版本更新）。

## 当前状态（2026-08-10）

- **已发布**：工坊 Workshop ID 3750485606（public）。
- **远端独立仓**：`https://github.com/Jianbao233/AutoModSubscriber`（已创建）。
- **功能完整**：进房 mod 不一致时弹双区块对话框——一键订阅工坊缺失 mod + 勾选禁用本机多余 mod。

## 关键路径

- 主源码：`K:\杀戮尖塔mod制作\STS2_mod\AutoModSubscriber\`
- 工坊 workspace：`STS2_mod/_workshop_workspaces/AutoModSubscriber/`（上传真源，见根 AGENTS.md 工坊表）
- 兼容目标：public-beta v0.110.1（README 兼容矩阵为准）

## 依赖（以 csproj 为准）

- TargetFramework: net9.0
- Godot.NET.Sdk 4.5.1
- Lib.Harmony（NuGet，当前 2.4.x 线）
- 直接 ref：`sts2.dll`、`0Harmony.dll`、`GodotSharp.dll`、`Steamworks.NET.dll`（游戏 `data_sts2_windows_x86_64/`）

## 架构速记

- sidecar 协议：`otherMods` 塞 `__ams_host__` 哨兵 + manifestId → workshopFileId 映射（`src/Protocol/`）
- 拦截点（v0.110.0 已验证）：`NetHostGameService.SendMessage<InitialGameInfoMessage>` Prefix、`InitialGameInfoMessage.Deserialize` Postfix、`NErrorPopup.Create` Postfix
- 订阅流：SteamUGC（`src/Subscribe/`）；禁用：`src/Disable/`
- 与 MultiplayerTools（LanConnect）联动：`LanConnectAutoModSubscriberCompat.cs`（外部弹窗接管为大厅风格 UI，见 `K:\临时\pr-body.md` 迁移的 PR 文案记录）

## 发布流程

1. `build.ps1` 构建 → `torelease/`（staging）
2. 同步 `_workshop_workspaces/AutoModSubscriber/content/`
3. `ModUploader.exe upload -w <workspace 绝对路径>`
4. 回写 `创意工坊/工坊条目台账.md`

## 注意

- 本地化已从 json 迁入代码内 `DialogStrings.cs`，`docs/DESIGN.md` §10 中 `localization/{eng,zho}/ui.json` 目录结构已过时，以源码为准。