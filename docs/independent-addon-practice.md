# Staged addon integration practice

Run this check from the repository root. It requires the fixed Godot 4.7.1 .NET executable.

```powershell
$env:GODOT_BIN = 'C:\Development\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe'
powershell -NoProfile -File .\scripts\Run-IndependentCopyPractice.ps1 -GodotExecutable $env:GODOT_BIN
```

The command creates a blank Godot .NET consumer outside the repository. It stages `addons/spatial_circuits` and the Core, Cells, Hierarchy, Workbench, Persistence, and Runner source projects under the temporary root's `src` directory. The temporary project references those staged projects, and its GdUnit phase runs against those staged files. This verifies staged-addon integration with explicit source dependencies. It does not verify a standalone binary package or a build with no repository source inputs.

Successful output includes these fields:

```text
[toolchain check] process code 0, timedOut=False, durationMs=<milliseconds>
[addon staging] process code 0, timedOut=False, durationMs=<milliseconds>
[independent-consumer managed build] process code 0, timedOut=False, durationMs=<milliseconds>
[Godot import] process code 0, timedOut=False, durationMs=<milliseconds>
[Godot managed-solution build] process code 0, timedOut=False, durationMs=<milliseconds>
[gdUnit4 Resource and Node adapter practice] process code 0, timedOut=False, durationMs=<milliseconds>
Staged-addon integration practice passed (staged source dependencies available).
Practice root: <system-temp>\spatial-wires-independent-consumer-<32 lowercase hexadecimal characters>
Manifest SHA-256: <64 lowercase hexadecimal characters>
Cleanup removed: <the same practice root>
```

The manifest hash covers the generated `spatial-circuits.manifest.json`. That manifest contains the addon path and ordered path and SHA-256 entries for the addon files. `DependencyFileCount` reports the staged project-source files under `src`, but the manifest does not hash that dependency tree. Repeating the command against unchanged addon sources produces the same manifest hash.

Cleanup is the rollback boundary. It may remove only the current run's direct child of the system temporary directory. The directory name must equal `spatial-wires-independent-consumer-<run identity>`. Its `.spatial-wires-practice.json` marker must contain the same 32-character run identity. Cleanup refuses paths outside the temporary boundary, unexpected names, identity mismatches, invalid markers, and filesystem links. It does not remove repository files or unrelated temporary siblings.
