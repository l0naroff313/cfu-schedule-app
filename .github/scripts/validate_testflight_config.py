#!/usr/bin/env python3

import argparse
import pathlib
import plistlib
import re
import sys
import xml.etree.ElementTree as ET


def fail(message: str) -> None:
    print(f"TestFlight preflight failed: {message}", file=sys.stderr)
    raise SystemExit(1)


def read_project_property(root: ET.Element, name: str) -> str | None:
    for property_group in root.findall("PropertyGroup"):
        element = property_group.find(name)
        if element is not None and element.text and not element.attrib.get("Condition"):
            return element.text.strip()
    return None


def load_plist(path: pathlib.Path, label: str) -> dict:
    try:
        with path.open("rb") as stream:
            value = plistlib.load(stream)
    except (OSError, plistlib.InvalidFileException) as error:
        fail(f"{label} is not a valid plist: {error}")

    if not isinstance(value, dict):
        fail(f"{label} root must be a dictionary")
    return value


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", required=True, type=pathlib.Path)
    parser.add_argument("--info-plist", required=True, type=pathlib.Path)
    parser.add_argument("--privacy-manifest", required=True, type=pathlib.Path)
    parser.add_argument("--expected-bundle-id", required=True)
    args = parser.parse_args()

    for path in (args.project, args.info_plist, args.privacy_manifest):
        if not path.is_file():
            fail(f"required file is missing: {path}")

    try:
        project_root = ET.parse(args.project).getroot()
    except ET.ParseError as error:
        fail(f"project file is not valid XML: {error}")

    target_frameworks = read_project_property(project_root, "TargetFrameworks") or ""
    application_id = read_project_property(project_root, "ApplicationId")
    display_version = read_project_property(project_root, "ApplicationDisplayVersion")
    application_version = read_project_property(project_root, "ApplicationVersion")

    if "net10.0-ios" not in target_frameworks.split(";"):
        fail("net10.0-ios is missing from TargetFrameworks")
    if application_id != args.expected_bundle_id:
        fail(f"ApplicationId must be {args.expected_bundle_id!r}, got {application_id!r}")
    if not display_version or not re.fullmatch(r"[0-9]+(?:\.[0-9]+){1,2}", display_version):
        fail("ApplicationDisplayVersion must contain two or three numeric components")
    if not application_version or not application_version.isdecimal() or int(application_version) < 1:
        fail("ApplicationVersion must be a positive integer")

    info_plist = load_plist(args.info_plist, "Info.plist")
    if info_plist.get("LSRequiresIPhoneOS") is not True:
        fail("Info.plist must declare LSRequiresIPhoneOS=true")
    if info_plist.get("ITSAppUsesNonExemptEncryption") is not False:
        fail("Info.plist must explicitly declare ITSAppUsesNonExemptEncryption=false")

    privacy_manifest = load_plist(args.privacy_manifest, "PrivacyInfo.xcprivacy")
    accessed_api_types = privacy_manifest.get("NSPrivacyAccessedAPITypes")
    if not isinstance(accessed_api_types, list) or not accessed_api_types:
        fail("PrivacyInfo.xcprivacy must declare the required-reason API categories")
    for entry in accessed_api_types:
        if not isinstance(entry, dict) or not entry.get("NSPrivacyAccessedAPITypeReasons"):
            fail("every privacy manifest API category must include at least one reason")

    print("TestFlight public configuration is valid:")
    print(f"  bundle id: {application_id}")
    print(f"  marketing version: {display_version}")
    print(f"  base build number: {application_version}")
    print(f"  privacy API categories: {len(accessed_api_types)}")


if __name__ == "__main__":
    main()
