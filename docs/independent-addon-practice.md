# Independent addon practice

Run this check from the repository root. It requires the fixed Godot 4.7.1 .NET executable.

```powershell
$env:GODOT_BIN = 'C:\Development\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe'
powershell -NoProfile -File .\scripts\Run-IndependentCopyPractice.ps1 -GodotExecutable $env:GODOT_BIN
```

The command creates a blank Godot .NET consumer outside the repository. It stages only `addons/spatial_circuits` as product code. It copies gdUnit4 separately as test infrastructure. The gdUnit4 C# test imports, builds, and instantiates the addon's public `Node` and `Resource` types.

Successful output includes these fields:

```text
[toolchain check] process code 0, timedOut=False, durationMs=<milliseconds>
[addon staging] process code 0, timedOut=False, durationMs=<milliseconds>
[independent-consumer managed build] process code 0, timedOut=False, durationMs=<milliseconds>
[Godot import] process code 0, timedOut=False, durationMs=<milliseconds>
[Godot managed-solution build] process code 0, timedOut=False, durationMs=<milliseconds>
[gdUnit4 public Node and Resource tests] process code 0, timedOut=False, durationMs=<milliseconds>
Independent-copy gdUnit4 practice passed.
Practice root: <system-temp>\spatial-wires-independent-consumer-<32 lowercase hexadecimal characters>
Manifest SHA-256: <64 lowercase hexadecimal characters>
Cleanup removed: <the same practice root>
```

The manifest hash covers the generated `spatial-circuits.manifest.json`. That manifest contains the portable addon path and the ordered path and SHA-256 entry for every staged product file. Repeating the command against unchanged product sources produces the same manifest hash. A product path or file-content change changes the manifest or its hash.

Cleanup is the rollback boundary. It may remove only the current run's direct child of the system temporary directory. The directory name must equal `spatial-wires-independent-consumer-<run identity>`. Its `.spatial-wires-practice.json` marker must contain the same 32-character run identity. Cleanup refuses paths outside the temporary boundary, unexpected names, identity mismatches, invalid markers, and filesystem links. It does not remove repository files or unrelated temporary siblings.
