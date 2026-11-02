<#
.SYNOPSIS
  构建歇一杯 Windows 版：跑核心单测、编译、输出到 windows\dist\BreakCup；加 -Install 安装到当前用户并启动。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File windows\scripts\build.ps1 -Install
#>
param(
    [switch]$Install,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\BreakCup"
$icon = Join-Path $project "Assets\BreakCup.ico"
$output = Join-Path $project "bin\Release\net48"
$dist = Join-Path $root "dist\BreakCup"

function Find-Dotnet {
    $candidates = @(
        $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT "dotnet.exe" }),
        (Get-Command dotnet.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"),
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe")
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
    # 只装了运行时的 dotnet.exe 没法构建，挑第一个带 SDK 的
    foreach ($candidate in $candidates) {
        if (& $candidate --list-sdks 2>$null) { return $candidate }
    }
    throw "没有找到 .NET SDK，请先安装 .NET SDK 8 或更新版本"
}

function Invoke-Dotnet {
    & $script:dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') 失败（退出码 $LASTEXITCODE）" }
}

$dotnet = Find-Dotnet

if (-not $SkipTests) {
    Write-Host "== 核心逻辑单测"
    Invoke-Dotnet run --project (Join-Path $root "tests\BreakCup.Tests") -c Release
}

Write-Host "== 编译"
Invoke-Dotnet build $project -c Release -nologo -v q
if (-not (Test-Path $icon)) {
    # 应用图标由程序自己绘制：先编一遍生成 .ico，再带着图标重编
    Write-Host "== 生成应用图标"
    New-Item -ItemType Directory -Force (Split-Path $icon) | Out-Null
    & (Join-Path $output "BreakCup.exe") --make-icon $icon | Out-Null
    Start-Sleep -Milliseconds 300
    if (-not (Test-Path $icon)) { throw "生成应用图标失败" }
    Invoke-Dotnet build $project -c Release -nologo -v q
}

Write-Host "== 输出到 $dist"
Remove-Item -Recurse -Force $dist -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $output "BreakCup.exe") $dist
Copy-Item (Join-Path $output "BreakCup.exe.config") $dist -ErrorAction SilentlyContinue
Copy-Item (Join-Path (Split-Path $root) "LICENSE") $dist -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $dist

if (-not $Install) {
    Write-Host "完成：$dist\BreakCup.exe"
    return
}

$target = Join-Path $env:LOCALAPPDATA "Programs\BreakCup"
Write-Host "== 安装到 $target"
$running = Get-Process BreakCup -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $dist "*") $target -Force
$exe = Join-Path $target "BreakCup.exe"

# 开始菜单快捷方式「歇一杯」
$programs = [Environment]::GetFolderPath("Programs")
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $programs "歇一杯.lnk"))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Description = "歇一杯 · 久坐强制休息"
$shortcut.Save()

# 开过开机启动的话，把启动项指向新位置
$run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
if (Get-ItemProperty $run -Name BreakCup -ErrorAction SilentlyContinue) {
    Set-ItemProperty $run -Name BreakCup -Value "`"$exe`""
}

if ($env:SSH_CONNECTION) {
    # 通过 SSH 安装时，借计划任务在已登录的桌面会话里启动
    schtasks /create /tn BreakCupLaunch /tr "`"$exe`"" /sc once /st 23:59 /it /f | Out-Null
    schtasks /run /tn BreakCupLaunch | Out-Null
    Start-Sleep -Seconds 2
    schtasks /delete /tn BreakCupLaunch /f | Out-Null
} else {
    Start-Process $exe
}
Write-Host "已安装并启动：$exe"
