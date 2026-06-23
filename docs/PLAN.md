# AutoModSubscriber 实施计划（落地副本）

> 与 Cursor Plan 中保存的当前 plan 同步。
> 设计见 [DESIGN.md](DESIGN.md)。

## 总体策略

- 独立 Git 仓，本地路径 `K:\杀戮尖塔mod制作\STS2_mod\AutoModSubscriber\`，远端建议 `https://github.com/Jianbao233/AutoModSubscriber`
- 不依赖 RitsuLib / KitLib / BaseLib；只 ref 游戏主程序集、Steamworks.NET、Harmony、Godot
- 无 ModConfig 配置项
- 完成后由用户手动重启，不调 `OS.Execute` / `GetTree().Quit()`

## 关键拦截点（已通过 SL2 侦察确认）

- host 端：`MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.InitialGameInfoMessage.Basic`，Postfix 改写返回值的 `otherMods`，追加一条 sidecar 条目
- 客机解析：`InitialGameInfoMessage.Deserialize`，Postfix 从 `otherMods` 抽出 sidecar 写入静态 `ModWorkshopMap`，并把 sidecar 条目从 list 删除
- 客机弹窗拦截：`MegaCrit.Sts2.Core.Nodes.CommonUi.NErrorPopup.Create(NetErrorInfo)`，Postfix；检测 `info.GetReason() == NetError.ModMismatch`，反射读 `NetErrorInfo._connectionExtraInfo` 拿到 `ConnectionFailureExtraInfo`，自挂 AutoSubscribeDialog 到主场景树，并把 `__result = null` 阻止原弹窗
- 持久化：禁用勾选后直接调 `SaveManager.Instance.SaveSettings()`，无需反射

## Sidecar 协议

- 字段：放在 `otherMods` 末尾，**不动 `gameplayAffectingMods`**
- 条目格式：`"__ams_map__-1.0\u0001<base64-utf8 json>"`，json 形如 `{"RitsuLib":"3700001234"}`
- 兼容性矩阵
  - host+client 都装：客机解析 sidecar 后 strip，订阅时通过 `ModWorkshopMap` 拿 fileId
  - host 装 / client 没装：vanilla 客机将 sidecar 视为 non-gameplay mod，按 JoinFlow 现有逻辑只 warn，不断连接
  - host 没装：客机无 sidecar → 区块 1 整体降级为"无法自动订阅"提示 + 跳工坊搜索按钮
  - 都没装：完全 vanilla 行为

## 阶段与检查点

### 阶段 1：仓与骨架 — 已完成

- `AutoModSubscriber/` 下落 `mod_manifest.json`、`AutoModSubscriber.csproj`、`project.godot`、`export_presets.cfg`、`build.ps1`、`.gitignore`
- `docs/DESIGN.md`、`docs/PLAN.md`、`docs/MEMORY.md`
- `localization/{eng,zho}/ui.json` 占位

### 阶段 2：协议层

- `Protocol/ModWorkshopMap.cs` — 静态线程安全字典
- `Protocol/SidecarCodec.cs` — Encode/Decode，常量 `SidecarTag = "__ams_map__-1.0"`、`Sep = '\u0001'`
- `Protocol/HostInitialInfoSidecarPatch.cs` — Postfix `InitialGameInfoMessage.Basic`，把 workshop map 编码后追加到 `__result.otherMods`
- `Protocol/ClientInitialInfoSidecarPatch.cs` — Postfix `InitialGameInfoMessage.Deserialize`，抽出 sidecar 并写入 `ModWorkshopMap`

### 阶段 3：订阅层

- `Subscribe/SubscribeJob.cs` — 状态机
- `Subscribe/WorkshopSubscriber.cs` — 长期持有 `Callback<DownloadItemResult_t>`、`Callback<ItemInstalled_t>`、`CallResult<RemoteStorageSubscribePublishedFileResult_t>`
- 5 秒后 `GetItemState` 仍非 Downloading/Installed 则补 `SteamUGC.DownloadItem(fileId, highPriority: true)`，5 分钟超时

### 阶段 4：UI 层

- `UI/DialogSceneFactory.cs` — 代码构造 Control 树
- `UI/AutoSubscribeDialog.cs` — 双区块主弹窗
- `UI/ModRow.cs` / `UI/DisableCheckboxRow.cs`
- `UI/ClientModMismatchInterceptPatch.cs` — Postfix `NErrorPopup.Create(NetErrorInfo)`
- `Disable/ModDisableApplier.cs` — 反射 `ModManager._settings`，写 `IsEnabled=false` 然后 `SaveManager.Instance.SaveSettings()`

### 阶段 5：本地化与边界

- `localization/{eng,zho}/ui.json` 填充
- `SteamInitializer.Initialized == false`、`_connectionExtraInfo == null`、弹窗内异常等降级路径

### 阶段 6：发布与上工坊

- 准备 `torelease/`
- 创建 `STS2_mod/_workshop_workspaces/AutoModSubscriber/`
- `ModUploader.exe upload -w <绝对路径>`
- 回写 `创意工坊/工坊条目台账.md`
- 主仓 `STS2_mod/README.md` / `MEMORY.md` 加跳转链接；`.gitignore` 加忽略