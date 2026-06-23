# AutoModSubscriber · 设计文档

> 客机进入房间时，若房主的 mod 列表与本机不一致，自动调用 Steam Workshop API 订阅本机缺失的 mod，并提供"我有 host 没有的 mod" 勾选禁用面板。
>
> 文档版本：v0.1（初稿，立项确认后落地）
> 作者：`@Bilibili我叫煎包`
> 目标游戏：Slay the Spire 2（Steam 正式版，AppID 2868840）

---

## 1. 立项动机

### 1.1 痛点

STS2 联机时，客机本地的 gameplay-relevant mod 列表必须与房主**逐字符串相等**，否则 `JoinFlow` 直接抛 `ModMismatch` 并断开。游戏没有提供任何"自动订阅缺失 mod"或"自动禁用多余 mod"的能力，玩家只能：

1. 看断线弹窗里红字列出的 mod 名
2. 自己去 Steam 工坊一个个搜
3. 订阅、等下载
4. 重启游戏
5. 再尝试加入

整个流程门槛对新手玩家极不友好。

### 1.2 目标

在客机进房流程中，**接管 mod 校对结果**，自动完成两件事：

- 自动订阅"房主有、本机无"的 mod（前提：协议端能拿到对应 workshopId）
- 提供 checkbox 列表，让玩家勾选"本机有、房主无"的 mod 中要禁用的项，一键写回 `ModSettings` 并提示重启

不在范围内：

- 跨 mod 版本错配的自动处理（Steam 工坊不支持订阅特定历史版本）
- 自动重启游戏（订阅完成后只提示"请手动重启"）
- ModConfig 集成（本 mod 不需要任何运行期配置项）

---

## 2. 关键事实（基于 SL2 v0.107.1 反编译证据）

| 事实 | 出处 | 影响设计 |
|---|---|---|
| 客机 mod 校对在 `JoinFlow.Begin` 里完成，差异即抛 `ClientConnectionFailedException(ModMismatch, ConnectionFailureExtraInfo{missingModsOnLocal, missingModsOnHost})` | `SL2/src/Core/Multiplayer/Game/JoinFlow.cs` | 注入点选这里 |
| host 发给客机的 mod 名格式是 `"<manifest.id>-<manifest.version>"`，**不带 workshop file id** | `SL2/src/Core/Modding/ModManager.cs` `GetGameplayRelevantModNameList` | 必须通过协议扩展才能拿 workshopId |
| 协议消息 `InitialGameInfoMessage` 是 struct，字段固定（`gameplayAffectingMods: List<string>` / `otherMods: List<string>`） | `SL2/src/Core/Multiplayer/Messages/Lobby/InitialGameInfoMessage.cs` | 不能新增字段，只能往字符串里塞 |
| 游戏进程已加载 Steamworks.NET（`using Steamworks; SteamUGC.SubscribeItem / Callback<ItemInstalled_t>`） | `SL2/src/Core/Modding/ModManager.cs` | mod 可直接 P/Invoke Steam API，不需要 URL |
| 工坊订阅后 `OnSteamWorkshopItemInstalled` 仅把 mod 标记为 `ModLoadState.AddedAtRuntime`，**不会热加载** | `SL2/src/Core/Modding/ModManager.cs::TryLoadMod` 中 `State != ModManagerState.None → Skipping ...` | 订阅完成必须重启 |
| `_steamItemInstalledCallback` 是 `Callback<ItemInstalled_t>` 静态字段长期持有 | 同上 | 本 mod 也必须长期持有 `Callback<T>` / `CallResult<T>` 实例 |

---

## 3. 总体架构

```
┌────────────────────────────────────────────────────────────────┐
│ host 进程                                                       │
│                                                                │
│  InitialGameInfoMessage.Basic()                                │
│    └── [Harmony Postfix: HostManifestAnnotatePatch]            │
│          遍历 ModManager.Mods                                  │
│          对每个 Steam workshop 来源的 mod：                    │
│            从 mod.path 切出 <workshopFileId>                   │
│          把 gameplayAffectingMods / otherMods 中的             │
│          "<id>-<ver>" 改写为 "<id>-<ver>\u0001ws=<fileId>"      │
└────────────────────────────────────────────────────────────────┘
                              │ 网络
                              ▼
┌────────────────────────────────────────────────────────────────┐
│ client 进程                                                     │
│                                                                │
│  JoinFlow.Begin (Harmony Patch: ClientJoinFlowInterceptPatch)  │
│    1. 在原 mod 比对发生前，先 strip 客机收到的 host mod 列表中  │
│       的 \u0001ws=... 后缀，并把 (manifestKey → workshopId)    │
│       写入 ModWorkshopMap                                      │
│    2. 让原 Except 比对在 strip 后的字符串上正常执行             │
│    3. 若抛 ClientConnectionFailedException(ModMismatch)：      │
│       捕获 ConnectionFailureExtraInfo                          │
│       不再让原版 NErrorPopup 弹 ModMismatch 错误               │
│       改为打开 AutoSubscribeDialog                             │
│                                                                │
│  AutoSubscribeDialog (Godot Control, 复用原生对话框样式)        │
│   ┌──────────────────────────────────────────────────────────┐ │
│   │ 区块 1：房主有，你没有 (missingOnLocal)                    │ │
│   │   ModRow × N                                              │ │
│   │     [icon] modId  vX.Y.Z   [状态文字 + 进度]              │ │
│   │   [全部自动订阅] 按钮                                      │ │
│   │                                                          │ │
│   │ 区块 2：你有，房主没有 (missingOnHost)                     │ │
│   │   CheckboxRow × M                                         │ │
│   │     [☐] modId vX.Y.Z                                     │ │
│   │   [禁用勾选项] 按钮                                       │ │
│   │                                                          │ │
│   │ [关闭]                                                   │ │
│   └──────────────────────────────────────────────────────────┘ │
│                                                                │
│  WorkshopSubscriber (静态)                                     │
│    管理 SteamUGC.SubscribeItem + Callback<DownloadItemResult_t>│
│              + Callback<ItemInstalled_t>                       │
│    每 250ms 调 GetItemDownloadInfo 更新进度                    │
│    全部完成 → 触发完成回调                                     │
│                                                                │
│  ModDisableApplier                                             │
│    读取 ModManager._settings.ModList                           │
│    对勾选项 IsEnabled = false                                  │
│    调 ModSettings 保存入口（具体方法名实现时确认）              │
└────────────────────────────────────────────────────────────────┘
```

---

## 4. 协议扩展（modId → workshopId）

### 4.1 字符串约定

| 协议版本 | 元素格式 | 出现条件 |
|---|---|---|
| vanilla | `"<id>-<ver>"` | host 没装本 mod，或 mod 来源是本地 `mods/` 目录 |
| v1 | `"<id>-<ver>\u0001ws=<fileId>"` | host 装了本 mod 且对应 mod 来自 Steam workshop |

- 分隔符选 `\u0001`（SOH，ASCII 0x01）：保证不出现在任何合法 manifest id / SemVer 字符串中。
- `<fileId>` 是十进制 `PublishedFileId_t.m_PublishedFileId`（uint64）。

### 4.2 host 侧改写

`InitialGameInfoMessage.Basic()` 内部调 `ModManager.GetGameplayRelevantModNameList()` / `GetNonGameplayRelevantModNameList()` 拿到原始字符串列表。`HostManifestAnnotatePatch` 在 Postfix 改写返回的 struct：

```text
buildIdToFileIdMap():
    for mod in ModManager.Mods:
        if mod.state == Loaded and mod.modSource == SteamWorkshop:
            fileId = ExtractFileIdFromPath(mod.path)
            if fileId != 0:
                map[mod.manifest.id] = fileId

annotate(list):
    for i in 0..list.Count:
        parts = list[i].SplitOnce('-')           # 拿到 manifestId 段
        if map.TryGetValue(parts.id, out fileId):
            list[i] = list[i] + "\u0001ws=" + fileId
```

`ExtractFileIdFromPath` 从 `.../steamapps/workshop/content/2868840/<fileId>/...` 切出第一个数字目录段，找不到返回 0。

### 4.3 client 侧解析

`ClientJoinFlowInterceptPatch` 用 Harmony Transpiler 或更稳的"Prefix + 重新调一遍"方式接管 `JoinFlow.Begin` 的 mod 比对段。建议路径：

- **Prefix 不动**（要全替原方法太重）；
- 改用 **Postfix `HandleInitialGameInfoMessage`** 之后、`JoinFlow.Begin` 主体读取 `initialMessage.gameplayAffectingMods` 之前的位置——也就是直接 Patch `JoinFlow.Begin` 自身（标记成不需要 prefix/postfix 而用 IL transpiler 不现实）。

**最终选择**：用 Harmony **Finalizer / Postfix on `JoinFlow.Begin`** 不行（异常已经抛出去），改为：

- Patch `JoinFlow.Begin` 的 **Prefix** 不可行（async 方法不能简单替换）；
- **采用方案**：Patch `ClientConnectionFailedException` 实际被消费的上层（弹错误 popup 的逻辑），在那里把 `NetError.ModMismatch` 分支替换成我们的弹窗；同时**额外** Patch `InitialGameInfoMessage` 收到时的处理点，把扫到的 workshopId 映射 stash 到静态 `ModWorkshopMap`。

调用错误弹窗的入口（待实现时精确确认）：

- `SL2/src/Core/Nodes/CommonUi/NErrorPopup.cs` 里 `case NetError.ModMismatch`（已 grep 到）—— 在它显示 popup 前 Prefix 拦截，根据 `_connectionExtraInfo` 调起 AutoSubscribeDialog 并 `return false` 跳过原 popup。

数据流：

```text
Patch InitialGameInfoMessage.Deserialize (Postfix):
    for each name in gameplayAffectingMods ∪ otherMods:
        if name contains "\u0001ws=":
            split, store (idPart → fileId) into ModWorkshopMap
            rewrite name back to plain "<id>-<ver>"  ← 让后续 Except 比对正确

Patch NErrorPopup.ShowError (Prefix, or等价方法):
    if errorInfo.reason == ModMismatch:
        Open AutoSubscribeDialog(extraInfo, ModWorkshopMap)
        return false   ← 阻止原版 popup
```

⚠️ 强制保证：strip 后送回原版 `Except` 时，host / 客机两边看到的就是相同的 `"<id>-<ver>"` 字符串，比对正常工作。本 mod 不应让"装/不装"导致比对结果变化。

### 4.4 host 没装本 mod 的退化

| 场景 | 客机看到 | 行为 |
|---|---|---|
| host 装 + 客机装 | 字段带 `ws=...` | strip 后比对，订阅时用 workshopId，**全自动** |
| host 没装 + 客机装 | 字段是纯 `"<id>-<ver>"` | strip 无操作，订阅时 `ModWorkshopMap` 查不到 → 区块 1 文案改为「房主未装 AutoModSubscriber，无法自动订阅。请前往工坊手动搜索」+ 单个跳工坊浏览页的按钮 |
| host 装 + 客机没装 | 字段带 `ws=...`，本 mod 不存在不会 strip | 原版 Except 比对发现"客机多出/少了带 suffix 的字符串" → 仍然 ModMismatch，但**会误报**：因为 host 字符串带 suffix，客机字符串不带，原本可能匹配的 mod 也会判不匹配 |

⚠️ 第三种情况是真正风险点。host 端 Patch 必须**只在本 mod 与原版客户端通信时不发副作用**。两种缓解策略：

- **缓解 A**：host 检测本机是否处于"客机里也都装了 AutoModSubscriber"是做不到的（host 启动时不知道客机情况）。
- **缓解 B**（推荐）：host 端 Patch **保持原列表不变**，新增一个**独立可选**的额外字段塞 workshop map —— 但 `InitialGameInfoMessage` 是 struct + 固定序列化，无法新增字段。
- **缓解 C**（实际采用）：host 端把 workshopId **不**塞进 `gameplayAffectingMods` 元素本身，而是塞进 `otherMods` 末尾一条**特殊条目** `"__amsmap\u0001<json>"`，即：
  - vanilla 客机看到这条会被加入 mod 比对差集 → 但 host 自己也会包含同名条目（因为我们改的是 `Basic()` 的返回结果，host 不再在本地校对），所以差集为空？不，比对发生在客机本地两个列表（自己的 `GetGameplayRelevantModNameList` vs 收到的 `gameplayAffectingMods`）之间，host 没有"再次比对自己"的逻辑——这条特殊条目客机本地不会产生 → **必然出现在 missingOnLocal 里**，让 vanilla 客机也 ModMismatch。

所以缓解 C 也有副作用。**结论**：必须做缓解 C 的变体——

- **最终采用**：把 workshop map 塞在 **`otherMods`** 的特殊条目里，且条目名形如 `"__ams_map__-1.0\u0001ws=<base64 json>"`。
  - 装本 mod 的客机：`InitialGameInfoMessage.Deserialize` Postfix 检测到这条 → 把它从 `otherMods` 列表里**移除**，并把 base64 json 解析成 `ModWorkshopMap`。
  - vanilla 客机：这条留在 `otherMods` 里，参与 vanilla 的"non-gameplay 比对"。`JoinFlow` 中 non-gameplay mod 的差异只 `Log.Warn`，**不会断线**，所以 vanilla 客机最多看见一行警告日志，不影响进房。

`otherMods` 差异不阻塞连接的证据已在 `JoinFlow.Begin` 中读到：

```text
if (list8.Count > 0 || list7.Count > 0)
{
    _logger.Warn($"Mismatch in non-gameplay relevant mods. This is allowed, but ...");
}
```

→ 我们用 `otherMods` 当 sidecar，**对 vanilla 完全无副作用，仅产生一行 warn log**。

### 4.5 修订后的协议

| 字段 | 内容 |
|---|---|
| `gameplayAffectingMods` | **完全不动**，保持 vanilla `"<id>-<ver>"` 列表 |
| `otherMods` | 末尾追加一条 sidecar：`"__ams_map__-1.0\u0001<base64-encoded json>"`，json 形如 `{"RitsuLib":"3700001234","FreeLoadout":"3700004567"}` |
| 装本 mod 的客机 | `InitialGameInfoMessage.Deserialize` Postfix → 从 `otherMods` 移除 sidecar、解析 map、写入静态 `ModWorkshopMap` |
| vanilla 客机 | sidecar 留在 `otherMods` 里，触发一行 warn log，不阻塞进房 |

---

## 5. 订阅子系统（WorkshopSubscriber）

### 5.1 Steamworks API 选用

| API | 用途 | 备注 |
|---|---|---|
| `SteamUGC.SubscribeItem(fileId)` | 提交订阅请求 | 返回 `SteamAPICall_t` |
| `CallResult<RemoteStorageSubscribePublishedFileResult_t>` | 订阅完成回调 | 仅表示"订阅记录写入成功"，不等于已下载 |
| `Callback<DownloadItemResult_t>` | 下载完成 | 长期持有的全局回调 |
| `Callback<ItemInstalled_t>` | 落盘完成 | 长期持有；与游戏自身的 `_steamItemInstalledCallback` 互不冲突，Steamworks.NET 允许多回调 |
| `SteamUGC.GetItemDownloadInfo(fileId, out done, out total)` | 进度查询 | 每 250 ms 拉一次 |
| `SteamUGC.GetItemState(fileId)` | 状态位掩码 | 启动时检查"是否已 Installed"，跳过重复订阅 |
| `SteamUGC.DownloadItem(fileId, highPriority: true)` | 兜底踢一脚 | 仅在订阅 5 秒后状态仍不是 Downloading/Installed 时调 |

### 5.2 单 mod 状态机

```text
Subscribing
    ↓ CallResult OK
Downloading (or Installed if already cached)
    ↓ DownloadItemResult OK
WaitingInstall
    ↓ ItemInstalled
Installed → completion.SetResult(true)

任意失败 → Failed → completion.SetResult(false)
超时（默认 5 min/项）→ TimedOut → completion.SetResult(false)
```

### 5.3 并发与进度上报

- 所有订阅请求并发提交（`SubscribeItem` 是异步 API，Steam 自身处理排队）
- 单一 `Callback<DownloadItemResult_t>` 和单一 `Callback<ItemInstalled_t>` 监听全部 fileId，按 `m_nPublishedFileId` 路由到对应 Job
- AutoSubscribeDialog 持有一个 Godot `Timer`（250 ms）轮询：对每个 `state == Downloading` 的 Job 调 `GetItemDownloadInfo` 更新 UI

### 5.4 完成提示

- 全部 Job `completion.Task` resolve 后，AutoSubscribeDialog 把每行状态改为最终结果（✅/❌）
- 弹一个**单按钮原生确认框**（复用 STS2 自带对话框样式，具体场景路径实现时定）：「已自动订阅 {成功数} 个 mod，{失败数} 个失败。请关闭游戏后重新启动以加载，再尝试加入房间。」
- **不调用** `OS.Execute` / `GetTree().Quit()`，由玩家自己决定何时退出

---

## 6. 「我有 host 没有的 mod」勾选禁用

### 6.1 数据源

`ConnectionFailureExtraInfo.missingModsOnHost: List<string>` —— 已经是 `"<id>-<ver>"` 列表，本地 `ModManager.Mods` 里能找到对应 `Mod` 对象。

### 6.2 UI

每行：

```text
[☐ checkbox]   <manifest.name>   <manifest.id> v<version>
```

默认全不勾选（避免误操作）。

### 6.3 应用

`ApplyDisable(selectedIds)`：

```text
foreach id in selectedIds:
    entry = _settings.ModList.FirstOrDefault(m => m.Id == id)
    if entry != null:
        entry.IsEnabled = false

调用 ModSettings 的保存入口（实现时通过反射，具体方法名在编码阶段读 SL2/src/Core/Modding/ModSettings.cs 确认）
```

完成后在 AutoSubscribeDialog 内追加一行提示：「已禁用 N 个 mod，重启游戏后生效。」不弹独立对话框，让玩家自己点底部"关闭"。

---

## 7. 注入与拦截点汇总

| Patch | 目标 | 类型 | 作用 |
|---|---|---|---|
| `HostInitialInfoSidecarPatch` | `MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.InitialGameInfoMessage.Basic` | Postfix | host 端往 `otherMods` 追加 sidecar 条目（含 workshopId 映射） |
| `ClientInitialInfoSidecarPatch` | 同上类型的 `Deserialize` 实例方法（或 `JoinFlow.HandleInitialGameInfoMessage`） | Postfix | client 端从 `otherMods` 抽出 sidecar，解析并写入静态 `ModWorkshopMap`，把 sidecar 条目从 list 里移除 |
| `ClientModMismatchInterceptPatch` | `NErrorPopup.ShowError`（或等价显示入口） | Prefix | 当 `NetError == ModMismatch` 时打开 AutoSubscribeDialog，return false 跳过原 popup |

具体方法签名/类名在编码阶段用 `codegraph-sl2` MCP 二次确认，必要时使用 `AccessTools.TypeByName(...)` 反射兜底。

---

## 8. 失败/降级矩阵

| 场景 | 表现 | 用户体验 |
|---|---|---|
| host 装 + 客机装 + 缺失 mod 都来自 workshop | 全自动 | 弹窗 → 一键订阅 → 进度 → 提示重启 |
| host 装 + 客机装 + 部分缺失 mod 来自本地 `mods/` | sidecar 里没有这些 mod 的 fileId | 对应行显示「无法自动订阅（非 Workshop 来源）」+ 跳转工坊搜索按钮 |
| host 没装本 mod | 无 sidecar | 区块 1 整体改文案「房主未装 AutoModSubscriber，无法自动订阅，请前往工坊手动搜索」+ 跳搜索按钮 |
| Steam 离线 / Steamworks 未初始化 | `SteamUGC` 调用直接失败 | 弹窗显示「Steam 不可用」，提供手动跳工坊链接 |
| 单条订阅失败 | Job 进入 Failed | 行尾红字标错误码，其他项继续 |
| 订阅超时（>5min） | Job 进入 TimedOut | 同上 |

---

## 9. 不做的事

- 不做自动重启
- 不做 ModConfig 配置面板
- 不做版本错配的自动处理（Steam 工坊本身不支持"订阅指定版本"）
- 不维护社区映射表 / 模糊搜索 / 工坊 UGC 全量扫描兜底（依赖 host 端协议扩展即可，简单可靠）
- 不依赖 RitsuLib、BaseLib、KitLib

---

## 10. 项目结构（计划）

```
AutoModSubscriber/                       # 独立 Git 仓
├── AutoModSubscriber.csproj
├── AutoModSubscriber.sln
├── project.godot
├── export_presets.cfg
├── mod_manifest.json
├── build.ps1
├── README.md
├── .gitignore                           # 忽略 torelease/、release/、bin/、obj/、.godot/
├── docs/
│   ├── DESIGN.md                        # 本文件
│   ├── PLAN.md                          # Plan 模式产出
│   └── MEMORY.md                        # 实现期工作记忆
├── localization/
│   ├── eng/ui.json
│   └── zho/ui.json
├── src/
│   ├── ModuleInit.cs                    # [ModuleInitializer] 三重保险初始化
│   ├── AutoModSubscriberMod.cs          # Harmony PatchAll 入口
│   ├── Stubs.cs                         # 反编译用占位类型（如需要）
│   ├── Protocol/
│   │   ├── ModWorkshopMap.cs            # 全局静态 map (manifestId → fileId)
│   │   ├── SidecarCodec.cs              # sidecar 条目编/解码
│   │   ├── HostInitialInfoSidecarPatch.cs
│   │   └── ClientInitialInfoSidecarPatch.cs
│   ├── Subscribe/
│   │   ├── WorkshopSubscriber.cs        # SteamUGC 调用 + Callback 持有
│   │   └── SubscribeJob.cs              # 单 mod 状态机
│   ├── Disable/
│   │   └── ModDisableApplier.cs         # 写回 ModSettings.ModList
│   └── UI/
│       ├── ClientModMismatchInterceptPatch.cs   # 拦截 NErrorPopup
│       ├── AutoSubscribeDialog.cs               # 主弹窗
│       ├── ModRow.cs                            # 订阅区块行
│       └── DisableCheckboxRow.cs                # 禁用区块行
├── torelease/                           # staging（不进 git）
└── release/                             # 历史发布包（不进 git）
```

工坊 workspace 后续在 `STS2_mod/_workshop_workspaces/AutoModSubscriber/` 创建（首次发布前完成）。

---

## 11. 风险登记

| 风险 | 等级 | 缓解 |
|---|---|---|
| 游戏更新改 `InitialGameInfoMessage` 字段名或序列化顺序 | 中 | `AccessTools.TypeByName` + 反射访问字段；首启动日志打印 mismatch 信息便于诊断 |
| host 装本 mod 时 sidecar 进入 `otherMods` 后被某个未来更新的逻辑拒绝 | 低 | 当前版本验证：`otherMods` 不一致只产 warn，不断连接 |
| Steamworks.NET 在某些 host 上未初始化（如本地局域网无 Steam 模式） | 低 | 检查 `SteamInitializer.Initialized`，未初始化时弹"Steam 不可用，请手动安装" |
| `NErrorPopup` 显示入口在未来版本被重命名 | 中 | 实现时多备一份 Patch 作 fallback；保留"完全不拦截、只在错误弹窗外平行弹我们的弹窗"作为更保守的兜底方案 |
| `ModSettings` 的保存入口方法名/签名不稳定 | 中 | 反射调用 + 失败时仅把 `IsEnabled = false` 写入内存，附加文案告知"如果禁用未生效，请到游戏 mod 菜单手动禁用" |

---

## 12. 验收标准

1. host 装 + 客机装：客机进入 host 房间时若有 mod 缺失，弹本 mod 对话框，点订阅可看到进度，全部完成后提示重启；重启后再次进房成功
2. host 装 + 客机装：客机本地有 host 没有的 mod，可在区块 2 勾选并点禁用，提示重启；重启后该 mod 不再加载
3. host 未装本 mod：客机弹窗显示降级提示与工坊搜索按钮
4. vanilla 客机连接装本 mod 的 host：能正常进房（仅 `otherMods` 多一行 warn），无副作用
5. Steam 离线：弹窗显示"Steam 不可用"提示，不崩溃