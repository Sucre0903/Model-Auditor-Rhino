<img src="docs/assets/model-auditor.png" alt="模型体检图标" width="64" />

# 模型体检 · Model Auditor for Rhino 8

Rhino 模型变慢时，先找出哪些物件、图层或嵌套图块的负载较高。模型体检在 Rhino 里生成可排序的排查清单，选中结果就能定位到模型。

**Windows x64 · Rhino 8 · v0.9.4 公开预览** · [English](README.en.md)

## 快速安装

1. 打开仓库右侧的 **Releases**，进入 **v0.9.4**。从 **Assets** 下载 `ModelAuditor-Rhino8-0.9.4-Windows-x64.zip`，解压到以后会保留的本机文件夹。
2. 在 Rhino 运行 `PlugInManager`，点击「安装」，选择解压后的 `ModelAuditor.rhp`。试用直接使用包里的预编译文件。
3. 打开模型，点击侧边栏的「模型体检」图标，或运行 `ModelAudit`，再点击「一键体检」。

建议使用 Rhino 8.28 或更新的 Rhino 8 维护版，并使用 .NET 运行模式。安装包内附[操作说明](docs/INSTALL_中文.md)和一个可直接打开的示例模型。

## 用示例试一次

打开安装包 `Example` 里的 `ModelAuditor-Demo.3dm` 并扫描。在「重点问题」选择 `Small dense mesh`，点击「定位缩放」，即可找出藏在大场景里的密集网格。切换到「图块」，比对 `Demo Bracket` 的 3 个顶层实例和 `Demo Fastener` 的 6 个展开实例。也可以在「物件」找到 `Locked example`，试用锁定对象的临时轮廓示意。

## 可以排查什么

- **小而重的几何。** 按对象、图层或图块查看内存估算和显示复杂度，发现密集网格及异常长的属性文本。
- **重复使用的嵌套图块。** 按实际实例链统计展开量，从块内问题定位到包含它的顶层实例。
- **藏在模型里的问题。** Ctrl/Shift 多选后批量定位。隐藏或锁定对象可用临时轮廓示意。图层链接可在 Rhino 原生图层面板中展开并高亮对应分支。
- **可分享的排查结果。** 扫描可停止，报告可导出 CSV。体检本身只读，并使用已有的渲染网格数据。

## 如何读数

磁盘文件大小来自上一次保存的 `.3dm`。估算内存用于比较对象负载。显示复杂度来自已有渲染网格或几何拓扑近似。这些指标适合定位候选问题，无法精确分摊 `.3dm` 的压缩字节。

## 源码与反馈

开发者可以用 Windows 和 .NET 8 或更新的 SDK 构建。首次还原需要访问 NuGet。关闭 Rhino 后运行 `1_一键构建安装.cmd`。

遇到安装、扫描或定位问题，请在仓库 **Issues** 附上插件版本、Rhino 维护版号、Windows 版本和复现步骤。用自制的小模型复现即可。

[版本记录](CHANGELOG.md) · [源码安装](README_中文.md) · [技术说明](docs/TECHNICAL_NOTES_中文.md) · [验证记录](VALIDATION_中文.md)
