# AutoModSubscriber 实施工作记忆

> 实现已完成并发布。设计见 `docs/DESIGN.md`（含早期方案保留），计划见 `docs/PLAN.md`。
> 本文件 2026-08-10 更新：此前版本停留在实现期，与现状严重不符（仓/workspace 已建、阶段全部完成、Harmony 版本更新）。

## 当前状态（2026-09-16）

- **v0.1.5 开发中（未发布）**：接入**版本分发机制**，改为「启动器 + 实现」结构，
  目标是让工坊条目在全部 Steam 分支上通用，消除「正式版收到 beta 包体」的分发错位。
  完整约定见 `docs/VERSION_BUNDLE.md`。**游戏内联机待手动测试**。
- **v0.1.4 为工坊当前线上版本**（public-beta 渠道；正式版渠道绑的是 v0.1.2）。
- **已发布**：工坊 Workshop ID 3750485606（public）。
- **远端独立仓**：`https://github.com/Jianbao233/AutoModSubscriber`（已创建）。
- **功能完整**：进房 mod 不一致时弹双区块对话框——一键订阅工坊缺失 mod + 勾选禁用本机多余 mod。

## v0.1.5 结构变化（重要）

| 项 | v0.1.4 及以前 | v0.1.5 |
|---|---|---|
| 根目录 DLL | `AutoModSubscriber.dll`（本体） | `AutoModSubscriber.dll`（**启动器**，来自工具区 `ModVersionLoader`） |
| 本体位置 | 根目录 | `bin/g<游戏版本>/AutoModSubscriber.Impl.dll` |
| 装配件名 | `AutoModSubscriber` | 本体改为 `AutoModSubscriber.Impl`（避免与启动器同名冲突） |
| 入口 | `[ModInitializer]` 标注在 `ModuleInit` | 启动器反射调用 `ModuleInit.Initialize()`（特性已移除） |
| 版本真源 | `mod_manifest.json` 手写 | `.csproj` 的 `<Version>`，build.ps1 写回 manifest |
| 构建产物 | `.godot/.../AutoModSubscriber.dll` 直接拷进 mods | `build/mods/AutoModSubscriber/`（启动器 + manifest + bin/） |

**约定（必须遵守）**：`mod_manifest.json` 的 `version` 在所有版本目录之间必须完全一致，
否则联机比对依然会失败 —— 等于白做。

## 关键路径

- 主源码：`D:\A-Developing\main\sts2\STS2_mod\AutoModSubscriber\`
- 版本启动器（工具区，跨项目复用）：`D:\A-Developing\main\sts2\tools\ModVersionLoader\`
- 启动器验证（不启动游戏）：`D:\A-Developing\main\sts2\tools\ModVersionLoader.Tests\`
- 工坊 workspace：`STS2_mod/_workshop_workspaces/AutoModSubscriber/`（上传真源，见根 AGENTS.md 工坊表）
- 兼容目标：public-beta v0.111.0（README 兼容矩阵为准；v0.1.4 起 sidecar 挂载点迁移到握手阶段 `PeerVersionInfo.LocalDefault` / `HandshakeManager.TryReadHandshakeMessage`）

## 依赖（以 csproj 为准）

- TargetFramework: net9.0
- Godot.NET.Sdk 4.5.1
- Lib.Harmony（NuGet，当前 2.4.x 线）
- 直接 ref：`sts2.dll`、`0Harmony.dll`、`GodotSharp.dll`、`Steamworks.NET.dll`（游戏 `data_sts2_windows_x86_64/`）
- **HintPath 已于 2026-09-16 从 `K:\SteamLibrary\...` 改为 `F:\Steam\...`**（迁移遗留，此前在本机无法构建）

## 架构速记

- sidecar 协议：`otherMods` 塞 `__ams_host__` 哨兵 + manifestId → workshopFileId 映射（`src/Protocol/`）
- 拦截点（v0.110.0 已验证）：`NetHostGameService.SendMessage<InitialGameInfoMessage>` Prefix、`InitialGameInfoMessage.Deserialize` Postfix、`NErrorPopup.Create` Postfix
- 订阅流：SteamUGC（`src/Subscribe/`）；禁用：`src/Disable/`
- 与 MultiplayerTools（LanConnect）联动：`LanConnectAutoModSubscriberCompat.cs`（外部弹窗接管为大厅风格 UI，见 `D:\A-Developing\tmp\pr-body.md` 迁移的 PR 文案记录）

## 发布流程

1. `build.ps1 -StageWorkshop` 构建 → 打包 → 同步 `_workshop_workspaces/AutoModSubscriber/content/`
2. `ModUploader.exe upload -w <workspace 绝对路径>`
3. 回写 `创意工坊/工坊条目台账.md`

## 注意

- 本地化已从 json 迁入代码内 `DialogStrings.cs`，`docs/DESIGN.md` §10 中 `localization/{eng,zho}/ui.json` 目录结构已过时，以源码为准。
- 本机测试期间本地副本会遮蔽工坊同版本副本；联机双方需用同一份本地部署，或等新版上传工坊。
- `ClientModMismatchInterceptPatch.ExternalDialogHandler` 的 CS0649 警告是预期行为（由 LAN Connect 在运行时写入）。
