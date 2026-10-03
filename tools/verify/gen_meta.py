#!/usr/bin/env python3
"""Generate missing Unity .meta files for everything under Assets/.

Unity creates these on import, but they must exist in the repository so that asset
references (which are stored as GUIDs) stay stable across machines and CI. This script
makes them deterministic: a GUID is derived from the asset's project-relative path, so
running it twice produces the same result and a moved file is the only thing that
changes a GUID.

Usage:
    python3 tools/verify/gen_meta.py [--check]

    --check   Report missing .meta files and exit non-zero, without writing anything.
              Used by CI.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(REPO_ROOT, "Assets")

# Files Unity ignores entirely; they must not get a .meta.
IGNORED_NAMES = {".DS_Store", "Thumbs.db"}


def guid_for(path: str) -> str:
    """Deterministic 32 hex-char GUID derived from the project-relative asset path."""
    return hashlib.md5(path.encode("utf-8")).hexdigest()


def folder_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def mono_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "MonoImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 2\n"
        "  defaultReferences: []\n"
        "  executionOrder: 0\n"
        "  icon: {instanceID: 0}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def asmdef_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "AssemblyDefinitionImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def native_meta(guid: str, main_object: int = 11400000) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "NativeFormatImporter:\n"
        "  externalObjects: {}\n"
        f"  mainObjectFileID: {main_object}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def default_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def text_meta(guid: str) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "TextScriptImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def meta_for(path: str, is_dir: bool) -> str:
    rel = os.path.relpath(path, REPO_ROOT).replace(os.sep, "/")
    guid = guid_for(rel)

    if is_dir:
        return folder_meta(guid)

    ext = os.path.splitext(path)[1].lower()
    if ext == ".cs":
        return mono_meta(guid)
    if ext == ".asmdef" or ext == ".asmref":
        return asmdef_meta(guid)
    if ext == ".asset" or ext == ".unity" or ext == ".prefab" or ext == ".mat":
        return native_meta(guid)
    if ext in (".json", ".txt", ".md", ".xml", ".csv"):
        return text_meta(guid)
    return default_meta(guid)


def iter_assets():
    """Yields (path, is_dir) for every asset Unity should import, deepest-last."""
    for root, dirs, files in os.walk(ASSETS):
        dirs[:] = sorted(d for d in dirs if not d.startswith("."))
        for d in dirs:
            yield os.path.join(root, d), True
        for f in sorted(files):
            if f.startswith(".") or f.endswith(".meta") or f in IGNORED_NAMES:
                continue
            yield os.path.join(root, f), False


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true",
                        help="report missing .meta files instead of writing them")
    args = parser.parse_args()

    if not os.path.isdir(ASSETS):
        print(f"error: {ASSETS} does not exist", file=sys.stderr)
        return 2

    missing, created = [], 0

    for path, is_dir in iter_assets():
        meta_path = path + ".meta"
        if os.path.exists(meta_path):
            continue

        missing.append(os.path.relpath(path, REPO_ROOT))
        if args.check:
            continue

        with open(meta_path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(meta_for(path, is_dir))
        created += 1

    if args.check and missing:
        print(f"FAIL: {len(missing)} asset(s) have no .meta file:")
        for rel in missing:
            print(f"  {rel}")
        return 1

    if args.check:
        print("OK: every asset under Assets/ has a .meta file")
        return 0

    print(f"OK: created {created} .meta file(s); {len(missing)} were missing")
    return 0


if __name__ == "__main__":
    sys.exit(main())
