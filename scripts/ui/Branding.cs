namespace MpFoundation.Ui;

/// <summary>
/// The wordmark and the studio mark, in ONE place — every visible surface (splash, title
/// screen, main menu, window title, crash/feedback dialogs) reads from here, so a rename
/// stays a one-line change.
///
/// <para><b>The name landed on 2026-09-02 (Talon, addendum §10).</b> The UI had been
/// deliberately name-agnostic since 2026-08-14 — the prior mockup name was dead for legal
/// reasons (an active remake of the 1983 film it collided with; see the title-clearance brief)
/// and Talon ruled all UI stay name-agnostic <i>until a real title is picked</i>. That
/// condition is now met: <c>WORKING TITLE</c> becomes <b>Watis World</b> and the splash's
/// <c>[ STUDIO MARK ]</c> placeholder becomes <b>Great-Grand-Software</b>.</para>
///
/// <para><b>The name-agnostic RULE is not repealed — it was satisfied.</b> The dead-name audit
/// (<c>DeadNameAuditTests</c>) still sweeps every UI-facing source and still holds; these two
/// strings are the two Talon named and nothing else was renamed. Namespaces, paths, the repo
/// and the <c>Sail</c> identifiers stay as they are, per canon.</para>
/// </summary>
public static class Branding
{
    /// <summary>The game's title, as shown on the splash's hand-off, the title screen and the
    /// OS window title. Talon, 2026-09-02 (addendum §10):
    /// <i>"update to display the actual title, Watis World"</i>.</summary>
    public const string Wordmark = "Watis World";

    /// <summary>The studio mark, shown on the splash. Talon, 2026-09-02 (addendum §10):
    /// <i>"change to show the studio name, Great-Grand-Software"</i>.
    ///
    /// <para><b>This replaces a placeholder that was deliberately not a name.</b>
    /// <c>Splash.cs</c> read the literal <c>[ STUDIO MARK ]</c>, whose own comment said it
    /// "stands in for a studio logo IMAGE, not a name" and that when the mark landed it would
    /// "replace this Label outright rather than filling in a name here". Talon has now named the
    /// studio, so the slot is filled with the name; a logo image landing later still replaces
    /// the Label, and this constant is what its alt text and its fallback read.</para></summary>
    public const string Studio = "Great-Grand-Software";

    /// <summary>Where Talon's title-screen graphic will land once supplied (spec §1). Checked
    /// with <c>ResourceLoader.Exists</c> rather than assumed present — until the real file is
    /// dropped in at this path, every screen falls back to the text <see cref="Wordmark"/>
    /// above. Do not create a placeholder image here; that would be faking the asset.</summary>
    public const string WordmarkImagePath = "res://resources/TitleWordmark.png";
}
