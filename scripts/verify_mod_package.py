"""Verify that a Pelican 4Ever release ZIP contains files from the same build."""

import argparse
import json
from pathlib import Path
from zipfile import ZipFile


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("build_dir", type=Path)
    args = parser.parse_args()

    with ZipFile(args.package) as package:
        for name in ("manifest.json", "Pelican4Ever.dll", "Pelican4Ever.Core.dll"):
            entry = f"Pelican4Ever/{name}"
            if package.namelist().count(entry) != 1:
                raise SystemExit(f"Expected exactly one {entry} in {args.package}")
            if package.read(entry) != (args.build_dir / name).read_bytes():
                raise SystemExit(f"{entry} does not match the build output")

        manifest = json.loads(package.read("Pelican4Ever/manifest.json"))
        if args.package.name != f"Pelican4Ever {manifest['Version']}.zip":
            raise SystemExit("ZIP filename does not match the manifest version")
        if manifest.get("EntryDll") != "Pelican4Ever.dll":
            raise SystemExit("Manifest entry DLL does not match the packaged module")

    print(f"Verified {args.package.name}")


if __name__ == "__main__":
    main()
