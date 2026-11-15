#!/usr/bin/env python3
"""Verify immutable OptionPricing RC archives, exact dependencies, DLLs and portable PDBs."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import runpy
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IDS = tuple("Boutquin.OptionPricing." + name for name in (
    "Abstractions", "Domain", "Analytics", "Trees", "Calibration", "Exotics", "Examples.Shared"))

# .NET 11 supplies these exact 11.0.0 contracts in Microsoft.NETCore.App.
# NuGet privatizes direct prunable references, excluding them from the nuspec:
# https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/nu1510-pruned-references
# Keep this narrow: a newer package version or a first-party dependency must
# still be present and checked normally.
FRAMEWORK_PROVIDED = {
    "net11.0": {
        "Microsoft.Extensions.DependencyInjection.Abstractions": "11.0.0",
        "Microsoft.Extensions.Logging.Abstractions": "11.0.0",
    },
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def source_bytes(commit: str, path: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(ROOT), "show", f"{commit}:{path}"], timeout=120)


def expected_dependencies(commit: str, name: str, version: str, central: dict, locked: dict) -> dict:
    """Read the candidate's direct runtime graph; private build tools do not ship.

    OptionPricing project references share this candidate version. Qualified upstream
    dependencies retain their separately frozen versions.
    """
    project = ET.fromstring(source_bytes(commit, f"src/{name}/{name}.csproj"))
    expected = {}
    for reference in project.findall(".//ProjectReference"):
        dependency = Path(reference.get("Include").replace("\\", "/")).stem
        require(dependency in IDS, f"Unknown project dependency: {name} -> {dependency}")
        expected[dependency] = version
    for reference in project.findall(".//PackageReference"):
        private = reference.get("PrivateAssets", reference.findtext("PrivateAssets", ""))
        if private.lower() == "all":
            continue
        dependency = reference.get("Include")
        required = version if dependency in IDS else reference.get("VersionOverride", reference.get("Version", central.get(dependency)))
        require(required is not None and "$(" not in required, f"Unresolved dependency: {name} -> {dependency}")
        if dependency in locked:
            require(required.strip("[]") == locked[dependency], f"Source dependency lock mismatch: {name} -> {dependency}")
        expected[dependency] = required
    return expected


def verify(feed: Path, version: str, commit: str, manifest: Path, inspector: Path) -> dict:
    require(re.fullmatch(r"[0-9a-f]{40}", commit) is not None, "Expected a full source commit")
    require(re.fullmatch(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-rc\.[0-9]+(?:\.[0-9]+)*)?", version) is not None,
            "Expected an exact stable or numeric RC identity without build metadata")
    expected = {f"{name}.{version}.{extension}" for name in IDS for extension in ("nupkg", "snupkg")}
    paths = sorted([*feed.glob("*.nupkg"), *feed.glob("*.snupkg")])
    require({path.name for path in paths} == expected, "Expected exactly 7 packages and 7 symbol archives")
    hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}
    identity = {"version": version, "sourceCommit": commit, "sha256": hashes}
    if manifest.exists():
        require(json.loads(manifest.read_text(encoding="utf-8")) == identity, "Candidate bytes changed after freezing")
    else:
        with manifest.open("x", encoding="utf-8") as stream:
            json.dump(identity, stream, indent=2)
            stream.write("\n")
    readme = source_bytes(commit, "README.md")
    dependency_lock = json.loads(source_bytes(commit, "dependency-source.json"))
    dependency_versions = {item["id"]: item["version"] for item in dependency_lock.get("publishedPackages", [])}
    central_xml = ET.fromstring(source_bytes(commit, "Directory.Packages.props"))
    properties = {item.tag: item.text for group in central_xml.findall("PropertyGroup") for item in group}
    def central_versions(framework):
        versions = {}
        for group in central_xml.findall("ItemGroup"):
            condition = group.get("Condition", "")
            if condition:
                match = re.fullmatch(r"'\$\(TargetFramework\)'\s*==\s*'(net10\.0|net11\.0)'", condition)
                require(match is not None, "Unsupported central package condition: " + condition)
                if match.group(1) != framework:
                    continue
            for item in group.findall("PackageVersion"):
                dependency = item.get("Include") or item.get("Update")
                versions[dependency] = re.sub(r"\$\(([^)]+)\)", lambda m: properties[m.group(1)], item.get("Version"))
        return versions

    xml_check = runpy.run_path(str(ROOT / "scripts/check-package-docs.py"))["package_violations"]
    results = []
    with tempfile.TemporaryDirectory(prefix="optionpricing-package-inspect-") as temporary:
        scratch = Path(temporary)
        for name in IDS:
            package = feed / f"{name}.{version}.nupkg"
            symbols = feed / f"{name}.{version}.snupkg"
            require(not xml_check(package), f"Private or missing XML documentation: {name}")
            with zipfile.ZipFile(package) as archive, zipfile.ZipFile(symbols) as symbol_archive:
                require(len(archive.namelist()) == len(set(archive.namelist())), f"Duplicate archive entry: {name}")
                require(len(symbol_archive.namelist()) == len(set(symbol_archive.namelist())), f"Duplicate symbol entry: {name}")
                frameworks = ("net10.0", "net11.0")
                expected_dlls = {f"lib/{framework}/{name}.dll" for framework in frameworks}
                expected_pdbs = {f"lib/{framework}/{name}.pdb" for framework in frameworks}
                require({entry for entry in archive.namelist() if entry.endswith('.dll')} == expected_dlls,
                        f"Unexpected runtime DLL inventory: {name}")
                require({entry for entry in symbol_archive.namelist() if entry.endswith('.pdb')} == expected_pdbs,
                        f"Unexpected symbol inventory: {name}")
                nuspecs = [entry for entry in archive.namelist() if entry.endswith(".nuspec")]
                require(len(nuspecs) == 1, f"Expected one nuspec: {name}")
                metadata = ET.fromstring(archive.read(nuspecs[0])).find("{*}metadata")
                require(metadata is not None, f"Missing metadata: {name}")
                require(metadata.findtext("{*}id") == name and metadata.findtext("{*}version") == version,
                        f"Wrong nuspec identity: {name}")
                repository = metadata.find("{*}repository")
                require(repository is not None and repository.get("commit") == commit
                        and repository.get("url") == "https://github.com/boutquin/Boutquin.OptionPricing",
                        f"Wrong source identity: {name}")
                symbol_nuspecs = [entry for entry in symbol_archive.namelist() if entry.endswith(".nuspec")]
                require(len(symbol_nuspecs) == 1, f"Expected one symbol nuspec: {name}")
                symbol_metadata = ET.fromstring(symbol_archive.read(symbol_nuspecs[0])).find("{*}metadata")
                require(symbol_metadata is not None
                        and symbol_metadata.findtext("{*}id") == name
                        and symbol_metadata.findtext("{*}version") == version,
                        f"Wrong symbol archive identity: {name}")
                symbol_repository = symbol_metadata.find("{*}repository")
                require(symbol_repository is not None and symbol_repository.get("commit") == commit
                        and symbol_repository.get("url") == "https://github.com/boutquin/Boutquin.OptionPricing",
                        f"Wrong symbol source identity: {name}")
                license_element = metadata.find("{*}license")
                require(license_element is not None and license_element.get("type") == "expression"
                        and license_element.text == "Apache-2.0", f"Wrong license: {name}")
                require(archive.read(metadata.findtext("{*}readme")) == readme, f"README differs from source: {name}")
                groups = metadata.findall("./{*}dependencies/{*}group")
                require(len(groups) == 2 and {group.get("targetFramework") for group in groups} == set(frameworks),
                        f"Wrong dependency framework groups: {name}")
                require(len(metadata.findall(".//{*}dependency")) == sum(len(group.findall("{*}dependency")) for group in groups),
                        f"Unexpected dependency placement: {name}")
                for group in groups:
                    framework = group.get("targetFramework")
                    central = central_versions(framework)
                    entries = group.findall("{*}dependency")
                    dependencies = {item.get("id"): item.get("version") for item in entries}
                    require(len(dependencies) == len(entries), f"Duplicate dependency: {name} {framework}")
                    expected_dependencies_for_package = expected_dependencies(commit, name, version, central, dependency_versions)
                    extras = dependencies.keys() - expected_dependencies_for_package.keys()
                    pruned = {dep for dep, required in expected_dependencies_for_package.items()
                              if FRAMEWORK_PROVIDED.get(framework, {}).get(dep) == required.strip("[]")}
                    require(expected_dependencies_for_package.keys() - pruned <= dependencies.keys() and extras <= central.keys(),
                            f"Missing or unexpected dependencies: {name} {framework}")
                    for dep in extras:
                        pinned = central[dep].strip("[]")
                        allowed = (f"[{pinned}]",) if dep.startswith("Boutquin.") else (pinned, f"[{pinned}]", f"[{pinned}, )")
                        require(dependencies[dep] in allowed, f"Wrong transitive pin {name} {framework} -> {dep}: {dependencies[dep]}")
                    for dep, required in expected_dependencies_for_package.items():
                        if dep in pruned and dep not in dependencies:
                            continue
                        resolved = dependencies[dep]
                        allowed = (f"[{required.strip('[]')}]",) if dep.startswith('Boutquin.') else (required, f"[{required}]", f"[{required}, )")
                        require(resolved in allowed, f"Wrong dependency {name} {framework} -> {dep}: {resolved}")
                    target = scratch / framework
                    target.mkdir(exist_ok=True)
                    dll, pdb = target / f"{name}.dll", target / f"{name}.pdb"
                    dll.write_bytes(archive.read(f"lib/{framework}/{name}.dll"))
                    pdb.write_bytes(symbol_archive.read(f"lib/{framework}/{name}.pdb"))
                    inspected = subprocess.check_output(["dotnet", str(inspector), str(dll), str(pdb), version, commit], text=True, timeout=120)
                    assembly_identity = json.loads(inspected)
                    require(assembly_identity.get("name") == name, f"Wrong packaged assembly identity: {name} {framework}")
                    results.append({"id": name, "framework": framework, "dependencies": dependencies, "identity": assembly_identity,
                                    "dllSha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
                                    "pdbSha256": hashlib.sha256(pdb.read_bytes()).hexdigest()})
    final_paths = [*feed.glob("*.nupkg"), *feed.glob("*.snupkg")]
    require(hashes == {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in final_paths},
            "Candidate bytes changed during inspection")
    return {**identity, "packages": results, "status": "PASS"}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("feed", type=Path)
    parser.add_argument("version")
    parser.add_argument("commit")
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--inspector", type=Path,
                        default=ROOT / "scripts/package-inspector/bin/Release/net10.0/PackageInspector.dll")
    args = parser.parse_args()
    report = verify(args.feed, args.version, args.commit, args.manifest, args.inspector)
    with args.report.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)
        stream.write("\n")
    print("PASS: 10 immutable package identities, exact candidate dependency versions, DLL/PDB identity and SourceLink")


if __name__ == "__main__":
    main()
