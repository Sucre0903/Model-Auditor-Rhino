param(
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectDirectory = Join-Path $projectRoot "ModelAuditor.Rhino8"
$projectName = "ModelAuditor.Rhino8.csproj"
$configuration = "Release"
$pluginId = "8b8f3b7e-5359-4b30-9e3b-0f0d6e5ad3a1"
$pluginVersion = "0.9.4"
$registryPath = "HKCU:\Software\McNeel\Rhinoceros\8.0\Plug-ins\$pluginId"
$relativeBuildOutput = "bin\$configuration\net8.0-windows"

function Invoke-ReleaseBuild {
    param(
        [string]$DotnetPath,
        [string]$ProjectPath,
        [string]$ErrorLog,
        [string]$PackagesDirectory
    )

    $restoreArguments = @("restore", $ProjectPath, "--nologo")
    if ($PackagesDirectory) {
        $restoreArguments += @("--packages", $PackagesDirectory, "--force")
    }
    & $DotnetPath @restoreArguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet 依赖还原失败。请查看上方的 NuGet 错误。"
    }

    & $DotnetPath build $ProjectPath -c $configuration --no-restore --nologo `
        "-flp:ErrorsOnly;LogFile=$ErrorLog" | Out-Host
    return $LASTEXITCODE
}

Write-Host ""
Write-Host "Model Auditor $pluginVersion - Rhino 8" -ForegroundColor Cyan
Write-Host "Project: $(Join-Path $projectDirectory $projectName)"

if (Get-Process -Name "Rhino" -ErrorAction SilentlyContinue) {
    throw "检测到 Rhino 正在运行。请完全关闭 Rhino 8 后重试。"
}

& (Join-Path $PSScriptRoot "ValidateSource.ps1")

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $dotnet) {
    throw "未找到 dotnet。请安装 .NET 8 或更高版本的 SDK 后重试。"
}

$sdkVersion = & $dotnet.Source --version | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) {
    throw "无法读取 .NET SDK 版本。请查看上方的 dotnet 错误。"
}
if ($sdkVersion -notmatch '^(\d+)\.' -or [int]$Matches[1] -lt 8) {
    throw "需要 .NET 8 或更高版本的 SDK。当前版本：$sdkVersion"
}
Write-Host ".NET SDK: $sdkVersion"

$buildId = [Guid]::NewGuid().ToString("N").Substring(0, 12)
$workspace = Join-Path ([IO.Path]::GetTempPath()) "MA-$buildId"
New-Item -ItemType Directory -Path $workspace | Out-Null
try {
    $stagedProject = Join-Path $workspace "src"
    New-Item -ItemType Directory -Path $stagedProject | Out-Null
    Get-ChildItem -LiteralPath $projectDirectory -Force |
        Where-Object { $_.Name -notin @("bin", "obj", ".vs") } |
        Copy-Item -Destination $stagedProject -Recurse -Force

    Write-Host "本机构建目录：$stagedProject"
    $stagedProjectPath = Join-Path $stagedProject $projectName
    $errorLog = Join-Path $workspace "build-errors.log"
    $buildExit = Invoke-ReleaseBuild -DotnetPath $dotnet.Source -ProjectPath $stagedProjectPath -ErrorLog $errorLog
    if ($buildExit -ne 0 -and (Test-Path -LiteralPath $errorLog) -and
        (Select-String -LiteralPath $errorLog -Pattern '\bNETSDK1064\b' -Quiet)) {
        Write-Host "NuGet 缓存缺少依赖，正在使用临时包目录重试。" -ForegroundColor Yellow
        $isolatedPackages = Join-Path $workspace "pkg"
        $buildExit = Invoke-ReleaseBuild -DotnetPath $dotnet.Source -ProjectPath $stagedProjectPath `
            -ErrorLog $errorLog -PackagesDirectory $isolatedPackages
    }
    if ($buildExit -ne 0) {
        throw "Release 编译失败。请查看上方的编译错误。"
    }

    $buildDirectory = Join-Path $stagedProject $relativeBuildOutput
    $rhpPath = Join-Path $buildDirectory "ModelAuditor.rhp"
    if (-not (Test-Path -LiteralPath $rhpPath -PathType Leaf)) {
        throw "编译结束但没有找到 ModelAuditor.rhp：$rhpPath"
    }

    if ($BuildOnly) {
        $outputDirectory = Join-Path $projectDirectory $relativeBuildOutput
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
        Get-ChildItem -LiteralPath $buildDirectory -File | Copy-Item -Destination $outputDirectory -Force
        Write-Host "编译成功：$(Join-Path $outputDirectory 'ModelAuditor.rhp')" -ForegroundColor Green
        Start-Process explorer.exe -ArgumentList ('"{0}"' -f $outputDirectory)
        return
    }

    $installDirectory = Join-Path $env:APPDATA "McNeel\Rhinoceros\8.0\Plug-ins\ModelAuditor ($pluginId)\$pluginVersion"
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $buildDirectory -File |
        Where-Object { $_.Name -like "ModelAuditor*" -and $_.Extension -in ".rhp", ".pdb", ".json", ".dll" } |
        Copy-Item -Destination $installDirectory -Force

    $installedRhpPath = Join-Path $installDirectory "ModelAuditor.rhp"
    if (-not (Test-Path -LiteralPath $installedRhpPath -PathType Leaf)) {
        throw "安装目录中没有找到 ModelAuditor.rhp。"
    }
} finally {
    Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -Path $registryPath -Force | Out-Null
New-ItemProperty -LiteralPath $registryPath -Name "Name" `
    -PropertyType String -Value "Model Auditor" -Force | Out-Null
New-ItemProperty -LiteralPath $registryPath -Name "FileName" `
    -PropertyType String -Value $installedRhpPath -Force | Out-Null
New-ItemProperty -LiteralPath $registryPath -Name "LoadMode" `
    -PropertyType DWord -Value 1 -Force | Out-Null

$registration = Get-ItemProperty -LiteralPath $registryPath
if ($registration.FileName -ne $installedRhpPath -or $registration.LoadMode -ne 1) {
    throw "插件文件已复制，但 Rhino 当前用户注册失败。"
}

Write-Host ""
Write-Host "安装成功：" -ForegroundColor Green
Write-Host $installDirectory
Write-Host "Rhino 插件注册成功：" -ForegroundColor Green
Write-Host $registryPath
Write-Host ""
Write-Host "请重新启动 Rhino 8，打开模型后点击侧边栏的「模型体检」图标。"
Start-Process explorer.exe -ArgumentList ('"{0}"' -f $installDirectory)
