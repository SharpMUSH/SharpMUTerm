# Bundled fonts

The window draws its own cells, so it needs a monospace face and the Nerd Font icons the client's chrome
uses. Before these were bundled, a machine with no Nerd Font installed drew every icon as a box.

| File | Source | Licence |
|---|---|---|
| `JetBrainsMono-*.ttf` | [JetBrains Mono 2.304](https://github.com/JetBrains/JetBrainsMono/releases/tag/v2.304) | SIL OFL 1.1 (`JetBrainsMono-OFL.txt`) |
| `SymbolsNerdFontMono-Regular.ttf` | [Nerd Fonts 3.4.0, Symbols Only](https://github.com/ryanoasis/nerd-fonts/releases/tag/v3.4.0) | MIT (`SymbolsNerdFont-LICENSE.txt`); the glyphs keep their upstream licences, listed at [nerd-fonts/license-audit](https://github.com/ryanoasis/nerd-fonts/blob/master/license-audit.md) |

An installed face still wins: a family named with `--font`, then `CellFonts.PreferredFamilies` (the Nerd
Font builds, Cascadia, Consolas, SF Mono, Menlo). The bundled JetBrains Mono comes next, ahead of the faces
that are merely present on most machines (DejaVu, Liberation, Noto, Courier New). The bundled symbols are
used for an icon the chosen face lacks and no installed Nerd Font has.
