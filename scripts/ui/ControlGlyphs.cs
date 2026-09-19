using System.Collections.Generic;
using Godot;

namespace MpFoundation.Ui;

/// <summary>
/// The How-to-Play panel's control table — ONE data table (spec §3 / §8 scenario 6), keyed to
/// the REAL project.godot input-map action names (verified 2026-08-29: move_forward=W,
/// move_back=S, move_left=A, move_right=D, jump=Space, sprint=Shift, interact=E, throw=Q,
/// voice_ptt=V, fire=LMB, aim=RMB), never a
/// hardcoded letter. Glyphs resolve live from <see cref="InputMap"/> every time the panel opens
/// — the same technique <c>InteractPrompt.InteractKeyLabel</c> already uses for the world-space
/// "E" prompt.
///
/// <para><b>HOWTO-2, 2026-08-29 — the table now yields structure, not just text.</b>
/// <see cref="BindingFor(string)"/> reports <i>what kind of thing</i> a binding is (a key, a
/// mouse button, the wheel) as well as its label, because Talon asked for icons and an icon
/// cannot be picked from the string "LMB" without parsing it back. The old string API is still
/// here and still correct; it is now derived from the structured one rather than duplicating it.</para>
///
/// <para><b>Rows are grouped into <see cref="Section"/>s.</b> Grouping is display data, not
/// layout: a section knows its own title and its own rows, and the panel decides where they go.
/// Appending a row — or a whole section — must never require touching a position.</para>
/// </summary>
public static class ControlGlyphs
{
    public readonly record struct Row(string Label, string[] Actions);

    /// <summary>A titled group of rows. The panel flows sections into columns by weight; nothing
    /// here knows or cares which column it lands in.</summary>
    public readonly record struct Section(string Title, Row[] Rows);

    /// <summary>What a binding physically is. The label alone cannot answer this — "LMB" is a
    /// string a keycap would happily render — and the icon choice depends on the answer.</summary>
    public enum GlyphKind
    {
        /// <summary>Nothing is bound. Renders as a "?" cap; deliberately not hidden, because
        /// "this verb exists and has no key" is information.</summary>
        Unbound,
        Key,
        MouseLeft,
        MouseRight,
        MouseMiddle,
        MouseWheel,
    }

    /// <summary>One resolved binding: what it is, and what to write on it.</summary>
    public readonly record struct Binding(GlyphKind Kind, string Label);

    /// <summary>
    /// The control table. Order inside a section is the order it renders.
    ///
    /// <para><b>Appending is the only supported edit.</b> The controller-support design doc's
    /// rule for this table has always been append-never-insert; sections make that cheaper, not
    /// looser. A new verb goes at the end of the section it belongs to, or becomes a new section
    /// at the end of this array.</para>
    ///
    /// <para><b>The Move row lists forward/left/back/right on purpose</b> — in that order the
    /// resolved glyphs concatenate to "WASD" for <see cref="GlyphFor(Row)"/>, and the panel lays
    /// the same four caps out as the physical cross.</para>
    /// </summary>
    public static readonly Section[] Sections =
    {
        new("MOVE", new[]
        {
            new Row("Move", new[] { "move_forward", "move_left", "move_back", "move_right" }),
            new Row("Jump", new[] { "jump" }),
            new Row("Run", new[] { "sprint" }),
        }),
        new("CARRY", new[]
        {
            // CARRY-1 (2026-09-19): E is still ONE key with one grammar — "act on what is in
            // front of me, or deal with what is in my hand" — but what the second half means now
            // depends on where you are looking, so the row says both. Appended to, never
            // reordered (this table's standing rule).
            new Row("Pick up / put down", new[] { "interact" }),
            new Row("Turn what you're holding", new[] { "rotate_held" }),
            new Row("Throw", new[] { "throw" }),
            // Two-slot carry (2026-08-07). Three rows rather than one because they answer three
            // different questions a player actually asks, and collapsing them would hide the rule
            // that matters most (you have two hands, and picking up a third thing costs you one).
        }),
        new("USE", new[]
        {
            // SWING-2 (2026-08-16). ONE ROW PER MOUSE BUTTON, naming the button's JOB rather than
            // one tool's use of it: the primary button uses whatever is in the hand (camera =>
            // shoot, empty hand => swing the net) and the secondary readies it, routed by held
            // item at one dispatch. A row per tool would have to grow every time a verb arrives,
            // and the per-item wording belongs with whatever knows what is actually in the
            // hand.
            new Row("Use held item", new[] { "fire" }),
            new Row("Ready held item (hold)", new[] { "aim" }),
            // HOWTO-2, 2026-08-29 — THE FLASHLIGHT ROW IS A FORWARD DECLARATION, AND IT IS SAFE
            // BECAUSE IT IS CONDITIONAL. NIGHT-2 is adding a flashlight toggle in a parallel
            // branch (Talon note 10: "Press F to toggle flashlight"), and the action name it will
            // register is not knowable from here. Several plausible names are listed; whichever
            // one exists in the live InputMap wins, and if NONE of them exists — because the
            // feature has not merged yet, or because it reads a raw key instead of an action —
            // DeclaredSections drops the row entirely and the screen never mentions a verb the
            // build does not have. This is the same guard that used to exist for an earlier
            // flag-gated net swing, reused rather than reinvented.
            new Row("Flashlight", new[] { "flashlight", "flashlight_toggle", "toggle_flashlight", "light_toggle" }),
        }),
        new("TEAM", new[]
        {
            new Row("Talk (hold)", new[] { "voice_ptt" }),
        }),
    };

    /// <summary>Every row in the table, flattened and unfiltered — the declaration of what the
    /// game's verbs are. A caller that wants all of them (a design-doc generator, a test
    /// asserting the table) should not have to work around a display rule.</summary>
    public static readonly Row[] Rows = Flatten();

    /// <summary>
    /// The sections worth showing a player: every section with at least one row the
    /// <see cref="InputMap"/> actually knows about, carrying only those rows.
    ///
    /// <para><b>A control list must not advertise a control that does not exist.</b> Rows whose
    /// actions are all undeclared are dropped rather than rendered with a <c>"?"</c> chip — which
    /// is what <see cref="BindingFor(string)"/> returns for an unbound action, correctly, since
    /// "bound to nothing" and "not a verb in this build" are different states and only the second
    /// one means the row should be absent. A section left with no rows disappears with its title,
    /// so a build without those verbs shows no empty heading.</para>
    /// </summary>
    public static IEnumerable<Section> DeclaredSections
    {
        get
        {
            foreach (Section section in Sections)
            {
                var kept = new List<Row>();
                foreach (Row row in section.Rows)
                {
                    if (IsDeclared(row))
                        kept.Add(row);
                }

                if (kept.Count > 0)
                    yield return new Section(section.Title, kept.ToArray());
            }
        }
    }

    /// <summary>The flat form of <see cref="DeclaredSections"/>, for callers that do not group.</summary>
    public static IEnumerable<Row> DeclaredRows
    {
        get
        {
            foreach (Section section in DeclaredSections)
            {
                foreach (Row row in section.Rows)
                    yield return row;
            }
        }
    }

    /// <summary>True if at least one of the row's actions is a verb this build has.</summary>
    public static bool IsDeclared(Row row)
    {
        foreach (string action in row.Actions)
        {
            if (InputMap.HasAction(action))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The bindings to draw for a row, in order — <b>skipping actions this build does not
    /// declare.</b>
    ///
    /// <para>Skipping matters for an alternatives row (see the flashlight above, which lists four
    /// candidate action names and expects at most one to exist): rendering "?" beside the real
    /// key for every name that did not win would be a legend that lies. For a row whose actions
    /// are all declared — every other row in the table — this returns all of them, unchanged.</para>
    /// </summary>
    public static IReadOnlyList<Binding> BindingsFor(Row row)
    {
        var bindings = new List<Binding>(row.Actions.Length);
        foreach (string action in row.Actions)
        {
            if (InputMap.HasAction(action))
                bindings.Add(BindingFor(action));
        }

        return bindings;
    }

    /// <summary>The live binding for a single action. <see cref="GlyphKind.Unbound"/> with a "?"
    /// label if nothing is bound — a real possibility once remapping exists; never throws.</summary>
    public static Binding BindingFor(string action)
    {
        foreach (InputEvent ev in InputMap.ActionGetEvents(action))
        {
            if (ev is InputEventKey key)
            {
                string label = KeyLabel(key);
                if (!string.IsNullOrEmpty(label))
                    return new Binding(GlyphKind.Key, Shorten(label));
            }
            // Mouse bindings were once skipped entirely, so every mouse-bound action read as "?"
            // — invisible while this table listed only keyboard rows, wrong the moment it listed
            // `aim` (right mouse) or the scroll-wheel slot swap. Godot has no GetKeycodeString
            // equivalent for mouse buttons, so they are named here; short conventional labels
            // rather than "Mouse Button 2", which nobody says out loud.
            else if (ev is InputEventMouseButton mouse)
            {
                switch (mouse.ButtonIndex)
                {
                    case MouseButton.Left:
                        return new Binding(GlyphKind.MouseLeft, "LMB");
                    case MouseButton.Right:
                        return new Binding(GlyphKind.MouseRight, "RMB");
                    case MouseButton.Middle:
                        return new Binding(GlyphKind.MouseMiddle, "MMB");
                    case MouseButton.WheelUp:
                    case MouseButton.WheelDown:
                        return new Binding(GlyphKind.MouseWheel, "Wheel");
                }
            }
        }

        return new Binding(GlyphKind.Unbound, "?");
    }

    /// <summary>The glyph for a single action's live binding ("W", "Space", "Shift", "LMB").
    /// Kept for callers that want text; it is <see cref="BindingFor(string)"/>'s label.</summary>
    public static string GlyphFor(string action) => BindingFor(action).Label;

    /// <summary>A row's combined glyph as text: single-character actions concatenate with no
    /// separator (the Move row reads "WASD" as one word); anything longer joins with " / "
    /// instead of running unreadable text together.</summary>
    public static string GlyphFor(Row row)
    {
        IReadOnlyList<Binding> bindings = BindingsFor(row);
        if (bindings.Count == 0)
            return "?";

        var parts = new string[bindings.Count];
        bool allSingleChar = true;
        for (int i = 0; i < bindings.Count; i++)
        {
            parts[i] = bindings[i].Label;
            if (parts[i].Length != 1)
                allSingleChar = false;
        }

        return allSingleChar ? string.Concat(parts) : string.Join(" / ", parts);
    }

    /// <summary>
    /// A key event's printable name.
    ///
    /// <para><b>Every keyboard binding in project.godot is physical-only</b> (<c>keycode: 0</c>,
    /// <c>physical_keycode</c> set), which is correct — it is what makes WASD land under the same
    /// three fingers on AZERTY. It also means the label has to come back through
    /// <c>KeyboardGetKeycodeFromPhysical</c>, and that call is a display-server call: under
    /// <c>--headless</c> it raises "Not supported by this display server" and returns nothing.
    /// The physical code IS a <see cref="Key"/> value, so falling back to it directly gives the
    /// US-layout name instead of a "?" — which is the honest answer for a headless render and
    /// strictly better than a legend full of question marks.</para>
    /// </summary>
    private static string KeyLabel(InputEventKey key)
    {
        if (key.Keycode != Key.None)
            return OS.GetKeycodeString(key.Keycode);

        string label = OS.GetKeycodeString(DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode));
        return string.IsNullOrEmpty(label) ? OS.GetKeycodeString(key.PhysicalKeycode) : label;
    }

    /// <summary>Godot's long names shortened to what fits on a cap and what players say out loud.
    /// Shortening lives here rather than in the drawing code so the text form and the icon form
    /// can never disagree about what a key is called.</summary>
    private static string Shorten(string label) => label switch
    {
        "Escape" => "Esc",
        "Control" => "Ctrl",
        "BackSpace" => "Bksp",
        "Delete" => "Del",
        _ => label,
    };

    private static Row[] Flatten()
    {
        var all = new List<Row>();
        foreach (Section section in Sections)
            all.AddRange(section.Rows);
        return all.ToArray();
    }
}
