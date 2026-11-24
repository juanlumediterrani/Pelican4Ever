#!/usr/bin/env python3
"""Decompile Stardew Valley, SMAPI, or Lidgren C# types using local NuGet ICSharpCode.Decompiler."""
from __future__ import annotations

import argparse
import re
import subprocess
import tempfile
from pathlib import Path

DEFAULT_GAME_DIR = Path("/home/mtx/.steam/debian-installation/steamapps/common/Stardew Valley")
DECOMPILER_VER = "8.2.0.7535"

ASSEMBLY_MAP = {
    "stardew": "Stardew Valley.dll",
    "lidgren": "Lidgren.Network.dll",
    "smapi": "StardewModdingAPI.dll",
    "xtile": "xTile.dll",
}

CSPROJ_TEMPLATE = f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ICSharpCode.Decompiler" Version="{DECOMPILER_VER}" />
  </ItemGroup>
</Project>
"""

PROGRAM_CS = """using System;
using System.IO;
using System.Linq;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

string gameDir = args[0];
string dllPath = args[1];
string mode = args[2];
string pattern = args[3];

var resolver = new UniversalAssemblyResolver(dllPath, false, ".NETCoreApp,Version=v6.0");
resolver.AddSearchDirectory(gameDir);
string smapiInternal = Path.Combine(gameDir, "smapi-internal");
if (Directory.Exists(smapiInternal))
    resolver.AddSearchDirectory(smapiInternal);

var pe = new PEFile(dllPath);
var decompiler = new CSharpDecompiler(pe, resolver, new DecompilerSettings());

if (mode == "list")
{
    foreach (var t in decompiler.TypeSystem.MainModule.TypeDefinitions)
    {
        if (string.IsNullOrEmpty(pattern) || t.FullName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            Console.WriteLine(t.FullName);
    }
    return 0;
}

foreach (string typeName in args.Skip(3))
{
    Console.WriteLine($"// === {typeName} ===");
    Console.WriteLine(decompiler.DecompileTypeAsString(new FullTypeName(typeName)));
}
return 0;
"""


def main() -> int:
    ap = argparse.ArgumentParser(description="Offline C# decompiler for Stardew Valley / SMAPI / Lidgren assemblies.")
    ap.add_argument(
        "--asm",
        choices=[*ASSEMBLY_MAP.keys()],
        default="stardew",
        help="Target assembly alias (default: stardew)",
    )
    ap.add_argument("--dll", type=Path, help="Explicit path to any .dll (overrides --asm)")
    ap.add_argument("--game-dir", type=Path, default=DEFAULT_GAME_DIR)
    ap.add_argument("--list", metavar="SUBSTR", nargs="?", const="", help="List type names matching substring instead of decompiling")
    ap.add_argument("--grep", metavar="REGEX", help="Filter decompiled output lines with regex (-C 2 context)")
    ap.add_argument("types", nargs="*", help="Full type names, e.g. StardewValley.Network.LidgrenClient")

    args = ap.parse_args()
    dll_path = args.dll or (args.game_dir / ASSEMBLY_MAP[args.asm])
    if not dll_path.is_file():
        raise SystemExit(f"Assembly not found: {dll_path}")

    if args.list is None and not args.types:
        ap.error("Provide at least one full type name or --list")

    with tempfile.TemporaryDirectory(prefix="p4e-decomp-") as tmp:
        tmp_dir = Path(tmp)
        (tmp_dir / "Decomp.csproj").write_text(CSPROJ_TEMPLATE, encoding="utf-8")
        (tmp_dir / "Program.cs").write_text(PROGRAM_CS, encoding="utf-8")

        cmd = ["dotnet", "run", "--project", str(tmp_dir), "-v", "q", "--", str(args.game_dir), str(dll_path)]
        if args.list is not None:
            cmd.extend(["list", args.list])
        else:
            cmd.extend(["decompile", *args.types])

        out = subprocess.check_output(cmd, text=True)

    if args.grep:
        rx = re.compile(args.grep)
        lines = out.splitlines()
        hits: set[int] = set()
        for idx, line in enumerate(lines):
            if rx.search(line):
                for j in range(max(0, idx - 2), min(len(lines), idx + 3)):
                    hits.add(j)
        for idx in sorted(hits):
            print(f"{idx + 1}: {lines[idx]}")
    else:
        print(out, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
