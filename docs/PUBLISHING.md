# Publishing a release

This repository includes the C# source, compiled Skyfarer mod, and documentation.
The download does not bundle BepInEx, Jötunn, game assemblies, or development
profiles. The current DLL is the tested 0.1.7 build; packaging does not rebuild it.

See [BUILDING.md](BUILDING.md) to compile from source. Source files and build
tools stay in GitHub; the install ZIP contains only the six entries below.

## Build the download ZIP

From the repository root, run Python 3:

```sh
python3 scripts/package.py
```

This creates `dist/SurtlingSkyfarer-0.1.7.zip` and `dist/SHA256SUMS.txt`.
The script checks the manifest, icon dimensions, exact DLL checksum, ZIP
contents, and build timestamp. It uses an explicit list of files:

```text
manifest.json
README.md
CHANGELOG.md
icon.png
BepInEx/plugins/Skyfarer/Wise.Skyfarer.dll
BepInEx/plugins/Skyfarer/Skyfarer.cfg
```

The ZIP is suitable for manual installation, local mod-manager import, and
Thunderstore upload. See Thunderstore's
[package requirements](https://wiki.thunderstore.io/mods/creating-a-package)
and [BepInEx folder rules](https://wiki.thunderstore.io/mods/packaging-your-mods).

## GitHub Releases

Create a release for tag `v0.1.7` and attach:

- `dist/SurtlingSkyfarer-0.1.7.zip`
- `BepInEx/plugins/Skyfarer/Wise.Skyfarer.dll`
- `BepInEx/plugins/Skyfarer/Skyfarer.cfg`
- `dist/SHA256SUMS.txt`

The standalone DLL supports existing installations; the ZIP includes the
installation guide and Thunderstore metadata. GitHub's automatically generated
source archives contain the repository, rather than just the install package.
Use the named release ZIP for installation.

## Thunderstore

1. Sign in to Thunderstore and select **wisedev**, the team that owns the
   existing SurtlingSkyfarer listing. This uploads an update to that listing.
2. Open [Upload package](https://thunderstore.io/package/create).
3. Upload `dist/SurtlingSkyfarer-0.1.7.zip`.
4. Select the **Valheim** community and appropriate categories available there.
5. Preview the README, review the metadata, and submit.

The manifest already declares these dependency strings:

```text
denikson-BepInExPack_Valheim-5.4.2351
ValheimModding-Jotunn-2.30.2
```

Thunderstore's team selection supplies the package namespace. The existing listing is
https://thunderstore.io/c/valheim/p/wisedev/SurtlingSkyfarer/. The GitHub release does not automatically
publish to Thunderstore; no upload token is stored in this repository.

## Future updates

Use a new version for each new DLL. Update the manifest, README, changelog, and
`release.json` together. Record the actual build's SHA256, UTC modification
time, and completed validation in `release.json`. Preserve the DLL build time
when making ZIPs: BepInEx uses modification times when caching plugin metadata.
Never replace a published package version with a different DLL.
