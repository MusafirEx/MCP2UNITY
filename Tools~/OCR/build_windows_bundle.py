#!/usr/bin/env python3
"""Stage a portable Windows-x64 Tesseract from MSYS2 UCRT64 for Git UPM.

Runs on a trusted GitHub Actions Windows runner, NOT in the Unity Editor.
Copies the CLI, recursively imported UCRT DLLs and eng/msa/ind models.
Writes SHA-256/owner/version manifest and retained third-party licences.
Does not download or execute code at Unity runtime.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

import pefile
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SOURCE_ROOT = Path(os.environ.get("MSYS2_UCRT64_ROOT", r"C:\msys64\ucrt64"))
BIN = SOURCE_ROOT / "bin"
SOURCE_DATA = SOURCE_ROOT / "share" / "tessdata"
DEST = ROOT / "Tools~" / "OCR" / "win-x64"
LANGS = ("eng", "msa", "ind")
MAX_COPY_BYTES = 100 * 1024 * 1024
MSYS_BASH = SOURCE_ROOT.parent / "usr" / "bin" / "bash.exe"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as reader:
        for block in iter(lambda: reader.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def copy_file(source: Path, dest: Path) -> None:
    if not source.is_file():
        raise RuntimeError(f"Missing required package file: {source}")
    if source.stat().st_size > MAX_COPY_BYTES:
        raise RuntimeError(f"Refusing large native binary: {source}")
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, dest)


def imports(path: Path) -> list[str]:
    pe = pefile.PE(str(path), fast_load=True)
    try:
        ids = [pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"],
               pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT"]]
        pe.parse_data_directories(directories=ids)
        names = set()
        for kind in ("DIRECTORY_ENTRY_IMPORT", "DIRECTORY_ENTRY_DELAY_IMPORT"):
            for item in getattr(pe, kind, []):
                names.add(item.dll.decode("ascii", "replace").lower())
        return sorted(names)
    finally:
        pe.close()


def owner_of(name: str) -> tuple[str, str]:
    if not MSYS_BASH.exists():
        raise RuntimeError("MSYS2 bash is missing; cannot record license owners.")
    # name is derived only from staged DLL / exe filenames.
    if not name.lower().endswith((".exe", ".dll")) or "/" in name or "\\" in name:
        raise ValueError("Unexpected native filename")
    result = subprocess.run(
        [str(MSYS_BASH), "-lc", f"pacman -Qo -q /ucrt64/bin/{name}"],
        text=True, capture_output=True, check=True, timeout=30)
    owner = result.stdout.strip().splitlines()[0]
    version = subprocess.run(
        [str(MSYS_BASH), "-lc", "pacman -Q " + owner],
        text=True, capture_output=True, check=True, timeout=30).stdout.strip()
    return owner, version


def save_licenses(owners: dict[str, str]) -> None:
    folder = DEST / "licenses"
    folder.mkdir(parents=True, exist_ok=True)
    copied = 0
    for owner in sorted(owners):
        result = subprocess.run(
            [str(MSYS_BASH), "-lc", "pacman -Qlq " + owner],
            text=True, capture_output=True, check=True, timeout=30)
        for raw in result.stdout.splitlines():
            path = raw.strip()
            if not path.startswith("/ucrt64/share/licenses/") or path.endswith("/"):
                continue
            relative = path[len("/ucrt64/"):]
            source = SOURCE_ROOT / relative
            if source.is_file():
                dest = folder / owner / source.name
                copy_file(source, dest)
                copied += 1
    # All package releases ship the Apache-2.0 Tesseract licence separately
    # in the source tree. Other dependencies may also include their licences
    # in the staged vendor folder.
    source_license = ROOT / "Tools~" / "OCR" / "licenses" / "TESSERACT-APACHE-2.0.txt"
    copy_file(source_license, folder / "TESSERACT-APACHE-2.0.txt")
    print(f"Copied {copied} MSYS2 dependency licence files.")


def smoke_test() -> str:
    exe = DEST / "tesseract.exe"
    tessdata = DEST / "tessdata"
    # Only Windows OS paths: prove there is no hidden dependency on MSYS2
    # or another Tesseract installation in PATH.
    env = os.environ.copy()
    windows = Path(os.environ.get("SystemRoot", r"C:\Windows"))
    env["PATH"] = os.pathsep.join([str(windows / "System32"), str(windows)])
    env.pop("TESSDATA_PREFIX", None)
    version = subprocess.run([str(exe), "--version"], env=env,
        capture_output=True, text=True, timeout=15, check=True)
    languages = subprocess.run(
        [str(exe), "--list-langs", "--tessdata-dir", str(tessdata)],
        env=env, capture_output=True, text=True, timeout=15, check=True)
    for language in LANGS:
        if language not in languages.stdout.split():
            raise RuntimeError(f"Bundled language not available: {language}")

    # Make a readable image with Windows fonts and require a real OCR result.
    temp = DEST.parent / "_ocr_smoke.png"
    try:
        picture = Image.new("RGB", (1000, 200), "white")
        draw = ImageDraw.Draw(picture)
        font_path = windows / "Fonts" / "arial.ttf"
        if not font_path.exists():
            raise RuntimeError("Windows Arial font missing for OCR smoke test.")
        font = ImageFont.truetype(str(font_path), 64)
        draw.text((35, 55), "MCP2UNITY OCR 12345", font=font, fill="black")
        picture.save(temp)
        result = subprocess.run([
            str(exe), str(temp), "stdout", "-l", "eng",
            "--psm", "7", "--tessdata-dir", str(tessdata)
        ], env=env, capture_output=True, text=True, timeout=20, check=True)
        found = result.stdout.strip()
        if "MCP2UNITY" not in found.upper() or "12345" not in found:
            raise RuntimeError("Bundled OCR smoke test did not recognize expected text: " + found)
        return version.stderr.strip().splitlines()[0] if version.stderr.strip() else (
            version.stdout.strip().splitlines()[0])
    finally:
        temp.unlink(missing_ok=True)


def build() -> None:
    if os.name != "nt":
        raise RuntimeError("Windows-x64 bundling must run on a Windows runner.")
    if not BIN.is_dir() or not SOURCE_DATA.is_dir():
        raise RuntimeError("Install MSYS2 UCRT64 Tesseract and traineddata packages first.")
    if DEST.exists():
        shutil.rmtree(DEST)
    DEST.mkdir(parents=True)
    native = BIN / "tesseract.exe"
    copy_file(native, DEST / "tesseract.exe")

    # Resolve native imports transitively rather than copying the entire
    # MSYS2 distribution. System DLLs are supplied by Windows itself.
    lookup = {p.name.lower(): p for p in BIN.glob("*.dll")}
    pending = [DEST / "tesseract.exe"]
    copied = {"tesseract.exe"}
    while pending:
        current = pending.pop(0)
        for dep in imports(current):
            if dep in (x.lower() for x in copied):
                continue
            source = lookup.get(dep)
            if source is None:
                # Standard Windows OS DLLs are not bundled.
                system = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32" / dep
                if system.exists() or dep.startswith(("api-ms-win-", "ext-ms-")):
                    continue
                raise RuntimeError(f"Unresolved DLL dependency {dep} in {current.name}")
            dest = DEST / source.name
            copy_file(source, dest)
            copied.add(source.name)
            pending.append(dest)

    for language in LANGS:
        copy_file(SOURCE_DATA / f"{language}.traineddata",
                  DEST / "tessdata" / f"{language}.traineddata")

    versions: dict[str, str] = {}
    owners: dict[str, str] = {}
    for name in sorted(copied):
        owner, version = owner_of(name)
        versions[name] = version
        owners[owner] = version
    save_licenses(owners)
    version = smoke_test()

    manifest = {
        "target": "Windows-x64 (UCRT64)",
        "engine": version,
        "source": "MSYS2 official UCRT64 packages",
        "buildTool": "Tools~/OCR/build_windows_bundle.py",
        "languages": list(LANGS),
        "nativePackageVersions": versions,
        "packages": owners,
        "files": {
            p.relative_to(DEST).as_posix(): {
                "bytes": p.stat().st_size,
                "sha256": sha256(p),
            }
            for p in sorted(DEST.rglob("*"))
            if p.is_file() and p.name != "bundle-manifest.json"
        },
    }
    (DEST / "bundle-manifest.json").write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Validated portable OCR bundle: {version}")
    print(f"Files: {len(manifest['files'])}, {sum(x['bytes'] for x in manifest['files'].values())/1024/1024:.1f} MiB")
    print("Destination:", DEST)


if __name__ == "__main__":
    build()
