param(
    [string]$GodotExe   = "D:\A-Developing\tools\Godot_v4.5.1\Godot_v4.5.1\Godot_v4.5.1-stable_mono_win64.exe",
    [string]$Config     = "Release",
    [string]$GameDir    = "F:\Steam\steamapps\common\Slay the Spire 2",
    [string]$LoaderDir  = "D:\A-Developing\main\sts2\tools\ModVersionLoader",
    [switch]$NoLocalDeploy,
    [switch]$StageWorkshop
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$ModId       = "AutoModSubscriber"

# ----------------------------------------------------------------------------
# 版本分发机制
#
# 工坊条目只发一份内容，所有 Steam 分支拿到的字节完全相同：
#
#   <ModId>/
#   ├── mod_manifest.json              一份版本号，所有分支共用
#   ├── <ModId>.dll                    ModVersionLoader 启动器（游戏只加载这一个）
#   └── bin/
#       └── g<游戏版本>/               按游戏版本存放实现
#           └── <ModId>.Impl.dll
#
# 游戏只加载 <mod.path>/<manifest.id>.dll（ModManager.TryLoadMod），
# bin/ 下的实现不会被游戏误加载，由启动器按 release_info.json 挑选后加载。
# 因此不会再出现「正式版收到 beta 包体」这类 Steam 分发错位。
# ----------------------------------------------------------------------------

Write-Host "=== $ModId Build (version-bundled) ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectRoot"
Write-Host "Config:  $Config"

# --- 1. 读取 mod 版本（csproj 为唯一真源） -----------------------------------
$csprojPath = Join-Path $ProjectRoot "$ModId.csproj"
[xml]$csproj = Get-Content $csprojPath
$modVersion = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $modVersion) { throw "Could not read <Version> from $csprojPath" }
Write-Host "Mod version: $modVersion" -ForegroundColor Cyan

# --- 2. 读取游戏版本 ---------------------------------------------------------
$releaseInfo = Join-Path $GameDir "release_info.json"
if (-not (Test-Path $releaseInfo)) { throw "release_info.json not found: $releaseInfo" }
$gameVersionRaw = (Get-Content $releaseInfo -Raw | ConvertFrom-Json).version
if (-not $gameVersionRaw) { throw "Could not read 'version' from $releaseInfo" }
$gameVersion = $gameVersionRaw.TrimStart('v', 'V')
$versionDir  = "g$gameVersion"
Write-Host "Game version: $gameVersionRaw  ->  bin/$versionDir" -ForegroundColor Cyan

$sts2DataDir = Join-Path $GameDir "data_sts2_windows_x86_64"

# --- 3. 构建实现 -------------------------------------------------------------
Write-Host "[1/5] Building implementation..." -ForegroundColor Yellow
Push-Location $ProjectRoot
try {
    dotnet build -c $Config --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "implementation build failed" }
} finally { Pop-Location }

$implDll = Join-Path $ProjectRoot ".godot\mono\temp\bin\$Config\$ModId.Impl.dll"
if (-not (Test-Path $implDll)) { throw "implementation DLL not found: $implDll" }
Write-Host "  OK  $([IO.Path]::GetFileName($implDll)) ($((Get-Item $implDll).Length) bytes)" -ForegroundColor Green

# --- 4. 构建启动器 -----------------------------------------------------------
Write-Host "[2/5] Building version loader..." -ForegroundColor Yellow
Push-Location $LoaderDir
try {
    dotnet build -c $Config --nologo -v q `
        /p:LoaderAssemblyName=$ModId `
        /p:Sts2DataDir=$sts2DataDir
    if ($LASTEXITCODE -ne 0) { throw "loader build failed" }
} finally { Pop-Location }

$loaderDll = Join-Path $LoaderDir ".godot\mono\temp\bin\$Config\$ModId.dll"
if (-not (Test-Path $loaderDll)) { throw "loader DLL not found: $loaderDll" }
Write-Host "  OK  $ModId.dll ($((Get-Item $loaderDll).Length) bytes)" -ForegroundColor Green

# --- 5. 组装发布包 -----------------------------------------------------------
Write-Host "[3/5] Assembling package..." -ForegroundColor Yellow
$stageRoot = Join-Path $ProjectRoot "build\mods\$ModId"
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stageRoot "bin\$versionDir") | Out-Null

Copy-Item $loaderDll (Join-Path $stageRoot "$ModId.dll") -Force
Copy-Item $implDll   (Join-Path $stageRoot "bin\$versionDir\$ModId.Impl.dll") -Force

# mod_manifest.json：以 csproj 版本为准写回，避免与工坊文案版本号不一致
$manifestSrc = Join-Path $ProjectRoot "mod_manifest.json"
$manifest = Get-Content $manifestSrc -Raw | ConvertFrom-Json
$manifest.version = $modVersion
# 用无 BOM 的 UTF-8 写回（游戏清单解析器不必处理 BOM）
$manifestText = $manifest | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText(
    (Join-Path $stageRoot "mod_manifest.json"),
    $manifestText,
    (New-Object System.Text.UTF8Encoding($false)))

# 打包自检：根目录必须只有一个 DLL（游戏只加载 <ModId>.dll）
$rootDlls = @(Get-ChildItem $stageRoot -Filter *.dll -File)
if ($rootDlls.Count -ne 1 -or $rootDlls[0].Name -ne "$ModId.dll") {
    throw "package self-check failed: root must contain exactly $ModId.dll, found: $($rootDlls.Name -join ', ')"
}
Write-Host "  OK  root: $ModId.dll + mod_manifest.json  |  bin/${versionDir}: $ModId.Impl.dll" -ForegroundColor Green

# --- 6. 本地部署（供本机联机测试） -------------------------------------------
if (-not $NoLocalDeploy) {
    Write-Host "[4/5] Deploying to local mods folder..." -ForegroundColor Yellow
    $localMods = Join-Path $GameDir "mods"
    if (-not (Test-Path $localMods)) { throw "mods folder not found: $localMods" }
    $target = Join-Path $localMods $ModId

    # 游戏运行时 DLL 被占用，删除会失败并留下半旧半新的目录 —— 提前拦下
    $running = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host ""
        Write-Host "  游戏正在运行（PID $($running.Id -join ', ')），mod 文件被占用，无法部署。" -ForegroundColor Red
        Write-Host "  请先完全退出游戏，再重新运行本脚本。" -ForegroundColor Red
        Write-Host "  （包体已组装好，位于 $stageRoot）" -ForegroundColor DarkGray
        exit 1
    }

    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item "$stageRoot\*" $target -Recurse -Force
    Write-Host "  OK  $target" -ForegroundColor Green
    Write-Host "      (本地副本会遮蔽工坊同版本副本，便于本机测试)" -ForegroundColor DarkGray
}

# --- 7. 同步工坊 workspace（上传真源，不自动上传） ---------------------------
if ($StageWorkshop) {
    Write-Host "[5/5] Staging workshop workspace content..." -ForegroundColor Yellow
    $wsContent = Join-Path (Split-Path $ProjectRoot -Parent) "_workshop_workspaces\$ModId\content"
    if (-not (Test-Path (Split-Path $wsContent -Parent))) {
        throw "workshop workspace not found: $(Split-Path $wsContent -Parent)"
    }
    if (Test-Path $wsContent) { Remove-Item $wsContent -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $wsContent | Out-Null
    Copy-Item "$stageRoot\*" $wsContent -Recurse -Force
    Write-Host "  OK  $wsContent" -ForegroundColor Green
    Write-Host "      上传请另行执行 ModUploader（本脚本不自动上传）" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Package: $stageRoot" -ForegroundColor Green
Get-ChildItem $stageRoot -Recurse -File | ForEach-Object {
    Write-Host ("  {0,-42} {1,8} bytes" -f $_.FullName.Replace("$stageRoot\", ""), $_.Length)
}
Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
