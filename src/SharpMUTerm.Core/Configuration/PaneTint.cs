namespace SharpMUTerm.Core.Configuration;

/// <summary>
/// The colour a character's output pane is painted in, so a glance says whose pane you are looking at
/// when several are open at once. <see cref="None"/> — the default — leaves the pane on the theme's own
/// surface, which is what every pane looked like before this existed.
/// <para>
/// <b>It is a name, not a colour.</b> A free <c>#rrggbb</c> would be the obvious design and it is the
/// wrong one here: this is the plane the <em>game's</em> text is read against, the game chooses that
/// text's own colours, and the client cannot validate a hex against a theme it does not know the user
/// will still be on tomorrow. A closed set can be — <c>SharpMUTerm.Tui</c>'s <c>WorkspacePalette</c>
/// renders each of these at the untinted surface's exact luminance, so no choice on this list can make
/// the output less legible than no choice at all. Naming them also keeps the value meaningful across a
/// theme change: <c>Moss</c> is still moss on a light theme, where a hex picked against a dark one is a
/// hole.
/// </para>
/// <para>
/// <b>It is on the character and not on the world.</b> <see cref="WorldDefinition.Accent"/> already
/// exists and is a <em>foreground</em> that marks a world's windows; this is a different question with a
/// different answer. Two characters on one world is exactly the case the feature is for — one pane per
/// character, side by side — and a world-level colour cannot tell them apart. (It is also a background
/// rather than ink: an accent is chosen to be legible <em>as</em> text, and a plane painted in one would
/// be far too loud to read the game on.)
/// </para>
/// <para>
/// <b>Six hues, spread around the wheel.</b> Fewer would not cover a client with four or five characters
/// open; more would be pairs a reader has to squint between, which is worse than none because it invites
/// a mistake rather than admitting one. They are named for the colour rather than for a role
/// (<c>Ember</c>, not <c>Alert</c>): a character's pane colour is an identity, and a role name would
/// quietly promise a meaning the client never acts on.
/// </para>
/// </summary>
public enum PaneTint
{
    /// <summary>
    /// No tint: the pane keeps the theme's own surface. The default, and deliberately so — a client
    /// with one character open gains nothing from a colour, and a migration that assigned tints would
    /// repaint every existing user's workspace over a preference they never expressed.
    /// </summary>
    None = 0,

    /// <summary>Blue.</summary>
    Slate,

    /// <summary>Blue-green.</summary>
    Teal,

    /// <summary>Green.</summary>
    Moss,

    /// <summary>Amber.</summary>
    Ochre,

    /// <summary>Red-orange.</summary>
    Ember,

    /// <summary>Violet.</summary>
    Plum,
}
