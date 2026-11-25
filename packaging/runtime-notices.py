"""Copy the notices supplied with the runtime packs used by a self-contained build."""

import json
from pathlib import Path
from shutil import copyfile
import subprocess
import sys


rid, output = sys.argv[1:]
report = subprocess.check_output([
    "dotnet", "msbuild", "src/MangaOrganizer.Server", "-nologo", "-target:ResolveFrameworkReferences",
    "-getItem:ResolvedRuntimePack", f"-p:RuntimeIdentifier={rid}", "-p:SelfContained=true", "-p:Configuration=Release",
], encoding="utf-8-sig")
runtime_packs = json.loads(report)["Items"]["ResolvedRuntimePack"]
destination = Path(output) / "runtime-notices"
destination.mkdir(parents=True, exist_ok=True)
if len(runtime_packs) != 2:
    raise ValueError(f"Expected .NET and ASP.NET Core runtime packs for {rid}")

for package in runtime_packs:
    directory = Path(package["PackageDirectory"])
    notices = [file for file in directory.iterdir()
               if file.name.lower().startswith(("license", "third-party-notices", "thirdpartynotices")) and file.is_file()]
    if not notices:
        raise FileNotFoundError(f"No license or notices found in {directory}")
    for notice in notices:
        copyfile(notice, destination / f"{package['NuGetPackageId']}-{notice.name}")
