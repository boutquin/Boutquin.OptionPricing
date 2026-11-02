"""Executing corruption controls for the package and cold consumer gates."""
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile


SCRIPTS = Path(__file__).resolve().parents[1]


def module(name):
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), SCRIPTS / (name + '.py'))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


VERIFY = module('verify-rc-packages')
CONSUMER = module('qualify-rc-consumer')
VERSION = '2.0.0-rc.20261027.1'
COMMIT = 'a' * 40


class PackageCorruptionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.feed = Path(self.temporary.name)
        self.sources = {'README.md': b'Committed readme', 'dependency-source.json': b'{"packages":[]}',
                        'Directory.Packages.props': b'<Project><ItemGroup><PackageVersion Include="Boutquin.Domain" Version="[1.0.0]"/></ItemGroup></Project>'}
        for name in VERIFY.IDS:
            dependencies = '' if name.endswith('.Abstractions') else '<ProjectReference Include="../Boutquin.OptionPricing.Abstractions/Boutquin.OptionPricing.Abstractions.csproj"/>'
            dependencies += '<PackageReference Include="Boutquin.Domain"/>'
            self.sources[f'src/{name}/{name}.csproj'] = f'<Project><ItemGroup>{dependencies}</ItemGroup></Project>'.encode()
            versions = '' if name.endswith('.Abstractions') else f'<dependency id="Boutquin.OptionPricing.Abstractions" version="[{VERSION}]"/>'
            versions += '<dependency id="Boutquin.Domain" version="[1.0.0]"/>'
            nuspec = f'<package><metadata><id>{name}</id><version>{VERSION}</version><repository url="https://github.com/boutquin/Boutquin.OptionPricing" commit="{COMMIT}"/><license type="expression">Apache-2.0</license><readme>README.md</readme><dependencies><group targetFramework="net10.0">{versions}</group></dependencies></metadata></package>'
            with zipfile.ZipFile(self.feed / f'{name}.{VERSION}.nupkg', 'w') as archive:
                archive.writestr(name + '.nuspec', nuspec)
                archive.writestr('README.md', self.sources['README.md'])
                archive.writestr(f'lib/net10.0/{name}.dll', b'packaged DLL')
                archive.writestr(f'lib/net10.0/{name}.xml', f'<doc><assembly><name>{name}</name></assembly><members><member name="T:Fixture"><summary>Returns an observation.</summary></member></members></doc>')
            with zipfile.ZipFile(self.feed / f'{name}.{VERSION}.snupkg', 'w') as archive:
                archive.writestr(name + '.nuspec', nuspec)
                archive.writestr(f'lib/net10.0/{name}.pdb', b'portable PDB')
        self.addCleanup(patch.stopall)
        patch.object(VERIFY, 'source_bytes', side_effect=lambda commit, path: self.sources[path]).start()
        patch.object(VERIFY.subprocess, 'check_output', side_effect=lambda args, **kwargs: json.dumps({'name': Path(args[2]).stem, 'exportedTypes': 0})).start()

    def verify(self):
        return VERIFY.verify(self.feed, VERSION, COMMIT, self.feed / 'hashes.json', Path('inspector.dll'))

    def mutate(self, member, transform, symbols=False):
        name = VERIFY.IDS[1]
        path = self.feed / f'{name}.{VERSION}.{"snupkg" if symbols else "nupkg"}'
        with zipfile.ZipFile(path) as archive:
            entries = [(entry, archive.read(entry)) for entry in archive.namelist()]
        with zipfile.ZipFile(path, 'w') as archive:
            for entry, content in entries:
                archive.writestr(entry, transform(content) if entry.endswith(member) else content)

    def test_valid_candidate_freezes_and_rechecks_same_identity(self):
        self.assertEqual('PASS', self.verify()['status'])
        self.assertEqual(14, len(self.verify()['sha256']))

    def test_frozen_bytes_cannot_be_replaced(self):
        self.verify()
        self.mutate('README.md', lambda payload: payload + b' changed')
        with self.assertRaisesRegex(ValueError, 'changed after freezing'):
            self.verify()

    def test_missing_archive_is_rejected(self):
        next(self.feed.glob('*.snupkg')).unlink()
        with self.assertRaisesRegex(ValueError, 'exactly 7'):
            self.verify()

    def test_corrupt_metadata_and_ranges_are_rejected(self):
        mutations = [
            (b'Apache-2.0', b'MIT', 'Wrong license'),
            (b'net10.0', b'net9.0', 'framework'),
            (COMMIT.encode(), b'b' * 40, 'source identity'),
            (f'[{VERSION}]'.encode(), f'[{VERSION}, )'.encode(), 'Wrong dependency'),
            (b'[1.0.0]', b'1.0.0', 'Wrong dependency'),
            (b'Boutquin.Domain', b'Unknown.Domain', 'unexpected dependencies'),
            (b'<id>Boutquin.OptionPricing.Domain</id>', b'<id>Unknown</id>', 'nuspec identity'),
        ]
        for old, new, message in mutations:
            with self.subTest(message=message):
                # Each mutation starts with no frozen identity and is restored afterwards.
                self.mutate('.nuspec', lambda payload: payload.replace(old, new))
                with self.assertRaisesRegex(ValueError, message):
                    self.verify()
                (self.feed / 'hashes.json').unlink(missing_ok=True)
                self.mutate('.nuspec', lambda payload: payload.replace(new, old))

    def test_symbol_source_mismatch_is_rejected(self):
        self.mutate('.nuspec', lambda data: data.replace(COMMIT.encode(), b'b' * 40), symbols=True)
        with self.assertRaisesRegex(ValueError, 'symbol source'):
            self.verify()

    def test_readme_must_equal_source_bytes(self):
        self.mutate('README.md', lambda payload: payload + b' changed')
        with self.assertRaisesRegex(ValueError, 'README differs'):
            self.verify()

    def test_private_xml_reference_is_rejected(self):
        self.mutate('.xml', lambda payload: payload.replace(b'Returns an observation.', b'specs/private.md'))
        with self.assertRaisesRegex(ValueError, 'XML documentation'):
            self.verify()

    def test_extra_dll_is_rejected(self):
        path = self.feed / f'{VERIFY.IDS[1]}.{VERSION}.nupkg'
        with zipfile.ZipFile(path, 'a') as archive:
            archive.writestr('lib/net9.0/Unexpected.dll', b'stale')
            archive.writestr('lib/net9.0/Unexpected.xml', '<doc><assembly><name>Unexpected</name></assembly><members><member name="T:Unexpected"><summary>Unexpected API.</summary></member></members></doc>')
        with self.assertRaisesRegex(ValueError, 'DLL inventory'):
            self.verify()

    def test_build_metadata_identity_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'exact stable or numeric RC identity'):
            VERIFY.verify(self.feed, VERSION + '+metadata', COMMIT, self.feed / 'hashes.json', Path('inspector'))

    def add_dependency(self, pinned, version='[2.0.1]'):
        if pinned:
            self.sources['Directory.Packages.props'] = (
                b'<Project><ItemGroup><PackageVersion Include="Boutquin.Domain" Version="[1.0.0]"/>'
                b'<PackageVersion Include="Boutquin.Storage.Domain" Version="[2.0.1]"/>'
                b'<PackageVersion Include="Polly.Core" Version="8.9.0"/></ItemGroup></Project>')
        self.mutate('.nuspec', lambda content: content.replace(
            b'</group>', f'<dependency id="Boutquin.Storage.Domain" version="{version}"/></group>'.encode()))

    def test_exact_transitive_pin_from_the_central_pins_is_accepted(self):
        self.add_dependency(True)
        self.assertEqual('PASS', self.verify()['status'])

    def test_extra_dependency_outside_the_central_pins_is_rejected(self):
        self.add_dependency(False)
        with self.assertRaisesRegex(ValueError, 'Missing or unexpected dependencies'):
            self.verify()

    def test_transitive_pin_at_the_wrong_version_is_rejected(self):
        self.add_dependency(True, '[2.0.9]')
        with self.assertRaisesRegex(ValueError, 'Wrong transitive pin'):
            self.verify()


class ConsumerCorruptionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.consumer, self.cache, self.feed = [self.root / name for name in ('consumer', 'cache', 'feed')]
        self.project = self.consumer / 'tests/Fixture/Fixture.csproj'
        self.project.parent.mkdir(parents=True)
        self.project.touch()
        self.feed.mkdir()
        self.name = 'Boutquin.OptionPricing.Core'
        self.archive = self.feed / f'{self.name}.{VERSION}.nupkg'
        with zipfile.ZipFile(self.archive, 'w') as archive:
            archive.writestr(f'lib/net10.0/{self.name}.dll', b'qualified assembly')
        self.folder = self.cache / self.name.lower() / VERSION.lower()
        (self.folder / 'lib/net10.0').mkdir(parents=True)
        (self.folder / 'lib/net10.0' / f'{self.name}.dll').write_bytes(b'qualified assembly')
        (self.folder / f'{self.name.lower()}.{VERSION.lower()}.nupkg').write_bytes(self.archive.read_bytes())
        self.metadata = dict(source=str(self.feed), contentHash=base64.b64encode(hashlib.sha512(self.archive.read_bytes()).digest()).decode())
        (self.folder / f'{self.name.lower()}.{VERSION.lower()}.nupkg.sha512').write_text(self.metadata['contentHash'])
        self.write_metadata()
        self.assets = dict(packageFolders={str(self.cache): {}}, libraries={self.name + '/' + VERSION: dict(type='package')})
        self.assets_path = self.project.parent / 'obj/project.assets.json'
        self.assets_path.parent.mkdir()
        self.write_assets()
        self.expected = {self.name: dict(version=VERSION, archive=self.archive)}

    def write_metadata(self):
        (self.folder / '.nupkg.metadata').write_text(json.dumps(self.metadata))

    def write_assets(self):
        self.assets_path.write_text(json.dumps(self.assets))

    def check(self):
        return CONSUMER.verify_restore(self.consumer, self.cache, self.expected)

    def test_valid_restore(self):
        self.assertEqual([self.name], self.check())

    def test_corrupt_extracted_dll_is_rejected_even_when_nupkg_matches(self):
        (self.folder / 'lib/net10.0' / f'{self.name}.dll').write_bytes(b'changed DLL')
        with self.assertRaisesRegex(ValueError, 'restored DLL'):
            self.check()

    def test_cached_archive_replacement_is_rejected(self):
        (self.folder / f'{self.name.lower()}.{VERSION.lower()}.nupkg').write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError, 'cached bytes'):
            self.check()

    def test_source_substitution_is_rejected(self):
        self.metadata['source'] = str(self.root / 'other-feed')
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, 'Wrong source'):
            self.check()

    def test_content_hash_substitution_is_rejected(self):
        self.metadata['contentHash'] = 'wrong'
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, 'resolved hash'):
            self.check()

    def test_shared_cache_fallback_is_rejected(self):
        self.assets['packageFolders'][str(self.root / 'global-cache')] = {}
        self.write_assets()
        with self.assertRaisesRegex(ValueError, 'Non-isolated'):
            self.check()

    def test_production_project_reference_is_rejected(self):
        self.assets['libraries'][self.name + '/' + VERSION]['type'] = 'project'
        self.write_assets()
        with self.assertRaisesRegex(ValueError, 'Production project reference'):
            self.check()

    def test_unpinned_private_package_is_rejected(self):
        self.assets['libraries']['Boutquin.Numerics/2.0.0'] = dict(type='package')
        self.write_assets()
        with self.assertRaisesRegex(ValueError, 'Unpinned private'):
            self.check()

    def test_archive_sha512_substitution_is_rejected(self):
        (self.folder / f'{self.name.lower()}.{VERSION.lower()}.nupkg.sha512').write_text('wrong')
        with self.assertRaisesRegex(ValueError, 'archive SHA-512'):
            self.check()

    def test_unknown_boutquin_family_is_rejected(self):
        self.assets['libraries']['Boutquin.Unqualified/2.0.0'] = dict(type='package')
        self.write_assets()
        with self.assertRaisesRegex(ValueError, 'Unpinned private'):
            self.check()

    def test_exact_public_source_is_required_for_allowlisted_domain(self):
        # The same restore verifier enforces public provenance as well as local feeds.
        self.expected[self.name]['packageSource'] = 'https://api.nuget.org/v3/index.json'
        self.metadata['source'] = self.expected[self.name]['packageSource']
        self.write_metadata()
        self.assertEqual([self.name], self.check())
        self.metadata['source'] = str(self.feed)
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, 'Wrong source'):
            self.check()

    def test_changed_candidate_inventory_is_rejected(self):
        expected = {self.archive.name: hashlib.sha256(self.archive.read_bytes()).hexdigest()}
        CONSUMER.verify_hashes(self.feed, expected)
        (self.feed / 'Extra.1.0.0.nupkg').write_bytes(b'extra')
        with self.assertRaisesRegex(ValueError, 'inventory or hashes'):
            CONSUMER.verify_hashes(self.feed, expected)


class OfflineExecutionTests(unittest.TestCase):
    @unittest.skipUnless(__import__('shutil').which('sandbox-exec'), 'macOS network-denial control')
    def test_real_network_attempt_is_denied(self):
        import os
        import socket
        import sys
        with tempfile.TemporaryDirectory() as directory, socket.socket() as listener:
            root = Path(directory)
            listener.bind(('127.0.0.1', 0))
            listener.listen()
            port = listener.getsockname()[1]
            with self.assertRaisesRegex(ValueError, 'Exit'):
                CONSUMER.run_offline([sys.executable, '-c', f'import socket; socket.create_connection(("127.0.0.1", {port}), 2)'], root, root / 'network.log', os.environ.copy())
            log = (root / 'network.log').read_text()
            self.assertIn('PermissionError', log)


class CommittedConsumerTests(unittest.TestCase):
    def test_skipped_test_lane_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'result.trx').write_text('<TestRun><ResultSummary><Counters total="1" passed="0" failed="0" notExecuted="1"/></ResultSummary></TestRun>')
            with self.assertRaisesRegex(ValueError, 'Non-green test lane'):
                CONSUMER.test_counters(root, [Path('Fixture.csproj')])

    def test_duplicate_project_lane_cannot_replace_missing_project(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for index in range(2):
                (root / f'result{index}.trx').write_text('<TestRun><ResultSummary><Counters total="1" passed="1" failed="0" notExecuted="0"/></ResultSummary><TestDefinitions><UnitTest storage="Fixture.dll"/></TestDefinitions></TestRun>')
            with self.assertRaisesRegex(ValueError, 'Missing or duplicate test project'):
                CONSUMER.test_counters(root, [Path('Fixture.csproj'), Path('Other.csproj')])

    def test_green_test_lane_records_assembly_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'result.trx').write_text('<TestRun><ResultSummary><Counters total="1" passed="1" failed="0" notExecuted="0"/></ResultSummary><TestDefinitions><UnitTest storage="Fixture.dll"/></TestDefinitions></TestRun>')
            counters = CONSUMER.test_counters(root, [Path('Fixture.csproj')])
            self.assertEqual('fixture', counters[0]['assembly'])

    def test_all_test_projects_and_example_are_preserved_without_library_source(self):
        import subprocess
        commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=CONSUMER.ROOT, text=True).strip()
        with tempfile.TemporaryDirectory() as directory:
            consumer = Path(directory) / 'consumer'
            projects = CONSUMER.prepare(consumer, VERSION, commit)
            self.assertEqual(10, len(projects))
            self.assertFalse((consumer / 'src').exists())
            self.assertTrue(all((consumer / 'consumer-apps' / name / 'Program.cs').exists() for name in CONSUMER.APPS))
            refs = [reference for project in consumer.rglob('*.csproj') for reference in ET.parse(project).findall('.//ProjectReference')]
            self.assertEqual(0, len(refs))
            packages = [reference for project in consumer.rglob('*.csproj') for reference in ET.parse(project).findall('.//PackageReference') if reference.get('Include') in CONSUMER.IDS]
            self.assertEqual(set(CONSUMER.IDS), {reference.get('Include') for reference in packages})
            self.assertTrue(all(reference.get('VersionOverride') == f'[{VERSION}]' for reference in packages))


if __name__ == '__main__':
    unittest.main()
