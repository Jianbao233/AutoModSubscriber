# AutoModSubscriber

[![Steam Workshop](https://img.shields.io/badge/Steam_Workshop-3750485606-blue)](https://steamcommunity.com/sharedfiles/filedetails/?id=3750485606)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](#license)

> Slay the Spire 2 multiplayer mod-mismatch resolver.

[English](#english) · [中文](#中文)

---

## English

When you join a multiplayer lobby in Slay the Spire 2 and your local mod list does not match the host's, the vanilla game just shows a red "Mod Mismatch" error and disconnects you. **AutoModSubscriber** replaces that error with an interactive dialog so you can fix the mismatch in one place:

- One-click auto-subscribe to the Steam Workshop mods the host has but you are missing.
- Pick the mods you have but the host doesn't, and disable them with one click (writes `settings.save`).

After subscribing or disabling, **restart the game manually** before trying to join again. This mod never restarts the game for you.

### Compatibility

This release targets the latest **public-beta v0.111.0** build. It requires game version `0.111.0` or later.

| Host has this mod | Client has this mod | Behaviour |
|---|---|---|
| ✓ | ✓ | Full auto-subscribe + selective auto-disable |
| ✓ | ✗ | Vanilla client logs one extra non-gameplay mod entry; multiplayer join is **not** broken |
| ✗ | ✓ | Dialog shows the missing list with a "Open Workshop" search shortcut |
| ✗ | ✗ | 100% vanilla behaviour |

The dialog UI automatically switches between Simplified Chinese and English based on the in-game language setting.

### How it works

The host attaches a base64-encoded sidecar entry to `PeerVersionInfo.otherMods` in a Harmony Postfix for `PeerVersionInfo.LocalDefault()`. Since v0.111.0 the mod list travels inside the handshake (magic bytes + serialized `PeerVersionInfo` via `HandshakeManager`), so the sidecar rides that handshake message. The sidecar contains a `{manifestId: workshopFileId}` map plus a sentinel key `__ams_host__`. After the client parses the remote handshake (`HandshakeManager.TryReadHandshakeMessage`), it extracts and removes this sidecar, populating a static `ModWorkshopMap` that the auto-subscribe dialog uses to find each missing mod's Workshop file id. Each handshake clears the prior room's mapping first, so a host without a valid sidecar cannot reuse stale Workshop IDs.

Subscription uses Steamworks.NET (`SteamUGC.SubscribeItem` + persistent `Callback<ItemInstalled_t>` / `Callback<DownloadItemResult_t>`). Mods installed via Steam Workshop can be auto-subscribed; mods placed manually under `mods/` cannot, and the dialog clearly says so.

### Version distribution

This mod ships as a **single Workshop item that works on every Steam branch**, so players never
have to re-subscribe or worry about Steam delivering the wrong build:

```text
AutoModSubscriber/
├── mod_manifest.json            # one version number, shared by all branches
├── AutoModSubscriber.dll        # version loader (the only DLL the game loads)
└── bin/g0.111.0/
    └── AutoModSubscriber.Impl.dll
```

The game only loads `<mod.path>/<manifest.id>.dll`, so the loader picks the implementation
matching the running game version (read from `release_info.json`) and initializes it.
Because every branch receives byte-identical content with the same version number,
multiplayer mod comparison can never fail due to Steam's branch-based delivery.

See [`docs/VERSION_BUNDLE.md`](docs/VERSION_BUNDLE.md) for the full convention.

### Install

Subscribe on Steam Workshop: <https://steamcommunity.com/sharedfiles/filedetails/?id=3750485606>

Or manually drop the whole package (loader + `mod_manifest.json` + `bin/`) under
`<Steam>/steamapps/common/Slay the Spire 2/mods/AutoModSubscriber/`.

### Build from source

Requires .NET 9 SDK and the Slay the Spire 2 game install (for referenced DLLs under `data_sts2_windows_x86_64/`).

```powershell
.\build.ps1                    # build + package + deploy to the game's mods folder
.\build.ps1 -NoLocalDeploy     # package only
.\build.ps1 -StageWorkshop     # also sync the Workshop workspace content/
```

The mod version is read from `AutoModSubscriber.csproj` and written back into
`mod_manifest.json`, so there is a single source of truth.

### Docs

- [`docs/VERSION_BUNDLE.md`](docs/VERSION_BUNDLE.md) — version distribution convention
- [`docs/DESIGN.md`](docs/DESIGN.md) — design notes
- [`docs/PLAN.md`](docs/PLAN.md) — implementation plan
- [`docs/MEMORY.md`](docs/MEMORY.md) — working memory

---

## 中文

在杀戮尖塔 2 联机时，如果本机模组列表与房主不一致，原版只会弹一个红色「模组不匹配」错误并直接断开。**AutoModSubscriber（自动模组订阅）** 替换了这个错误弹窗，让你能在同一个对话框里一次性把不一致修好：

- 一键自动订阅房主有、本机缺失的 Steam 创意工坊模组。
- 一键勾选并禁用本机有、房主没有的模组（写入 `settings.save`）。

订阅或禁用完成后，请**手动关闭并重启游戏**，再尝试重新加入房间。本模组永远不会主动重启游戏。

### 兼容性

本次发布面向最新 **public-beta v0.111.0**，要求游戏版本至少为 `0.111.0`。

| 房主装本模组 | 客机装本模组 | 行为 |
|---|---|---|
| ✓ | ✓ | 全自动订阅 + 勾选禁用 |
| ✓ | ✗ | 原版客机只多记一条 non-gameplay mod 日志，**不影响**入房 |
| ✗ | ✓ | 弹窗列出缺失项 + 跳转「打开工坊搜索」快捷按钮 |
| ✗ | ✗ | 完全保留原版行为 |

弹窗 UI 会按游戏内语言设置自动在简体中文 / 英文之间切换。

### 工作原理

主机在 `PeerVersionInfo.LocalDefault()` 的 Harmony Postfix 中，把 base64 编码的 sidecar 追加到 `PeerVersionInfo.otherMods`。v0.111.0 起 mod 列表随握手消息传输（`HandshakeManager` 发送 magic + 序列化的 `PeerVersionInfo`），sidecar 因此随握手消息携带。sidecar 包含 `{manifestId: workshopFileId}` 映射以及哨兵 key `__ams_host__`。客机在解析远端握手消息（`HandshakeManager.TryReadHandshakeMessage`）后提取并移除 sidecar，将数据写入静态 `ModWorkshopMap`，弹窗据此知道每个缺失模组对应的工坊 file id。每次握手都会先清空上一房间的映射，因此未携带有效 sidecar 的房主不能复用旧的 Workshop ID。

订阅走 Steamworks.NET（`SteamUGC.SubscribeItem` + 长期持有的 `Callback<ItemInstalled_t>` / `Callback<DownloadItemResult_t>`）。Steam 创意工坊安装的模组可以被自动订阅；手动放进 `mods/` 目录的模组不能自动订阅，弹窗会明确提示。

### 版本分发

本模组是**一个在全部 Steam 分支上都能正常工作的工坊条目**，玩家不必重新订阅，
也不必担心 Steam 发错包体：

```text
AutoModSubscriber/
├── mod_manifest.json            # 只有一份版本号，所有分支共用
├── AutoModSubscriber.dll        # 版本启动器（游戏只加载这一个 DLL）
└── bin/g0.111.0/
    └── AutoModSubscriber.Impl.dll
```

游戏只会加载 `<mod.path>/<manifest.id>.dll`，启动器读 `release_info.json` 得到当前
游戏版本，挑出对应实现并初始化。因为所有分支拿到的是**字节相同、版本号相同**的内容，
联机模组比对不会再因 Steam 的分支分发而出错。

完整约定见 [`docs/VERSION_BUNDLE.md`](docs/VERSION_BUNDLE.md)。

### 安装

订阅创意工坊：<https://steamcommunity.com/sharedfiles/filedetails/?id=3750485606>

或手动把**整个包体**（启动器 + `mod_manifest.json` + `bin/`）放到
`<Steam>/steamapps/common/Slay the Spire 2/mods/AutoModSubscriber/`。

### 从源码构建

需要 .NET 9 SDK 以及杀戮尖塔 2 本体（用于引用 `data_sts2_windows_x86_64/` 下的 DLL）。

```powershell
.\build.ps1                    # 构建 + 打包 + 部署到游戏 mods 目录
.\build.ps1 -NoLocalDeploy     # 只打包不部署
.\build.ps1 -StageWorkshop     # 同时同步工坊 workspace 的 content/
```

模组版本以 `AutoModSubscriber.csproj` 为准并写回 `mod_manifest.json`，避免两处版本号不一致。

### 文档

- [`docs/VERSION_BUNDLE.md`](docs/VERSION_BUNDLE.md) — 版本分发约定
- [`docs/DESIGN.md`](docs/DESIGN.md) — 设计说明
- [`docs/PLAN.md`](docs/PLAN.md) — 实施计划
- [`docs/MEMORY.md`](docs/MEMORY.md) — 实现工作记忆

---

## Author / 作者

`@Bilibili我叫煎包`

- Bilibili: <https://space.bilibili.com/234054413>
- QQ Group / QQ 群: `1029172361`

## License

MIT