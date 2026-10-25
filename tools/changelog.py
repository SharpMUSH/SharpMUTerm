#!/usr/bin/env python3
"""Keeps CHANGELOG.md honest, and a release tag to it. The version itself is the git tag (MinVer).

  changelog.py check                 the changelog's shape
  changelog.py unreleased-grew <ref>  [Unreleased] has an entry that <ref>'s changelog does not
  changelog.py release-check <tag>   a release tag: v<semver>, with a dated, non-empty section
  changelog.py notes <version>       that version's section, for the release page

Exits 1 with one line per problem. Used by changelog.yml and release.yml; see docs/RELEASING.md.
"""
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHANGELOG = ROOT / "CHANGELOG.md"

# SemVer 2.0 without build metadata (semver.org's own pattern): no leading zeros, no empty identifiers.
PRERELEASE_ID = r"(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)"
SEMVER = (r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
          r"(?:-(" + PRERELEASE_ID + r"(?:\." + PRERELEASE_ID + r")*))?")
BUILD = r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?"
RELEASED = re.compile(r"^## \[(?P<version>" + SEMVER + r")\] - (?P<date>\d{4}-\d{2}-\d{2})$")
SUBSECTIONS = {"Added", "Changed", "Deprecated", "Removed", "Fixed", "Security"}


def key(version):
    """Sort key per SemVer 2.0 precedence: a prerelease sorts before its release."""
    m = re.fullmatch(SEMVER, version)
    if not m:
        raise ValueError(version)
    core = tuple(int(m.group(i)) for i in (1, 2, 3))
    pre = m.group(4)
    if pre is None:
        return core, (1,)
    parts = tuple((0, int(p), "") if p.isdigit() else (1, 0, p) for p in pre.split("."))
    return core, (0,) + parts


def sections():
    """[(heading line, line number, body lines)] for every '## ' heading."""
    result = []
    for number, line in enumerate(CHANGELOG.read_text(encoding="utf-8").splitlines(), 1):
        if line.startswith("## "):
            result.append((line.rstrip(), number, []))
        elif result:
            result[-1][2].append(line.rstrip())
    return result


def has_entries(body):
    return any(line.startswith("- ") for line in body)


def check():
    problems = []
    found = sections()
    if not found or found[0][0] != "## [Unreleased]":
        problems.append("the first '## ' heading must be '## [Unreleased]'")

    previous = None
    for heading, number, body in found:
        where = f"CHANGELOG.md:{number}"
        if heading != "## [Unreleased]":
            m = RELEASED.match(heading)
            if not m:
                problems.append(f"{where}: '{heading}' is not '## [X.Y.Z] - YYYY-MM-DD'")
                continue
            version, date = m["version"], m["date"]
            if previous and key(version) >= key(previous[0]):
                problems.append(f"{where}: {version} is listed below {previous[0]}; newest goes first")
            if previous and date > previous[1]:
                problems.append(f"{where}: {version} is dated {date}, after {previous[0]} ({previous[1]})")
            if not has_entries(body):
                problems.append(f"{where}: {version} has no entries")
            previous = (version, date)
        for line in body:
            if line.startswith("### ") and line[4:] not in SUBSECTIONS:
                problems.append(f"{where}: '{line}' is not one of {', '.join(sorted(SUBSECTIONS))}")

    return problems


def section(version):
    for heading, _, body in sections():
        m = RELEASED.match(heading)
        if m and m["version"] == version:
            return body
    return None


def release_check(tag):
    problems = check()
    m = re.fullmatch(r"v(" + SEMVER + r")" + BUILD, tag)
    if not m:
        return problems + [f"tag {tag} is not v<semantic version>, e.g. v1.2.3 or v1.3.0-beta.1"]
    version = m.group(1)  # build metadata names a build, not a release, so the section is the version's
    body = section(version)
    if body is None:
        problems.append(f"CHANGELOG.md has no '## [{version}] - YYYY-MM-DD' section")
    elif not has_entries(body):
        problems.append(f"CHANGELOG.md's {version} section has no entries")
    return problems


def unreleased_entries(text):
    entries, inside = [], False
    for line in text.splitlines():
        if line.startswith("## "):
            inside = line.rstrip() == "## [Unreleased]"
        elif inside and line.startswith("- "):
            entries.append(line.rstrip())
    return entries


def unreleased_grew(base_ref):
    """A PR that changes what ships adds at least one entry under [Unreleased]."""
    try:
        before = subprocess.run(["git", "show", f"{base_ref}:CHANGELOG.md"], cwd=ROOT, check=True,
                                capture_output=True, text=True).stdout
    except subprocess.CalledProcessError:
        before = ""
    old = set(unreleased_entries(before))
    new = [e for e in unreleased_entries(CHANGELOG.read_text(encoding="utf-8")) if e not in old]
    return [] if new else ["no new entry under '## [Unreleased]' in CHANGELOG.md"]


def main(argv):
    if len(argv) == 2 and argv[1] == "check":
        problems = check()
    elif len(argv) == 3 and argv[1] == "unreleased-grew":
        problems = unreleased_grew(argv[2])
    elif len(argv) == 3 and argv[1] == "release-check":
        problems = release_check(argv[2])
    elif len(argv) == 3 and argv[1] == "notes":
        body = section(argv[2])
        if body is None or not has_entries(body):
            print(f"CHANGELOG.md has no entries for {argv[2]}", file=sys.stderr)
            return 1
        print("\n".join(body).strip())
        return 0
    else:
        print(__doc__, file=sys.stderr)
        return 2

    for problem in problems:
        print(problem, file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
