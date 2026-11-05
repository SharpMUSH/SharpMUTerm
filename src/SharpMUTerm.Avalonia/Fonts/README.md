# Bundled fonts

The window draws its own cells and its own native views, so it carries the faces it is designed in: Inter for every
native view, JetBrains Mono for the cells, and the Nerd Font icons the client's chrome uses. Before these were bundled,
a machine with no Nerd Font installed drew every icon as a box, and the native views took whatever sans-serif the
platform offered.

| File | Size | Source | Licence |
|---|---|---|---|
| `Inter-{Regular,Medium,SemiBold,Bold}.ttf` | 1.67 MB | [Inter 4.1](https://github.com/rsms/inter/releases/tag/v4.1), `extras/ttf` (static weights) | SIL OFL 1.1 (`Inter-OFL.txt`) |
| `JetBrainsMono-*.ttf` | 1.11 MB | [JetBrains Mono 2.304](https://github.com/JetBrains/JetBrainsMono/releases/tag/v2.304) | SIL OFL 1.1 (`JetBrainsMono-OFL.txt`) |
| `SymbolsNerdFontMono-Regular.ttf` | 2.51 MB | [Nerd Fonts 3.4.0, Symbols Only](https://github.com/ryanoasis/nerd-fonts/releases/tag/v3.4.0) | MIT (`SymbolsNerdFont-LICENSE.txt`); the glyphs keep their upstream licences, listed at [nerd-fonts/license-audit](https://github.com/ryanoasis/nerd-fonts/blob/master/license-audit.md) |

Inter's four static weights are smaller than the `Avalonia.Fonts.Inter` package (six weights, about 1.9 MB) and are
pinned here by file; the variable font is larger still.

**The bundle is the default on every platform.** `BundledFonts` registers every face with Avalonia's font manager
under `fonts:SharpMUTerm`, makes Inter the default family, and maps the bare names `Inter` and `JetBrains Mono` onto
the bundle, so a view naming either gets the bundled file whatever is installed. `NativeTokens.UiFont` and
`NativeTokens.MonoFont` name the bundle first and the platform's faces after it, which are used only for a glyph the
bundle lacks (emoji, CJK): no emoji or CJK face is bundled.

The cells use the bundled JetBrains Mono unless `--font` names an installed family. The bundled symbols are tried for
an icon the text face lacks, then an installed Nerd Font, then the platform's fallback.
