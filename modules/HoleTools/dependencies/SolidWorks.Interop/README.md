# SOLIDWORKS Interop fallback

This private migration folder contains the three .NET Interop assemblies needed to compile the current solution when the new computer does not use the same SOLIDWORKS installation path.

- Expected source/version: user's SOLIDWORKS 2024 installation, file version 32.0.1.4.
- Purpose: private development continuity and offline reference resolution.
- It does not contain SOLIDWORKS itself and cannot perform COM automation without an installed and licensed SOLIDWORKS instance.
- Do not publish these assemblies as an independent public package. On a machine with another supported SOLIDWORKS version, an explicit `SolidWorksInteropDir` build property can select that installation's `api\redist` directory.
