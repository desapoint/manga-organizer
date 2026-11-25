"""Reconcile retained pinned evidence through a selected audit pass (14–17)."""

import argparse
import csv
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path, PurePosixPath
from urllib.parse import unquote

AUDIT = Path(__file__).resolve().parent
PIN_REF = re.compile(
    r"https://github\.com/([^/\s]+/[^/\s]+)/(?:blob|tree)/"
    r"([0-9a-f]{40})/([^\s#\"')\x60<>;,|?]+)",
    re.IGNORECASE,
)
CATEGORIES = {
    "source candidate": "Implementation",
    "test candidate": "Test or example",
    "build/deploy": "Build or deployment",
    "config/resource": "Configuration or resource",
    "documentation": "Documentation",
    "other": "Asset or other",
}
FOLLOW_UP = {
    "Implementation": "Trace entry points, consumers, behavior, and tests",
    "Test or example": "Review assertions and execution prerequisites",
    "Build or deployment": "Review build inputs, shipping scope, and qualify target",
    "Configuration or resource": "Trace defaults, consumers, permissions, and persistence",
    "Documentation": "Check claims against pinned source and releases",
    "Asset or other": "Identify purpose, provenance, and redistribution terms",
    "Vendored dependency candidate": "Confirm dependency boundary, origin, version, and license",
    "Generated candidate": "Confirm generator and input, compare output contract",
}


def read_rows(path):
    with path.open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source, strict=True))


def citations(path):
    return PIN_REF.findall(path.read_text(encoding="utf-8-sig"))


def module_and_role(path, classification):
    parts = PurePosixPath(path).parts
    folders = parts[:-1]
    lower_folders = tuple(part.lower() for part in folders)
    name = parts[-1].lower()
    for i, folder in enumerate(lower_folders):
        if folder in {"node_modules", "vendor", "vendored", "third_party", "third-party"}:
            return "/".join(folders[:i + 1]), "Vendored dependency candidate"
    if (
        {"generated", "__generated__"}.intersection(lower_folders)
        or name.endswith((".designer.cs", ".g.cs", ".generated.cs", ".generated.ts",
                          ".min.js", ".min.css"))
    ):
        role = "Generated candidate"
    else:
        role = CATEGORIES[classification]

    # Retain functional packages below common language-source directories.
    depth = 3
    if "src" in folders:
        i = folders.index("src")
        after_src = folders[i + 1:]
        if len(after_src) >= 2 and after_src[0] in {"main", "test", "androidTest"}:
            depth = i + 4
            if after_src[1] in {"java", "kotlin"}:
                depth = i + 3
                while depth < len(folders) and folders[depth] in {
                    "android", "app", "eu", "kanade", "mihon", "suwayomi", "tachidesk",
                    "tachiyomi", "xyz", "nulldev",
                }:
                    depth += 1
                depth += 2
        else:
            depth = max(3, i + 3)
    return "/".join(folders[:depth]) or "(root)", role


def main(output, missing_repo, through_pass):
    registry = (AUDIT / "README.md").read_text(encoding="utf-8")
    repo_ids = {
        slug.casefold(): (repo, sha)
        for slug, sha, repo in re.findall(
            r"https://github\.com/([^/]+/[^/]+)/tree/([0-9a-f]{40})\)\s*\|"
            r"[^\n]+?\[([A-Z]{2})\]\(", registry
        )
    }
    if len(repo_ids) != 7:
        raise ValueError("Expected seven pinned upstream identities")
    trees = {
        repo: {row["Path"]: row for row in read_rows(AUDIT / "index" / f"{repo}-files.csv")}
        for repo, _ in repo_ids.values()
    }
    baseline = {
        (row["Repository"], row["Pinned tracked path"])
        for row in read_rows(AUDIT / "EVIDENCE-PATH-INDEX.csv")
    }
    if len(baseline) != 329 or any(path not in trees[repo] for repo, path in baseline):
        raise ValueError("Historical evidence baseline changed")

    historical_files = []
    for path in sorted((*AUDIT.glob("*.md"), *AUDIT.glob("*.csv"),
                        *AUDIT.parent.glob("*.md"), *AUDIT.parent.parent.glob("*.md"))):
        numbered_pass = re.match(r"^PASS(\d+)[.-]", path.name)
        if numbered_pass and int(numbered_pass[1]) >= 14:
            continue
        historical_files.append(path)
    citations_by_path = defaultdict(set)
    excluded = defaultdict(set)
    evidence_files = [AUDIT / f"PASS{n}-EVIDENCE.csv" for n in range(14, through_pass + 1)]
    for source in [*historical_files, *evidence_files]:
        for slug, sha, raw_path in citations(source):
            if slug.casefold() not in repo_ids:
                continue
            repo, pin = repo_ids[slug.casefold()]
            path = unquote(raw_path)
            if sha != pin:
                excluded["different revision"].add((repo, sha, path))
            elif path not in trees[repo]:
                excluded["missing exact file match"].add((repo, sha, path))
            else:
                citations_by_path[repo, path].add(source.name)

    before = baseline | {
        key for key, files in citations_by_path.items() if files - {path.name for path in evidence_files}
    }
    covered = baseline | citations_by_path.keys()
    writer = csv.writer(output, lineterminator="\n")
    if missing_repo:
        if missing_repo not in trees:
            raise ValueError(f"Unknown upstream repository: {missing_repo}")
        writer.writerow(("Repository", "Module", "Pinned tracked path"))
        for path, record in sorted(trees[missing_repo].items()):
            module, role = module_and_role(path, record["Classification"])
            if role == "Implementation" and (missing_repo, path) not in covered:
                writer.writerow((missing_repo, module, path))
        return

    bucket_counts = defaultdict(Counter)
    for repo, tree in trees.items():
        for path, record in tree.items():
            module, role = module_and_role(path, record["Classification"])
            bucket = bucket_counts[repo, module, role]
            bucket["tracked"] += 1
            bucket["bytes"] += int(record["Bytes"] or 0)
            bucket["evidence"] += (repo, path) in covered

    writer.writerow(("Repository", "Module", "Inventory role", "Tracked paths",
                     "Paths with evidence", "Paths without evidence", "Tracked bytes",
                     "Evidence disposition", "Remaining review"))
    for (repo, module, role), bucket in sorted(bucket_counts.items()):
        tracked, evidenced = bucket["tracked"], bucket["evidence"]
        if evidenced == 0:
            status = "Inventory only"
        elif evidenced == tracked:
            status = "Each path cited; review depth open"
        else:
            status = "Some paths cited; review depth open"
        writer.writerow((repo, module, role, tracked, evidenced, tracked - evidenced,
                         bucket["bytes"], status, FOLLOW_UP[role]))

    print("Repo Tracked Pass8 Through13 ThroughSelectedPass Impl ImplCited ImplNoCitation", file=sys.stderr)
    for repo, tree in sorted(trees.items()):
        counts = [sum(key[0] == repo for key in paths) for paths in (baseline, before, covered)]
        implementation = [v for key, v in bucket_counts.items()
                          if key[0] == repo and key[2] == "Implementation"]
        total = sum(v["tracked"] for v in implementation)
        cited = sum(v["evidence"] for v in implementation)
        print(repo, len(tree), *counts, total, cited, total - cited, file=sys.stderr)
    print(f"{len(bucket_counts)} rows; {len(covered)} cited paths", file=sys.stderr)
    for reason, keys in sorted(excluded.items()):
        print(f"Excluded {len(keys)} distinct targets ({reason})", file=sys.stderr)
        if reason == "missing exact file match":
            for repo, _, path in sorted(keys):
                print(f"  {repo} {path}", file=sys.stderr)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="Write CSV here instead of standard output")
    parser.add_argument("--missing-implementation", metavar="REPO",
                        help="List exact implementation paths without citations for one upstream")
    parser.add_argument("--through-pass", type=int, default=17, choices=range(14, 18))
    args = parser.parse_args()
    if args.output is None:
        main(sys.stdout, args.missing_implementation, args.through_pass)
    else:
        with args.output.open("w", encoding="utf-8", newline="") as output:
            main(output, args.missing_implementation, args.through_pass)
