#!/usr/bin/env python3
"""Reject private planning references in the actual XML shipped inside packages."""

import argparse
from pathlib import Path
import re
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

PRIVATE_REFERENCE = re.compile(r"specs/|\bfeature-[a-z0-9-]+\.md\b|§|\bF-L\d|\bM-\d")


def xml_violations(payload):
    root = ET.fromstring(payload)
    if not root.findall("./members/member"):
        return ["no documented members"]
    return [
        member.get("name", "<unknown>")
        for member in root.findall("./members/member")
        if PRIVATE_REFERENCE.search(ET.tostring(member, encoding="unicode"))
        or member.find(".//inheritdoc") is not None
    ]


def package_violations(path):
    with zipfile.ZipFile(path) as archive:
        documents = [name for name in archive.namelist() if name.startswith("lib/") and name.endswith(".xml")]
        if not documents:
            return [f"{path.name}: no packaged XML documentation"]
        assemblies = [name for name in archive.namelist() if name.startswith("lib/") and name.endswith(".dll")]
        expected_documents = {name[:-4] + ".xml" for name in assemblies}
        if not expected_documents or set(documents) != expected_documents:
            return [f"{path.name}: XML documentation must match each shipped assembly"]
        errors = []
        for name in documents:
            try:
                payload = archive.read(name)
                if ET.fromstring(payload).findtext("assembly/name") != Path(name).stem:
                    errors.append(f"{path.name}:{name}: XML assembly identity mismatch")
                errors.extend(f"{path.name}:{name}:{member}" for member in xml_violations(archive.read(name)))
            except ET.ParseError as error:
                errors.append(f"{path.name}:{name}: invalid XML: {error}")
        return errors


class PackageDocumentationTests(unittest.TestCase):
    def test_each_private_reference_is_rejected(self):
        for text in ("specs/internal.md", "feature-secret.md", "§2.3", "F-L5-02", "M-7 deliverable"):
            with self.subTest(text=text):
                payload = f'<doc><members><member name="T:Example"><summary>{text}</summary></member></members></doc>'
                self.assertEqual(["T:Example"], xml_violations(payload))

    def test_public_contract_is_accepted(self):
        self.assertEqual([], xml_violations('<doc><members><member name="T:Example"><summary>Returns cached observations.</summary></member></members></doc>'))

    def test_links_and_escaped_section_marks_are_checked(self):
        self.assertEqual(["T:Example"], xml_violations('<doc><members><member name="T:Example"><see href="specs/internal.md"/></member></members></doc>'))
        self.assertEqual(["T:Example"], xml_violations('<doc><members><member name="T:Example">&#167;2</member></members></doc>'))

    def test_malformed_xml_is_not_accepted(self):
        with self.assertRaises(ET.ParseError):
            xml_violations('<doc>')

    def test_archive_boundary_checks_shipped_documents(self):
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "Example.1.0.0.nupkg"
            for summary, expected in (("Returns cached observations.", False), ("specs/internal.md", True)):
                with self.subTest(summary=summary):
                    with zipfile.ZipFile(package, "w") as archive:
                        archive.writestr("lib/net10.0/Example.dll", b"fixture")
                        archive.writestr("lib/net10.0/Example.xml", f'<doc><assembly><name>Example</name></assembly><members><member name="T:Example"><summary>{summary}</summary></member></members></doc>')
                    self.assertEqual(expected, bool(package_violations(package)))

    def test_empty_document_and_unresolved_inheritance_fail(self):
        self.assertEqual(["no documented members"], xml_violations('<doc><members/></doc>'))
        self.assertEqual(["T:Example"], xml_violations('<doc><members><member name="T:Example"><inheritdoc/></member></members></doc>'))

    def test_different_package_xml_cannot_replace_functional_package_docs(self):
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "Analytics.2.0.0.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("lib/net10.0/Boutquin.OptionPricing.Analytics.dll", b"fixture")
                archive.writestr("lib/net10.0/Boutquin.OptionPricing.Domain.xml", "<doc><members/></doc>")
            self.assertIn("match each shipped assembly", package_violations(package)[0])

    def test_every_optionpricing_library_rejects_empty_xml(self):
        with tempfile.TemporaryDirectory() as directory:
            for suffix in ("Abstractions", "Domain", "Analytics", "Trees", "Calibration", "Exotics", "Examples.Shared"):
                name = "Boutquin.OptionPricing." + suffix
                package = Path(directory) / (name + ".2.0.0.nupkg")
                with zipfile.ZipFile(package, "w") as archive:
                    archive.writestr("lib/net10.0/" + name + ".dll", b"fixture")
                    archive.writestr("lib/net10.0/" + name + ".xml", f"<doc><assembly><name>{name}</name></assembly><members/></doc>")
                self.assertTrue(any("no documented members" in error for error in package_violations(package)), name)

    def test_archive_without_xml_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "Example.1.0.0.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("lib/net10.0/Example.dll", b"fixture")
            self.assertIn("no packaged XML", package_violations(package)[0])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        result = unittest.TextTestRunner().run(unittest.defaultTestLoader.loadTestsFromTestCase(PackageDocumentationTests))
        return 0 if result.wasSuccessful() else 1
    if args.packages is None:
        parser.error("packages directory is required")
    packages = sorted(args.packages.glob("*.nupkg"))
    if not packages:
        parser.error("no .nupkg files found")
    errors = [error for package in packages for error in package_violations(package)]
    for error in errors:
        print(error)
    if not errors:
        print(f"PASS: public XML boundary in {len(packages)} packages")
    return bool(errors)


if __name__ == "__main__":
    raise SystemExit(main())
