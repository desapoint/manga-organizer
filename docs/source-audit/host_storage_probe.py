"""Check hardlinks and no-clobber behavior in temporary folders on chosen roots."""

import argparse
import errno
import json
import os
import platform
import shutil
from contextlib import ExitStack
from pathlib import Path
from tempfile import TemporaryDirectory


def file_identity(path):
    stat = path.stat()
    return {"device": stat.st_dev, "inode": stat.st_ino, "links": stat.st_nlink}


def probe(source_dir, target_dir):
    source = source_dir / "fixture.cbz"
    source.write_bytes(b"synthetic source bytes")
    identical = source_dir / "copy.cbz"
    shutil.copyfile(source, identical)
    assert source.read_bytes() == identical.read_bytes()
    occupied = target_dir / "occupied.cbz"
    occupied.write_bytes(b"synthetic destination to preserve")
    link = target_dir / "linked.cbz"
    report = {
        "identical_copy_shares_identity": os.path.samefile(source, identical),
        "source_before": file_identity(source),
    }
    try:
        os.link(source, link)
    except OSError as error:
        report["link_status"] = errno.errorcode.get(error.errno, str(error.errno))
    else:
        report["link_status"] = "created"
        report["link_shares_identity"] = os.path.samefile(source, link)
        report["link_identity"] = file_identity(link)
        assert link.read_bytes() == source.read_bytes()
    try:
        os.link(source, occupied)
    except OSError as error:
        report["occupied_target_status"] = errno.errorcode.get(error.errno, str(error.errno))
    else:
        raise RuntimeError("A hardlink unexpectedly replaced an occupied destination")
    assert occupied.read_bytes() == b"synthetic destination to preserve"
    report["source_after"] = file_identity(source)
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_root", type=Path)
    parser.add_argument("target_root", nargs="?", type=Path)
    args = parser.parse_args()
    with ExitStack() as stack:
        source = Path(stack.enter_context(TemporaryDirectory(prefix=".manga-audit-", dir=args.source_root)))
        results = {"platform": platform.platform(), "same_root": probe(source, source)}
        if args.target_root is not None:
            other_source = Path(stack.enter_context(TemporaryDirectory(prefix=".manga-audit-", dir=args.source_root)))
            target = Path(stack.enter_context(TemporaryDirectory(prefix=".manga-audit-", dir=args.target_root)))
            results["between_roots"] = probe(other_source, target)
        print(json.dumps(results, indent=2))
