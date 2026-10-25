#!/usr/bin/env python3
"""Keeps CHANGELOG.md and the version in Directory.Build.props honest.

  changelog.py check                 the changelog's shape, and the version against it
  changelog.py version               the version Directory.Build.props declares
  changelog.py release-check <tag>   a release tag: v<version>, with a dated, non-empty section
  changelog.py notes <version>       that version's section, for the release page

Exits 1 with one line per problem. Used by ci.yml and release.yml; see docs/RELEASING.md.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CHANGELOG = ROOT / "CHANGELOG.md"
PROPS = ROOT / "Directory.Build.props"

SEMVER = r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?"
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


def props_version():
    m = re.search(r"<VersionPrefix>([^<]+)</VersionPrefix>", PROPS.read_text(encoding="utf-8"))
    if not m:
        sys.exit(f"{PROPS.name}: no <VersionPrefix>")
    return m.group(1).strip()


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

    version = props_version()
    try:
        key(version)
    except ValueError:
        problems.append(f"{PROPS.name}: '{version}' is not a semantic version")
    else:
        latest = next((RELEASED.match(h)["version"] for h, _, _ in found if RELEASED.match(h)), None)
        if latest and key(version) < key(latest):
            problems.append(f"{PROPS.name}: {version} is older than the last release in CHANGELOG.md, {latest}")
    return problems


def section(version):
    for heading, _, body in sections():
        m = RELEASED.match(heading)
        if m and m["version"] == version:
            return body
    return None


def release_check(tag):
    problems = check()
    version = props_version()
    if tag != f"v{version}":
        problems.append(f"tag {tag} does not match {PROPS.name}'s version; it should be v{version}")
    body = section(tag[1:] if tag.startswith("v") else tag)
    if body is None:
        problems.append(f"CHANGELOG.md has no '## [{tag.lstrip('v')}] - YYYY-MM-DD' section")
    elif not has_entries(body):
        problems.append(f"CHANGELOG.md's {tag.lstrip('v')} section has no entries")
    return problems


def main(argv):
    if len(argv) == 2 and argv[1] == "check":
        problems = check()
    elif len(argv) == 2 and argv[1] == "version":
        print(props_version())
        return 0
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
