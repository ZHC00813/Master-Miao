# Master Miao V1.2.6

Local revision: `0905-R4` (file version `1.2.6.4`). R4 refines R3's interface and interaction while keeping the scan/export backend unchanged. Check `Master Miao · V1.2.6 · 0905-R4` in the title. Save your project in the old program before switching, then Open project in R4; do not overwrite the old installation. See [R4_UI_NOTES.md](R4_UI_NOTES.md).

R3's sequential issue navigation, shortcuts, percentage dialog and STEP-only option are retained. R4 improves typography, tool grouping and narrow-window layouts, adding a collapsible sidebar and expanded classification workspace. Chinese and English are supported. See the R4 notes for current UI evidence and [R3_CHANGES_AND_TESTS.md](R3_CHANGES_AND_TESTS.md) for R3 features/backend limitations. R2 results below are historical.

[中文](README.md) | [English](README.en.md) | [Background and history](DEVELOPMENT_HISTORY.md) | [V1.2.5 compatibility audit](V1.2.5_COMPATIBILITY_AUDIT.md) | [Architecture](ARCHITECTURE.md)

Master Miao is a Windows utility for organizing SolidWorks multi-body parts. Import SLDPRT files, review three views, rename bodies, assign folder-based categories, and export individual parts, optional STEP files, in-place assemblies and illustrated Excel reports. The red-and-white interface retains the cat-and-wrench branding.

V1.2.6 is a stability and data-safety update based on V1.2.5. Current evidence is in [VALIDATION.md](VALIDATION.md); requirement status is in [ACCEPTANCE_CHECKLIST.md](ACCEPTANCE_CHECKLIST.md). Historical desktop successes are not presented as acceptance of this build.

This is a test candidate. R2 scanned 94 solids from an isolated copy in real SolidWorks 2024. Active-object attachment then failed in the test environment, so complete desktop locating and full STEP/assembly export remain unaccepted. See [R2_FIX_VALIDATION.md](R2_FIX_VALIDATION.md) for scoped evidence. Test engineering copies before relying on production results.

## Requirements and startup

- Windows 10/11 x64, .NET Framework 4.x, and a working local SolidWorks installation. Current testing uses SolidWorks 2024; other releases are not comprehensively verified.
- Run this application and SolidWorks at the same Windows privilege level. Do not arbitrarily change system or macro security settings.
- Extract the complete package, run `MasterMiao.exe`, and choose Chinese or English.
- Drag in or select SLDPRT files. Confirm the SolidWorks access notice before scanning. If automatic startup fails, open SolidWorks manually and retry; an Open SolidWorks button is also available.
- Source files are never saved by scanning, locating or exporting. A SolidWorks dirty flag caused by opening/rebuilding permits viewing and classification with a warning; actual export still rejects unsaved changes, configuration mismatch or a changed source SHA-256.
- Successfully scanned source documents remain open for locating bodies. A session handed to the user is no longer considered an owned background process eligible for forced cleanup.

## Organizing parts

1. Use three-view or compact lists, zoom, multi-selection, search and unclassified/failed/possible-duplicate filters. Visible scope and project-wide export scope are reported separately.
2. Click a name to use the dedicated editor. IME selection and autosave do not commit drafts; Enter remains inside the editor for candidate confirmation. Finish naming or move to another cell to commit; Esc cancels. Guided mode commits name, category and selection together before navigation or return to the list.
3. Tags are folder nodes. Drag blocks to change parents, reuse templates, preview batch names and undo recent edits. Sibling collisions and cycles are rejected; deleting a parent preserves and promotes its children.
4. Checking “Export one per identical geometry” immediately folds matching rows; unchecking restores every record without deleting edits. Group edits share names, categories and selection. Exclude members needing separate production in duplicate review. Export verifies every folded solid against its representative and rejects a noncongruent group with an explanation. Quantities and all occurrences are retained. Review materials and finishes separately.
5. “Locate in SW” resolves bodies in the open source document without hashing the disk file or rejecting its pending-save flag. Configuration mismatches and ambiguous identity still require a rescan. Export retains strict source and geometry checks.
6. Geometry does not establish production equivalence. Material, processing, finish and custom properties require human review. In-place assemblies and one-per-group export are mutually exclusive to prevent missing instance positions.
7. Choose the output root and formats. Options immediately update the project; a detached snapshot defines each running task. Task inputs are disabled while busy. Completion displays elapsed time, results and failure reasons.

## Export integrity

- SLDPRT files are staged, reopened and checked for single solids, volume, area, precise bounds and geometric consistency before commit.
- STEP requires SLDPRT and uses the bundled compiled macro for assembly batch export. After preferences are restored, files are reimported to check body count, geometry and placement; a STEP header alone is not acceptance.
- Assemblies use verified current-task outputs. References, component count, fixed state and transforms are checked. Skipped, unverified old files are not trusted assembly inputs.
- Conflicts support skip, global numbering and backup-before-replace. Planning includes both roots, assemblies and other planned names. Temporary files and atomic replacement preserve valid previous outputs.
- Partial failure retains verified SLDPRT files. Checkpoints distinguish success, failure, unverified skips, cancellation and not-run items. Failed-only retry rechecks identities and dependencies.
- Synchronous SolidWorks calls may delay cancellation until a safe boundary. The UI reports this explicitly and does not forcibly terminate user sessions.

STEP can share each part's category folder or use parallel category trees under `零件源文件` (native files) and `STEP生产文件` (production STEP). Reports remain at their common root.

The bilingual report includes three aspect-preserved images, actual and planned names, numeric quantities, all occurrences, actual paths and separate format/verification outcomes. Headers are frozen and filterable. Missing previews have text placeholders.

## Projects and recovery

Projects contain JSON and a `Previews` folder. Images use immutable content-addressed filenames and relative references. Move the entire folder to retain previews; legacy schema 2 absolute references can be migrated.

Source CAD files are not copied into projects. Rebinding requires matching SHA-256; changed files require rescanning. Autosave writes committed model state only, with exclusive locking, atomic replacement and `.bak` backup. Errors remain visible and leave the project dirty. Recovery is isolated by application instance and project.

## Development and build

```powershell
.\build.ps1
.\build.ps1 -SolidWorksApiPath 'D:\SOLIDWORKS\api\redist'
```

Alternatively set `MASTER_MIAO_SW_API`. The build installs no global dependencies and changes no registry settings. Output goes to `build/`. Distribute the EXE, configuration, compiled macro and interop assemblies together. The implementation remains WinForms with a few focused helpers, without extra framework layers.

Developer documents are bilingual: [architecture](ARCHITECTURE.md), [changes](V1.2.6_CHANGES.md), [V1.2.5 compatibility audit](V1.2.5_COMPATIBILITY_AUDIT.md), [validation](VALIDATION.md). Tests are in `tests/`. Private CAD, work projects, caches and machine-specific records are excluded from public source and packages.

## Scope limits

This is not a standalone CAD kernel: export requires SolidWorks. The application does not install or license SolidWorks or change system configuration. Human duplicate confirmation does not replace engineering review. DXF, BOM, material recognition and online collaboration are deferred while stabilizing existing workflows.
