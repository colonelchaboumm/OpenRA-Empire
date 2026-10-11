"""Run the packaged native game through its existing frontend integration audit.

All changes and settings are confined to a disposable copy inside a build folder.
No developer profile is modified; installed original game resources are read only.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("build", type=Path)
    args = parser.parse_args()
    build = args.build.resolve()
    runtime = build / "smoke-runtime"
    if runtime.exists():
        raise RuntimeError("Le dossier de test existe deja.")
    shutil.copytree(build / "payload", runtime)
    output = build / "smoke-result"
    profile = output / "profile"
    profile.mkdir(parents=True)
    support = Path(os.environ["APPDATA"]) / "OpenRA"
    if not (support / "Content/ra/v2").exists():
        support = Path(os.environ["USERPROFILE"]) / "Documents/OpenRA"
    if not (support / "Content/ra/v2").exists():
        raise RuntimeError("Les ressources Red Alert doivent etre installees pour le test graphique.")
    package = runtime / "mods/medieval-distribution-audit"
    package.mkdir()
    text = (runtime / "mods/medieval/mod.yaml").read_text(encoding="utf-8")
    content = Path(os.path.relpath(support / "Content/ra/v2", runtime)).as_posix()
    text = text.replace("~^SupportDir|Content/ra/v2/", "~^EngineDir|" + content + "/")
    text = text.replace("Rules:\n", "Rules:\n\tmedieval-distribution-audit|rules.yaml\n", 1)
    text = text.replace("\t\t$medieval: medieval\n", "\t\t$medieval: medieval\n\t\t$medieval-distribution-audit: medieval-distribution-audit\n", 1)
    (package / "mod.yaml").write_text(text, encoding="utf-8")
    (package / "rules.yaml").write_text("World:\n\tMedievalFrontendGameValidation:\n", encoding="utf-8")
    command = [str(runtime / "OpenRA.exe"), "--", "Engine.EngineDir=" + str(runtime),
               "Engine.SupportDir=" + str(profile), "Game.Mod=medieval-distribution-audit",
               "Graphics.Mode=Windowed", "Graphics.WindowedSize=1280,800", "Graphics.UIScale=1",
               "Graphics.VSync=False", "Sound.Mute=True", "Debug.CheckVersion=False", "Game.FetchNews=False",
               "Game.ViewportEdgeScroll=False", "Game.IntroductionPromptVersion=2",
               "Debug.SystemInformationVersionPrompt=6", "Medieval.AuditMenu=True",
               "Medieval.AuditOutput=" + str(output)]
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    with (output / "native.log").open("w", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=runtime, stdout=log, stderr=subprocess.STDOUT, startupinfo=startup)
        try:
            code = process.wait(timeout=180)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
            raise RuntimeError("Le test graphique a depasse 180 secondes.")
    result = json.loads((output / "result.json").read_text(encoding="utf-8"))
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if code or not result["passed"]:
        raise RuntimeError("Le test graphique a echoue.")


if __name__ == "__main__":
    main()
