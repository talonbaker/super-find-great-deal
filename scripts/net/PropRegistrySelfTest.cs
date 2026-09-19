using System.Collections.Generic;
using System.Linq;
using Godot;

namespace MpFoundation.Net;

/// <summary>
/// Headless self-test for the pure prop-state store (<see cref="PropRegistry"/>) and its
/// value types. No scene tree, no physics, no net stack — just the store's state-machine
/// invariants, the property the server's object authority rests on: ids are stable, grabs
/// are first-come-wins, mode transitions are legal, and a bad/hostile request returns false
/// instead of corrupting the store or throwing. Prints one line per check and
/// "PROPREG-TEST OVERALL: PASS|FAIL"; exit 0 only if everything passed.
/// Run: Godot --headless --path . res://tests/scenes/PropRegistrySelfTest.tscn
/// </summary>
public partial class PropRegistrySelfTest : Node
{
    private readonly List<(string Name, bool Ok)> _results = new();

    public override void _Ready()
    {
        RunTests();
        Finish();
    }

    private void RunTests()
    {
        var reg = new PropRegistry();
        var a = Transform3D.Identity.Translated(new Vector3(1, 0, 0));
        var b = Transform3D.Identity.Translated(new Vector3(2, 0, 0));

        int id1 = reg.Register(PropKind.Crate, a);
        int id2 = reg.Register(PropKind.Ball, b);
        Check("register_assigns_unique_ascending_ids", id1 == 1 && id2 == 2 && reg.Count == 2);
        Check("registered_props_are_resting",
            reg.TryGet(id1, out PropState s1) && s1.Mode == PropMode.Resting && s1.Kind == PropKind.Crate
            && s1.Transform.Origin == new Vector3(1, 0, 0) && s1.HolderPeerId == 0);
        Check("all_reflects_registered",
            reg.All.Count == 2 && reg.All.Any(p => p.Id == id1) && reg.All.Any(p => p.Id == id2));

        // The "no holder" sentinel (peerId 0, and any non-positive id) must never be accepted as
        // a real holder — otherwise a prop soft-locks as permanently Held by nobody.
        Check("set_holder_zero_rejected",
            !reg.SetHolder(id1, 0) && reg.TryGet(id1, out PropState z) && z.Mode == PropMode.Resting && z.HolderPeerId == 0);
        Check("set_holder_negative_rejected",
            !reg.SetHolder(id1, -1) && reg.TryGet(id1, out PropState z2) && z2.Mode == PropMode.Resting);

        // First-grab-wins.
        Check("set_holder_succeeds",
            reg.SetHolder(id1, 7) && reg.TryGet(id1, out PropState h) && h.Mode == PropMode.Held && h.HolderPeerId == 7);
        Check("second_holder_rejected_while_held",
            !reg.SetHolder(id1, 9) && reg.TryGet(id1, out PropState h2) && h2.HolderPeerId == 7);
        Check("same_holder_idempotent",
            reg.SetHolder(id1, 7) && reg.TryGet(id1, out PropState h3) && h3.HolderPeerId == 7);
        Check("set_holder_zero_rejected_while_held",
            !reg.SetHolder(id1, 0) && reg.TryGet(id1, out PropState h4) && h4.Mode == PropMode.Held && h4.HolderPeerId == 7);

        // Release moves Held -> Loose (into physics); an unheld prop cannot be released.
        Check("release_moves_held_to_loose",
            reg.Release(id1, a) && reg.TryGet(id1, out PropState l) && l.Mode == PropMode.Loose && l.HolderPeerId == 0);
        Check("release_unheld_rejected", !reg.Release(id2, b)); // id2 still resting
        Check("regrab_after_release",
            reg.SetHolder(id1, 9) && reg.TryGet(id1, out PropState rg) && rg.HolderPeerId == 9);

        // Loose transform streaming is only valid while Loose.
        reg.Release(id1, a); // back to loose
        var moved = Transform3D.Identity.Translated(new Vector3(5, 3, 0));
        Check("set_loose_transform_updates",
            reg.SetLooseTransform(id1, moved) && reg.TryGet(id1, out PropState mv) && mv.Transform.Origin == new Vector3(5, 3, 0));
        Check("set_loose_transform_rejected_when_resting", !reg.SetLooseTransform(id2, moved)); // id2 resting

        // Latch to resting (a loose prop that went to sleep).
        var rest = Transform3D.Identity.Translated(new Vector3(5, 0, 0));
        Check("set_resting_latches_loose",
            reg.SetResting(id1, rest) && reg.TryGet(id1, out PropState rs) && rs.Mode == PropMode.Resting
            && rs.Transform.Origin == new Vector3(5, 0, 0));

        // ReleaseAllHeldBy: a departed peer's holds all latch to Resting; others untouched.
        var reg2 = new PropRegistry();
        int p1 = reg2.Register(PropKind.Crate, a);
        int p2 = reg2.Register(PropKind.Ball, b);
        int p3 = reg2.Register(PropKind.Crate, a);
        reg2.SetHolder(p1, 42);
        reg2.SetHolder(p2, 42);
        reg2.SetHolder(p3, 7);
        var home = Transform3D.Identity.Translated(new Vector3(9, 0, 9));
        int released = reg2.ReleaseAllHeldBy(42, home);
        Check("release_all_held_by_counts_released", released == 2);
        Check("release_all_held_by_latches_resting",
            reg2.TryGet(p1, out PropState rp1) && rp1.Mode == PropMode.Resting && rp1.HolderPeerId == 0
            && rp1.Transform.Origin == new Vector3(9, 0, 9)
            && reg2.TryGet(p2, out PropState rp2) && rp2.Mode == PropMode.Resting);
        Check("release_all_held_by_leaves_other_holders",
            reg2.TryGet(p3, out PropState rp3) && rp3.Mode == PropMode.Held && rp3.HolderPeerId == 7);
        Check("release_all_held_by_none_for_absent_peer", reg2.ReleaseAllHeldBy(999, home) == 0);

        // Bad ids never throw, always false.
        Check("bad_id_set_holder_false", !reg.SetHolder(999, 1));
        Check("bad_id_release_false", !reg.Release(999, a));
        Check("bad_id_set_loose_false", !reg.SetLooseTransform(999, a));
        Check("bad_id_set_resting_false", !reg.SetResting(999, a));
        Check("bad_id_tryget_false", !reg.TryGet(999, out _));

        // Register must never hand out an id already occupied by an authored prop registered
        // "ahead" of the auto-incrementing counter (RISK-AUDIT-2026-07-12.md 5.1d) — it should
        // skip forward past the occupied id instead of colliding with it.
        var reg3 = new PropRegistry();
        reg3.Register(PropKind.Crate, a); // id 1
        reg3.Register(PropKind.Ball, b); // id 2
        var authored = Transform3D.Identity.Translated(new Vector3(7, 7, 7));
        bool authoredOk = reg3.RegisterAt(3, PropKind.Crate, authored); // claims the "next" id
        int r4 = reg3.Register(PropKind.Ball, b); // must skip past id 3
        int r5 = reg3.Register(PropKind.Crate, a);
        Check("register_skips_past_occupied_authored_id",
            authoredOk && r4 != 3 && r5 != 3 && r4 != r5
            && reg3.TryGet(3, out PropState authoredState) && authoredState.Kind == PropKind.Crate
            && authoredState.Mode == PropMode.Resting && authoredState.Transform.Origin == new Vector3(7, 7, 7));

        // RegisterAt must refuse (not overwrite) an already-occupied id.
        var reg4 = new PropRegistry();
        var first = Transform3D.Identity.Translated(new Vector3(1, 1, 1));
        var second = Transform3D.Identity.Translated(new Vector3(2, 2, 2));
        bool firstOk = reg4.RegisterAt(100, PropKind.Crate, first);
        bool secondOk = reg4.RegisterAt(100, PropKind.Ball, second);
        Check("register_at_refuses_duplicate_id",
            firstOk && !secondOk
            && reg4.TryGet(100, out PropState dup) && dup.Kind == PropKind.Crate
            && dup.Transform.Origin == new Vector3(1, 1, 1));
    }

    private void Check(string name, bool ok)
    {
        _results.Add((name, ok));
        GD.Print($"PROPREG-TEST {name}: {(ok ? "PASS" : "FAIL")}");
    }

    private void Finish()
    {
        int failed = _results.FindAll(r => !r.Ok).Count;
        GD.Print($"PROPREG-TEST OVERALL: {(failed == 0 ? "PASS" : "FAIL")} ({_results.Count - failed}/{_results.Count})");
        GetTree().Quit(failed == 0 ? 0 : 1);
    }
}
