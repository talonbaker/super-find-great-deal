using System.Collections.Generic;
using Godot;
using MpFoundation;
using MpFoundation.Game;
using MpFoundation.Game.Sandbox;
using MpFoundation.Game.World;
using MpFoundation.Game.Watcher;
using Sail.Game.Bubble;
using Sail.Game.Run;

namespace Sail.Game.World.BubbleTest;

/// <summary>
/// <b>The Bubble Test world — the seam seven section files plug into, and nothing else.</b>
///
/// <para>This class builds <b>no geometry</b>, and that is the packet's whole point rather than an
/// implementation detail. Talon, 2026-08-27: <i>"No procedural generation, anywhere. Every part of
/// this level — geometry, effects, everything — must be physically authored and present in the
/// Godot scene file, so it can be opened and flown around in the Godot editor directly."</i>
/// <see cref="BubbleTestSelfTest"/> enforces that mechanically: it counts
/// <c>MeshInstance3D</c>/<c>CollisionShape3D</c>/<c>StaticBody3D</c> in each section's <i>packed
/// state</i> — no instantiation, no <c>_Ready</c> — and again in the live tree, and fails on any
/// difference. A future packet that quietly builds a rock in code turns that test red.</para>
///
/// <para><b>The three things it does add at runtime are all writers, not geometry:</b>
/// <list type="number">
/// <item><see cref="OutdoorAtmosphere"/> — the sole sun/moon/sky/ambient writer, added exactly the
/// way the original level added it. It is a lighting writer, so it is not
/// "geometry present in the scene file" and it does not fall under the rule above; equally it is
/// single-writer (<c>.claude/rules/single-writer.md</c>) and this class INSTANTIATES it and never
/// edits it.</item>
/// <item><see cref="RespawnService"/> — see <see cref="SetUpRespawn"/> for why this world creates
/// its own rather than finding one.</item>
/// <item>Nothing else. No dome: <c>NightDome</c> is the camp's fog-density writer sized to a
/// 385 m forest bowl, and this level is a 300 m open cross with no treeline to hide an edge behind.
/// Adding it would put a fog wall across the middle of the cyan run.</item>
/// </list></para>
///
/// <para><b>Spawns</b> are collected from the hub section's <c>Spawn*</c> markers exactly the way
/// <c>PlaygroundWorld</c> does — sorted by name so every peer agrees on spawn order — with
/// one difference: <c>PlaygroundWorld</c> reads its own direct children, and here the
/// markers live one level down, inside the instanced <c>Hub.tscn</c> (program D3: one section, one
/// file, one owner). The positions are therefore converted to world space rather than read raw,
/// because <c>Gameplay.SpawnPlayer</c> treats a spawn point as a world position and the hub's
/// anchor being (0,0,0) today is a fact about program §4, not a guarantee.</para>
/// </summary>
public partial class BubbleTestWorld : Node3D, IGameWorld
{
    public Godot.Collections.Array<Vector3> SpawnPoints { get; } = new();

    private RespawnService? _respawn;

    /// <summary>Where the scoreboard stands when the hub has not landed its <c>PedestalMount</c>
    /// marker yet: the plinth position the layout already declares, lifted to the plinth top that
    /// BT-5's packet specifies (local y = 0.9). Derived rather than restated, so the day the
    /// marker arrives the two cannot be in different places.</summary>
    private static readonly Vector3 PedestalFallback =
        BubbleTestLayout.PedestalPos + new Vector3(0f, 0.9f, 0f);

    /// <summary>The hub's television, and the hidden one at the far end of cyan. Named here
    /// rather than written as literals at the <c>AddChild</c>, because
    /// <see cref="BubbleTestSelfTest"/> now looks both of them up by name (GUARD-1) and two
    /// spellings of one node is the "second copy" trap the <c>PedestalMount</c> lookup in
    /// <see cref="PedestalMount"/> already paid for once.</summary>
    public const string HubTvNodeName = "HubTv";

    /// <inheritdoc cref="HubTvNodeName"/>
    public const string HiddenTvNodeName = "HiddenTv";

    public override void _Ready()
    {
        CollectSpawnPoints();
        SetUpAtmosphere();
        SetUpRespawn();
        // BEFORE SetUpTvPortals, and that ordering is the whole of the seam: the lab route is a
        // destination override on an entrance that SetUpTvPortals builds, so the lab has to have
        // already said whether it exists.
        SetUpPuffinLab();
        SetUpTvPortals();
        SetUpBubbleCounter();
        // AFTER the counter: the secret bubble rides BubbleCounter.Changed to come back when the
        // reset lever is pulled, and its effect is a write on the counter's shared film material.
        SetUpSecretBubble();
        SetUpNightAids();
        // FRAME-1. After the rooms exist; it only reaches into the TvRoom section for nodes the
        // section file already authored, and does nothing at all when the PNG is not present.
        SetUpSecretPicture();
        SetUpWatcher();
        // Inert unless --bt-perf-readout is passed; see PerfReadout's class doc for why the
        // shadow change in this world needs an instrument rather than an assurance.
        AddChild(new PerfReadout { Name = PerfReadout.NodeName });
    }

    /// <summary>
    /// <b>Hangs Talon's drawing in the room behind the sunken television — if and only if he has
    /// supplied one.</b> FRAME-1, 2026-09-04: <i>"maybe there is a picture frame and or like a
    /// drawing on the wall ... Can you put if I gave you a drawing to put on the wall a PNG with
    /// transparency? Could you put this on the wall?"</i>
    ///
    /// <para><b>*** THE DRAWING LIVES AT <c>resources/SecretRoomPicture.png</c> ***</b>
    /// (<see cref="BubbleTestLayout.SecretPictureImagePath"/>). The shipped file is Talon's own
    /// crayon "fwends" — four televisions and four figures on a torn sheet with masking tape at
    /// the top, this game's own TV room drawn by hand. <b>Swapping it is a ONE FILE operation</b>:
    /// this method reads whatever image is on disk, refits the sheet to its aspect, and hangs it,
    /// so nothing here needs editing when the art changes.</para>
    ///
    /// <para><b>Why this is code at all, in a level whose law is that everything is authored in
    /// its scene file.</b> The frame, its rails, its lamp, its light and the canvas quad are ALL
    /// authored in <c>TvRoom.tscn</c> — <c>BubbleTestSelfTest.CheckBake</c> counts that section's
    /// packed meshes against its live ones and would fail anything built here. This method builds
    /// nothing: it sets one texture on an existing material, resizes one existing mesh, and flips
    /// one existing node's <c>Visible</c>. A conditional cannot be written in a <c>.tscn</c>, and
    /// the condition — "does the artist's file exist yet" — is the entire point.</para>
    ///
    /// <para><b><c>ResourceLoader.Exists</c> before <c>GD.Load</c></b>, exactly as
    /// <see cref="SetUpPuffinLab"/> spells out and for the same reason: <c>GD.Load</c> on a
    /// missing path emits an engine ERROR line, and something being absent on purpose is not an
    /// error. The absent case is the SHIPPED case until the art lands — the frame hangs empty
    /// under its lamp, which reads as an empty frame rather than as a bug, and the launch log
    /// stays clean. That is a designed state, so this method says so at <c>GD.Print</c> level
    /// rather than warning about it.</para>
    ///
    /// <para><b>The image is FITTED, never stretched, cropped or letterboxed.</b> The sheet is
    /// resized to the image's own aspect and scaled to sit inside
    /// <see cref="BubbleTestLayout.SecretPictureMountM"/>, so the shipped 1388 x 1053 comes out at
    /// 3.637 x 2.759 m and a portrait, square or panoramic replacement would each hang correctly
    /// without anyone being told a ratio to draw to. Transparency is the authored material's
    /// (<c>transparency = 1</c>, alpha blend) — this method never touches it, so the paper's torn
    /// edge and clear surround show the murk wall straight through.</para></summary>
    private void SetUpSecretPicture()
    {
        string path = $"{BubbleTestLayout.Section.TvRoom}/{BubbleTestLayout.SecretPicturePath}";
        var picture = GetNodeOrNull<Node3D>(path);
        if (picture is null)
        {
            GD.PushWarning($"[bubbletest.picture] {path} is missing from the TV room section — "
                           + "the room behind the sunken television has nothing on its wall, "
                           + "which is the 'nothing special' state FRAME-1 exists to fix.");
            return;
        }

        var canvas = picture.GetNodeOrNull<MeshInstance3D>(
            BubbleTestLayout.SecretPictureCanvasName);
        if (canvas is null)
        {
            GD.PushWarning($"[bubbletest.picture] {path} has no "
                           + $"'{BubbleTestLayout.SecretPictureCanvasName}' — the mount and its "
                           + "lamp are there but there is nothing for a drawing to land on.");
            return;
        }

        if (!ResourceLoader.Exists(BubbleTestLayout.SecretPictureImagePath))
        {
            canvas.Visible = false;
            GD.Print($"[bubbletest.picture] no {BubbleTestLayout.SecretPictureImagePath} in this "
                     + "build — the Deep Room's wall shows a lit, empty mount. Drop a PNG in at "
                     + "that path and it hangs itself; this is a designed state, not an error.");
            return;
        }

        var image = GD.Load<Texture2D>(BubbleTestLayout.SecretPictureImagePath);
        if (image is null || image.GetWidth() <= 0 || image.GetHeight() <= 0)
        {
            canvas.Visible = false;
            GD.PushWarning($"[bubbletest.picture] {BubbleTestLayout.SecretPictureImagePath} "
                           + "exists but would not load as a Texture2D — the wall stays empty "
                           + "rather than showing a broken surface.");
            return;
        }

        // Fit inside the mount box, preserving the image's aspect: whichever axis is
        // proportionally tighter sets the scale, so the sheet touches two bounds and overflows
        // neither. Never a crop and never a letterbox — an opaque backing behind a drawing whose
        // own alpha is its shape would erase the thing Talon asked for.
        Vector2 aperture = BubbleTestLayout.SecretPictureMountM;
        float fit = Mathf.Min(aperture.X / image.GetWidth(), aperture.Y / image.GetHeight());
        var size = new Vector2(image.GetWidth() * fit, image.GetHeight() * fit);
        if (canvas.Mesh is QuadMesh quad)
            quad.Size = size;

        // The material is the SCENE'S, alpha mode and all; only the albedo texture is set here.
        // Assigning a fresh material would be the "a fresh material per instance breaks batching"
        // trap (ART-BIBLE §4.2) and would silently drop the authored transparency.
        if (canvas.MaterialOverride is StandardMaterial3D mat)
            mat.AlbedoTexture = image;
        else
            GD.PushWarning("[bubbletest.picture] the sheet has no StandardMaterial3D override — "
                           + "the drawing cannot be applied without inventing a material, and an "
                           + "invented one would not carry the authored alpha blend.");

        canvas.Visible = true;
        GD.Print($"[bubbletest.picture] {BubbleTestLayout.SecretPictureImagePath} hung in the "
                 + $"Deep Room — {image.GetWidth()}x{image.GetHeight()} px fitted to "
                 + $"{size.X:F3} x {size.Y:F3} m inside a {aperture.X:F3} x {aperture.Y:F3} m "
                 + "mount, aspect preserved, alpha blended.");
    }

    /// <summary>D10's "easier at night" aid driver.
    ///
    /// <para><b>Why this exists as its own three lines.</b> BT-9 wrote
    /// <see cref="NightAidDriver"/> and the <c>bt_darkness</c> shader global it publishes, and
    /// every geometry packet dutifully built emissive edges that read from it — but NOTHING ever
    /// instantiated the driver. BT-4 caught it: the global stayed at its seed value, so no night
    /// aid in the level ever lit in a live session and D10 was silently dead. The driver
    /// self-drives from its own <c>_Ready</c>/<c>_Process</c>, so existing is the whole
    /// contract.</para></summary>
    private void SetUpNightAids()
    {
        AddChild(new NightAidDriver { Name = nameof(NightAidDriver) });
    }

    // --- The tally, the scoreboard and the lever (BT-8) --------------------------------------

    /// <summary>
    /// The wiring BT-6 deliberately left to this packet: one <see cref="BubbleCounter"/>, one
    /// adoption pass over the authored level, and the two props that make the tally a thing in
    /// the world rather than only a number in a corner of the frame.
    ///
    /// <para><b>These runtime <c>AddChild</c>s are not a breach of "no procedural generation".</b>
    /// That rule (program D2, and the class doc above) governs the level's GEOMETRY — what a
    /// player walks on, climbs and looks at must be openable in the editor, which
    /// <see cref="BubbleTestSelfTest"/> enforces by comparing each SECTION's packed node counts
    /// against its live ones. Nothing here goes into a section: the counter is a netcode writer
    /// with no visual at all, and the display and the lever are instanced scene FILES that open
    /// and fly in the editor exactly like any other prop. The only thing decided in code is WHERE
    /// they go, and that is read from an authored marker rather than computed.</para>
    ///
    /// <para><b>Why this script instances them instead of <c>Hub.tscn</c> authoring them.</b>
    /// Single-writer (<c>.claude/rules/single-writer.md</c>): the hub section file is the
    /// environment role's (BT-5), and burying an RPC-carrying node inside someone else's scene is
    /// the trap <c>BedInteractable</c>'s own doc already records. The seam BT-5 left is the
    /// <c>PedestalMount</c> marker, which is data this script reads.</para>
    /// </summary>
    private void SetUpBubbleCounter()
    {
        bool isServer = NetworkManager.Instance is null
                        || NetworkManager.Instance.Role != NetworkManager.SessionRole.Client;

        // --bubble-selftest brings its OWN counter and its own six bubbles, and Gameplay adds the
        // fixture AFTER the world — so building a second counter here would leave two adoption
        // passes and a BubbleCounter.Instance pointing at whichever _Ready ran last, with the
        // display reading one tally while the bubbles obeyed another. The fixture wins,
        // deliberately: it is the only way to see this level with bubbles in it until BT-11
        // authors them, which is exactly what a capture of the pedestal needs. The display and
        // the lever below resolve the counter lazily, so they attach to the fixture's without
        // needing to know which case they are in.
        bool fixtureOwnsTheCounter = NetworkManager.Instance?.Options?.BubbleSelfTest == true;
        if (!fixtureOwnsTheCounter)
        {
            var counter = new BubbleCounter { Name = BubbleCounter.NodeName };
            AddChild(counter);
            counter.Setup(isServer);      // before adoption: adoption wires the server-only hooks.
            counter.AdoptAuthoredBubbles(this);
        }

        Transform3D mount = PedestalMount();
        InstanceAt(BubbleCounterDisplay.ScenePath, BubbleCounterDisplay.NodeName, mount);

        // 1.5 m east of the plinth (+X; north is −Z), per the packet: far enough that the lever's
        // 1.6 m press radius does not overlap a player standing against the plinth, close enough
        // that the two read as one installation rather than as two props.
        Transform3D leverAt = mount;
        leverAt.Origin += new Vector3(BubbleResetLever.OffsetFromPedestalM, 0f, 0f);
        // LEVER-1: and back DOWN to the floor. `mount` is the marker on the plinth TOP, 0.9 m up,
        // and copying its transform put the lever's own 0.9 m pedestal in the air beside the
        // plinth rather than on the ground next to it — visible in every headed shot of the hub
        // and in none of the headless suites, because nothing measures a prop's Y. The plinth is
        // the only thing that stands on the mount; the lever stands on the floor the plinth
        // stands on, which is this section's own origin height.
        leverAt.Origin.Y = GlobalPosition.Y;
        var lever = GD.Load<PackedScene>(BubbleResetLever.ScenePath)
            ?.Instantiate<BubbleResetLever>();
        if (lever == null)
        {
            GD.PushWarning($"[bubbletest] could not load {BubbleResetLever.ScenePath} — no "
                           + "second run without a relaunch (program D9).");
            return;
        }
        lever.Name = BubbleResetLever.NodeName;
        lever.IsServer = isServer; // set before AddChild: _Ready reads it.
        AddChild(lever);
        lever.GlobalTransform = leverAt;

        // LEVER-1: --bubble-reset-at, honoured in the PLAYED level and not only in BT-6's
        // open-world fixture. One scripted pull, which under the two-stage press ARMS the lever
        // rather than resetting anything — so a headed capture can photograph the warning, which
        // is otherwise reachable only by a human standing at the lever pressing a key twice. The
        // flag's meaning where it already applied is unchanged; this is the same schedule
        // reaching the thing the schedule is now about.
        if (isServer && (NetworkManager.Instance?.Options?.BubbleResetAtSec ?? -1) >= 0)
        {
            _leverArmAtSec = NetworkManager.Instance!.Options!.BubbleResetAtSec;
            _scriptedLever = lever;
        }
    }

    private BubbleResetLever? _scriptedLever;
    private double _leverArmAtSec = -1;
    private double _leverClock;
    private double _nextArmAtSec = -1;
    private int _scriptedPeer = 900;

    /// <summary>How often the scripted pull re-arms, once it has started. Half the warning window,
    /// so the sign is never briefly quiet between one arm lapsing and the next landing.</summary>
    private const double ScriptedArmPeriodSec = BubbleResetConfirm.ConfirmWindowSec / 2.0;

    /// <summary>
    /// The scripted arming pull (see <see cref="SetUpBubbleCounter"/>), and nothing else — this
    /// world otherwise has no per-frame work.
    ///
    /// <para><b>Why it repeats.</b> A real warning lasts
    /// <see cref="BubbleResetConfirm.ConfirmWindowSec"/> and then lapses, which is correct and is
    /// the point. But the two processes a headed capture runs — a server holding the clock this
    /// pull is scheduled on, and a bot holding the clock the shot is scheduled on — boot seconds
    /// apart, so a single four-second window is not something a capture can be aimed at. Once
    /// started, this re-arms every <see cref="ScriptedArmPeriodSec"/> and holds the sign up for
    /// the rest of the session.</para>
    ///
    /// <para><b>Each re-arm is a NEW synthetic peer id</b>, and that is load-bearing rather than
    /// tidy: warnings are per peer, so a second pull from the SAME id inside the window would be
    /// a confirm and would wipe the board — the flag would quietly become the thing it exists to
    /// photograph the alternative to. Ids start at 900, well clear of any real peer.</para>
    ///
    /// <para>A plain accumulator rather than a <c>SceneTreeTimer</c> because the harness reasons
    /// in seconds since launch and a timer created during <c>_Ready</c> starts before the level
    /// has finished building.</para>
    /// </summary>
    public override void _Process(double delta)
    {
        if (_leverArmAtSec < 0 && _nextArmAtSec < 0)
            return;
        _leverClock += delta;
        if (_leverArmAtSec >= 0)
        {
            if (_leverClock < _leverArmAtSec)
                return;
            _leverArmAtSec = -1;
            _nextArmAtSec = _leverClock;
        }
        if (_leverClock < _nextArmAtSec)
            return;
        _nextArmAtSec = _leverClock + ScriptedArmPeriodSec;
        if (_scriptedLever == null || !GodotObject.IsInstanceValid(_scriptedLever))
        {
            _nextArmAtSec = -1;
            return;
        }
        _scriptedLever.ServerPress(_scriptedPeer++);
        GD.Print($"[bubbletest] scripted lever pull at {_leverClock:F1}s — armed="
                 + $"{_scriptedLever.ArmedPresses} accepted={_scriptedLever.AcceptedPresses}");
    }

    /// <summary>
    /// Where the hub's scoreboard goes: the <c>PedestalMount</c> marker BT-5 leaves on the plinth
    /// top, read at ready.
    ///
    /// <para><b>The fallback is not a shrug.</b> BT-5 and BT-8 land independently, and a hub
    /// without the marker would otherwise put the only readable number in the level at the world
    /// origin, inside the spawn ring, where it is both wrong and easy to miss as wrong. The plinth
    /// position from BT-5's own packet is used instead and the substitution is logged, so the
    /// level is playable before the hub lands and the log says exactly why the board is where it
    /// is.</para>
    /// </summary>
    private Transform3D PedestalMount()
    {
        Node? hub = GetNodeOrNull(BubbleTestLayout.Section.Hub.ToString());
        // RECURSIVE, deliberately. This used to be GetNodeOrNull<Marker3D>(name), a DIRECT-child
        // lookup — but BT-5 authored the marker at "Plinth/PedestalMount", one level down, so the
        // search never matched and the board silently stood at the fallback in every run. Neither
        // packet was wrong: the seam simply never said how deep the marker sits. The hub owns
        // where its own plinth goes, so this asks the hub for the marker wherever it put it.
        // owned:false because nodes inside an instanced section are owned by the section root.
        if (hub?.FindChild(BubbleCounterDisplay.PedestalMountName, recursive: true, owned: false)
                is Marker3D marker)
            return marker.GlobalTransform;

        GD.PushWarning($"[bubbletest] no {BubbleCounterDisplay.PedestalMountName} marker under the hub "
                       + $"— the counter display falls back to {PedestalFallback}.");
        return new Transform3D(Basis.Identity, PedestalFallback);
    }

    private void InstanceAt(string scenePath, string name, Transform3D at)
    {
        var packed = GD.Load<PackedScene>(scenePath);
        if (packed == null)
        {
            GD.PushWarning($"[bubbletest] could not load {scenePath} — '{name}' is missing.");
            return;
        }
        var node = packed.Instantiate<Node3D>();
        node.Name = name;
        AddChild(node);
        node.GlobalTransform = at;
        // Printed because "the board is not where I am looking" is otherwise indistinguishable
        // from "the board did not load", and the two have different fixes.
        GD.Print($"[bubbletest] {name} at {node.GlobalPosition}");
    }

    // --- The TV easter egg (BT-10) ---------------------------------------------------------

    // WHERE EVERY ENTRANCE STANDS IS NOW A ROW IN BubbleTestLayout.TvRoutes, not a constant here.
    // LEVEL-4, 2026-08-29 (Talon's note 9): five televisions, five rooms, one room each. The hub's
    // spot and cyan's hidden one are unchanged VALUES — they moved file, not place — and a second
    // copy of either here is the trap this level has already paid for four times.
    //
    // The two facts that used to live in this block still bind every row. The hub's TV is in the
    // NORTH-west quadrant because EVERY TV HERE FACES SOUTH (see SetUpTvPortals), so a player
    // crossing the plaza toward a TV south of them arrives at its BACK and is stopped by the
    // cabinet hull without ever reaching the screen — a bug the two-peer test caught. And the
    // hidden one is at cyan's far end because THRILL §12 wants it where a player arrives FOR
    // ANOTHER REASON. Every new row obeys both.

    /// <summary>Where the return TV puts you: inside the spawn ring, clear of the pedestal at
    /// (0, 0, −8), with the hub's TV still visible across the plaza and still playing. That last
    /// clause is the direction rather than a coincidence — the beat is the world declining to
    /// acknowledge the trip, and it only reads if the unchanged prop is in shot on the way
    /// back.
    ///
    /// <para><b>DERIVED from the ring since W7-1, not typed.</b> It was the literal (0, 1, 3),
    /// which was inside the ring only while the ring straddled the origin; Talon's note 1 backed
    /// the ring's centre 18 m south and the literal would have kept BT-10's coordinate while
    /// losing BT-10's direction. The offset from the ring's centre is what BT-10 actually chose,
    /// so the offset is what survives — and the hub television, now 45 m off across the plaza
    /// rather than 26, sits ahead and to the left of a returning player looking north instead of
    /// behind them.</para></summary>
    private static readonly Vector3 HubReturn =
        BubbleTestLayout.SpawnRingCentre + new Vector3(0f, 1f, 3f);

    /// <summary>Every entrance TV, every room's way back, and the host that moves people through
    /// all of them. <b>One loop over <see cref="BubbleTestLayout.TvRoutes"/></b> — LEVEL-4,
    /// 2026-08-29 — so a sixth television is a row in that table plus a room in
    /// <c>TvRoom.tscn</c>, and no third place to forget.
    ///
    /// <para><b>Every entrance is rotated 180° about Y, and that is load-bearing rather than
    /// cosmetic.</b> <see cref="TvPortal"/> puts its screen and its walk-in trigger on the node's
    /// local −Z, so a player entering one is travelling along its local +Z. Facing them all the
    /// same way makes the approach heading world −Z at every one of them, which is what makes
    /// <c>TvRoom.tscn</c>'s "you arrive looking at the room and must turn to leave" true from
    /// every entrance rather than from one — and keeps the rule consistent between them
    /// (THRILL §7.2). A teleport resets <c>MoveState.Yaw</c> to 0, which is −Z as well, so the
    /// property holds whether or not the client's look yaw survives the snap.</para>
    ///
    /// <para><b>No room is ever a one-way door, and that is asserted rather than assumed.</b> A
    /// room whose return TV is missing strands whoever reaches it — the section is sealed, 20 m
    /// down, out of reach of both the void plane and the off-map radius, which is correct and is
    /// exactly why the exit's absence would be unrecoverable. The warning below is loud for that
    /// reason, and <c>TvPortalSelfTest</c> walks the same table and drives the real round trip on
    /// every route.</para>
    ///
    /// <para><b>Why these two are built here while the room's third is authored in its scene
    /// file.</b> Not a preference: <c>BubbleTestSelfTest.CheckBake</c> walks the eight SECTION
    /// scenes and fails one whose live node counts exceed its packed ones, so a TvPortal inside
    /// <c>TvRoom.tscn</c> has to author its screen, glow pool and trigger — it does, and the
    /// script adopts them. The world scene is not walked by that check, and
    /// <c>BubbleTest.tscn</c> belongs to BT-0 rather than to this packet, so the two entrances
    /// come through the script the packet nominated. <b>That is a compromise, not a design:</b>
    /// Talon's constraint wants every part of this level openable in the editor and these two are
    /// not. Authoring them into <c>BubbleTest.tscn</c> is a two-node change for whoever owns that
    /// file next.</para></summary>
    private void SetUpTvPortals()
    {
        foreach (BubbleTestLayout.TvRoute route in BubbleTestLayout.TvRoutes)
        {
            AddChild(MakeEntranceTv(route));

            // The way back. Set from here rather than exported into each room, because the
            // destination is a fact about a SURFACE section and a room file that hard-coded
            // another section's coordinate is exactly the cross-file duplication program §4
            // exists to stop.
            string path = $"{BubbleTestLayout.Section.TvRoom}/{route.ReturnTvPath}";
            if (GetNodeOrNull(path) is TvPortal returnTv)
                returnTv.Destination = ReturnFor(route);
            else
                GD.PushWarning($"[bubbletest.tv] {path} is missing — the room behind "
                               + $"{route.EntranceName} has no way out, which strands any player "
                               + "who reaches it.");
        }

        // The lab's own way back, wired from here for the same reason each room's is: the
        // destination is a fact about a SURFACE section, and EGG-1's scene must not hard-code one.
        if (LabReturnTv is not null)
        {
            LabReturnTv.Destination = ReturnFor(LabRoute);
            GD.Print($"[bubbletest.tv] lab route wired - {LabRouteEntranceName} -> "
                     + $"{LabArrival!.GlobalPosition}, {PuffinLabReturnTvName} -> "
                     + $"{LabReturnTv.Destination}");
        }

        // Added last: its _Ready defers the portal scan, so it sees every TV regardless.
        AddChild(new TvPortalHost { Name = TvPortalHost.NodeName });
    }

    /// <summary>Where <paramref name="route"/>'s room puts a player back on the surface. The hub
    /// keeps BT-10's directed <see cref="HubReturn"/> (the spawn ring); the sunken television
    /// keeps <see cref="BubbleTestLayout.SunkenTvReturn"/> (the south bank); every other room
    /// lands you beside the television you came in by, per
    /// <see cref="BubbleTestLayout.RoomReturnOffset"/>.
    ///
    /// <para><b>Three named exceptions, and they are all the same exception.</b> The offset rule
    /// assumes the ground you left is ground you can be put back on. It is not, three times: the
    /// hub's television stands mid-plaza where BT-10 directed the return at the spawn ring
    /// instead; the sunken one stands 2 m under a lake, where the offset would drop a player back
    /// into deep water with a fresh drowning clock; and ROOFTOP-1's stands on the puffin lab's
    /// roof 6.39 m BELOW grade, where the offset would be a way out that never leaves the room —
    /// <c>TvPortalSelfTest.CheckDestinations</c> requires every return to land above y = 0 and is
    /// right to. A branch per exception, named, rather than a rule with a silent hole in it.</para>
    ///
    /// <para><b>The rooftop return is the one that changes where a player ends up rather than just
    /// how the arithmetic is done</b>, and that is stated on
    /// <see cref="BubbleTestLayout.RooftopTvReturn"/>: it puts them back on cyan's plate at the
    /// top of the run-up they made, six metres in from the lip they jumped off, which is the only
    /// above-ground point this route has ever touched.</para></summary>
    public static Vector3 ReturnFor(BubbleTestLayout.TvRoute route) => route.EntranceName switch
    {
        HubTvNodeName => HubReturn,
        BubbleTestLayout.SunkenTvNodeName => BubbleTestLayout.SunkenTvReturn,
        BubbleTestLayout.RooftopTvNodeName => BubbleTestLayout.RooftopTvReturn,
        _ => route.EntrancePos + BubbleTestLayout.RoomReturnOffset,
    };

    private TvPortal MakeEntranceTv(BubbleTestLayout.TvRoute route) => new()
    {
        Name = route.EntranceName,
        Position = route.EntrancePos,
        RotationDegrees = new Vector3(0f, 180f, 0f),
        Destination = DestinationFor(route),
    };

    /// <summary>Where <paramref name="route"/>'s entrance sends a traveller: its own room, unless
    /// this is the lab route AND the lab actually loaded, in which case the lab's
    /// <c>Arrival</c>. One function, so the world, the log and the self-test cannot disagree about
    /// which case the level is in.</summary>
    public Vector3 DestinationFor(BubbleTestLayout.TvRoute route) =>
        route.EntranceName == LabRouteEntranceName && LabArrival is not null
            ? LabArrival.GlobalPosition
            : BubbleTestLayout.ArrivalOf(route);

    // --- The night-only creature (EGG-2 item 4; Talon's addendum §4) -------------------------

    /// <summary>
    /// <b>The Watcher, gated on night.</b> All of the wiring, the gate and the reasoning live in
    /// <see cref="BubbleTestWatcher"/>; this is the three lines that put it in the level.
    ///
    /// <para><b>It is NOT behind <c>--watcher-spawns</c>, and that is a deliberate difference from
    /// camp.</b> That flag exists because <c>Gameplay.SetUpWatcher</c> adds a creature to the
    /// shipped camp world and Talon wanted it off by default there; it is also matched across
    /// peers by hand, and a peer that misses it constructs no node and fails the RPCs with an
    /// unresolved path. Here the creature IS the level's content — an easter egg nobody would ever
    /// pass a flag to see — so every peer builds it unconditionally and the paths cannot
    /// disagree. <c>Run-WatcherGateTest.ps1</c> exercises the flag in <c>--world camp</c> and is
    /// untouched by this.</para>
    ///
    /// <para><b>Gameplay's own watcher does not also spawn here.</b> <c>SetUpWatcher</c> there
    /// requires a <c>Campfire</c> node on the world root and prints
    /// <c>[watcher] no "Campfire" node in this world; watcher not spawned</c> when there is none —
    /// this level has never had one. <c>Watcher.Instance</c> is a single static, so the guard is
    /// worth stating rather than assuming: if that ever changes, the second construction wins the
    /// static and the first one's RPCs go nowhere.</para></summary>
    private void SetUpWatcher()
    {
        if (Watcher.Instance is not null)
        {
            GD.PushWarning("[bubbletest.watcher] a Watcher already exists in this process — this "
                           + "level is not adding a second. Watcher.Instance is a single static "
                           + "and the loser's RPCs would resolve to nothing.");
            return;
        }
        var host = new BubbleTestWatcher { Name = BubbleTestWatcher.NodeName, Avatars = EnumerateAvatars };
        AddChild(host);
    }

    // --- The secret bubble (EGG-2 item 3; Talon's addendum §3) -------------------------------

    /// <summary>
    /// <b>One <see cref="SecretBubble"/>, under the lake behind the island.</b>
    ///
    /// <para><b>Added from code rather than authored into GreenHills.tscn</b>, and for a specific
    /// reason rather than convenience: <c>BubbleTestSelfTest.CheckBake</c> compares each SECTION
    /// scene's packed node counts against its live ones, and this node builds its own mesh, its own
    /// collider and its own material in <c>_Ready</c> — inside a section file that would read as
    /// runtime generation and turn the bake check red. The entrance televisions are in the world for
    /// exactly the same reason, and this follows them.</para>
    ///
    /// <para><b>It is not a bubble and it is not counted</b> — see <see cref="SecretBubble"/>'s
    /// class doc. The count before and after this method is identical, which is why nothing here
    /// touches <c>BubbleCounter.AdoptAuthoredBubbles</c>; the adoption pass has already run and
    /// walks by type.</para></summary>
    private void SetUpSecretBubble()
    {
        bool isServer = NetworkManager.Instance is null
                        || NetworkManager.Instance.Role != NetworkManager.SessionRole.Client;

        var secret = new SecretBubble { Name = SecretBubble.NodeName };
        AddChild(secret);                       // _Ready builds the visual and the collider
        secret.GlobalPosition = BubbleTestLayout.SecretBubblePos;
        secret.Setup(isServer);                 // wires the server-only BodyEntered, and only there

        // Printed for the reason InstanceAt prints: "I never found it" and "it was never placed"
        // are different bugs with different fixes, and only one of them is mine.
        GD.Print($"[bubbletest.secret] secret bubble at {secret.GlobalPosition} "
                 + $"(server={isServer}, counter total unchanged at "
                 + $"{BubbleCounter.Instance?.BubbleCount ?? -1})");
    }

    // --- The lab throwback (EGG-2 item 2; the scene is EGG-1's) ------------------------------

    /// <summary>EGG-1's ported lab scene. <b>This world instances it and never edits it</b> - the
    /// wave's seam puts the scene on EGG-1 and every route and every placement on EGG-2.</summary>
    public const string PuffinLabScenePath = "res://scenes/game/world/puffinlab/PuffinLab.tscn";

    /// <summary>Node name the lab is instanced under.</summary>
    public const string PuffinLabNodeName = "PuffinLab";

    /// <summary>The two node names the seam fixes: a <c>Marker3D</c> on solid floor where a
    /// traveller is placed, and a <c>TvPortal</c> at the old fade-to-black endpoint. Both are
    /// direct children of the lab scene's root.</summary>
    public const string PuffinLabArrivalName = "Arrival";

    /// <inheritdoc cref="PuffinLabArrivalName"/>
    public const string PuffinLabReturnTvName = "ReturnTv";

    /// <summary>
    /// <b>Which television goes to the lab</b>, and it is EGG-2's call rather than the seam's.
    /// <c>HiddenTv</c>: the lab's payoff is disorientation - "where did I just go" - and the
    /// hidden one is the television a player reaches by detouring off the tangle spire for no
    /// stated reward, which is the same shape of beat. <c>TangleTv</c> at the top of the world is
    /// the level's summit prize and reads as an arrival, not a dislocation.
    /// </summary>
    public const string LabRouteEntranceName = HiddenTvNodeName;

    /// <summary>
    /// <b>Where the lab sits: sealed, on the diagonal, 20 m down.</b>
    ///
    /// <para>Three constraints fix it almost completely. It must be BELOW the surface sections and
    /// out of sight of them, like the TV room. It must be ABOVE <c>NetProfile.KillPlaneY</c>
    /// (-30) - the global const <c>SandboxAvatar.ServerTick</c> enforces every tick, which is the
    /// trap that put the TV room at -20 rather than -40, and it applies to any sealed room this
    /// level ever adds. And it must claim ground no section claims, in plan, so a lab of unknown
    /// size cannot end up inside somebody else's room: (90, 90) is the NE diagonal outside blue
    /// (z &lt;= 50) and outside cyan (x &lt;= 70), 127 m from the origin against an
    /// <see cref="BubbleTestLayout.OffMapRadiusM"/> of 210, and 92 m from the TV room's nearest
    /// corner.</para>
    ///
    /// <para><b>Y = -14, and it is MEASURED rather than matched.</b> The obvious value was -20,
    /// to line up with <c>TvRoomAnchor.Y</c> — and it was wrong, which is why the self-test now
    /// measures instead of assuming. EGG-1 settled the lab as a lab PLUS a continuous escape
    /// route, so it descends: <c>--tvportal-selftest</c> reads the shipped scene's own geometry
    /// and reports its deepest point <b>7.45 m below this anchor</b> (its <c>ReturnTv</c> alone is
    /// 6.9 m down and 80 m east). At -20 that put the far end of the route at <b>y = -27.45</b>,
    /// <b>2.55 m</b> above <c>NetProfile.KillPlaneY</c> — the global const
    /// <c>SandboxAvatar.ServerTick</c> enforces every tick, whose failure mode is a player
    /// silently teleported back to spawn with nothing in any log saying why. It is the same trap
    /// that moved the TV room off -40, arriving from the one direction that room's fix could not
    /// see: not the ROOM being too deep, but the route out of it. At -14 the deepest point is
    /// -21.45 and the margin is 8.55 m, and <c>CheckLabRoute</c> re-measures it every run against
    /// whatever EGG-1 ships next.</para>
    ///
    /// <para><b>It is still sealed, which is the half that owns the LOOK.</b> EGG-1 removed the
    /// ported scene's own <c>WorldEnvironment</c> — correctly, because a second one would have
    /// fought <c>OutdoorAtmosphere</c>'s single-writer role over the whole level's sky — so the
    /// lab now renders under this level's environment and its own fluorescents. Depth does not
    /// change that (ambient is global); the room's own ceiling and walls do, and this plan
    /// position has no surface above it to leak sky through. Whether the residual daytime ambient
    /// still washes the room lighter than the source intended is EGG-1's open affect question and
    /// is carried into this packet's report as one, not guessed at here.</para></summary>
    public static readonly Vector3 PuffinLabAnchor = new(90f, -14f, 90f);

    /// <summary>The lab's arrival marker, or null when the scene did not load. Public because
    /// <c>TvPortalSelfTest</c> asks this world whether the route is live rather than re-deriving
    /// it from a file that may not be there.</summary>
    public Marker3D? LabArrival { get; private set; }

    /// <inheritdoc cref="LabArrival"/>
    public TvPortal? LabReturnTv { get; private set; }

    /// <summary>True when the lab loaded AND both seam nodes were found - i.e. the route is live.
    /// False is a supported, logged state, not a failure.</summary>
    public bool LabWired => LabArrival is not null && LabReturnTv is not null;

    /// <summary>The route the lab overrides, read out of the shipped table rather than rebuilt.
    /// </summary>
    public static BubbleTestLayout.TvRoute LabRoute
    {
        get
        {
            foreach (BubbleTestLayout.TvRoute r in BubbleTestLayout.TvRoutes)
                if (r.EntranceName == LabRouteEntranceName)
                    return r;
            return BubbleTestLayout.TvRoutes[0];
        }
    }

    /// <summary>
    /// <b>Instance EGG-1's lab if it is there, and be loud and harmless if it is not.</b>
    ///
    /// <para>EGG-1 and EGG-2 run in parallel and either may land first, so this method's real
    /// contract is the ABSENT case: with no <c>PuffinLab.tscn</c> on disk the level must come up
    /// with every other surprise working, <c>HiddenTv</c> must keep the room it has today, and the
    /// log must say why - <c>TvPortalHost.Subscribe</c>'s "loud rather than silent" warning is the
    /// pattern, and its reasoning is the reason: <i>a level whose easter egg quietly does nothing
    /// looks exactly like a level that never had one</i>.</para>
    ///
    /// <para><b><c>ResourceLoader.Exists</c> before <c>GD.Load</c>, not a null check after it.</b>
    /// <c>GD.Load</c> on a missing path emits an engine ERROR line before returning null, which in
    /// a suite log is indistinguishable from something being wrong - and something being missing
    /// on purpose is not.</para>
    ///
    /// <para><b>A half-built lab is treated as an absent one.</b> If the scene loads but either
    /// seam node is missing or is the wrong type, the route stays on its room: an entrance pointed
    /// at a lab with no floor marker, or a lab with no way out, strands whoever walks into it, and
    /// a sealed pocket is exactly where nothing can rescue them.</para></summary>
    private void SetUpPuffinLab()
    {
        if (!ResourceLoader.Exists(PuffinLabScenePath))
        {
            GD.PushWarning($"[bubbletest.lab] {PuffinLabScenePath} is not in this build - "
                           + $"{LabRouteEntranceName} keeps its own room and the lab throwback is "
                           + "absent. This is the EGG-1-has-not-landed case, not an error.");
            return;
        }

        var packed = GD.Load<PackedScene>(PuffinLabScenePath);
        var lab = packed?.Instantiate<Node3D>();
        if (lab is null)
        {
            GD.PushWarning($"[bubbletest.lab] {PuffinLabScenePath} exists but would not "
                           + $"instantiate - {LabRouteEntranceName} keeps its own room.");
            return;
        }
        lab.Name = PuffinLabNodeName;
        AddChild(lab);
        lab.GlobalPosition = PuffinLabAnchor;

        // owned:false - nodes inside an instanced scene are owned by that scene's root, which is
        // the same trap PedestalMount() records one method up. recursive:true because the seam
        // fixes the NAMES, not the depth, and a lab that grew a wrapper node is still a lab.
        LabArrival = lab.FindChild(PuffinLabArrivalName, recursive: true, owned: false) as Marker3D;
        LabReturnTv = lab.FindChild(PuffinLabReturnTvName, recursive: true, owned: false) as TvPortal;

        if (!LabWired)
        {
            GD.PushWarning($"[bubbletest.lab] {PuffinLabScenePath} loaded but the seam is not "
                           + $"there: {PuffinLabArrivalName}="
                           + $"{(LabArrival is null ? "MISSING" : "ok")} {PuffinLabReturnTvName}="
                           + $"{(LabReturnTv is null ? "MISSING" : "ok")}. {LabRouteEntranceName} "
                           + "keeps its own room rather than sending players into a pocket they "
                           + "cannot leave.");
            LabArrival = null;
            LabReturnTv = null;
            return;
        }

        GD.Print($"[bubbletest.lab] lab at {lab.GlobalPosition} - arrival "
                 + $"{LabArrival!.GlobalPosition}, return TV present.");
    }

    // --- Spawns ---------------------------------------------------------------------------

    private void CollectSpawnPoints()
    {
        Node? hub = GetNodeOrNull(BubbleTestLayout.Section.Hub.ToString());
        if (hub is null)
        {
            // Loud, because the failure is silent otherwise: with no spawn points every player
            // spawns at the world origin on top of each other, which reads as a netcode bug.
            GD.PushWarning("[bubbletest] no Hub section in the world scene — no spawn points; "
                           + "every player will spawn at the origin.");
            return;
        }

        var markers = new List<Marker3D>();
        foreach (Node child in hub.GetChildren())
        {
            if (child is Marker3D marker && marker.Name.ToString().StartsWith("Spawn"))
                markers.Add(marker);
        }
        markers.Sort((a, b) => string.CompareOrdinal(a.Name.ToString(), b.Name.ToString()));
        foreach (Marker3D marker in markers)
            SpawnPoints.Add(marker.GlobalPosition);

        if (SpawnPoints.Count != BubbleTestLayout.SpawnCount)
        {
            GD.PushWarning($"[bubbletest] hub carries {SpawnPoints.Count} spawn marker(s), "
                           + $"expected {BubbleTestLayout.SpawnCount} — Protocol.MaxPlayers is 6 "
                           + "and a short ring means two players share a spawn.");
        }
    }

    // --- Sky ------------------------------------------------------------------------------

    /// <summary>Instantiate the shipped atmosphere. Lifted from the original (since-removed)
    /// level's setup, minus everything that belonged to that level:
    ///
    /// <para><b>No <c>Environment</c>/<c>Sun</c> teardown.</b> That level had to
    /// <c>QueueFree</c> two placeholder nodes because its generator authored them into its
    /// generated scene. <c>BubbleTest.tscn</c> is hand-authored and deliberately contains
    /// neither, so <see cref="OutdoorAtmosphere"/> — which brings its own
    /// <c>WorldEnvironment</c> and <c>Sun</c> — is the only writer from frame zero, with no
    /// one-frame window in which two suns exist.</para>
    ///
    /// <para><b>No <c>NightDome</c>, no ground-chroma push, no <c>--cycle-readout</c>.</b> The
    /// first two were that level's; the third is available on any world through the shared
    /// flag and duplicating it here would be a second readout.</para>
    ///
    /// <para><b>The cycle period is left alone deliberately.</b>
    /// <c>RunDriver.DefaultCyclePeriodSec</c> is 720 s, so a one-hour session sees five full
    /// day/night cycles — comfortably over program §6 item 7's "at least one full night", and
    /// over the ~2-nights-per-hour the packet asked for as a floor. Calling
    /// <c>LaunchOptions.RequestDefaultCyclePeriod</c> here would only make this world disagree
    /// with every other world for no gain, and <c>--cycle-period</c> still overrides for a
    /// capture.</para></summary>
    private void SetUpAtmosphere()
    {
        int dayIndexOffset = NetworkManager.Instance?.Options?.CycleStartDay ?? 0;

        // THE CONTROL FOR THE PERFORMANCE GATE, and why it is a flag rather than an edit.
        // Turning the shadow on reverses PR #55's draw-call ruling, so the reversal has to be
        // argued on a measured before/after. Comparing against a pre-packet checkout would
        // confound the shadow's cost with this packet's material and lighting changes; this flag
        // toggles the ONE variable, so the difference between two runs of the same binary is the
        // shadow and nothing else. Read from GetCmdlineUserArgs (NightAidDriver's idiom), so it
        // must sit AFTER the `--` separator or the engine swallows it.
        bool shadows = true;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == NoShadowsFlag)
                shadows = false;
        }

        AddChild(new OutdoorAtmosphere
        {
            Name = "OutdoorAtmosphere",
            DayIndexOffset = dayIndexOffset,

            // --- DARK-1's profile (2026-08-28). See the block comment below. ---
            DaySunEnergyScale = DaySunScale,
            DayAmbientEnergyScale = DayAmbientScale,
            DaySkyColorScale = DaySkyScale,
            SunShadowEnabled = shadows,
            SunShadowTwoSplits = true,
            SunShadowMaxDistanceM = ShadowRangeM,
            GlowEnabled = true,
            GlowThreshold = GlowThresholdHdr,
            GlowIntensity = GlowIntensityValue,

            // DARK-1b ADDENDUM B: NIGHT IS DARK, NOT A WALL.
            //
            // This world has no PlayerSightService, so ReadSight() falls to "not authoritative"
            // and TargetRangeM returns PlayerSightCurve.DarkFloorM = 3 m. That authors a fog
            // density near 1.0 m^-1 -- full at 4 m, 20 % at 6 m, ZERO at 8 m. FIX-1 and FIX-2
            // measured the same wall independently, and it is why both of them shot night
            // captures that read as evidence of a broken feature when the feature was fine and
            // simply eight metres away.
            //
            // The fix is the seam this class already has: SightSource is the hook AtmosphereLab
            // uses to stand in for the service, and setting it here is a WORLD-level answer
            // rather than a change to the shared curve. Nothing about PlayerSightCurve,
            // PlayerSightService or NightAmbientFloor (THRILL-BIBLE 6.2, Talon's) moves.
            //
            // ASSUMPTION, FLAGGED, NOT INFERENCE DRESSED AS FACT: the MVP plan 5 Q2 offered
            // (a) lock this world to daylight, (b) a fixed ~25-30 m night range, (c) leave the
            // 7 m wall. Talon's ruling of 2026-08-28 says "see both light and dark" and "darker
            // in general", which the orchestrator read as (b) and explicitly flagged as its own
            // reading rather than his words. This line is that reading. If he rules otherwise it
            // is one constant, not a rebuild.
            SightSource = () => (true, NightSightRangeM),
        });
    }

    // --- DARK-1: this world's daylight profile (2026-08-28) ---------------------------------
    //
    // Talon: the level "looks clinical and sterile rather than atmospheric" and had "drifted
    // into a bright white test lab look"; then, "I would like bolder overall."
    //
    // THE DIAGNOSIS WAS NOT A HUE PROBLEM AND NOT A COLOUR-SPACE BUG. Measured over the
    // committed captures, 91 % of every day frame sat at or above luminance 192 and 0.3 % below
    // 64 — the frame had no dark end at all. Two causes, both here rather than in the materials:
    // nothing in this world was ever occluded (no shadow caster anywhere in the repo, by PR
    // #55's convention), and total illumination was ~1.85x (MaxSunEnergy 1.3 + DayAmbientEnergy
    // 0.55) landing on ground authored at 0.80-0.86 albedo. A surface cannot be dark if nothing
    // can shade it and the key light is that strong.
    //
    // WHY KNOBS AND NOT CONSTANTS. OutdoorAtmosphere is the single writer of sky/sun/moon/
    // ambient for EVERY world (`.claude/rules/single-writer.md`). Every other world that has
    // read it read the same constants and suites assert on their
    // result. These properties default to exact no-ops, so this world is the only one to move.
    //
    // WHY NIGHT IS UNTOUCHED. Every scale ramps to 1 by NightFactor, so at deep night this world
    // renders exactly what it rendered before. THRILL-BIBLE §6.2's night floor
    // (OutdoorAtmosphere.NightAmbientFloor, 0.05) is Talon's live blank and is not moved here;
    // this world's night fog is already a wall and this pass does not make it worse.

    /// <summary>Turns the sun shadow off for one run, so the performance gate's before/after is
    /// two runs of ONE binary rather than a comparison across two different trees. Pair it with
    /// <see cref="PerfReadout.Flag"/>; both go after the <c>--</c> separator.</summary>
    public const string NoShadowsFlag = "--bt-no-shadows";

    // THE THREE EXPOSURE NUMBERS, AND WHY THEY ARE WHERE THEY ARE (measured, 2026-08-29).
    //
    // Talon's ruling of 2026-08-28 is TWO constraints, not one: "the super bright light colours
    // was too much ... darker in general" AND "see both light and dark" — he must still be able
    // to SEE. Those pull opposite ways, and the number that satisfies one alone fails the other,
    // so this trio was picked off a measured ladder rather than off a swatch:
    //
    //   sun/ambient/sky      shadows(<64)  midtones  highlights(>=192)  mean   verdict
    //   1.00 / 1.00 / 1.00       0.3 %       0.2 %       99.0 %          241   the shipped defect
    //   0.50 / 0.16 / 0.20      53.2 %      46.7 %        0.1 %           85   too far — see below
    //   0.62 / 0.34 / 0.42 <-- shipped; the four-band table is in DARK-1's report
    //
    // THE MIDDLE ROW IS WORTH NAMING because it is the trap this packet is most likely to be
    // re-tuned back into. It was the previous agent's landing point and it is genuinely "bolder",
    // but a NOON frame with half its pixels below 64 has simply swapped one missing end of the
    // histogram for the other: the red section's cairn crushed to a single black silhouette with
    // no readable blocks in it, and the level stopped reading as daytime. Darker is the ask;
    // unlit is not.

    /// <summary>Day sun energy scale. Cut least of the three: the sun is the only DIRECTIONAL
    /// channel, so it is what the new shadow reads against and what keeps a lit face and a shaded
    /// face different values. 1.30 x 0.62 = 0.806 effective peak sun.</summary>
    private const float DaySunScale = 0.62f;

    /// <summary>Day ambient energy scale. 0.55 x 0.34 = 0.187 effective day ambient. Ambient is
    /// cut harder than the sun on purpose: it is the channel with no direction, so it is what
    /// was filling the shadows before there were any, and cutting it is what lets a shadow mean
    /// something once one exists. It is deliberately NOT cut to nothing — ambient is the only
    /// light reaching a face the sun cannot, so it is the knob that decides whether a shadowed
    /// surface is dark or is absent.</summary>
    private const float DayAmbientScale = 0.34f;

    /// <summary>Day sky value scale. The shipped midday horizon is 0.90/0.90/0.88 — a near-white
    /// wall behind every shot and, because AmbientLightSource is Sky, the ambient's own colour.
    /// A flat value multiply keeps the gradient's hue exactly and takes the wall down to a dim
    /// overcast. This knob also carries the FOG colour (see <c>OutdoorAtmosphere.ApplyProfile</c>),
    /// which across 100 m sightlines is most of the frame's area — it is the single biggest
    /// de-washing lever in the pass. Held at 0.42 rather than lower because under ~0.3 the DAY
    /// sky goes to near-black and the level stops reading as daytime at all, which is the half of
    /// the ruling that says "see both light and dark".</summary>
    private const float DaySkyScale = 0.42f;

    /// <summary>Cascade range. The sections are ~100 m arms off a 40 m hub, but a shadow only has
    /// to read where the player is: 120 m covers the hub plus a whole arm from the hub's centre,
    /// and refusing to pay for the 210 m off-map radius is most of why this is affordable.</summary>
    private const float ShadowRangeM = 120f;

    /// <summary>Bloom threshold, linear HDR. Above the graded ground (which lands far below 1
    /// after the scales above) and below the bubbles' emission peak, so the bloom belongs to the
    /// emitters and is not a haze over the frame.</summary>
    private const float GlowThresholdHdr = 1.15f;

    /// <summary>Bloom intensity. Deliberately modest: THRILL-BIBLE §8.2 — a glow that spikes is a
    /// startle nobody directed, and the bubbles' own night curve is a slow oscillation.</summary>
    private const float GlowIntensityValue = 0.9f;

    /// <summary>This world's night sight range in metres (DARK-1b addendum B). 28 m sits in the
    /// middle of the plan's 25–30 m band and under <c>PlayerSightCurve.MaxSightM</c> (36), so it
    /// is a range the shared curve could itself have produced — it is not an out-of-band value
    /// smuggled in through the seam. <c>SightPresentation</c> turns it into
    /// <c>3.0 / 28 = 0.107 m⁻¹</c> of fog, i.e. 5 % of a surface's contrast survives at exactly
    /// 28 m: the next block is visible, the far end of a 100 m arm is not.</summary>
    private const float NightSightRangeM = 28f;

    // --- Out of bounds --------------------------------------------------------------------

    /// <summary>Off-map, the void, and the walk back.
    ///
    /// <para><b>Why this world creates the service instead of configuring one.</b>
    /// <see cref="RespawnService"/> had exactly one creator in the codebase when this was
    /// written — a since-removed world's setup in <c>Gameplay</c>, which returned immediately for
    /// any other world. So for every other world <c>RespawnService.Instance</c> was null,
    /// and "configure the existing one" had nothing to configure. Creating it here keeps the
    /// change inside this world (BT-0 owns one row of <c>Gameplay</c> and no more) and means a
    /// future world gets respawn by asking for it rather than by being special-cased in
    /// <c>Gameplay</c>. <c>RespawnService._EnterTree</c> sets the static
    /// <see cref="RespawnService.Instance"/>, so BT-6/BT-8 find it the usual way.</para>
    ///
    /// <para><b>Avatars are resolved lazily, per scan.</b> <c>Gameplay</c> owns the
    /// <c>Players</c> node and creates the <c>PropManager</c> AFTER it adds the world (see
    /// <c>Gameplay._Ready</c>: the world is built before connecting, prop manager after), so
    /// anything captured at this world's <c>_Ready</c> would capture null. The lambda walks the
    /// parent's <c>Players</c> node on each 10 Hz scan instead — the same node
    /// <c>Gameplay.EnumerateAvatars</c> walks, one indirection later.</para>
    ///
    /// <para><b><c>Props</c> is left null on purpose.</b> Its only job is dropping carried items
    /// on death, and this level has nothing to carry — bubbles are popped by touch, not picked
    /// up (D6). <see cref="RespawnService"/> documents the null case as supported
    /// ("with no PropManager a death simply keeps its cargo"). BT-8 or BT-10 can set it in one
    /// line if that changes.</para>
    ///
    /// <para><b>Falling is never lethal in the ordinary sense</b> (§4 rule 3): the blue tower's
    /// 42 m summit is 112 m above <see cref="BubbleTestLayout.VoidKillY"/>, so a fall from
    /// anywhere in the level lands on ground. The penalty is the walk back, which is what makes
    /// the tower's height mean something without making a mistake cost a session.</para></summary>
    private void SetUpRespawn()
    {
        bool isServer = NetworkManager.Instance is null
                        || NetworkManager.Instance.Role != NetworkManager.SessionRole.Client;

        _respawn = new RespawnService
        {
            Name = RespawnService.NodeName,
            OffMapRadiusM = BubbleTestLayout.OffMapRadiusM,
            VoidKillY = BubbleTestLayout.VoidKillY,
        };
        AddChild(_respawn);
        _respawn.Setup(
            isServer,
            avatars: EnumerateAvatars,
            respawnPoint: () => BubbleTestLayout.RespawnPoint,
            props: null);

        // Program §6 item 6's other instrumented event. It lives here rather than on
        // SectionVolume (where the packet first put it) for a plain correctness reason: there are
        // six volumes and one death, so six subscriptions would print every death six times.
        // Same log shape as the section crossings, for the same grep.
        _respawn.Died += (peer, cause) =>
            GD.Print($"[bubbletest.death] peer={peer} cause={cause}");
    }

    private IEnumerable<SandboxAvatar> EnumerateAvatars()
    {
        Node? players = GetParent()?.GetNodeOrNull("Players");
        if (players is null) yield break;
        foreach (Node child in players.GetChildren())
            if (child is SandboxAvatar avatar)
                yield return avatar;
    }
}
