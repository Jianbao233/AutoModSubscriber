# 版本分发机制（version bundle）

> 目的：**玩家只需要订阅一次，Steam 给哪个分支发哪一版都能正常跑**，
> 不需要重新订阅、不需要挂 VPN、也不存在"下到错误包体"这件事。

## 要解决的问题

Steam 创意工坊一个条目只有一份内容。当 mod 需要针对不同游戏版本提供不同代码时，
常见做法是把不同版本的内容**绑定到不同的 Steam 分支**分发。这个做法不可靠：

- Steam 服务器会抽风，把正式版的包体发给 beta 分支玩家（反之亦然）；
- 玩家切分支后需要重新校验/重新下载，还可能拿到不匹配的那一份；
- 后果：联机时同一 mod 的版本字符串对不上，被判定「模组不匹配」而无法进房。

## 做法：一份包体，运行时选版本

```text
AutoModSubscriber/                      # 工坊条目内容 —— 所有分支字节完全相同
├── mod_manifest.json                   # 一份版本号 + min_game_version = 最低支持版本
├── AutoModSubscriber.dll               # ModVersionLoader 启动器（游戏只加载这一个）
└── bin/
    ├── g0.107.1/                       # 正式版（Latest Version 分支）实现
    │   └── AutoModSubscriber.Impl.dll
    └── g0.111.0/                       # public-beta 分支实现
        └── AutoModSubscriber.Impl.dll
```

原理：游戏只会加载 `<mod.path>/<manifest.id>.dll`（见 `ModManager.TryLoadMod`），
所以 `bin/` 下的实现不会被游戏误加载。启动器读游戏根目录的 `release_info.json`
得到当前游戏版本，挑出对应目录的实现 DLL，反射调用它的 `Initialize()`。

**关键：两套代码装在同一个包里、共用同一份 `mod_manifest.json`。**
所以不管 Steam 发的是哪一版，发到的都是同一个包、同一个版本号 —— 联机比对永远一致。

## 当前支持的游戏版本

| 游戏分支 | 版本 | 实现目录 | 挂载点 |
|---|---|---|---|
| Latest Version（正式版） | v0.107.1 | `bin/g0.107.1/` | `InitialGameInfoMessage.Basic` / `.Deserialize` |
| public-beta | v0.111.0 | `bin/g0.111.0/` | `PeerVersionInfo.LocalDefault` / `HandshakeManager.TryReadHandshakeMessage` |

两份实现必须分开，因为挂载点 API **互不兼容**：

- v0.111.0 移除了 `InitialGameInfoMessage.versionInfo` 字段 → 老代码编译不过；
- v0.107.x 没有 `HandshakeManager` → 新代码编译不过。

## 硬性约定（务必遵守）

1. **`mod_manifest.json` 的 `version` 在所有实现之间必须完全一致。**
   实现 A 写 `0.1.5-beta`、实现 B 写 `0.1.5` 的话，联机比对依然会不匹配，等于白做。
   `build.ps1` 会从 `.csproj` 的 `<Version>` 统一写回，**csproj 是唯一真源**。
2. **`min_game_version` 必须写「最低支持版本」，不能写最新版本。**
   实测教训：写 `0.111.0` 时，正式版 v0.107.1 直接拒绝加载整个 mod：
   ```
   [ERROR] Tried to load mod with id AutoModSubscriber, but its declared
           min game version 0.111.0 is higher than the current game version v0.107.1
   ```
   `build.ps1` 会自动取所有实现里最低的游戏版本写入。
3. **根目录只能有一个 DLL**，且必须叫 `<ModId>.dll`（启动器）。`build.ps1` 会自检。
4. **实现 DLL 的装配件名必须是 `<ModId>.Impl`**，与启动器区分，避免同名装配件冲突。
5. **实现里不要用 `[ModInitializer]`**，入口交给启动器反射调用
   （签名：`public static void Initialize()`）。

## 目录命名

`bin/g<游戏版本>`，点分版本号，例如 `bin/g0.107.1`、`bin/g0.111.0`。

选择顺序（见 `ModVersionLoader/VersionLoader.cs`）：
1. 精确命中游戏版本目录；
2. 否则取**不高于**游戏版本的最大者（例：游戏 v0.110.1 会回退到 `g0.107.1`）；
3. 否则退回 `bin/latest`；
4. 游戏版本读不出来时取可用的最高版本；
5. 都不成立 → **显式报错**，绝不静默跑错版本。

## 源码结构（多实现）

```text
src/                        共享代码（协议编解码、订阅、UI 控件，各版本一致）
src/versions/v107/          v0.107.x 专属：ModuleInit + 挂载点补丁 + UI
src/versions/v111/          v0.111.0 专属：ModuleInit + 挂载点补丁 + UI + Compat
```

`AutoModSubscriber.csproj` 用 `/p:GameCompat=<v107|v111>` 选择版本目录，
用 `/p:Sts2DataDir=<该版本 SDK 目录>` 选择编译所对的游戏 DLL。

游戏 SDK 按版本存档在 `D:\A-Developing\tools\sts2_sdk_by_version\`：

| 目录 | 来源 |
|---|---|
| `v0.107.1` | 正式版分支在装时从游戏目录抓取（真实 SDK） |
| `v0.108` / `v0.109.0` / `v0.110.1` / `v0.111.0` | GDRE 快照 `tools/SL2_<版本>/.godot/mono/temp/bin/Debug/` |

## 新增一个游戏版本

1. 把该版本游戏的 `sts2.dll` 等存档到 `tools/sts2_sdk_by_version/v<版本>/`；
2. 在 `src/versions/` 下新建版本目录（从最接近的现有版本复制，改挂载点）；
3. 在 `build.ps1` 的 `$Targets` 里加一行；
4. 跑一次 `build.ps1`，自检会确认包体结构正确。

## 构建与本地测试

```powershell
.\build.ps1                    # 编译全部实现 + 打包 + 部署到本机 mods/
.\build.ps1 -NoLocalDeploy     # 只打包
.\build.ps1 -StageWorkshop     # 额外同步工坊 workspace 的 content/
```

产物：`build\mods\AutoModSubscriber\`。

> 游戏运行时 DLL 被占用，脚本会提前报错退出（不会留下半旧半新的目录）。

## 启动器位置

`D:\A-Developing\main\sts2\tools\ModVersionLoader\`（工具区，跨项目复用）。
其他 mod 接入：`dotnet build -c Release /p:LoaderAssemblyName=<ModId> /p:Sts2DataDir=<SDK>`

## 验证情况（2026-10-03）

| 验证项 | 结果 |
|---|---|
| 版本解析 / 目录挑选逻辑自检（19 项） | ✅ 全部通过 |
| beta 环境（release_info=v0.111.0）→ 挑中 `g0.111.0` | ✅ |
| 正式版环境（release_info=v0.107.1）→ 挑中 `g0.107.1` | ✅ |
| 打包自检（根目录唯一 DLL、每个实现都在位） | ✅ |
| 游戏内 beta 版实际加载（v0.111.0） | ✅ 日志确认启动器与实现均被调起，3/3 补丁生效 |
| 游戏内正式版实际加载（v0.107.1） | ⏳ 待测（本次已修 min_game_version 门槛） |
