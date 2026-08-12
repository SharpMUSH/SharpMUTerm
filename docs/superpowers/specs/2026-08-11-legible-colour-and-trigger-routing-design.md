# Legible colour, and a route that means "leave it where it is"

**Date:** 2026-08-11
**Status:** approved

Two reported defects that turn out to share a cause.

> "we are using unreadable colors by default against our backgrounds. Such as Freeze
> being purple against a blue background."

> "we failed to solve the issue of Triggers — we should be able to Highlight text and
> send it to the pane where we found it. That way, players can highlight character name
> text."

The second is mostly the first: a highlight rule *does* fire, and the colour it fires in
is invisible. What is genuinely missing on the trigger side is smaller than it looked,
and is stated in §5.

## The measurement

Contrast ratios (WCAG relative luminance) of each colour against the pane plane it is
actually painted on — the focused pane surface, which is the brightest plane a dark theme
can wear and therefore the worst case for a foreground being read on it.

The F2 highlight picker's palette:

| name | dark | light | | name | dark | light |
|---|---|---|---|---|---|---|
| purple | **1.27** | 8.43 | | gold | 8.55 | **1.26** |
| blue | **1.40** | 7.69 | | yellow | 11.16 | **1.04** |
| black | **1.75** | 18.79 | | white | 11.99 | **1.12** |
| green | **2.33** | 4.60 | | cyan | 9.56 | **1.12** |
| teal | **2.51** | 4.27 | | pink | 7.79 | **1.38** |
| red | **3.00** | 3.58 | | lime | 8.74 | **1.23** |
| magenta | 3.82 | **2.81** | | orange | 6.07 | **1.77** |
| silver | 6.59 | **1.63** | | grey | 3.04 | 3.53 |

Six of sixteen fail on dark, nine of sixteen on light, and **only `grey` clears 3:1 on
both**. That is the finding the whole design turns on: a palette of fixed hexes cannot
serve two themes, so the resolution has to happen at the moment of painting, against the
plane the text lands on — not at the moment of picking.

The client's own chrome, same measurement:

| what | value | dark | light |
|---|---|---|---|
| freeze / away / restore bar accent | `ResolveIndex(5)` = `#800080` | **1.27** | 8.43 |
| rail accent, drop zones, ⌃P chip | `#00f5b7` | 11.13 | **1.42** |
| draft pen | `#ffd700` | 11.30 | **1.26** |
| prefix panel, client warnings | `#e5c07b` | 9.18 | **1.73** |

The reported Freeze defect is the first row. The rest of the table says the Light theme's
chrome has never been readable.

And the server's own text, on the default dark theme's focused pane: ANSI 4 blue
**1.34**, ANSI 1 red **1.09**, ANSI 0 black **1.75**, ANSI 5 magenta **1.27**. MU\* servers
emit these constantly.

## 1 — One rule, applied where a colour meets its plane

New `SharpMUTerm.Core.Text.Contrast`. Pure, no UI dependency:

- `RelativeLuminance(Rgb)` and `Ratio(Rgb, Rgb)` — WCAG 2.x, the definition every number
  in this document was produced with.
- `Legible(Rgb foreground, Rgb plane, double floor)` — the foreground blended toward white
  when `plane` is dark and toward black when it is light, by the **smallest** amount that
  clears `floor`. Returns the foreground unchanged when it already clears it.

Two properties this has to have, and one it cannot.

**Direction is the plane's, not the colour's.** One function serves all three themes
because it asks the plane which way "away" is. A dark plane always lifts, a light plane
always darkens; there is no theme in which the answer is ambiguous, because a pane plane is
never mid-grey.

**Hue survives until headroom runs out, and then it desaturates.** Blending toward white
raises luminance monotonically and reaches any target without clipping a channel — which
scaling upward cannot do, and which is the same reasoning `WorkspacePalette.AtLuma` is
already written on. It has to desaturate eventually: pure `#0000ff` has a relative
luminance of 0.0722 and so tops out at 1.88:1 against a dark pane *at full blue*. A rule
that preserved hue absolutely would leave that colour unreadable, which is the defect.

**It is not reversible.** `Legible` is a projection: two foregrounds that differ only below
the floor come out closer together than they went in. That is the cost of the floor and it
is accepted — the alternative is the current behaviour, where they are equally invisible.

**The floor is 3.0:1**, WCAG AA for large text and UI components. Deliberately not 4.5: at
4.5 the server's bright-black de-emphasis and the `dim` attribute stop meaning anything —
the client would flatten every deliberate act of de-emphasis a game makes.

## 2 — What the server sends

`MarkupFormatter.StyleTag` passes its resolved foreground through `Legible` before writing
the hex, measured against:

- the span's **own background** when it has one (a highlight's background, or a `reverse`
  swap) — that plane is known exactly, and it is the one the text lands on;
- otherwise the **pane plane**, because a span with a default background emits none and
  takes whatever it is drawn on.

**The plane handed to the formatter is per theme, not per pane.** A pane's actual plane
varies with the character's tint and with focus, and re-resolving per pane would mean
re-formatting a whole buffer on every focus move — the expensive path CLAUDE.md reserves for
one deliberate keystroke. Instead the lift targets the **extreme of the band** in the plane's
own direction: on a dark theme the brightest plane a pane can wear (untinted, focused), on a
light theme the darkest. Clearing the floor there clears it on every other plane, because
once the foreground is past the background's luminance the ratio is monotone in the
background — so one reference plane per theme is not an approximation, it is the worst case.

Gated by a new F7 preference, `keep text legible`, **default on**. Off emits today's exact
bytes: a user who wants their game's own palette untouched, or who has a theme where the
floor fights their taste, turns it off and nothing else changes.

## 3 — What the client paints itself

The chrome literals in the table above are not rescued by §2 — they are the client's own
colours and should be *derived* correctly rather than repaired at the last moment. They
become named inks on `WorkspacePalette`, each resolved from the active theme and then held
to the same floor against the plane it actually lands on:

| ink | replaces | plane it is measured against |
|---|---|---|
| `Marker` | `SharpMUTermApp.FrozenAccentHex()` (`ResolveIndex(5)`) | the pane band |
| `Accent` | `RailRenderer.DefaultAccent`, `PaneDropRenderer.ZoneColor` | `Backdrop` |
| `Notice` | `PrefixPanel` / `ClientMessageRenderer` `#e5c07b` | `Backdrop` |
| `Draft` | `RailRenderer` `#ffd700` | `Backdrop` |

`ScreenPalette` is **not** touched. Those constants sit on the settings screens' own fixed
dark backdrop, which the theme does not move — measuring them against a theme plane would be
measuring them against a plane they are never painted on.

## 4 — The picker

The palette's names stay. What changes is that the F2 swatch is painted through the same
lift, so the picker shows the colour the pane will show. A name is then a *hue* the theme
resolves, which is what the pane-tint work already established as this codebase's way of
naming a colour that has to survive a theme change.

## 5 — Triggers

Four changes. No schema change and no migration: `TriggerActions.SpawnTarget` keeps its
type and its null.

**`route` gains an explicit `(none)`.** The rule adds no destination; the line follows
whatever the other matched rules decided, and lands in the session's main window if nobody
routed it. This is exactly what `SpawnTarget = null` has always meant — F2 labelled it
`main`, and that label is why "highlight it and leave it where it was" looked like something
the screen could not express. New rules default to it.

**`main` becomes a real destination** — the matching session's own main window. Reserved
name; no window is titled `main` today (a character's session window is titled after the
character, and `main` is only the rail's *label* for it), so nothing collides. A config that
already says `SpawnTarget = "main"` currently conjures a capture pane called "main"; after
this it reaches the window whoever wrote it meant.

**Gag suppresses the default delivery only.** Explicit destinations survive it. That is
already true of spawn panes — `route: Chat` + gag has always meant "only in Chat" — and it
becomes true of `main`, so `route: main` + gag keeps the line in the main window instead of
deleting it. This is the answer to "gag means only where I routed it".

**Destinations are deduplicated.** Two rules naming one pane deliver one line. Today
`TriggerEngine` appends to a bare list and `WorldSession` raises `SpawnLine` per entry, so
a highlight rule pointed at the same pane as its capture rule delivers the line twice.

What is deliberately *not* changed: a highlight rule does not need a route to reach the pane
a capture rule sent the line to. There is one line and one set of destinations, and every
matched rule's highlight is on it — which is what the user's own framing asked for ("it
should still follow the original route, as long as it does not change where it routes to").

## What the frame audit found that reading the source did not

The design above was implemented and then the *paint* was measured — every emitted SGR pair, over 24
views × 3 themes. Five more offenders, each with a plausible-looking call site:

| what | measured | why the source looked fine |
|---|---|---|
| the trigger left-rule `▌` | 1.42 (Light) | `MarkupFormatter` resolved it a dozen lines above the floor it applies to every other foreground |
| the header ribbon's chip | 1.53 (Light) | a fixed `#3f4859` — a *dark* chip whatever the theme, so the world accent on it was resolved for the wrong plane |
| a world's own accent on the rail | 1.03 (Light) | only the *fallback* accent had been derived; a row carrying its own RGB went through raw |
| the unread badge on a tab | 1.42 (Light) | `UnreadBadge.Tint` was a `const` pointing at `ScreenPalette.Accent` |
| the command line's ink | 2.43 (Solarized) | the band was derived and the ink on it was not — the least readable text in the client, on the theme most often chosen for comfort |

`FrameContrastTests` is that audit as a test. It exempts three things, each for its own reason and not
because it was failing: the powerline wedges and box-drawing rules (fill boundaries and dividers, not
text), the solid blocks (F2's swatch is a colour *sample*, shown as the pane will paint it), and the
framework's `[dim]`. The half blocks are deliberately **not** exempt — `▌` is the trigger rule and the
focus marker, and one of them was a real defect this found.

**One thing is outside the floor's reach and is named rather than hidden.** SharpConsoleUI resolves the
`[dim]` tag to a fixed `#808080` of its own, through no option we hold: 4.01:1 on the default dark theme
and **2.52:1** on Solarized Dark's focused pane. Reaching it means giving up `[dim]` across every
renderer in favour of an explicit floor-checked grey — a sweep, for a near miss on one theme. The
exemption is a named predicate with the number in it, so whoever does that sweep can delete it and watch
the test still pass.

## Testing

**Core.**
- `ContrastTests` — the floor is reached; the ratio is at least the floor and not
  wastefully above it; hue is held while headroom lasts; both directions; already-legible
  colours come back byte-identical; idempotent.
- `LegiblePaletteTests` — a table over all 16 picker names × ANSI 0–15 × three themes ×
  the whole plane band (untinted and all six tints, focused and not), asserting the floor.
  This is the test that would have caught the reported defect, and it is the one that keeps
  a future theme from reintroducing it.
- Trigger routing: `(none)` adds nothing; `main` delivers to the main window; gag +
  explicit route keeps the line; two rules on one target deliver once.

**Tui.**
- `WorkspacePaletteTests` — every named ink clears the floor on every theme, against the
  plane it is documented as landing on.
- Snapshots: `freeze`, `away`, `highlight`, and a Light-theme frame, with a decoded-grid
  assertion on the freeze bar's painted cells rather than on the markup string — the bug
  was in what reached the screen.
