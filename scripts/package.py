#!/usr/bin/env python3
"""Package the checked-in mod for GitHub Releases and Thunderstore (Python 3)."""
from datetime import datetime
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo
import hashlib
import json
import re
import struct
import time

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "dist"
DLL = "BepInEx/plugins/Skyfarer/Wise.Skyfarer.dll"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main():
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    release = json.loads((ROOT / "release.json").read_text(encoding="utf-8"))
    version = manifest["version_number"]
    assert re.fullmatch(r"[A-Za-z0-9_]{1,128}", manifest["name"])
    assert re.fullmatch(r"\d+\.\d+\.\d+", version)
    assert 0 < len(manifest["description"]) <= 250
    assert manifest["website_url"] == "https://github.com/alsocat/SurtlingSkyfarer"
    assert manifest["dependencies"] == [
        "denikson-BepInExPack_Valheim-5.4.2351",
        "ValheimModding-Jotunn-2.30.2",
    ]
    assert release["version"] == version
    # Explicit allowlist: no dependency binaries, game files, saves, or logs.
    names = ["README.md", "CHANGELOG.md", "manifest.json", "icon.png", DLL]
    files = {name: (ROOT / name).read_bytes() for name in names}
    assert sha256(files[DLL]) == release["dll_sha256"], "DLL differs from the recorded tested build"
    png = files["icon.png"]
    assert png[:8] == b"\x89PNG\r\n\x1a\n" and png[12:16] == b"IHDR"
    assert struct.unpack(">II", png[16:24]) == (256, 256), "Icon must be 256x256"
    for name in ("README.md", "CHANGELOG.md", "manifest.json"):
        text = files[name].decode("utf-8")
        assert not re.search(r"/(?:home|Users)/[^/\s]+/", text), "Personal path in package docs"
    # Record the build time separately because a fresh git checkout resets mtimes.
    build_time = datetime.fromisoformat(release["dll_build_time_utc"]).timestamp()
    package_time = time.time()
    OUT.mkdir(exist_ok=True)
    archive = OUT / f"SurtlingSkyfarer-{version}.zip"
    with ZipFile(archive, "w", ZIP_DEFLATED, compresslevel=9) as z:
        for name, data in files.items():
            stamp = build_time if name == DLL else package_time
            info = ZipInfo(name, time.localtime(stamp)[:6])
            info.extra = struct.pack("<HHBI", 0x5455, 5, 1, int(stamp))
            info.compress_type = ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            z.writestr(info, data)
    with ZipFile(archive) as z:
        assert z.testzip() is None
        assert set(z.namelist()) == set(names)
        for name, data in files.items():
            assert z.read(name) == data
        info = z.getinfo(DLL)
        expected = list(time.localtime(build_time)[:6])
        expected[5] -= expected[5] % 2
        assert info.date_time == tuple(expected)
        assert info.extra == struct.pack("<HHBI", 0x5455, 5, 1, int(build_time))
    (OUT / "SHA256SUMS.txt").write_text(
        f"{sha256(archive.read_bytes())}  {archive.name}\n"
        f"{release['dll_sha256']}  Wise.Skyfarer.dll\n", encoding="utf-8"
    )
    print(f"PASS: {archive.name} ({archive.stat().st_size:,} bytes)")
    print("PASS: manifest, 256x256 PNG, UTF-8 docs, archive contents and checksums")
    print("PASS: exact tested DLL and original build timestamp preserved")
    print(f"DLL SHA256: {release['dll_sha256']}")


if __name__ == "__main__":
    main()
