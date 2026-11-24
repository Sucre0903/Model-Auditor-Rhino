<img src="docs/assets/model-auditor.png" alt="Model Auditor icon" width="64" />

# Model Auditor for Rhino 8

Find heavy objects, layers, and nested blocks when a Rhino model becomes slow. Model Auditor scans the document, ranks potential issues, and lets you locate them in the viewport.

**Windows x64 · Rhino 8 · v0.9.4 public preview** · [简体中文](README.md)

## Quick start

1. Open the repository's **Releases** page and select **v0.9.4**. Download `ModelAuditor-Rhino8-0.9.4-Windows-x64.zip` from **Assets** and extract it to a folder you plan to keep.
2. In Rhino, run `PlugInManager`, choose **Install**, and select `ModelAuditor.rhp` from the extracted folder. Use Rhino 8.28 or a later Rhino 8 service release with the .NET runtime mode.
3. Open a model, run `ModelAudit`, and click **一键体检** (Quick scan). The preview currently uses a Simplified Chinese panel.

The package includes `Example/ModelAuditor-Demo.3dm`. Scan it and select `Small dense mesh` in **重点问题** (Findings), then click **定位缩放** (Locate and zoom). In **图块** (Blocks), look for 3 placed brackets and 6 expanded fasteners.

## What it does

- Ranks objects and layers by memory estimates and display complexity. Flags dense geometry and unusually long attribute text.
- Follows nested block references, counts expanded placements, and locates the top-level instances that contain an issue.
- Supports multi-row selection, temporary outlines for hidden or locked objects, layer highlighting in Rhino, and CSV export.
- Runs a cancellable, read-only audit. Memory and display values are comparative estimates. The saved file size is reported separately.

## Source and feedback

Build from source on Windows with the .NET 8 SDK or later. Close Rhino and run `1_一键构建安装.cmd`. Use **Issues** for reproducible reports, including the exact Rhino service release, the plug-in version, and a short set of steps.

[Changelog (Chinese)](CHANGELOG.md) · [Installation details (Chinese)](docs/INSTALL_中文.md) · [Validation notes (Chinese)](VALIDATION_中文.md)
