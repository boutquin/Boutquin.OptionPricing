"""Reject runtime substitution across distinct framework assets."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location('runtime_targets', Path(__file__).parents[1] / 'verify-dependency-identities.py')
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


class RuntimeTargetsTests(unittest.TestCase):
    def verify_fixture(self, substitute=False, single_target=False):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            cache, outputs = root / 'cache', root / 'outputs'
            package = cache / 'boutquin.fixture/3.0.0/boutquin.fixture.3.0.0.nupkg'
            package.parent.mkdir(parents=True)
            with zipfile.ZipFile(package, 'w') as archive:
                archive.writestr('lib/net10.0/Boutquin.Fixture.dll', b'net10 bytes')
                if not single_target:
                    archive.writestr('lib/net11.0/Boutquin.Fixture.dll', b'net11 bytes')
            lock = {'publishedPackages': [{'id': 'Boutquin.Fixture', 'version': '3.0.0', 'sourceCommit': 'a' * 40}]}
            (root / 'dependency-source.json').write_text(json.dumps(lock))
            for framework in ('net10.0', 'net11.0'):
                path = outputs / f'tests/bin/Release/{framework}/Boutquin.Fixture.dll'
                path.parent.mkdir(parents=True)
                path.write_bytes(b'net10 bytes' if framework == 'net10.0' or substitute or single_target else b'net11 bytes')
            with patch.object(check, 'ROOT', root), patch.object(check.subprocess, 'run'), patch.object(check.subprocess, 'check_output', return_value='{}'):
                return check.verify(cache, outputs)

    def test_distinct_framework_assets_are_verified(self):
        result = self.verify_fixture()
        self.assertEqual('PASS', result['status'])
        self.assertEqual(2, len(result['runtimeCopies']))
        self.assertEqual({'net10.0', 'net11.0'}, {i['framework'] for i in result['identities']})

    def test_net10_substitution_for_shipped_net11_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Runtime copy differs.*net11.0'):
            self.verify_fixture(substitute=True)

    def test_net10_fallback_is_allowed_for_single_target_dependency(self):
        result = self.verify_fixture(single_target=True)
        self.assertEqual('PASS', result['status'])
        self.assertEqual(2, len(result['runtimeCopies']))
