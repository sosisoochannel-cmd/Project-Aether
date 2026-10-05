#!/usr/bin/env python3
"""Generate missing Unity .meta files for everything under Assets/.

Unity creates these on import, but they must exist in the repository so that asset
references (which are stored as GUIDs) stay stable across machines and CI. This script
makes them deterministic: a GUID is derived from the asset's project-relative path, so
running it twice produces the same result and a moved file is the only thing that
changes a GUID.

Usage:
    python3 tools/verify/gen_meta.py [--check]

Extensions that need settings beyond "Unity, import this" get them here: a texture is imported as
a readable, uncompressed, alpha-preserving sprite, and a scene as the text asset Unity writes.

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


def texture_meta(guid: str) -> str:
    """A sprite texture's importer settings.

    These are not cosmetic. The studio intro draws the mark from its alpha channel and recolours
    it, which only works if the texture is imported as a **sprite** (textureType 8), keeps its
    **alpha** (alphaIsTransparency 1), is **readable** from script (isReadable 1), is not
    downscaled or block-compressed into fringes, and uses clamp wrapping with bilinear filtering so
    the edges of a mark drawn near the screen edge do not wrap or shimmer. A texture imported
    without those settings is a mark that is invisible, fuzzy or the wrong colour, and the failure
    looks like a bug in the intro rather than in the import.

    `tools/verify/verify.py` checks these fields on every brand texture, so a re-import that drops
    one of them fails the gate rather than the device.
    """
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        "TextureImporter:\n"
        "  internalIDToNameTable: []\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 13\n"
        "  mipmaps:\n"
        "    mipMapMode: 0\n"
        "    enableMipMap: 0\n"
        "    sRGBTexture: 1\n"
        "    linearTexture: 0\n"
        "    fadeOut: 0\n"
        "    borderMipMap: 0\n"
        "    mipMapsPreserveCoverage: 0\n"
        "    alphaTestReferenceValue: 0.5\n"
        "    mipMapFadeDistanceStart: 1\n"
        "    mipMapFadeDistanceEnd: 3\n"
        "  bumpmap:\n"
        "    convertToNormalMap: 0\n"
        "    externalNormalMap: 0\n"
        "    heightScale: 0.25\n"
        "    normalMapFilter: 0\n"
        "    flipGreenChannel: 0\n"
        "  isReadable: 1\n"
        "  streamingMipmaps: 0\n"
        "  streamingMipmapsPriority: 0\n"
        "  vTOnly: 0\n"
        "  ignoreMipmapLimit: 0\n"
        "  grayScaleToAlpha: 0\n"
        "  generateCubemap: 6\n"
        "  cubemapConvolution: 0\n"
        "  seamlessCubemap: 0\n"
        "  textureFormat: 1\n"
        "  maxTextureSize: 2048\n"
        "  textureSettings:\n"
        "    serializedVersion: 2\n"
        "    filterMode: 1\n"
        "    aniso: 1\n"
        "    mipBias: 0\n"
        "    wrapU: 1\n"
        "    wrapV: 1\n"
        "    wrapW: 1\n"
        "  nPOTScale: 0\n"
        "  lightmap: 0\n"
        "  compressionQuality: 50\n"
        "  spriteMode: 1\n"
        "  spriteExtrude: 1\n"
        "  spriteMeshType: 0\n"
        "  alignment: 0\n"
        "  spritePivot: {x: 0.5, y: 0.5}\n"
        "  spritePixelsToUnits: 100\n"
        "  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n"
        "  spriteGenerateFallbackPhysicsShape: 0\n"
        "  alphaUsage: 1\n"
        "  alphaIsTransparency: 1\n"
        "  spriteTessellationDetail: -1\n"
        "  textureType: 8\n"
        "  textureShape: 1\n"
        "  singleChannelComponent: 0\n"
        "  flipbookRows: 1\n"
        "  flipbookColumns: 1\n"
        "  maxTextureSizeSet: 0\n"
        "  compressionQualitySet: 0\n"
        "  textureFormatSet: 0\n"
        "  ignorePngGamma: 0\n"
        "  applyGammaDecoding: 0\n"
        "  swizzle: 50462976\n"
        "  cookieLightType: 0\n"
        "  platformSettings:\n"
        "  - serializedVersion: 3\n"
        "    buildTarget: DefaultTexturePlatform\n"
        "    maxTextureSize: 2048\n"
        "    resizeAlgorithm: 0\n"
        "    textureFormat: -1\n"
        "    textureCompression: 0\n"
        "    compressionQuality: 50\n"
        "    crunchedCompression: 0\n"
        "    allowsAlphaSplitting: 0\n"
        "    overridden: 0\n"
        "    ignorePlatformSupport: 0\n"
        "    androidETC2FallbackOverride: 0\n"
        "    forceMaximumCompressionQuality_BC6H_BC7: 0\n"
        "  - serializedVersion: 3\n"
        "    buildTarget: Android\n"
        "    maxTextureSize: 2048\n"
        "    resizeAlgorithm: 0\n"
        "    textureFormat: -1\n"
        "    textureCompression: 0\n"
        "    compressionQuality: 50\n"
        "    crunchedCompression: 0\n"
        "    allowsAlphaSplitting: 0\n"
        "    overridden: 0\n"
        "    ignorePlatformSupport: 0\n"
        "    androidETC2FallbackOverride: 0\n"
        "    forceMaximumCompressionQuality_BC6H_BC7: 0\n"
        "  spriteSheet:\n"
        "    serializedVersion: 2\n"
        "    sprites: []\n"
        "    outline: []\n"
        "    physicsShape: []\n"
        "    bones: []\n"
        "    spriteID: 5e97eb03825dee720800000000000000\n"
        "    internalID: 0\n"
        "    vertices: []\n"
        "    indices: \n"
        "    edges: []\n"
        "    weights: []\n"
        "    secondaryTextures: []\n"
        "    nameFileIdTable: {}\n"
        "  mipmapLimitGroupName: \n"
        "  pSDRemoveMatte: 0\n"
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
    if ext == ".asset" or ext == ".mat":
        # .mat and .asset are native assets, and Unity writes NativeFormatImporter for them.
        return native_meta(guid)
    if ext == ".unity" or ext == ".prefab":
        # Scenes and prefabs are text assets that Unity imports with DefaultImporter, which is what
        # every scene in this repository already carries.
        return default_meta(guid)
    if ext in (".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr"):
        return texture_meta(guid)
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
