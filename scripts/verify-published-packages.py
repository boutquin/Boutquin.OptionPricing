#!/usr/bin/env python3
"""Verify published packages from nuget.org alone, in an empty package folder.

Restores every package of the release into a fresh folder using a nuget.org-only configuration, loads each
assembly, and requires every payload entry to equal the qualified candidate (nuget.org adds only its
repository signature). A populated global NuGet cache can serve a different build of the same version,
so this never uses one.

Usage: verify-published-packages.py <version> <candidate packages dir> <work dir>
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
IDS = tuple('Boutquin.OptionPricing.' + suffix for suffix in (
    'Abstractions', 'Domain', 'Analytics', 'Trees', 'Calibration', 'Exotics', 'Examples.Shared'))
CONFIG = ('<?xml version="1.0" encoding="utf-8"?><configuration><fallbackPackageFolders><clear/></fallbackPackageFolders><packageSources><clear/>'
          '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')


def payload(path):
    with zipfile.ZipFile(path) as archive:
        return {i.filename: hashlib.sha256(archive.read(i)).hexdigest() for i in archive.infolist() if not i.is_dir()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('version')
    parser.add_argument('candidate', type=Path)
    parser.add_argument('work', type=Path)
    args = parser.parse_args()
    work = args.work.resolve()
    work.mkdir(parents=True, exist_ok=False)
    refs = ''.join(f'<PackageReference Include="{name}" Version="[{args.version}]"/>' for name in IDS)
    (work / 'nuget.config').write_text(CONFIG)
    (work / 'c.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                                   f'<TargetFrameworks>net10.0;net11.0</TargetFrameworks></PropertyGroup><ItemGroup>{refs}</ItemGroup></Project>')
    loads = ''.join(f'Console.WriteLine("{n} " + Assembly.Load("{n}").GetName().Version + " " + '
                    f'Assembly.Load("{n}").GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);\n'
                    for n in IDS)
    (work / 'P.cs').write_text('using System;\nusing System.Reflection;\n' + loads)
    packages = work / 'pk'
    restore = ['dotnet', 'restore', '--configfile', 'nuget.config', '--packages', str(packages), '--no-http-cache', '-p:RestorePackagesWithLockFile=true', '-p:_WorkloadLibraryPacksFolder=' + str(work / 'absent-sdk-feed')]
    subprocess.run(restore, cwd=work, check=True)
    subprocess.run(restore + ['--locked-mode'], cwd=work, check=True)
    for framework in ('net10.0', 'net11.0'):
        subprocess.run(['dotnet', 'run', '-c', 'Release', '--no-restore', '--framework', framework], cwd=work, check=True)
    bad = []
    for name in IDS:
        file = f'{name}.{args.version}.nupkg'
        candidate = payload(args.candidate / file)
        published_path = packages / name.lower() / args.version.lower() / file.lower()
        subprocess.run(['dotnet', 'nuget', 'verify', str(published_path), '--all', '--verbosity', 'detailed'], check=True)
        published = payload(published_path)
        with zipfile.ZipFile(published_path) as archive:
            signed = '.signature.p7s' in archive.namelist()
        ok = signed and candidate.keys() <= published.keys() and all(candidate[k] == published[k] for k in candidate) \
            and set(published) - set(candidate) <= {'.signature.p7s'}
        print(name, 'OK' if ok else 'BAD', len(candidate), 'entries')
        if not ok:
            bad.append(name)
    if bad:
        raise SystemExit('Published payload differs from the candidate: ' + ', '.join(bad))
    assets = json.loads((work / 'obj/project.assets.json').read_text())
    if {Path(p).resolve() for p in assets['packageFolders']} != {packages}:
        raise ValueError('Non-isolated package folder')
    copies = []
    for name in IDS:
        metadata = json.loads((packages / name.lower() / args.version.lower() / '.nupkg.metadata').read_text())
        if metadata['source'] != 'https://api.nuget.org/v3/index.json':
            raise ValueError('Non-public package source: ' + name)
        with zipfile.ZipFile(args.candidate / f'{name}.{args.version}.nupkg') as archive:
            for framework in ('net10.0', 'net11.0'):
                expected = archive.read(f'lib/{framework}/{name}.dll')
                actual = (work / 'bin/Release' / framework / (name + '.dll')).read_bytes()
                if actual != expected:
                    raise ValueError('Runtime DLL substitution: ' + name + ' ' + framework)
                copies.append(dict(package=name, framework=framework, sha256=hashlib.sha256(actual).hexdigest()))
    (work / 'receipt.json').write_text(json.dumps(dict(status='PASS', version=args.version, signedPayloadIdenticalPackages=list(IDS), runtimeCopies=copies, source='https://api.nuget.org/v3/index.json', coldAndLockedRestore=True), indent=2) + '\n')
    print(f'PASS: {len(IDS)} signed payload-identical packages and {len(copies)} actual runtime DLLs from nuget.org alone')


if __name__ == '__main__':
    main()
