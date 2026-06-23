# AutoModSubscriber

[![Steam Workshop](https://img.shields.io/badge/Steam_Workshop-3750485606-blue)](https://steamcommunity.com/sharedfiles/filedetails/?id=3750485606)

Slay the Spire 2 multiplayer mod-mismatch resolver. When you join a multiplayer lobby and your local mod list doesn't match the host's, this mod replaces the vanilla "Mod Mismatch" error popup with an interactive dialog that lets you:

- One-click auto-subscribe to the Steam Workshop mods the host has but you are missing.
- Pick the mods you have but the host doesn't, and disable them with one click (writes `settings.save`).

After subscribing or disabling, **restart the game manually** before trying to join again. This mod never restarts the game for you.

---

杀戮尖塔 2 联机模组不匹配解决方案。加入联机房间时，如果本机模组列表与房主不一致，本 mod 会替代游戏原版的「模组不匹配」红色弹窗，给出一个可交互的对话框：

- 一键自动订阅房主有、本机缺失的 Steam 创意工坊模组。
- 一键勾选并禁用本机有、房主没有的模组（写入 `settings.save`）。

订阅或禁用完成后，请**手动关闭并重启游戏**，再尝试重新加入房间。本 mod 永远不会主动重启游戏。

---

## How it works | 工作原理

**Sidecar protocol.** When the host has this mod installed too, its `InitialGameInfoMessage.Basic()` Postfix attaches a base64-encoded JSON map `{manifestId: workshopFileId}` as one extra entry at the end of `otherMods`, plus a sentinel key `__ams_host__`. The client extracts this sidecar before vanilla mod-list comparison, populates a static `ModWorkshopMap`, and the auto-subscribe dialog reads it to know each missing mod's workshop file id.

Compatibility:

| Side | Host installed | Client installed | Behaviour |
|---|---|---|---|
| Both | ✓ | ✓ | Full auto-subscribe + auto-disable |
| Host only | ✓ | ✗ | Vanilla client logs one extra non-gameplay mod entry; multiplayer join is **not** broken |
| Client only | ✗ | ✓ | Dialog shows the missing list with a "Open Workshop" search shortcut |
| Neither | ✗ | ✗ | 100% vanilla behaviour |

**当主客双方都装本 mod 时**，房主会在 `InitialGameInfoMessage` 的 `otherMods` 末尾附加一条 base64 编码的 JSON 映射 `{manifestId: workshopFileId}`（含哨兵 key `__ams_host__`）。客机在原版 mod 列表比对前先解析这条 sidecar，把工坊 file id 写入静态 `ModWorkshopMap`，弹窗据此自动订阅缺失 mod。

## Build | 构建

```powershell
.\build.ps1            # Debug
.\build.ps1 -Config Release
```

构建脚本会执行 `dotnet build`，并把 `AutoModSubscriber.dll` + `mod_manifest.json` 复制到游戏的 `mods/AutoModSubscriber/` 目录。

## Documentation | 文档

- `docs/DESIGN.md` — 设计说明
- `docs/PLAN.md` — 实施计划
- `docs/MEMORY.md` — 实现工作记忆

## Author

`@Bilibili我叫煎包`

- Bilibili: <https://space.bilibili.com/234054413>
- QQ Group: 1029172361

## License

MIT