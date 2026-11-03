# Building from source

## Prerequisites

- .NET SDK 8; the tested release was built with **8.0.425**.
- An installed copy of Valheim. This code targets **l-1.0.21**, with Unity
  **6000.0.75f1**; game updates can change the referenced APIs.
- BepInExPack Valheim **5.4.2351** and Jötunn **2.30.2** in a local mod profile.
- Python 3 if you want to recreate the release ZIP.

The plugin targets **.NET Framework 4.8**. Compilation references the game and
mod-profile assemblies locally. Those assemblies are not copied into the output
or distributed by this repository. NuGet may restore framework reference
assemblies on the first build.

## Compile

From the repository root, provide paths to the installed game and BepInEx
profile. A mod-manager profile's BepInEx folder works too.

Linux example:

```sh
dotnet build Skyfarer.csproj -c Release \
  -p:ValheimPath="$HOME/.local/share/Steam/steamapps/common/Valheim" \
  -p:BepPath="/path/to/your/BepInEx"
```

Windows PowerShell example:

```powershell
dotnet build Skyfarer.csproj -c Release `
  -p:ValheimPath="C:\Program Files (x86)\Steam\steamapps\common\Valheim" `
  -p:BepPath="C:\path\to\your\BepInEx"
```

Alternatively, set the `VALHEIM_PATH` and `BEPINEX_PATH` environment variables.
The referenced layout is:

```text
Valheim/
  valheim_Data/Managed/assembly_valheim.dll
  valheim_Data/Managed/UnityEngine*.dll
  ...other installed game assemblies...
BepInEx/
  core/BepInEx.dll
  core/0Harmony.dll
  plugins/Jotunn/Jotunn.dll
```

Output: `bin/Release/net48/Wise.Skyfarer.dll`.

Close the game before replacing the DLL in a test profile. A source build does
not overwrite the checked-in release DLL or the user's installed mod. Different
SDKs, paths, or source changes can produce a different binary hash; the published
0.1.8 download is pinned by `release.json` to its tested binary.

## Implementation

- `AircraftFactory` assembles the aircraft from runtime Valheim prefabs and
  procedural geometry, registers its recipe, and creates five seats.
- `Aircraft`, `SkySeat`, and `Patches` handle flight, input, authenticated seats,
  aircraft-specific ownership behavior, and damage immunity.
- `SkyfarerSettings` loads the adjacent text config, validates recipes, and mirrors
  settings into Jötunn-managed entries for server synchronization.
- `LandingGear` provides the four WheelColliders, steering, and wheel visuals.
- `AircraftUtilities` handles terrain suspension, VTOL, and collision-checked recovery.
- `AircraftServices`, `CargoUpgrades`, and `ServiceAccess` provide the service menu,
  persistent upgrades, native mist clearing, and requester-specific ward checks.
- `CargoHold` and `CargoLinks` add a separately owned chest with a saved GUID
  link that survives Valheim's object-ID remapping on world load.

## Validation

Version 0.1.8 passed 193 real-game regression checks, 45 feature checks,
25 separate-process reload checks and 6 dedicated-server checks on Linux
Valheim l-1.0.21. The feature checks cover paid cargo expansion, last-row
inventory persistence, ward permissions, native mist clearing, grounded VTOL,
water reverse, blocked recovery destinations and repeated area unloading.
The previous damage, health, flight, ownership, hammer, recipe and speed checks
remain covered. A game-rendered preview was visually inspected.
See `release.json` for the exact shipped DLL hash and validation counts.
Ownership fixtures simulate peer IDs inside one client; no new multi-client
flight or Windows runtime test was performed for this update.

`RuntimeSmoke` and the `*Smoke` fixtures are opt-in developer code. To run them,
launch a separate BepInEx test profile with the mod installed and set:

```text
SKYFARER_SMOKE=1
SKYFARER_TEST_SAVES=/absolute/path/to/disposable/test-saves
SKYFARER_TEST_OUTPUT=/absolute/path/to/results.txt
```

Create the output directory first. Use the game's `-batchmode -nographics -noaudio`
flags for headless tests, with a separate profile, cache, logs, and save folder.
Automated tests mute their isolated audio preferences; keep test audio disabled.
The harness creates a test character/world and exits the game after completion.
Never point it at normal player saves. For the reload check, launch a second
process using the same isolated saves and add `SKYFARER_CONTROL_RELOAD=1`.
To also check loading edited settings, change the test profile's adjacent
`Skyfarer.cfg` to `TopSpeed = 31` and the test recipe specified in
`RuntimeSmoke`, then set
`SKYFARER_SETTINGS_RELOAD=1` for the reload run.
For the durability reload check, save through the current full suite (which
leaves the plane at half health), then use a test config with
`DurabilityEnabled = true`, `MaxHealth = 3600`, and set
`SKYFARER_DURABILITY_RELOAD=1` along with `SKYFARER_CONTROL_RELOAD=1`.
All tests should finish with `SKYFARER_SMOKE_PASS` and no `FAIL` lines.

For the upgrades, VTOL, water reverse, recovery and area-streaming suite, use
`SKYFARER_FEATURES=1` with a separate test directory. Then run it again with
`SKYFARER_CONTROL_RELOAD=1` to verify the saved upgrades and last-row cargo.

Normal launches do not set these variables and do not run the test harness.
