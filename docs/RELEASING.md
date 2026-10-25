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
| The GitHub release's notes are the tag's section, verbatim; without one, `[Unreleased]`; without either, notes GitHub generates from the commits | **Release** (`changelog.py notes`) |
| A tag whose release is already published builds nothing | **Release**, before anything is built |

## Versions

[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, a minor bump (0.**2**.0) may break
configuration or keys; a patch bump (0.1.**1**) only fixes. A config schema migration is always at least a
minor bump and is named in the changelog. A prerelease (`0.2.0-beta.1`) is published as a GitHub prerelease.

## Cutting a release

1. Optionally, open a PR that renames `## [Unreleased]` to `## [X.Y.Z] - <today>` and puts a fresh, empty
   `## [Unreleased]` above it. Nothing requires it; without it the release notes are `[Unreleased]` as it stands.
2. Tag the merge commit and push the tag:
   ```bash
   git tag vX.Y.Z <merge-commit>
   git push origin vX.Y.Z
   ```
3. **Release** builds the terminal and the window for Linux and Windows at exactly `X.Y.Z`. It creates a **draft**
   GitHub release, `SharpMUTerm X.Y.Z`, with the changelog notes above, and attaches the `.tar.gz`, the
   AppImage, the `.zip` and the installer. A tag with a prerelease suffix (`v0.2.0-beta.1`) is drafted as a
   prerelease.
4. Open the draft on the Releases page, check it, and press **Publish release**. The release is immutable from
   that point on.

A tag is never moved or reused, because the repository has immutable releases on. If a release goes wrong, tag
the next patch version instead.

**Don't create the release yourself; publish the draft the workflow makes.** A published release is immutable, so
its packages can't be attached afterwards, and GitHub only attaches files to a draft. A tag that already has a
published release is refused before the build. `v0.1.0` to `v0.1.3`
shipped no packages (the first failed the changelog check, the other three were published from the UI before the
workflow ran), so the first release with packages is 0.1.4.

Running **Release** by hand (workflow dispatch) builds whatever MinVer makes of that commit and keeps the
packages as workflow artifacts; it publishes nothing.
