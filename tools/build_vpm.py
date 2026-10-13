"""Build a deterministic VPM zip and a listing that retains prior releases."""
import argparse
import hashlib
import json
import re
import shutil
import urllib.request
import zipfile
from pathlib import Path


PACKAGE = "net.panpacatiltott.pptavatareditor"
REPOSITORY = "PanpacaTiltott/PPTAvatarEditor"
ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ROOT = ROOT / "Packages" / PACKAGE
TEXT_EXTENSIONS = {".cs", ".meta", ".asmdef", ".asset", ".shader", ".anim", ".txt", ".json", ".url"}
ALLOWED_EXTENSIONS = TEXT_EXTENSIONS | {".png"}


def package_bytes(path):
    data = path.read_bytes()
    # Git checkouts on Windows and Linux must generate the same archive.
    return data.replace(b"\r\n", b"\n") if path.suffix in TEXT_EXTENSIONS else data


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def prior_versions(history, repository):
    versions = {}
    for page in history:
        for release in page if isinstance(page, list) else [page]:
            if release.get("draft"):
                continue
            manifest_asset = next((a for a in release.get("assets", []) if a["name"] == "package.json"), None)
            if manifest_asset is None:
                continue
            url = manifest_asset["browser_download_url"]
            if not url.startswith(f"https://github.com/{repository}/releases/download/"):
                raise ValueError("Release manifest is outside this repository")
            request = urllib.request.Request(url, headers={"User-Agent": "PPTAvatarEditor-release"})
            with urllib.request.urlopen(request, timeout=30) as response:
                manifest = json.load(response)
            if manifest.get("name") != PACKAGE:
                continue
            if not manifest.get("url") or not manifest.get("zipSHA256"):
                raise ValueError("Published PPAE release is missing its download URL or checksum")
            versions[manifest["version"]] = manifest
    return versions


def build(output, repository, history):
    manifest = json.loads((PACKAGE_ROOT / "package.json").read_text(encoding="utf-8-sig"))
    version = manifest["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?", version):
        raise ValueError("Invalid package version")
    if manifest["name"] != PACKAGE or manifest["displayName"] != "PPTAvatarEditor":
        raise ValueError("Incorrect package identity")
    if any(manifest.get(key) for key in ("legacyFolders", "legacyFiles", "legacyPackages")):
        raise ValueError("The release must not remove another package or legacy asset")
    if "com.vrchat.avatars" not in manifest.get("vpmDependencies", {}):
        raise ValueError("VRChat Avatars dependency is missing")
    if manifest.get("license") != "Zlib":
        raise ValueError("The Zlib license declaration is missing")
    if "zipSHA256" in manifest:
        raise ValueError("zipSHA256 belongs in the listing, not the package itself")
    if package_bytes(ROOT / "LICENSE.txt") != package_bytes(PACKAGE_ROOT / "LICENSE.txt"):
        raise ValueError("Original license notice must be retained unchanged")
    if not (PACKAGE_ROOT / "USING_SOFTWARE_LICENSES.txt").is_file():
        raise ValueError("Bundled software notices are missing")

    tag = "v" + version
    filename = f"{PACKAGE}-{version}.zip"
    expected_url = f"https://github.com/{repository}/releases/download/{tag}/{filename}"
    if manifest["url"] != expected_url:
        raise ValueError(f"Set package.json url to {expected_url}")

    files = sorted(p for p in PACKAGE_ROOT.rglob("*") if p.is_file())
    guids = set()
    for path in files:
        if path.is_symlink() or path.suffix not in ALLOWED_EXTENSIONS:
            raise ValueError(f"Unexpected package file: {path.relative_to(PACKAGE_ROOT)}")
        if path.suffix == ".meta":
            match = re.search(rb"(?m)^guid: ([a-f0-9]{32})$", package_bytes(path))
            if match is None or match[1] in guids:
                raise ValueError(f"Missing or duplicate asset GUID: {path}")
            guids.add(match[1])

    output.mkdir(parents=True, exist_ok=True)
    archive_path = output / filename
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files:
            entry = zipfile.ZipInfo(path.relative_to(PACKAGE_ROOT).as_posix(), (1980, 1, 1, 0, 0, 0))
            entry.create_system = 3
            entry.external_attr = 0o100644 << 16
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, package_bytes(path), compresslevel=9)
    with zipfile.ZipFile(archive_path) as archive:
        if archive.testzip() is not None or "package.json" not in archive.namelist():
            raise ValueError("VPM archive failed integrity checks")
        for path in files:
            if archive.read(path.relative_to(PACKAGE_ROOT).as_posix()) != package_bytes(path):
                raise ValueError("Archive payload differs from source")

    digest = hashlib.sha256(archive_path.read_bytes()).hexdigest()
    published_manifest = {**manifest, "zipSHA256": digest}
    versions = prior_versions(history, repository)
    if version in versions and versions[version] != published_manifest:
        raise ValueError(f"Version {version} is already published with different contents; increment the version")
    versions[version] = published_manifest
    write_json(output / "package.json", published_manifest)

    owner, repo_name = repository.split("/")
    listing_url = f"https://{owner.lower()}.github.io/{repo_name}/index.json"
    website = output / "website"
    website.mkdir(exist_ok=True)
    listing = {
        "name": "PPTAvatarEditor",
        "id": "net.panpacatiltott.pptavatareditor.repository",
        "author": "PanpacaTiltott",
        "url": listing_url,
        "description": "PPTAvatarEditor VPM repository",
        "packages": {PACKAGE: {"versions": versions}},
    }
    write_json(website / "index.json", listing)
    html = (ROOT / "Website" / "index.html").read_text(encoding="utf-8")
    if listing_url not in html:
        raise ValueError("Website repository URL does not match the listing")
    html = re.sub(r'(<span id="version">)[^<]+', lambda m: m[1] + version, html)
    (website / "index.html").write_text(html, encoding="utf-8", newline="\n")
    (website / ".nojekyll").touch()
    for notice in ("LICENSE.txt", "USING_SOFTWARE_LICENSES.txt"):
        shutil.copyfile(PACKAGE_ROOT / notice, website / notice)
    metadata = {"version": version, "tag": tag, "zip": str(archive_path), "zipSHA256": digest,
                "files": len(files), "guids": len(guids), "listingUrl": listing_url}
    write_json(output / "build.json", metadata)
    print(json.dumps(metadata, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "dist" / "vpm")
    parser.add_argument("--repository", default=REPOSITORY)
    parser.add_argument("--history", type=Path, help="GitHub release JSON, including gh api --paginate --slurp output")
    args = parser.parse_args()
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", args.repository):
        parser.error("Invalid repository")
    history = json.loads(args.history.read_text(encoding="utf-8-sig")) if args.history else []
    build(args.output.resolve(), args.repository, history)
