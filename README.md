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

| Host has this mod | Client has this mod | Behaviour |
|---|---|---|
| ✓ | ✓ | Full auto-subscribe + selective auto-disable |
| ✓ | ✗ | Vanilla client logs one extra non-gameplay mod entry; multiplayer join is **not** broken |
| ✗ | ✓ | Dialog shows the missing list with a "Open Workshop" search shortcut |
| ✗ | ✗ | 100% vanilla behaviour |

The dialog UI automatically switches between Simplified Chinese and English based on the in-game language setting.

### How it works

The host's `InitialGameInfoMessage.Basic()` is Harmony-postfixed to append a base64-encoded sidecar entry to `otherMods`, containing a `{manifestId: workshopFileId}` map plus a sentinel key `__ams_host__`. The client extracts this sidecar before vanilla mod-list comparison, populates a static `ModWorkshopMap`, and the auto-subscribe dialog reads it to know each missing mod's Workshop file id.

Subscription uses Steamworks.NET (`SteamUGC.SubscribeItem` + persistent `Callback<ItemInstalled_t>` / `Callback<DownloadItemResult_t>`). Mods installed via Steam Workshop can be auto-subscribed; mods placed manually under `mods/` cannot, and the dialog clearly says so.

### Install

Subscribe on Steam Workshop: <https://steamcommunity.com/sharedfiles/filedetails/?id=3750485606>

Or manually drop the DLL + `mod_manifest.json` under `<Steam>/steamapps/common/Slay the Spire 2/mods/AutoModSubscriber/`.

### Build from source

Requires .NET 9 SDK and the Slay the Spire 2 game install (for referenced DLLs under `data_sts2_windows_x86_64/`).

```powershell
.\build.ps1                  # Debug build, copies output to the game's mods folder
.\build.ps1 -Config Release  # Release build
```

### Docs

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

| 房主装本模组 | 客机装本模组 | 行为 |
|---|---|---|
| ✓ | ✓ | 全自动订阅 + 勾选禁用 |
| ✓ | ✗ | 原版客机只多记一条 non-gameplay mod 日志，**不影响**入房 |
| ✗ | ✓ | 弹窗列出缺失项 + 跳转「打开工坊搜索」快捷按钮 |
| ✗ | ✗ | 完全保留原版行为 |

弹窗 UI 会按游戏内语言设置自动在简体中文 / 英文之间切换。

### 工作原理

房主端 Harmony Postfix `InitialGameInfoMessage.Basic()`，在 `otherMods` 末尾追加一条 base64 编码的 sidecar，内容是 `{manifestId: workshopFileId}` 映射以及一个哨兵 key `__ams_host__`。客机在原版模组比对之前解析这条 sidecar，把数据写入静态 `ModWorkshopMap`，弹窗据此知道每个缺失模组对应的工坊 file id。

订阅走 Steamworks.NET（`SteamUGC.SubscribeItem` + 长期持有的 `Callback<ItemInstalled_t>` / `Callback<DownloadItemResult_t>`）。Steam 创意工坊安装的模组可以被自动订阅；手动放进 `mods/` 目录的模组不能自动订阅，弹窗会明确提示。

### 安装

订阅创意工坊：<https://steamcommunity.com/sharedfiles/filedetails/?id=3750485606>

或手动把 DLL + `mod_manifest.json` 放到 `<Steam>/steamapps/common/Slay the Spire 2/mods/AutoModSubscriber/`。

### 从源码构建

需要 .NET 9 SDK 以及杀戮尖塔 2 本体（用于引用 `data_sts2_windows_x86_64/` 下的 DLL）。

```powershell
.\build.ps1                  # Debug，构建并复制到游戏 mods 目录
.\build.ps1 -Config Release  # Release
```

### 文档

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