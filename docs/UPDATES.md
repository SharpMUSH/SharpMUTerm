# Updates

An installed SharpMUTerm keeps itself up to date. It looks for a newer release on GitHub, tells you when there is one,
downloads it when you ask, and restarts into it. The terminal client (`sharpmuterm`) and the window
(`sharpmuterm-window`) do this the same way. The updater is [Velopack](https://velopack.io).

## Which copies update

| Copy | Updates itself |
|---|---|
| Windows, installed with `SharpMUTerm-win-Setup.exe` | yes |
| Windows, `SharpMUTerm-win-Portable.zip` | yes, in the folder it was unzipped to |
| Linux, `SharpMUTerm.AppImage` | yes; the AppImage file is replaced |
| Linux, `sharpmuterm-linux-x64.tar.gz` | no |
| A build from the repository | no |
| Windows, installed by the old Inno Setup installer (before Velopack) | no; uninstall it and run Setup once |

A copy that cannot update offers nothing: no ⌃P entries and no notices. Asking it anyway (the command id
`app:check-updates`) says *This copy cannot update itself: only the installed one can.*

## What you see

1. **Looking.** Thirty seconds after start, and every 24 hours while it stays open, the client asks GitHub for the
   latest release. A look that finds nothing, or fails, says nothing. ⌃P ▸ *Check for updates* looks at once and
   always says the answer, including *SharpMUTerm 1.2.0 is the newest release.* or why the look failed.
2. **Found.** A notice says *SharpMUTerm 1.3.0 is out (you have 1.2.0)*, with an **Update** action. Each new
   version is announced once per run. Nothing has been downloaded yet.
3. **Downloading.** *Update* (the notice's action, or ⌃P ▸ *Update to 1.3.0*) downloads the release. The status row
   counts it in quarters. A failed download says why and offers *Try again*.
4. **Ready.** *SharpMUTerm 1.3.0 is ready; restart to use it*, with a **Restart** action (also ⌃P ▸ *Restart to
   update to 1.3.0*). Nothing changes until you choose it. If you quit without restarting, the downloaded update is
   not installed; the next look finds it again.
5. **Restart.** If nothing would be lost, the client restarts at once. If something would be (open connections, an
   unsent draft), or on Windows, the quit prompt asks *Restart to update to SharpMUTerm 1.3.0?* and lists what
   closes. The configuration is saved first, as on any quit, and the new version is put in place only after the
   client has stopped.

How each program comes back:

- **The window**, on both platforms: Velopack's updater waits for it to exit, installs the update, and opens the
  window again with the same arguments.
- **The terminal on Linux** installs the new AppImage and then replaces its own process with it, so it comes back in
  the same terminal, with the same arguments.
- **The terminal on Windows** cannot hand its console to another program. It installs the update as it exits and
  prints *SharpMUTerm 1.3.0 is being installed. Run sharpmuterm again in a moment.*
- A launch that came from a `telnet://` link restarts without the link, so the world is not dialled twice.

On Windows the terminal and the window are one installation, so restarting one to update closes the other too. The
quit prompt says so.

## Settings

F7 ▸ **Updates**:

- **check for updates** (on): the look at start and once a day. Turned off, the client never asks GitHub on its own;
  ⌃P ▸ *Check for updates* still works.
- **include prereleases** (off): count a release marked *pre-release* on GitHub (`0.3.0-beta.1`) as an update.

The client never downloads or installs anything you have not asked for, and never offers an older version.

## What it contacts

Only GitHub: the API for this repository's releases (about a dozen requests a look, without a token, so well inside
GitHub's 60 an hour per address), and the release's own files when you download. It sends no account, configuration or
usage data.

## When something goes wrong

- **A look or download failed:** ⌃P ▸ *Show client messages* has the reason, and so does the client's diagnostics log
  (`client-diagnostics-*.log` in the `logs` folder beside `config.json`).
- **The update did not install:** Velopack writes its own log. Windows: `%LocalAppData%\SharpMUTerm\velopack.log`.
  Linux: `velopack_SharpMUTerm.log` in the temp folder (`/tmp`). The old version stays in place when an update fails,
  and the Linux terminal prints the updater's exit code.
- **An update is never offered:** check that the copy is one that updates (the table above), and that the release has
  its feed files (below). A release without them is invisible to the updater.

## For maintainers

Releases are published on the GitHub Releases page as before (see [RELEASING.md](RELEASING.md)). The **Release**
workflow attaches Velopack's feed beside the packages, and that feed is all the updater reads:

| Asset | For |
|---|---|
| `releases.win.json`, `SharpMUTerm-<version>-full.nupkg` | Windows (Setup and Portable) |
| `releases.linux.json`, `SharpMUTerm-<version>-linux-full.nupkg` | the AppImage |

The version in the feed is the tag's (MinVer), so an installed copy compares tags. To try an update end to end without
touching users on the stable channel, publish a prerelease (`vX.Y.Z-beta.1`, *Set as a pre-release*) and turn on
F7 ▸ *include prereleases* in an installed copy of an older version.

Code: `src/SharpMUTerm.Tui/Hosting/VelopackUpdates.cs` (finding the installed layout, the feed, applying),
`src/SharpMUTerm.Tui/SharpMUTermApp.Updates.cs` (when to look, the notices, the restart), and
`src/SharpMUTerm.Avalonia/VelopackHooks.cs` (Windows install, update and uninstall hooks). Packaging is in
[PACKAGING.md](PACKAGING.md).

Tested so far: the Linux updater applying a newer AppImage in place, and both platforms' packages building. Not yet
tested: an update from a real GitHub release, and anything run on Windows.
