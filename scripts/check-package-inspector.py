#!/usr/bin/env python3
"""Execute the real DLL/PDB inspector against valid and corrupted disposable builds."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
COMMIT = "a" * 40
VERSION = "2.0.0-rc.control"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    inspector = ROOT / "scripts/package-inspector/bin/Release/net10.0/PackageInspector.dll"
    subprocess.run(["dotnet", "build", str(ROOT / "scripts/package-inspector/PackageInspector.csproj"), "-c", "Release"], check=True)
    env = dict(os.environ, NUGET_PACKAGES=str(output / "cache"), NUGET_HTTP_CACHE_PATH=str(output / "http"))
    for variant in ("valid", "other"):
        directory = output / variant
        directory.mkdir()
        (directory / "Directory.Build.props").write_text("<Project />", encoding="utf-8")
        (directory / "Directory.Packages.props").write_text("<Project />", encoding="utf-8")
        (directory / "nuget.config").write_text("<configuration><packageSources><clear/></packageSources></configuration>", encoding="utf-8")
        (directory / "Fixture.cs").write_text("// RC_EMBEDDED_SOURCE_SENTINEL\npublic class Fixture { public int Value => " + ("1" if variant == "valid" else "2") + "; }\n", encoding="utf-8")
        link = {"documents": {str(directory) + "/*": "https://raw.githubusercontent.com/boutquin/Boutquin.OptionPricing.Dev/" + COMMIT + "/*"}}
        (directory / "source-link.json").write_text(json.dumps(link), encoding="utf-8")
        project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
        properties = ET.SubElement(project, "PropertyGroup")
        for name, value in (("TargetFramework", "net10.0"), ("AssemblyVersion", "2.0.0.0"),
                            ("FileVersion", "2.0.0.0"), ("InformationalVersion", VERSION + "+" + COMMIT),
                            ("DebugType", "portable"), ("EmbedAllSources", "true"),
                            ("IncludeSourceRevisionInInformationalVersion", "false"), ("EnableSourceLink", "false"),
                            ("SourceLink", str(directory / "source-link.json")), ("IsPackable", "false")):
            ET.SubElement(properties, name).text = value
        ET.ElementTree(project).write(directory / "Fixture.csproj", encoding="unicode")
        with (directory / "build.log").open("x", encoding="utf-8") as log:
            subprocess.run(["dotnet", "build", "Fixture.csproj", "-c", "Release", "--disable-build-servers", "-p:UseSharedCompilation=false", "--configfile", "nuget.config"], cwd=directory, env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=300)
    dll = output / "valid/bin/Release/net10.0/Fixture.dll"
    pdb = output / "valid/bin/Release/net10.0/Fixture.pdb"
    controls = []

    def inspect(label, dll_path, pdb_path, version=VERSION, should_pass=False):
        result = subprocess.run(["dotnet", str(inspector), str(dll_path), str(pdb_path), version, COMMIT], capture_output=True, text=True, timeout=60)
        (output / (label + ".log")).write_text(result.stdout + result.stderr, encoding="utf-8")
        if (result.returncode == 0) != should_pass:
            raise ValueError("Inspector control has incorrect disposition: " + label)
        if should_pass and json.loads(result.stdout)["embeddedSourceCount"] < 1:
            raise ValueError("Fixture did not exercise embedded-source validation")
        controls.append({"control": label, "exitCode": result.returncode, "expected": "accept" if should_pass else "reject"})

    inspect("valid", dll, pdb, should_pass=True)
    inspect("wrong-informational-version", dll, pdb, version="2.0.0-rc.wrong")
    inspect("mismatched-pdb", dll, output / "other/bin/Release/net10.0/Fixture.pdb")
    original = pdb.read_bytes()
    for label, before, after in (("wrong-private-source", b"OptionPricing.Dev", b"OptionPricing.Bad"),
                                 ("wrong-source-commit", COMMIT.encode(), b"b" * 40),
                                 ("corrupt-embedded-source", b"RC_EMBEDDED_SOURCE_SENTINEL", b"RC_EMBEDDED_SOURCE_CORRUPT!")):
        if before not in original or len(before) != len(after):
            raise ValueError("Fixture mutation cannot execute: " + label)
        mutated = output / (label + ".pdb")
        mutated.write_bytes(original.replace(before, after))
        inspect(label, dll, mutated)
    (output / "report.json").write_text(json.dumps({"status": "PASS", "controls": controls}, indent=2) + "\n", encoding="utf-8")
    print("PASS: real inspector accepted valid metadata and rejected five DLL/PDB/source/embedded-source corruptions")


if __name__ == "__main__":
    main()
