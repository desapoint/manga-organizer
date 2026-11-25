"""Generate the Compose bundle and Unraid template for a specific release image."""

import argparse
from pathlib import Path
from xml.sax.saxutils import escape
from zipfile import ZIP_DEFLATED, ZipFile


def build(output, registry_image, archive_image, project_url):
    template_dir = Path(__file__).resolve().parent
    project_owner, project_name = project_url.rsplit("/", 2)[-2:]
    package_url = f"https://github.com/{project_owner}/{project_name}/pkgs/container/{project_name}"
    output.mkdir(parents=True, exist_ok=True)
    environment = (template_dir / ".env.example").read_text(encoding="utf-8")
    environment = environment.replace("__REGISTRY_IMAGE__", registry_image).replace("__ARCHIVE_IMAGE__", archive_image)
    with ZipFile(output / "MangaOrganizer-docker-install.zip", "w", compression=ZIP_DEFLATED) as archive:
        archive.writestr(".env.example", environment)
        archive.write(template_dir / "compose.yaml", "compose.yaml")
        archive.write(template_dir.parents[1] / "docs" / "INSTALL.md", "INSTALL.md")

    unraid = (template_dir / "unraid.xml.in").read_text(encoding="utf-8")
    for marker, value in {"__REGISTRY_IMAGE__": registry_image, "__PACKAGE_URL__": package_url,
                          "__PROJECT_URL__": project_url}.items():
        unraid = unraid.replace(marker, escape(value))
    (output / "MangaOrganizer-unraid.xml").write_text(unraid, encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("registry_image")
    parser.add_argument("archive_image")
    parser.add_argument("project_url")
    args = parser.parse_args()
    build(args.output, args.registry_image, args.archive_image, args.project_url)
