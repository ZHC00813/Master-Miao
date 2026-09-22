# MasterMiao V1.2.6 · 0922-R5

A Windows utility for organizing SolidWorks multi-body parts: preview, rename, categorize, and export individual parts, STEP files and in-place assemblies.

[中文](README.md) · [Download R5](https://github.com/ZHC00813/Master-Miao/releases/tag/v1.2.6-0922-R5) · [Fixes and validation](R5_FIX_NOTES.md)

## Changes

File version **1.2.6.5** addresses the recovery flow when a scanned source becomes unsaved, or Save As changes its path and re-adding loses names.

- **Save sources and rescan** explicitly saves after confirmation, accounts for dirty state produced during reading, and retains edits for uniquely matched bodies.
- **Relink** changes the source path on the existing record, preserving recoverable names, categories and selection.
- Projects and previews are backed up before rescanning. Changed geometry and ambiguous identities require review.
- Exact-path checks explain same-name document conflicts, read-only state and locks.
- STEP verification avoids same-name native references, handles symmetric in-place solids, and exports a self-contained assembly STEP. File, configuration and geometry checks remain enabled.

## Download and run

Download `MasterMiao-v1.2.6-0922-R5.zip` from [Releases](https://github.com/ZHC00813/Master-Miao/releases/tag/v1.2.6-0922-R5), extract everything, and run `MasterMiao.exe`.

Requires Windows x64, .NET Framework 4.8 and a working local SolidWorks installation. Run both applications at the same privilege level. Save projects and close the old version before switching; retain project `Previews` folders and existing `Data` when upgrading in place.

The original tested package contains 51 files and is 3,469,892 bytes. SHA-256:

```text
e92eed252d7a5086e2b021424b13dbf65ae9049ed44ab5eaa9b1ddcaf6b07f00
```

The package and checksum are also in [releases/v1.2.6-0922-R5](releases/v1.2.6-0922-R5). Personal projects, CAD models and runtime data are excluded.

## Validation scope

Local acceptance on SolidWorks 2024 SP0.1 used an isolated seven-body model: all seven names survived save/rescan; seven native parts, seven STEP parts, one in-place assembly and one assembly STEP passed their geometry checks. The final export needed no manual template selection. Translated and asymmetric mirrored solids were rejected; six import preferences were restored.

This does not claim acceptance of every historical test, arbitrary models or other SolidWorks versions. R2/R3/R4 documents remain historical evidence. GitHub Actions checks and publishes the exact locally tested archive; it does not run SolidWorks.

## Build

From the repository root:

```powershell
.\build.ps1 -SolidWorksApiPath 'D:\SOLIDWORKS\api\redist'
.\tools\Package.ps1 -OutputDirectory 'D:\MasterMiao-release'
```

Application source: `src`; STEP macro: `macro`; test source: `tests`. See [third-party notices](THIRD_PARTY_NOTICE.md).
