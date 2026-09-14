#!/usr/bin/env python3
"""Keep NuGet, native packs, bindings, Data Studio, and IDE extensions on one version."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


SEMVER = re.compile(r"^(\d+)\.(\d+)\.(\d+)(?:-.*)?$")


def fail(message: str) -> None:
    print(f"::error::{message}")
    raise SystemExit(1)


def local_name(tag: str) -> str:
    return tag.split("}")[-1]


def first_property(path: Path, name: str) -> str | None:
    if not path.is_file():
        return None
    try:
        tree = ET.parse(path)
    except (ET.ParseError, OSError) as exc:
        fail(f"Could not parse {path}: {exc}")
    for element in tree.iter():
        if local_name(element.tag) != name:
            continue
        if element.attrib.get("Include") or element.attrib.get("include"):
            continue
        text = (element.text or "").strip()
        if text and not text.startswith("$("):
            return text
    return None


def write_output(name: str, value: str) -> None:
    print(f"{name}={value}")
    output = os.environ.get("GITHUB_OUTPUT")
    if not output:
        return
    with open(output, "a", encoding="utf-8") as handle:
        handle.write(f"{name}={value}\n")


def packable(csproj: Path) -> bool:
    flag = first_property(csproj, "IsPackable")
    test = first_property(csproj, "IsTestProject")
    if test == "true":
        return False
    return flag != "false"


def project_version(csproj: Path) -> str | None:
    return first_property(csproj, "PackageVersion") or first_property(csproj, "Version")


def json_version(path: Path) -> str:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"Could not parse {path}: {exc}")
    version = str(data.get("version") or "").strip()
    if not version:
        fail(f"{path} has no version")
    return version


def vsix_version(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    match = re.search(r'<Identity\b[^>]*\bVersion="([^"]+)"', text)
    if not match:
        fail(f"{path} has no Identity Version")
    return match.group(1)


def first_match(path: Path, pattern: str, label: str) -> str:
    if not path.is_file():
        fail(f"{label} is missing ({path})")
    match = re.search(pattern, path.read_text(encoding="utf-8"))
    if not match:
        fail(f"{path} has no {label}")
    return match.group(1)


def android_version_code(version: str) -> int:
    match = SEMVER.match(version)
    if not match:
        fail(f"Version {version} is not major.minor.patch")
    major, minor, patch = (int(part) for part in match.groups())
    return major * 10000 + minor * 100 + patch


def replace_one(path: Path, pattern: str, replacement: str, label: str) -> None:
    text = path.read_text(encoding="utf-8")
    updated, count = re.subn(pattern, replacement, text, count=1)
    if count != 1:
        fail(f"Could not write {label} in {path}")
    if updated != text:
        path.write_text(updated, encoding="utf-8")


def collect(root: Path) -> dict[str, str]:
    versions: dict[str, str] = {}
    src = root / "src"
    if not src.is_dir():
        fail(f"No src/ under {root}")

    props = root / "Directory.Build.props"
    product = first_property(props, "PackageVersion") or first_property(props, "Version")
    if not product:
        fail("Directory.Build.props has no Version")
    versions["Directory.Build.props"] = product

    packable_projects = [
        path
        for path in sorted(src.rglob("*.csproj"))
        if "bin" not in path.parts and "obj" not in path.parts and packable(path)
    ]
    if not packable_projects:
        fail("No packable src/*.csproj")

    for path in packable_projects:
        version = project_version(path) or product
        if not version:
            fail(f"{path.relative_to(root)} has no Version or PackageVersion")
        versions[str(path.relative_to(root))] = version

    versions["VS Code"] = json_version(src / "Nuventra.NuvexaDB.VSCode" / "package.json")
    versions["Visual Studio"] = vsix_version(
        src / "Nuventra.NuvexaDB.VisualStudio" / "source.extension.vsixmanifest"
    )
    versions["Java / Kotlin"] = first_match(
        root / "bindings/jvm/build.gradle.kts",
        r'(?m)^version\s*=\s*"([^"]+)"',
        "Gradle version",
    )
    versions["Android AAR"] = first_match(
        root / "bindings/android/build.gradle.kts",
        r'(?m)^version\s*=\s*"([^"]+)"',
        "Gradle version",
    )
    versions["Android versionName"] = first_match(
        root / "bindings/android/gradle.properties",
        r"(?m)^nuvexa\.versionName=(.+)$",
        "nuvexa.versionName",
    )
    versions["Python"] = first_match(
        root / "bindings/python/pyproject.toml",
        r'(?m)^version\s*=\s*"([^"]+)"',
        "project version",
    )
    versions["Python __version__"] = first_match(
        root / "bindings/python/nuvexadb/__init__.py",
        r'__version__\s*=\s*"([^"]+)"',
        "__version__",
    )
    versions["Node"] = json_version(root / "bindings/node/package.json")
    versions["React Native"] = json_version(root / "bindings/react-native/package.json")
    versions["React Native Android"] = first_match(
        root / "bindings/react-native/android/build.gradle",
        r'(?m)^version\s*=\s*"([^"]+)"',
        "Gradle version",
    )
    versions["React Native versionName"] = first_match(
        root / "bindings/react-native/android/gradle.properties",
        r"(?m)^nuvexa\.versionName=(.+)$",
        "nuvexa.versionName",
    )
    versions["Flutter"] = first_match(
        root / "bindings/flutter/pubspec.yaml",
        r'(?m)^version:\s+(\S+)',
        "pubspec version",
    )
    versions["C++"] = first_match(
        root / "bindings/cpp/CMakeLists.txt",
        r"project\(\s*nuvexadb\s+VERSION\s+(\S+)",
        "CMake VERSION",
    )
    versions["Go"] = first_match(
        root / "bindings/go/version.go",
        r'PackageVersion\s*=\s*"([^"]+)"',
        "PackageVersion",
    )
    versions["Swift"] = first_match(
        root / "bindings/swift/Sources/NuvexaDB/Version.swift",
        r'static let version\s*=\s*"([^"]+)"',
        "SDK version",
    )
    versions["VS Code About"] = first_match(
        src / "Nuventra.NuvexaDB.VSCode" / "src" / "extension.ts",
        r"NuvexaDB (\d+\.\d+\.\d+) · \.nvx format",
        "About version",
    )
    versions["Data Studio manifest"] = first_match(
        src / "Nuventra.NuvexaDB.Explorer" / "app.manifest",
        r'assemblyIdentity version="(\d+\.\d+\.\d+)\.0"',
        "assemblyIdentity version",
    )
    versions["README"] = first_match(
        root / "README.md",
        r"\*\*Version:\*\* (\d+\.\d+\.\d+)",
        "README Version",
    )
    return versions


def collect_version_codes(root: Path) -> dict[str, int]:
    return {
        "Android versionCode": int(
            first_match(
                root / "bindings/android/gradle.properties",
                r"(?m)^nuvexa\.versionCode=(\d+)$",
                "nuvexa.versionCode",
            )
        ),
        "React Native versionCode": int(
            first_match(
                root / "bindings/react-native/android/gradle.properties",
                r"(?m)^nuvexa\.versionCode=(\d+)$",
                "nuvexa.versionCode",
            )
        ),
    }


def write_versions(root: Path, version: str) -> None:
    code = android_version_code(version)
    replace_one(
        root / "src/Nuventra.NuvexaDB.VSCode/package.json",
        r'("version"\s*:\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "VS Code version",
    )
    replace_one(
        root / "src/Nuventra.NuvexaDB.VSCode/src/extension.ts",
        r"(NuvexaDB )\d+\.\d+\.\d+( · \.nvx format)",
        rf"\g<1>{version}\g<2>",
        "VS Code About",
    )
    replace_one(
        root / "src/Nuventra.NuvexaDB.Explorer/app.manifest",
        r'(assemblyIdentity version=")\d+\.\d+\.\d+(\.0")',
        rf"\g<1>{version}\g<2>",
        "Data Studio manifest",
    )
    replace_one(
        root / "README.md",
        r"(\*\*Version:\*\* )\d+\.\d+\.\d+",
        rf"\g<1>{version}",
        "README Version",
    )
    readme = root / "README.md"
    text = readme.read_text(encoding="utf-8")
    updated = re.sub(r"v\d+\.\d+\.\d+", f"v{version}", text)
    if updated != text:
        readme.write_text(updated, encoding="utf-8")
    replace_one(
        root / "src/Nuventra.NuvexaDB.VisualStudio/source.extension.vsixmanifest",
        r'(<Identity\b[^>]*\bVersion=")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "VSIX Identity Version",
    )
    replace_one(
        root / "bindings/jvm/build.gradle.kts",
        r'(?m)^(version\s*=\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "Java version",
    )
    replace_one(
        root / "bindings/android/build.gradle.kts",
        r'(?m)^(version\s*=\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "Android version",
    )
    replace_one(
        root / "bindings/android/gradle.properties",
        r"(?m)^(nuvexa\.versionName=).+$",
        rf"\g<1>{version}",
        "Android versionName",
    )
    replace_one(
        root / "bindings/android/gradle.properties",
        r"(?m)^(nuvexa\.versionCode=)\d+$",
        rf"\g<1>{code}",
        "Android versionCode",
    )
    replace_one(
        root / "bindings/python/pyproject.toml",
        r'(?m)^(version\s*=\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "Python version",
    )
    replace_one(
        root / "bindings/python/nuvexadb/__init__.py",
        r'(__version__\s*=\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "Python __version__",
    )
    replace_one(
        root / "bindings/node/package.json",
        r'("version"\s*:\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "Node version",
    )
    replace_one(
        root / "bindings/react-native/package.json",
        r'("version"\s*:\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "React Native version",
    )
    replace_one(
        root / "bindings/react-native/android/build.gradle",
        r'(?m)^(version\s*=\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "React Native Android version",
    )
    replace_one(
        root / "bindings/react-native/android/gradle.properties",
        r"(?m)^(nuvexa\.versionName=).+$",
        rf"\g<1>{version}",
        "React Native versionName",
    )
    replace_one(
        root / "bindings/react-native/android/gradle.properties",
        r"(?m)^(nuvexa\.versionCode=)\d+$",
        rf"\g<1>{code}",
        "React Native versionCode",
    )
    replace_one(
        root / "bindings/flutter/pubspec.yaml",
        r"(?m)^(version:\s+)\S+",
        rf"\g<1>{version}",
        "Flutter version",
    )
    replace_one(
        root / "bindings/cpp/CMakeLists.txt",
        r"(project\(\s*nuvexadb\s+VERSION\s+)\S+",
        rf"\g<1>{version}",
        "C++ version",
    )
    replace_one(
        root / "bindings/go/version.go",
        r'(PackageVersion\s*=\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "Go version",
    )
    replace_one(
        root / "bindings/swift/Sources/NuvexaDB/Version.swift",
        r'(static let version\s*=\s*")[^"]+(")',
        rf"\g<1>{version}\g<2>",
        "Swift version",
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", default=".")
    parser.add_argument(
        "--write",
        action="store_true",
        help="Copy Directory.Build.props Version into every library, IDE, and extension.",
    )
    args = parser.parse_args()
    root = Path(args.repo_root).resolve()
    if not root.is_dir():
        fail(f"Repo folder not found: {root}")

    props = root / "Directory.Build.props"
    expected = first_property(props, "PackageVersion") or first_property(props, "Version")
    if not expected:
        fail("Directory.Build.props has no Version")

    if args.write:
        write_versions(root, expected)
        print(f"Wrote {expected} (Android versionCode {android_version_code(expected)})")

    versions = collect(root)
    print("Version alignment (must all equal Directory.Build.props):")
    mismatched: list[str] = []
    for label, version in versions.items():
        mark = "OK" if version == expected else "MISMATCH"
        print(f"  [{mark}] {version}  {label}")
        if version != expected:
            mismatched.append(f"{label}={version}")

    expected_code = android_version_code(expected)
    for label, code in collect_version_codes(root).items():
        mark = "OK" if code == expected_code else "MISMATCH"
        print(f"  [{mark}] {code}  {label} (from {expected})")
        if code != expected_code:
            mismatched.append(f"{label}={code}")

    if mismatched:
        fail(f"Versions must match {expected}. Fix: " + ", ".join(mismatched))

    write_output("version", expected)
    write_output("version_code", str(expected_code))
    print(f"Version {expected} is aligned (versionCode {expected_code})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
