# 安装与试用

## 安装预编译版

使用 Windows 10/11 x64 和 Rhino 8，推荐 Rhino 8.28 或更新的 Rhino 8 维护版及 .NET 运行模式。

1. 在 GitHub **Releases → v0.9.4 → Assets** 下载 `ModelAuditor-Rhino8-0.9.4-Windows-x64.zip`。解压到长期保留的本机目录，例如「文档」中的 `ModelAuditor`。
2. 打开 Rhino，运行 `PlugInManager`。点击「安装」，选择解压目录中的 `ModelAuditor.rhp`。让同目录的 `ModelAuditor.deps.json` 与它放在一起。
3. 打开模型，点击「模型体检」侧边栏图标，或运行 `ModelAudit`。点击「一键体检」。试用预编译版可直接在 Rhino 安装。

## 试用示例

打开安装包 `Example/ModelAuditor-Demo.3dm`，运行「一键体检」。

| 想试什么 | 到哪里看 | 可核对的结果 |
| --- | --- | --- |
| 找到小而重的模型 | 「重点问题」→ `Small dense mesh` →「定位缩放」 | 小网格有 115,200 个四边面 |
| 理清嵌套图块 | 「图块」 | `Demo Bracket` 放置 3 次，每个含 2 个 `Demo Fastener`，后者展开为 6 个实例 |
| 定位锁定对象 | 「物件」→ `Locked example` →「定位缩放」 | 视口用临时轮廓示意，物件保持锁定 |
| 核对图层链接 | 折叠 Rhino 原生面板里的 `Audit Demo`，再点物件的图层链接 | 展开对应父层，并高亮目标图层 |

在 **Issues** 反馈问题时，请附插件和 Rhino 的完整版本号、实际操作步骤及相关报错。示例模型可用于公开复现。

## 从源码构建

下载 `ModelAuditor-Rhino8-0.9.4-Standalone-Source.zip`，使用 .NET 8 或更新的 SDK。关闭 Rhino，完整解压后双击 `1_一键构建安装.cmd`。同一源码包中的 `3_卸载插件.cmd` 可移除使用该脚本安装的插件。首次还原依赖需要访问 NuGet。详见[源码说明](../README_中文.md)。

[Rhino 插件管理文档](https://docs.mcneel.com/rhino/8/help/en-us/options/plug-ins.htm)
