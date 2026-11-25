"""Copy the notices supplied with the runtime packs used by a self-contained build."""

import json
from pathlib import Path
from shutil import copyfile
import sys


rid, output = sys.argv[1:]
assets_file = Path("src/MangaOrganizer.Server/obj/project.assets.json")
assets = json.loads(assets_file.read_text(encoding="utf-8-sig"))
framework = assets["project"]["frameworks"]["net10.0"]
runtime_packs = [package for package in framework["downloadDependencies"]
                 if package["name"].lower().endswith(f".app.runtime.{rid}")]
destination = Path(output) / "runtime-notices"
destination.mkdir(parents=True, exist_ok=True)
if len(runtime_packs) != 2:
    raise ValueError(f"Expected .NET and ASP.NET Core runtime packs for {rid}")

for package in runtime_packs:
    version = package["version"].strip("[]").split(",", 1)[0].strip()
    for root in assets["packageFolders"]:
        directory = Path(root) / package["name"].lower() / version
        if directory.is_dir():
            break
    else:
        raise FileNotFoundError(package["name"])
    notices = [file for file in directory.iterdir()
               if file.name.lower().startswith(("license", "third-party-notices", "thirdpartynotices")) and file.is_file()]
    if not notices:
        raise FileNotFoundError(f"No license or notices found in {directory}")
    for notice in notices:
        copyfile(notice, destination / f"{package['name']}-{notice.name}")
