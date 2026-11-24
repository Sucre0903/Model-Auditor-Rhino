$ErrorActionPreference = "Stop"
$pluginId = "8b8f3b7e-5359-4b30-9e3b-0f0d6e5ad3a1"
$pluginRoot = Join-Path $env:APPDATA "McNeel\Rhinoceros\8.0\Plug-ins\ModelAuditor ($pluginId)"
$registryPath = "HKCU:\Software\McNeel\Rhinoceros\8.0\Plug-ins\$pluginId"
$removed = $false

if (Get-Process -Name "Rhino" -ErrorAction SilentlyContinue) {
    throw "检测到 Rhino 正在运行。请完全关闭 Rhino 8 后重新执行卸载。"
}

if (Test-Path $pluginRoot) {
    $trashRoot = Join-Path $env:LOCALAPPDATA "ModelAuditor\Uninstalled"
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $trashTarget = Join-Path $trashRoot $stamp
    New-Item -ItemType Directory -Path $trashRoot -Force | Out-Null
    Move-Item -Path $pluginRoot -Destination $trashTarget
    Write-Host "原文件已移动到可恢复位置：$trashTarget"
    $removed = $true
}

if (Test-Path $registryPath) {
    Remove-Item -LiteralPath $registryPath -Recurse -Force
    Write-Host "已移除 Rhino 插件注册。"
    $removed = $true
}

if (-not $removed) {
    Write-Host "没有找到已安装或已注册的 Model Auditor。"
    exit 0
}

Write-Host "已卸载 Model Auditor。"
Write-Host "请重新启动 Rhino 8。"
