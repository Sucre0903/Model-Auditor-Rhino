$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot "ModelAuditor.Rhino8"
$projectPath = Join-Path $sourceRoot "ModelAuditor.Rhino8.csproj"
$assemblyInfo = Join-Path $sourceRoot "Properties\AssemblyInfo.cs"
$pluginSource = Join-Path $sourceRoot "ModelAuditorPlugin.cs"

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
if ($project.Project.PropertyGroup.Version -notcontains "0.9.4") {
    throw "项目版本不是 0.9.4。"
}

$assemblyText = Get-Content -LiteralPath $assemblyInfo -Raw
if ($assemblyText -notmatch '8B8F3B7E-5359-4B30-9E3B-0F0D6E5AD3A1') {
    throw "缺少固定的程序集插件 GUID。"
}

$pluginText = Get-Content -LiteralPath $pluginSource -Raw
if ($pluginText -notmatch '(?s)\[Guid\("8B8F3B7E-5359-4B30-9E3B-0F0D6E5AD3A1"\)\]\s*public sealed class ModelAuditorPlugin\s*:\s*PlugIn') {
    throw "插件主类缺少固定 GUID，Rhino 将无法可靠发现插件。"
}

$sourceFiles = foreach ($entry in (Get-ChildItem -LiteralPath $sourceRoot -Force)) {
    if ($entry.Name -in @("bin", "obj", ".vs")) {
        continue
    }
    if ($entry.PSIsContainer) {
        Get-ChildItem -LiteralPath $entry.FullName -Recurse -File -Filter *.cs
    } elseif ($entry.Extension -eq ".cs") {
        $entry
    }
}
$allSource = ($sourceFiles |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"

$forbidden = @(
    'BeginUndoRecord',
    '(doc|_document)\.Objects\.Add',
    '(doc|_document)\.Objects\.Delete',
    '(doc|_document)\.Objects\.Replace',
    '(doc|_document)\.Objects\.ModifyAttributes',
    '(doc|_document)\.Objects\.Transform',
    'CreateMeshes\('
)

foreach ($pattern in $forbidden) {
    if ($allSource -match $pattern) {
        throw "只读扫描约束失败，发现禁止调用：$pattern"
    }
}

if ($allSource -notmatch 'GetMeshes\(MeshType\.Render\)') {
    throw "缺少已有渲染网格读取。"
}
if ($allSource -notmatch 'GetUserStrings\(\)') {
    throw "缺少对象属性文本扫描。"
}
if ($allSource -notmatch 'SmallHeavyScore') {
    throw "缺少小而重评分。"
}
if ($allSource -notmatch 'RuntimeSerialNumber') {
    throw "缺少多文档结果隔离。"
}
$installerText = Get-Content -LiteralPath (Join-Path $PSScriptRoot "BuildAndInstall.ps1") -Raw
if ($installerText -notmatch 'HKCU:\\Software\\McNeel\\Rhinoceros\\8\.0\\Plug-ins') {
    throw "安装器缺少 Rhino 当前用户插件注册。"
}
if ($installerText -notmatch 'New-ItemProperty.+-Name "FileName"') {
    throw "安装器缺少 Rhino 插件 FileName 注册值。"
}

if ($pluginText -notmatch 'PlugInLoadTime\.AtStartup' -or
    $installerText -notmatch '(?s)-Name "LoadMode"\s+`\s+-PropertyType DWord -Value 1\b') {
    throw "插件必须在 Rhino 启动时加载，以注册模型体检面板。"
}
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'Resources\ModelAudit.ico'))) {
    throw "缺少模型体检面板图标。"
}

Write-Host "Model Auditor 0.9.4 源码约束检查通过。" -ForegroundColor Green
