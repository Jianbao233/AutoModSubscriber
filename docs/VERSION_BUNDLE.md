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
├── mod_manifest.json                   # 只有一份版本号，所有分支共用
├── AutoModSubscriber.dll               # ModVersionLoader 启动器（游戏只加载这一个）
└── bin/
    └── g0.111.0/                       # 按游戏版本存放实现
        └── AutoModSubscriber.Impl.dll
```

原理：游戏只会加载 `<mod.path>/<manifest.id>.dll`（见 `ModManager.TryLoadMod`），
所以 `bin/` 下的实现不会被游戏误加载。启动器读游戏根目录的 `release_info.json`
得到当前游戏版本，挑出对应目录的实现 DLL，反射调用它的 `Initialize()`。

**关键：两套代码装在同一个包里、共用同一份 `mod_manifest.json`。**
所以不管 Steam 发的是哪一版，发到的都是同一个包、同一个版本号 —— 联机比对永远一致。

## 硬性约定（务必遵守）

1. **`mod_manifest.json` 的 `version` 在所有版本目录之间必须完全一致。**
   实现 A 写 `0.1.5-beta`、实现 B 写 `0.1.5` 的话，联机比对依然会不匹配，等于白做。
   现在 `build.ps1` 会从 `.csproj` 的 `<Version>` 统一写回 manifest，**csproj 是唯一真源**。
2. **根目录只能有一个 DLL**，且必须叫 `<ModId>.dll`（启动器）。`build.ps1` 会自检。
3. **实现 DLL 的装配件名必须是 `<ModId>.Impl`**，与启动器区分，避免同名装配件冲突。
4. **实现里不要用 `[ModInitializer]`**，入口交给启动器反射调用（签名：`public static void Initialize()`）。
5. `min_game_version` 写**最低支持版本**，不要写最新版本 —— 否则老分支会被游戏自身的
   版本检查挡下（这正是 RitsuLib 在打包时把 `min_game_version` 压到最低 target 的原因）。

## 目录命名

`bin/g<游戏版本>`，点分版本号，例如：

| 游戏版本 | 目录名 |
|---|---|
| v0.111.0 | `bin/g0.111.0` |
| v0.110.1 | `bin/g0.110.1` |
| 未来补丁 v0.111.5（无专用实现时） | 回退到 `bin/g0.111.0` |

选择顺序（见 `ModVersionLoader/VersionLoader.cs`）：
1. 精确命中游戏版本目录；
2. 否则取**不高于**游戏版本的最大者；
3. 否则退回 `bin/latest`；
4. 游戏版本读不出来时取可用的最高版本；
5. 都不成立 → **显式报错**，绝不静默跑错版本。

## 新增一个游戏版本

1. 在实现工程里针对新 API 做适配（当前实现是 v0.111.0 的握手挂载点）；
2. 把新实现放进 `bin/g<新版本>/AutoModSubscriber.Impl.dll`；
3. 老目录**保留不删** —— 老分支玩家仍然需要它。

本机开发时，`build.ps1` 会自动按当前游戏版本放进对应目录（只放当前这一份）。

## 构建与本地测试

```powershell
# 构建 + 打包 + 部署到本机 mods/（供 fastmp 双开联机测试）
.\build.ps1

# 只打包，不部署
.\build.ps1 -NoLocalDeploy

# 额外同步到工坊 workspace 的 content/（不自动上传）
.\build.ps1 -StageWorkshop
```

产物：`build\mods\AutoModSubscriber\`，同时复制到
`F:\Steam\steamapps\common\Slay the Spire 2\mods\AutoModSubscriber\`。

> **注意**：本地副本会遮蔽工坊同版本副本（游戏 `RemoveDisabledMods` 对
> 同 id 同版本的本地/工坊两份会禁用工坊那份）。所以本机测试期间，
> 联机双方都应使用同一份本地部署，或等新版本上传工坊后再联机。

## 启动器位置

`D:\A-Developing\main\sts2\tools\ModVersionLoader\`（工具区，跨项目复用）。
其他 mod 要接入时：

```powershell
dotnet build -c Release /p:LoaderAssemblyName=<ModId> /p:Sts2DataDir=<游戏 data 目录>
```

## 验证情况（2026-09-16）

| 验证项 | 结果 |
|---|---|
| 版本解析 / 目录挑选逻辑自检（19 项） | 全部通过（`ModVersionLoader.Tests\logic`） |
| 模拟游戏加载：启动器 → 挑版本 → 加载实现 → 定位入口 | 通过（`ModVersionLoader.Tests\harness`，`--dry`） |
| 实际日志 | `[AutoModSubscriber/Loader] game 0.111.0.0 -> g0.111.0 (AutoModSubscriber.Impl.dll)` |
| 打包自检（根目录唯一 DLL） | 通过 |
| 游戏内实际联机 | **待用户手动测试** |
