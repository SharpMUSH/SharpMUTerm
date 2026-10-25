# Releasing SharpMUTerm

Two files hold the release record, and the build refuses to ship when they disagree:

- **`Directory.Build.props`** — `<VersionPrefix>`, the one place the version is written. Every assembly,
  both executables and the installer take it from there.
- **`CHANGELOG.md`** — [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Newest first; an
  `## [Unreleased]` section on top; each release `## [X.Y.Z] - YYYY-MM-DD` with entries under `Added`,
  `Changed`, `Deprecated`, `Removed`, `Fixed` or `Security`.

## Rules, and what enforces them

| Rule | Enforced by |
|---|---|
| The changelog has `[Unreleased]` first, release headings are `[X.Y.Z] - YYYY-MM-DD`, newest first by version and by date, no empty releases, only the six subsection names | `tools/changelog.py check` in the **Changelog** workflow, on every PR and push to `main` |
| The version in `Directory.Build.props` is never older than the last release in the changelog | same |
| A PR that changes `src/`, `packaging/` or `Directory.*.props` adds a line under `[Unreleased]` | **Changelog** workflow; label the PR `skip-changelog` when no user could notice the change |
| A release tag is exactly `v<VersionPrefix>` and the changelog has a dated, non-empty section for it | `tools/changelog.py release-check` in **Release**, before anything is built |
| The release page's notes are that section, verbatim | **Release** (`changelog.py notes`) |

## Versions

[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, a minor bump (0.**2**.0) may break
configuration or keys; a patch bump (0.1.**1**) only fixes. A config schema migration is always at least a
minor bump and is named in the changelog. A prerelease (`0.2.0-beta.1`) is published as a GitHub prerelease.

## Cutting a release

1. Open a PR that:
   - renames `## [Unreleased]` to `## [X.Y.Z] - <today>` and puts a fresh, empty `## [Unreleased]` above it;
   - sets `<VersionPrefix>X.Y.Z</VersionPrefix>` in `Directory.Build.props`.
2. Merge it once CI is green.
3. Tag the merge commit and push the tag:
   ```bash
   git tag vX.Y.Z <merge-commit>
   git push origin vX.Y.Z
   ```
4. **Release** builds the terminal and the window for Linux and Windows and attaches the `.tar.gz`, the
   AppImage, the `.zip` and the installer to a release named `SharpMUTerm X.Y.Z`.

A tag that fails the check builds nothing; delete it (`git push origin :refs/tags/vX.Y.Z`), fix the PR, and
tag again. A tag that has been released is never moved: ship a new patch version instead.

Running **Release** by hand (workflow dispatch) builds `<VersionPrefix>-dev.<run>` packages as workflow
artifacts and publishes nothing.
