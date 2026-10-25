# Releasing SharpMUTerm

The **git tag is the version**. `v1.2.3` on a commit builds 1.2.3; any later commit builds
`1.2.4-alpha.0.<commits since the tag>` ([MinVer](https://github.com/adamralph/minver), configured in
`Directory.Build.props`). Nothing in the repository states a version, so there is no bump commit and nothing
that can disagree with the tag. Before the first tag, builds are `0.1.0-alpha.0.<height>`.

What the repository does hold is **`CHANGELOG.md`** — [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Newest first; an `## [Unreleased]` section on top; each release `## [X.Y.Z] - YYYY-MM-DD` with entries under
`Added`, `Changed`, `Deprecated`, `Removed`, `Fixed` or `Security`. A tag without its section is not released.

## Rules, and what enforces them

| Rule | Enforced by |
|---|---|
| The changelog has `[Unreleased]` first, release headings are `[X.Y.Z] - YYYY-MM-DD`, newest first by version and by date, no empty releases, only the six subsection names | `tools/changelog.py check` in the **Changelog** workflow, on every PR and push to `main` |
| A PR that changes `src/`, `packaging/` or `Directory.*.props` adds a line under `[Unreleased]` | **Changelog** workflow; label the PR `skip-changelog` when no user could notice the change |
| A release tag is `v<semantic version>` and the changelog has a dated, non-empty section for it | `tools/changelog.py release-check` in **Release**, before anything is built |
| The GitHub release's notes are that section, verbatim | **Release** (`changelog.py notes`) |

## Versions

[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, a minor bump (0.**2**.0) may break
configuration or keys; a patch bump (0.1.**1**) only fixes. A config schema migration is always at least a
minor bump and is named in the changelog. A prerelease (`0.2.0-beta.1`) is published as a GitHub prerelease.

## Cutting a release

1. Open a PR that renames `## [Unreleased]` to `## [X.Y.Z] - <today>` and puts a fresh, empty
   `## [Unreleased]` above it. Merge it once CI is green.
2. Tag the merge commit and push the tag:
   ```bash
   git tag vX.Y.Z <merge-commit>
   git push origin vX.Y.Z
   ```
3. **Release** builds the terminal and the window for Linux and Windows at exactly `X.Y.Z`, creates the GitHub
   release `SharpMUTerm X.Y.Z` with the changelog section as its notes, and attaches the `.tar.gz`, the AppImage,
   the `.zip` and the installer. A tag with a prerelease suffix (`v0.2.0-beta.1`) becomes a GitHub prerelease.

A tag that fails the check builds nothing; delete it (`git push origin :refs/tags/vX.Y.Z`), fix the changelog,
and tag again. A tag that has been released is never moved: ship a new patch version instead.

Running **Release** by hand (workflow dispatch) builds whatever MinVer makes of that commit and keeps the
packages as workflow artifacts; it publishes nothing.
