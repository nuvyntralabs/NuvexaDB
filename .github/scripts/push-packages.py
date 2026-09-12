#!/usr/bin/env python3
"""Push nupkg and snupkg to nuget.org and GitHub Packages."""

from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path


NUGET_ORG = "https://api.nuget.org/v3/index.json"


def fail(message: str) -> None:
    print(f"::error::{message}")
    raise SystemExit(1)


def skip_symbols(nupkg_stem: str) -> bool:
    return nupkg_stem.lower().startswith("nuventra.nuvexadb.cli.")


def github_packages_source(owner: str) -> str:
    return f"https://nuget.pkg.github.com/{owner}/index.json"


def find_nupkgs(packages_dir: Path) -> list[Path]:
    return sorted(
        path
        for path in packages_dir.rglob("*.nupkg")
        if path.is_file() and not path.name.endswith(".snupkg")
    )


def run_push(path: Path, api_key: str, source: str) -> None:
    print(f"Pushing {path.name} to {source}")
    result = subprocess.run(
        [
            "dotnet",
            "nuget",
            "push",
            str(path),
            "--api-key",
            api_key,
            "--source",
            source,
            "--skip-duplicate",
        ],
        check=False,
    )
    if result.returncode != 0:
        fail(f"dotnet nuget push failed for {path} ({source})")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--packages-dir", default="artifacts")
    args = parser.parse_args()

    packages_dir = Path(args.packages_dir)
    nuget_key = os.environ.get("NUGET_KEY", "").strip()
    github_token = os.environ.get("GITHUB_TOKEN", "").strip()
    owner = (
        os.environ.get("GITHUB_REPOSITORY_OWNER")
        or os.environ.get("GITHUB_REPOSITORY", "nuvyntralabs/NuvexaDB").split("/", 1)[0]
    ).strip()

    if not nuget_key:
        fail("NUGET_KEY secret is empty. Add a nuget.org API key under Settings → Secrets → Actions.")
    if not github_token:
        fail("GITHUB_TOKEN is empty. Grant packages: write on the publish job.")
    if not packages_dir.is_dir():
        fail(f"No package folder at {packages_dir}.")

    nupkgs = find_nupkgs(packages_dir)
    if not nupkgs:
        fail(f"No .nupkg files found in {packages_dir}.")

    github_source = github_packages_source(owner)
    print(f"nuget.org: {NUGET_ORG}")
    print(f"GitHub Packages: {github_source}")

    for pkg in nupkgs:
        symbol = pkg.with_name(pkg.name[: -len(".nupkg")] + ".snupkg")
        run_push(pkg, nuget_key, NUGET_ORG)
        run_push(pkg, github_token, github_source)
        if skip_symbols(pkg.stem):
            print(f"Skipping symbols for {pkg.name} (PackAsTool)")
            continue
        if not symbol.is_file():
            fail(f"Missing symbol package for {pkg.name}: {symbol.name}")
        run_push(symbol, nuget_key, NUGET_ORG)
        run_push(symbol, github_token, github_source)

    print(
        "GitHub Packages defaults to private on first publish. "
        f"Set each package Public at https://github.com/orgs/{owner}/packages if the repo is public."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
