#!/usr/bin/env python3
"""Build and publish OpenRA Empire; Python standard library + .NET 10 + git + gh.

Builds use a frozen, explicitly selected source snapshot. No player data, local
SDK, previous binaries, analysis captures, credentials or private settings ship.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.request
import uuid
import zipfile

ROOT = Path(__file__).resolve().parent.parent
REPOSITORY = "colonelchaboumm/OpenRA-Empire"
DOWNLOAD = f"https://github.com/{REPOSITORY}/releases/download/"
WORK = ROOT / ".distribution"
MODS = ("common", "common-content", "ra", "ra-content", "medieval")
SKIP_DIRS = {"bin", "obj", "__pycache__", ".git", ".vs", "TestResults"}
ROOT_FILES = {
    "AUTHORS", "COPYING", "Directory.Build.props", "OpenRA.slnx", "VERSION",
    "global mix database.dat", ".editorconfig", ".gitattributes", "INSTALL.md",
    "CONTRIBUTING.md", "CODE_OF_CONDUCT.md", "Makefile", "make.cmd", "make.ps1",
    "configure-system-libraries.sh", "fetch-geoip.sh", "utility.cmd", "utility.sh",
    "launch-game.cmd", "launch-game.sh", "launch-dedicated.cmd", "launch-dedicated.sh",
    "launch-medieval.cmd", "launch-medieval-multiplayer.cmd", "launch-medieval-immense.cmd",
    "build-medieval.ps1", "Publier-une-mise-a-jour.cmd",
}


def run(args, cwd=ROOT, capture=False, env=None):
    print(">", " ".join(str(arg) for arg in args), flush=True)
    result = subprocess.run([str(arg) for arg in args], cwd=cwd, check=True,
                            text=True, encoding="utf-8", errors="replace",
                            stdout=subprocess.PIPE if capture else None, env=env)
    return result.stdout.strip() if capture else None


def digest(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def download(url, destination):
    request = urllib.request.Request(url, headers={"User-Agent": "OpenRA-Empire-Publisher/1.0"})
    with urllib.request.urlopen(request, timeout=60) as response, open(destination, "wb") as stream:
        shutil.copyfileobj(response, stream)


def latest_manifest():
    url = f"https://github.com/{REPOSITORY}/releases/latest/download/empire-manifest.json"
    request = urllib.request.Request(url, headers={"User-Agent": "OpenRA-Empire-Publisher/1.0"})
    try:
        with urllib.request.urlopen(request, timeout=45) as response:
            result = json.load(response)
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise
    if result["schema"] != 1 or result["repository"] != REPOSITORY:
        raise RuntimeError("Le manifeste public ne correspond pas au projet.")
    return result


def version_tuple(value):
    if not re.fullmatch(r"\d+\.\d+\.\d+", value):
        raise ValueError("Utilisez une version comme 1.0.1.")
    return tuple(int(part) for part in value.split("."))


def source_files():
    for path in sorted(ROOT.iterdir()):
        if path.is_file() and path.name in ROOT_FILES:
            yield path
        elif path.is_dir() and (path.name.startswith("OpenRA.") or
                                path.name in {"mods", "glsl", "tools", "packaging", "distribution"}):
            for folder, dirs, files in os.walk(path):
                dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS)
                for name in sorted(files):
                    file = Path(folder) / name
                    if file.suffix.lower() in {".log", ".pyc", ".user", ".tmp"}:
                        continue
                    if file.is_symlink():
                        raise RuntimeError(f"Lien inattendu dans les sources : {file}")
                    if file.stat().st_size > 95 * 1024 * 1024:
                        raise RuntimeError(f"Fichier trop volumineux pour Git : {file}")
                    if name.startswith(".env") or name.lower() in {"credentials", "id_rsa", "id_ed25519"}:
                        raise RuntimeError(f"Fichier prive refuse : {file}")
                    yield file


def snapshot(destination):
    destination.mkdir(parents=True)
    for file in source_files():
        target = destination / file.relative_to(ROOT)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(file, target)
    upstream_readme = ROOT / "README.OpenRA.md"
    shutil.copy2(upstream_readme if upstream_readme.exists() else ROOT / "README.md", destination / "README.OpenRA.md")
    shutil.copy2(ROOT / "distribution" / "README.public.md", destination / "README.md")
    shutil.copy2(ROOT / "distribution" / "source.gitignore", destination / ".gitignore")
    workflow = destination / ".github" / "workflows" / "empire-checks.yml"
    workflow.parent.mkdir(parents=True)
    shutil.copy2(ROOT / "distribution" / "empire-checks.yml", workflow)


def inventory(directory):
    return [{"path": file.relative_to(directory).as_posix(),
             "size": file.stat().st_size, "sha256": digest(file)}
            for file in sorted(directory.rglob("*")) if file.is_file()]


def make_zip(output, directory, paths=None):
    paths = paths if paths is not None else [f["path"] for f in inventory(directory)]
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name in sorted(paths):
            info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            with archive.open(info, "w") as target, open(directory / name, "rb") as source:
                shutil.copyfileobj(source, target)


def asset(path, version):
    if path.stat().st_size >= 2 * 1024**3:
        raise RuntimeError("Une archive depasse la limite GitHub de 2 Gio.")
    return {"url": DOWNLOAD + "v" + version + "/" + path.name,
            "size": path.stat().st_size, "sha256": digest(path)}


def build(version, previous):
    version_tuple(version)
    if previous and version_tuple(version) <= version_tuple(previous["version"]):
        raise RuntimeError("La version doit etre superieure a la derniere version publique.")
    directory = WORK / ("build-" + version + "-" + uuid.uuid4().hex[:8])
    directory.mkdir(parents=True)
    source = directory / "source"
    snapshot(source)
    payload = directory / "payload"
    payload.mkdir()
    output = directory / "assets"
    output.mkdir()
    dotnet = ROOT / ".tools" / "dotnet" / "dotnet.exe"
    if not dotnet.exists():
        dotnet = shutil.which("dotnet")
    if not dotnet:
        raise RuntimeError("Le SDK .NET 10 est necessaire pour publier.")
    env = os.environ.copy()
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["ENGINE_DIR"] = str(source)
    run(["powershell", "-NoProfile", "-File", source / "distribution/tests/Test-Updater.ps1",
         "-OutputDirectory", directory / "updater-tests"], cwd=source)
    run([dotnet, "test", "OpenRA.Test/OpenRA.Test.csproj", "-c", "Release", "--nologo",
         "-v", "minimal", f"-p:OutputPath={directory / 'test-bin'}"], cwd=source, env=env)
    run([dotnet, "publish", "OpenRA.slnx", "-c", "Release", "-r", "win-x64",
         "--self-contained", "true", "--nologo", "-v", "minimal",
         f"-p:PublishDir={payload}", f"-p:OutputPath={directory / 'compile-bin'}",
         "-p:TargetPlatform=win-x64", "-p:CopyGenericLauncher=True",
         "-p:Deterministic=true", f"-p:PathMap={source}=.",
         "-p:DebugType=None", "-p:DebugSymbols=false"], cwd=source, env=env)
    for name in ("AUTHORS", "COPYING", "global mix database.dat"):
        shutil.copy2(source / name, payload / name)
    shutil.copytree(source / "glsl", payload / "glsl")
    for mod in MODS:
        shutil.copytree(source / "mods" / mod, payload / "mods" / mod,
                        ignore=shutil.ignore_patterns("art-source", "tools", "__pycache__"))
    (payload / "VERSION").write_text("empire-" + version + "\n", encoding="utf-8")
    for mod in MODS:
        config = payload / "mods" / mod / "mod.yaml"
        if config.exists():
            text = config.read_text(encoding="utf-8").replace("{DEV_VERSION}", "empire-" + version)
            if mod == "medieval":
                text = text.replace("Website: https://www.openra.net", f"Website: https://github.com/{REPOSITORY}")
            if mod == "ra-content":
                # This distribution launches Medieval; return there after installing
                # the shared Red Alert resources on a player's first run.
                text = text.replace("\tMod: ra\n", "\tMod: medieval\n")
            config.write_text(text, encoding="utf-8")
    (payload / "updater").mkdir()
    run(["powershell", "-NoProfile", "-File", source / "distribution/Build.Launcher.ps1",
         "-Output", payload / "updater/Empire.Launcher.exe"], cwd=source)
    required = ["OpenRA.exe", "OpenRA.Mods.Medieval.dll", "OpenRA.Mods.Cnc.dll",
                "OpenRA.Platforms.Default.dll", "coreclr.dll", "lua51.dll", "SDL2.dll"]
    for name in required:
        if not (payload / name).exists():
            raise RuntimeError(f"Dependance absente du paquet : {name}")
    env["ENGINE_DIR"] = str(payload)
    run([payload / "OpenRA.Utility.exe", "medieval", "--check-yaml"], cwd=payload, env=env)
    files = inventory(payload)
    full = output / f"empire-{version}-full.zip"
    make_zip(full, payload, [f["path"] for f in files])
    delta = None
    changed = len(files)
    if previous:
        old = {f["path"]: f for f in previous["files"]}
        paths = [f["path"] for f in files if old.get(f["path"], {}).get("sha256") != f["sha256"]]
        changed = len(paths)
        delta_path = output / f"empire-{version}-patch.zip"
        make_zip(delta_path, payload, paths)
        delta = asset(delta_path, version)
    manifest = {"schema": 1, "repository": REPOSITORY, "version": version,
                "previous": previous["version"] if previous else None,
                "full": asset(full, version), "delta": delta, "files": files}
    write_json(output / "empire-manifest.json", manifest)
    write_json(payload / "release-manifest.json", manifest)
    portable = directory / "portable"
    portable.mkdir()
    run(["powershell", "-NoProfile", "-File", source / "distribution/Build.Bootstrap.ps1",
         "-Output", portable / "Empire.exe"], cwd=source)
    write_json(portable / "current.json", {"version": version, "previous": None})
    shutil.copy2(source / "distribution/LIRE-MOI.txt", portable / "LIRE-MOI.txt")
    (portable / "versions").mkdir()
    shutil.copytree(payload, portable / "versions" / version)
    installer = output / f"OpenRA-Empire-{version}-Windows-x64.zip"
    make_zip(installer, portable)
    # Project intermediate files are generated inside the frozen snapshot. They
    # are build outputs, not corresponding source, and must not reach GitHub.
    for project in source.glob("OpenRA.*"):
        intermediate = project / "obj"
        if intermediate.is_dir() and intermediate.resolve().is_relative_to(source.resolve()):
            shutil.rmtree(intermediate)
    make_zip(output / f"OpenRA-Empire-{version}-sources.zip", source)
    checksums = output / "SHA256SUMS.txt"
    checksums.write_text("".join(f"{digest(file)}  {file.name}\n" for file in sorted(output.iterdir())), encoding="utf-8")
    report = {"version": version, "build": str(directory), "files": len(files), "changed_files": changed,
              "full_bytes": full.stat().st_size, "patch_bytes": delta["size"] if delta else None,
              "source_inventory": inventory(source)}
    write_json(directory / "build-report.json", report)
    write_json(WORK / "last-build.json", {"directory": str(directory), "version": version})
    print(f"\nVersion {version} preparee : {installer}\n{changed} fichiers modifies.", flush=True)
    return directory


def sync_repository(source, version):
    mirror = WORK / "repository"
    if not mirror.exists():
        run(["gh", "repo", "clone", REPOSITORY, mirror])
    remote = run(["git", "remote", "get-url", "origin"], cwd=mirror, capture=True)
    if remote not in {f"https://github.com/{REPOSITORY}.git", f"git@github.com:{REPOSITORY}.git"}:
        raise RuntimeError("Depot de publication inattendu.")
    run(["git", "fetch", "origin"], cwd=mirror)
    remote_head = subprocess.run(["git", "rev-parse", "--verify", "origin/main"], cwd=mirror,
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode
    if remote_head == 0:
        run(["git", "merge", "--ff-only", "origin/main"], cwd=mirror)
    tracking = mirror / ".empire-source-files.json"
    owned = json.loads(tracking.read_text(encoding="utf-8")) if tracking.exists() else []
    paths = []
    for file in sorted(source.rglob("*")):
        if not file.is_file():
            continue
        relative = file.relative_to(source)
        paths.append(relative.as_posix())
        target = mirror / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(file, target)
    for removed in set(owned) - set(paths):
        target = (mirror / removed).resolve()
        if not target.is_relative_to(mirror.resolve()) or ".git" in Path(removed).parts:
            raise RuntimeError("Chemin de synchronisation refuse.")
        if target.is_file():
            target.unlink()
    write_json(tracking, paths)
    identity = json.loads(run(["gh", "api", "user", "--jq", "{login: .login, id: .id}"], capture=True))
    run(["git", "config", "user.name", identity["login"]], cwd=mirror)
    run(["git", "config", "user.email", f"{identity['id']}+{identity['login']}@users.noreply.github.com"], cwd=mirror)
    run(["git", "add", "--all"], cwd=mirror)
    changed = subprocess.run(["git", "diff", "--cached", "--quiet"], cwd=mirror).returncode
    if changed:
        run(["git", "commit", "-m", f"Publish OpenRA Empire {version} with differential updates"], cwd=mirror)
    run(["git", "push", "origin", "HEAD:main"], cwd=mirror)
    return run(["git", "rev-parse", "HEAD"], cwd=mirror, capture=True)


def publish(directory, notes=None):
    directory = directory.resolve()
    if not directory.is_relative_to(WORK.resolve()):
        raise RuntimeError("La compilation doit se trouver dans .distribution.")
    report = json.loads((directory / "build-report.json").read_text(encoding="utf-8"))
    version = report["version"]
    output = directory / "assets"
    manifest = json.loads((output / "empire-manifest.json").read_text(encoding="utf-8"))
    latest = latest_manifest()
    if (latest or {}).get("version") != manifest["previous"]:
        raise RuntimeError("Une version a ete publiee depuis cette compilation. Recompilez pour repartir de la bonne version.")
    if inventory(directory / "source") != report["source_inventory"]:
        raise RuntimeError("Les sources ont change apres compilation. Recompilez.")
    for line in (output / "SHA256SUMS.txt").read_text(encoding="utf-8").splitlines():
        expected, name = line.split("  ", 1)
        if digest(output / name) != expected:
            raise RuntimeError(f"Archive modifiee apres verification : {name}")
    commit = sync_repository(directory / "source", version)
    notes_path = directory / "release-notes.md"
    text = f"# OpenRA Empire {version} — Les Terres du Bourg\n\n"
    if notes:
        text += Path(notes).read_text(encoding="utf-8") + "\n\n"
    text += (f"**Pour jouer sous Windows 64 bits :** telechargez `OpenRA-Empire-{version}-Windows-x64.zip`, "
             "extrayez-le completement, puis ouvrez **Empire.exe**.\n\n"
             "**Jeu deja installe :** ouvrez votre lanceur habituel et cliquez sur **Mettre a jour et jouer**. "
             "Les sauvegardes et reglages restent dans le dossier de donnees OpenRA.\n\n"
             "Au premier lancement, le gestionnaire OpenRA peut proposer les ressources Red Alert necessaires. "
             ".NET est inclus. Aucun compte GitHub n'est necessaire pour jouer.\n\n"
             "Prototype en cours de developpement. La compatibilite des anciennes sauvegardes depend des modifications du jeu. "
             "Les parties multijoueurs demandent la meme version chez tous les joueurs.\n\n"
             "Les archives `full`, `patch` et le manifeste servent au lanceur. "
             "Le code source correspondant et la licence GPL-3.0-or-later sont fournis.\n")
    notes_path.write_text(text, encoding="utf-8")
    tag = "v" + version
    existing = subprocess.run(["gh", "release", "view", tag, "--repo", REPOSITORY, "--json", "isDraft"],
                              capture_output=True, text=True)
    if existing.returncode == 0:
        if not json.loads(existing.stdout)["isDraft"]:
            raise RuntimeError("Cette version est deja publique ; elle ne sera pas remplacee.")
        run(["gh", "release", "upload", tag, "--repo", REPOSITORY, "--clobber", *sorted(output.iterdir())])
        run(["gh", "release", "edit", tag, "--repo", REPOSITORY, "--target", commit, "--notes-file", notes_path])
    else:
        run(["gh", "release", "create", tag, "--repo", REPOSITORY, "--draft", "--target", commit,
             "--title", "OpenRA Empire " + version, "--notes-file", notes_path, *sorted(output.iterdir())])
    run(["gh", "release", "edit", tag, "--repo", REPOSITORY, "--draft=false", "--latest"])
    print(f"\nPublication terminee : https://github.com/{REPOSITORY}/releases/tag/{tag}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["build", "publish"])
    parser.add_argument("--version", help="Exemple : 1.0.1 ; sinon increment automatique")
    parser.add_argument("--prepared", type=Path, help="Publier une compilation deja testee")
    parser.add_argument("--notes", type=Path, help="Fichier texte des nouveautes")
    args = parser.parse_args()
    if args.prepared:
        if args.command != "publish":
            parser.error("--prepared s'utilise avec publish")
        publish(args.prepared, args.notes)
        return
    previous = latest_manifest()
    version = args.version
    if not version:
        if previous:
            major, minor, patch = version_tuple(previous["version"])
            version = f"{major}.{minor}.{patch + 1}"
        else:
            version = "1.0.0"
    directory = build(version, previous)
    if args.command == "publish":
        publish(directory, args.notes)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"\nECHEC : {error}", file=sys.stderr)
        sys.exit(1)
