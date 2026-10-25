# Releasing SharpMUTerm

The **git tag is the version**. `v1.2.3` on a commit builds 1.2.3; any later commit builds
`1.2.4-alpha.0.<commits since the tag>` ([MinVer](https://github.com/adamralph/minver), configured in
`Directory.Build.props`). Nothing in the repository states a version, so there is no bump commit and nothing
that can disagree with the tag. Before the first tag, builds are `0.1.0-alpha.0.<height>`.

What the repository does hold is **`CHANGELOG.md`** — [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Newest first; an `## [Unreleased]` section on top; each release `## [X.Y.Z] - YYYY-MM-DD` with entries under
`Added`, `Changed`, `Deprecated`, `Removed`, `Fixed` or `Security`.

## Rules, and what enforces them

| Rule | Enforced by |
|---|---|
| The changelog has `[Unreleased]` first, release headings are `[X.Y.Z] - YYYY-MM-DD`, newest first by version and by date, no empty releases, only the six subsection names | `tools/changelog.py check` in the **Changelog** workflow, on every PR and push to `main` |
| A PR that changes `src/`, `packaging/` or `Directory.*.props` adds a line under `[Unreleased]` | **Changelog** workflow; label the PR `skip-changelog` when no user could notice the change |
| A release tag is `v<semantic version>` | **Release**, before anything is built |
| A release published with empty notes gets the tag's changelog section, verbatim; without one, `[Unreleased]` | **Release** (`changelog.py notes`) |

## Versions

[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, a minor bump (0.**2**.0) may break
configuration or keys; a patch bump (0.1.**1**) only fixes. A config schema migration is always at least a
minor bump and is named in the changelog. A prerelease (`0.2.0-beta.1`) is published as a GitHub prerelease.

## Cutting a release

1. Optionally, open a PR that renames `## [Unreleased]` to `## [X.Y.Z] - <today>` and puts a fresh, empty
   `## [Unreleased]` above it.
2. On the Releases page, **Draft a new release**: create the tag `vX.Y.Z` on `main`, title it, and publish it.
   Leave the notes empty to have the changelog filled in, or write your own. Tick *Set as a pre-release* for a
   version like `v0.2.0-beta.1`.
3. Publishing runs **Release**. It builds the terminal and the window for Linux and Windows at exactly `X.Y.Z`
   and attaches the `.tar.gz`, the AppImage, the `.zip` and the installer to the release. Re-running the workflow replaces them.

A tag is never moved or reused. If a release goes wrong, release the next patch version. `v0.1.0` to `v0.1.3`
shipped no packages, and the last three were immutable releases, which can never take files. The first release
with packages is 0.1.4.

Running **Release** by hand (workflow dispatch) builds whatever MinVer makes of that commit and keeps the
packages as workflow artifacts; it publishes nothing.
