# Master Miao V1.2.4

[中文](README.md) | [English](README.en.md) | [Development history](DEVELOPMENT_HISTORY.md) | [Architecture](ARCHITECTURE.md)

**Master Miao** is a portable, offline Windows application for organizing and safely exporting SolidWorks multi-body parts. It can scan one or more `.SLDPRT` files, generate three previews for every body, rename and classify bodies, export verified single-body parts, optionally generate STEP files and in-place assemblies, and create an Excel report with embedded thumbnails.

![Master Miao main window](docs/UI_PREVIEW.png)

This repository contains reproducible source code and validation documents. User projects, runtime `Data`, SolidWorks test models, build caches, and machine-specific paths are intentionally excluded. A ready-to-run V1.2.4 package is provided on the Releases page.

## Requirements

- Windows 10 or Windows 11 with .NET Framework 4.x.
- A locally installed and working SolidWorks. The current build was validated against SolidWorks 2024, Revision 32.0.1.
- Save documents already being edited before a scan or export, and do not operate SolidWorks while an automated task is running.
- Master Miao may reuse one existing SolidWorks session. It stops when multiple SolidWorks processes are detected so it cannot attach to the wrong window.

## Typical workflow

1. Start `MasterMiao.exe` and select Chinese or English. The choice can be remembered and changed later in Settings.
2. Drop or select one or more `.SLDPRT` files. After the SolidWorks notice is confirmed, Master Miao scans bodies and generates isometric, front, and top previews.
3. Organize bodies in the table with multi-selection and batch classification, or use Guided mode to focus on one body at a time.
4. A tag is also the destination folder. Create, rename, or remove categories in the tree, or drag blocks in the relationship view to change parent-child relationships.
5. Optionally enable "Export one per identical geometry" to collapse equivalent bodies into one representative item. Changes to its name, category, and selection state apply to the whole group.
6. Select the main output folder, formats, assembly option, STEP destination mode, and name-conflict policy, then export.
7. Save unfinished work as a project folder containing `project.swbody.json` and `Previews`, and continue it later without copying the original SolidWorks files.

## Main capabilities

- Detects installed SolidWorks versions, API availability, part and assembly templates, and the STEP export entry point.
- Scans multiple source parts and keeps successfully scanned source documents open for later body highlighting.
- Shows three thumbnails per body in the list and three larger views in Guided mode.
- Supports 80%–200% list scaling, Ctrl + mouse wheel, multi-selection, batch classification, and filtering by source file.
- Highlights one or more bodies from the same source inside an already-open SolidWorks document without saving it.
- Supports reusable folder/tag templates and a draggable hierarchy editor.
- Supports project save, delayed automatic save, recent projects, recovery records, source relocation, and source-change detection.
- Provides Chinese and English interfaces and localized Excel report headers.
- Uses isolated staging, single-body verification, STEP header verification, overwrite backups, and explicit failure messages.
- Reports elapsed time, the actual number of files exported, and detailed failure causes.

## STEP destination modes

STEP export always produces the verified SLDPRT files required by the assembly-based batch process.

### Same folder as SLDPRT

Each STEP file is stored beside its corresponding SLDPRT inside the assigned category folder. This preserves the behavior of earlier versions.

### Separate mirrored folder trees

Master Miao creates two roots under the selected main output folder:

```text
Main output/
├─ 零件源文件/             # SLDPRT files and optional SLDASM
│  └─ <mirrored categories>/
├─ STEP生产文件/           # production STEP files and assembly STEP
│  └─ <mirrored categories>/
└─ 实体导出清单_*.xlsx
```

Both roots use the same category hierarchy. Source part files and production STEP files remain physically separate, while the Excel report records their real locations.

## How STEP export works

Master Miao first creates and verifies single-body SLDPRT files. It then builds a temporary or retained in-place assembly. The compiled `MasterMiao.StepMacro.dll` temporarily enables SolidWorks' option to export assembly components as individual STEP files and saves the assembly as STEP in one batch. The macro restores the original SolidWorks STEP option in success, failure, and exception paths.

Generated STEP files remain in an isolated staging folder until the log confirms `RESTORED|True`, the batch reports success, and every file starts with the standard `ISO-10303-21;` header. Only then are files committed to their final folders. V1.2.4 changes the final routing only; it does not change the tested SolidWorks macro sequence.

The same export core was validated on a real user desktop in V1.1.2 with three independent SLDPRT files, three part STEP files, one in-place assembly, one assembly STEP, and one Excel report. The current automation environment could not repeat a complete visible-desktop STEP run because the test process and SolidWorks were running at different Windows integrity levels. This limitation is documented instead of being reported as a successful V1.2.4 desktop test.

## Project persistence

- The first manual save creates `<project name>_SWBO项目` at a user-selected location.
- `project.swbody.json` stores source references, body names and tags, category hierarchy, output settings, selection state, zoom, guided-mode position, and latest export state.
- `Previews` stores three PNG images for every body; original `.SLDPRT` files are never copied into the project.
- Saved projects are automatically updated after edits. Unsaved sessions use `Data/Recovery` until a permanent project location is selected.
- Missing source files can be rebound. Changed file size or modification time requires a rescan before export.

Runtime data is intentionally stored beside the portable executable. When moving to a new extracted version, open the previously saved `project.swbody.json` or copy only the required templates and settings after reviewing them.

## Safety boundaries

- No service, installer, database, Office automation, or registry write is used.
- Original SLDPRT files are opened with Silent + ReadOnly and are never saved by Master Miao.
- Source identity, output writability, path safety, and available disk space are checked before export.
- Parts are reopened and verified as single-body files before they enter the final output tree.
- Assemblies are reopened and checked for component count, referenced paths, and fixed state.
- STEP files must exist, be non-empty, and pass the standard header check.
- Overwrite mode moves existing targets into `Data/Backups` before committing new files.
- A SolidWorks session started by the application is identified by process ID and start time. A user-owned session is restored instead of being closed.

## Known limitations

- In-place assembly generation and geometry deduplication cannot be enabled together because a representative part cannot preserve every duplicate occurrence's original global position.
- Assembly and batch STEP modes require globally unique final part names within the selected batch.
- Body highlighting requires the source document to be open in the single active SolidWorks session.
- The current scanner reads the active saved configuration; a configuration selector is not yet available.
- Geometry deduplication is based on a fingerprint containing volume, area, topology counts, and face information. It is optional and disabled by default.
- No automation can detect every possible manual action inside SolidWorks; users should still avoid interacting with it while a task notice is active.

## Build from source

The source consists of nine C# application files, one compiled STEP macro, and a reproducible multi-size icon script. It uses no NuGet packages.

Run:

```powershell
.\build.ps1
```

The script uses the x64 .NET Framework compiler and references SolidWorks interop assemblies from the local SolidWorks installation. All build output is written to `build/`.

See [ARCHITECTURE.md](ARCHITECTURE.md) for file-by-file responsibilities and validation commands. See [DEVELOPMENT_HISTORY.md](DEVELOPMENT_HISTORY.md) for the product background, major design decisions, and version timeline.
