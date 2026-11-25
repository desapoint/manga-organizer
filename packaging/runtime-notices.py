"""Copy the notices supplied with the runtime packs used by a self-contained build."""

import json
from pathlib import Path
from shutil import copyfile
import os
import sys


output = Path(sys.argv[1])
dependencies = json.loads((output / "MangaOrganizer.Server.deps.json").read_text(encoding="utf-8-sig"))
runtime_packs = [library.removeprefix("runtimepack.").rsplit("/", 1)
                 for library in dependencies["libraries"] if library.startswith("runtimepack.")]
if not runtime_packs:
    raise ValueError("The published server dependency manifest lists no runtime packs.")

assets_file = Path("src/MangaOrganizer.Server/obj/project.assets.json")
assets = json.loads(assets_file.read_text(encoding="utf-8-sig"))
roots = [Path(path) for path in assets["packageFolders"]]
if dotnet_root := os.environ.get("DOTNET_ROOT"):
    roots.append(Path(dotnet_root) / "packs")

destination = output / "runtime-notices"
destination.mkdir(parents=True, exist_ok=True)
for package, version in runtime_packs:
    for root in roots:
        directory = root / package.lower() / version
        if not directory.is_dir():
            directory = root / package / version
        if not directory.is_dir():
            continue
        notices = [file for file in directory.iterdir() if file.is_file() and
                   file.name.lower().startswith(("license", "third-party-notices", "thirdpartynotices"))]
        if notices:
            break
    else:
        raise FileNotFoundError(f"No runtime license or notices for {package}/{version} in {roots}")
    for notice in notices:
        copyfile(notice, destination / f"{package}-{notice.name}")
