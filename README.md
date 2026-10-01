# Surtling Skyfarer

A wooden, Surtling-powered airplane for Valheim. Bring **one pilot and four
passengers**, roll down a runway on wagon wheels, and carry supplies in a
ten-slot cargo chest.

![Surtling Skyfarer icon](https://raw.githubusercontent.com/alsocat/SurtlingSkyfarer/main/icon.png)

**Current version: 0.1.5** · [Download](https://github.com/alsocat/SurtlingSkyfarer/releases/latest)
· [Report a bug](https://github.com/alsocat/SurtlingSkyfarer/issues)
· [View the in-game aircraft](https://raw.githubusercontent.com/alsocat/SurtlingSkyfarer/main/media/skyfarer.png)

## Features

- Wooden boat hull, cloth wings, glowing Surtling core, and a carved propeller.
- Five seats: one pilot and four passengers.
- Four wagon wheels with suspension, steering, brakes, and ground reverse.
- Full loops, barrel rolls, and rudder control.
- Throttle stays set while someone remains seated or standing on deck. When
  everyone leaves, power cuts out and the plane slows and descends.
- A normal wooden chest's **10 storage slots**. Cargo saves with the plane and
  drops when you deliberately dismantle it.
- An **indestructible** hull and unlimited core power. Collisions still apply;
  hammer dismantling remains available. The hull floats for water landings.

## Requirements

Install these on **every player's computer and on the server**:

| Dependency | Tested version | Download and setup |
| --- | --- | --- |
| BepInExPack Valheim | **5.4.2351** | [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) |
| Jötunn, the Valheim Library | **2.30.2** | [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) |
| Surtling Skyfarer | **0.1.5** | [GitHub releases](https://github.com/alsocat/SurtlingSkyfarer/releases/latest) |

Use the same Skyfarer version on all clients and the server. This release was
tested against **Valheim l-1.0.16 / Unity 6000.0.75f1**.

The Skyfarer download contains the mod itself. Install BepInEx and Jötunn
separately; their files are not bundled. The package manifest declares both
dependencies for Thunderstore-compatible mod managers.

## Installation

### Manual installation

1. Close Valheim, or stop the dedicated server.
2. Install **BepInExPack Valheim** using its instructions for your operating
   system. Launch once through BepInEx so its folders are created, then close it.
3. Install **Jötunn** into the active `BepInEx/plugins` folder.
4. Download `SurtlingSkyfarer-0.1.5.zip` from
   [Releases](https://github.com/alsocat/SurtlingSkyfarer/releases/latest).
5. Extract its `BepInEx` folder into the game or server folder. The mod DLL
   should end up at:

   ```text
   BepInEx/plugins/Skyfarer/Wise.Skyfarer.dll
   ```

6. Launch through your normal BepInEx-enabled method. On Linux, follow the
   BepInEx pack's game or dedicated-server launcher instructions.

If you download the standalone DLL instead, put it in the same folder above.
Keep **one copy** of `Wise.Skyfarer.dll` in your active plugin tree.

### Mod-manager profiles

Install the two dependencies in your Valheim profile, then import the release
ZIP using the manager's local-mod import option. If local import is unavailable,
open the profile folder and copy the DLL into its `BepInEx/plugins/Skyfarer`
folder. Start the game through that modded profile.

### Dedicated servers and AMP

The server needs all three components too. Stop it, install or enable its
BepInEx setup, and upload Jötunn and Skyfarer into its active
`BepInEx/plugins` folder. In AMP, use the instance's File Manager and restart
through its BepInEx-enabled launch method. The same Skyfarer DLL is used by
Windows and Linux clients and servers.

### Updating an existing installation

Stop the server and close each player's game. Replace the existing
`Wise.Skyfarer.dll`, then restart everyone on the same version. Updating from
0.1.2, 0.1.3, or 0.1.4 to 0.1.5 does not require reinstalling BepInEx or Jötunn
when the tested versions above are already installed. Existing aircraft gain
the cargo chest when loaded.

## Build the plane

Equip a **hammer**, stand near a **workbench**, and open **Transport**. Look for
**Surtling Skyfarer**, or type `Skyfarer` into the hammer menu's search field.
Your character must discover the recipe materials first.

| Material | Amount |
| --- | ---: |
| Fine Wood | 80 |
| Copper | 25 |
| Deer Hide | 20 |
| Surtling Core | 8 |

## Controls

| Input | Action |
| --- | --- |
| E on the front chair | Sit in the pilot seat |
| E on a rear chair | Ride as a passenger |
| Hold Shift / Ctrl | Raise / lower throttle |
| W / S | Pitch nose down / nose up |
| A / D | Roll in flight; steer wheels on land |
| Q / E while piloting | Rudder; also steers wheels on land |
| Hold R | Reverse on the ground, up to 4 m/s |
| Hold right mouse button | Wheel brakes |
| Space | Leave the seat |
| E on the cargo chest | Open storage |

**Space is the only manual pilot exit.** E becomes rudder while piloting;
attacking, dodging, and crouching do not eject the pilot.

- **Take off:** line up on open ground, increase throttle with Shift, then
  pull up with S as speed builds.
- **Cruise:** release Shift/Ctrl to hold throttle. Release the flight controls
  to hold the current attitude; there is no automatic leveling.
- **Acrobatics:** keep holding W/S for continuous loops, or A/D for rolls.
- **Land:** level the wings, lower the nose with W, reduce throttle with Ctrl,
  then brake with right mouse after touching down.
- **Reverse:** hold R on the ground. Releasing R resumes the selected throttle;
  brakes override reverse. R has no effect on thrust in the air.
- **Park:** lower throttle to zero. An empty aircraft cuts power automatically,
  and boarding it again leaves the engine off until throttle is raised.

These controls assume default game bindings. Throttle, pitch, roll, brakes,
and dismount use their corresponding game actions; Q/E and R are dedicated keys.

## Changes in 0.1.5

- The pilot owns and simulates the aircraft; vanilla boat ownership checks no
  longer transfer it to seated passengers.
- Seated heartbeats tolerate lagged player positions, while boarding distance
  and disconnect cleanup remain enforced.
- Empty-aircraft engine cutoff, ten-slot cargo, and the 25-Copper recipe.
- Cargo remains linked to the plane across full world reloads.
- Release archives preserve the DLL's actual build timestamp so BepInEx can
  detect the update correctly.

See the [changelog](https://github.com/alsocat/SurtlingSkyfarer/blob/main/CHANGELOG.md)
for earlier versions.

## Testing and support

0.1.5 passed **143 gameplay checks**, **14 separate-process save/reload checks**,
and **6 dedicated-server startup/shutdown checks** on native Linux. The tested
DLL is the one distributed here. Windows runtime and a flight with five actual
clients have not been verified in those tests. This is an early release, and
broader mod compatibility still needs testing.

If the plane is missing, check the hammer's Transport category, discovered
materials, and `BepInEx/LogOutput.log`. A successful load includes:

```text
Loading [Surtling Skyfarer 0.1.5]
Skyfarer registered: 1 pilot, 4 passenger seats, 13.3 m wingspan.
```

For connection problems, compare Valheim and mod versions on every client and
the server, and check for duplicate DLLs. Report bugs through
[GitHub Issues](https://github.com/alsocat/SurtlingSkyfarer/issues) with versions,
steps to reproduce, and relevant log excerpts. Remove server passwords and
other personal information before posting logs.

## Source and building

The C# source is in [`src/`](https://github.com/alsocat/SurtlingSkyfarer/tree/main/src).
See [Building from source](https://github.com/alsocat/SurtlingSkyfarer/blob/main/docs/BUILDING.md)
for the .NET SDK and local game references, and
[Publishing](https://github.com/alsocat/SurtlingSkyfarer/blob/main/docs/PUBLISHING.md)
for recreating the GitHub/Thunderstore package.

Unofficial community mod, unaffiliated with Iron Gate. Each player needs their
own installed copy of Valheim. Game assemblies, extracted game assets,
dependency binaries, and development saves are not included in the mod package.
