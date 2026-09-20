using System.Collections.Generic;
using Godot;

namespace MpFoundation.Tools;

/// <summary>
/// PORTED VERBATIM from the sibling repo <c>C:/repos/mp-foundation</c>,
/// <c>scripts/tools/AuthoredSceneDumper.cs</c> @ <c>bf530c7</c> ("feat(world): authored scenes +
/// real models everywhere, aim-based interact targeting"). The two repos share no git history
/// (roots <c>f731fa49</c> vs <c>65931fde</c>), so this is a file port and never a cherry-pick;
/// the body and the doc below are the original author's, and the namespace already matched.
/// Brought over by BT-0 as the mechanism behind program decision D2 —
/// <c>docs/agents/2026-08-27-bubble-test-program.md</c> §3 — which is Talon's 2026-08-27 ruling
/// that "no procedural generation" means none <i>at runtime</i>: a generator may be run once, in
/// a tool run, and its packed result committed as ordinary hand-editable nodes. BT-1 bakes the
/// scramble cairn with it and BT-2 the hills and the Postpile; <c>BubbleTestSelfTest</c> then
/// proves the result really is inert. Drive it with <c>--dump-scene &lt;in.tscn&gt;
/// &lt;out.tscn&gt;</c> (see <c>Boot.cs</c>).
///
/// One-shot conversion utility: packs a live, procedurally-built environment node into
/// an authored .tscn so the world becomes editor-editable data instead of code. Used by
/// short-lived driver tools during the authored-scene migration, then the drivers are
/// deleted; this stays as the reusable core (and the record of the dump rules).
///
/// Rules encoded here:
///   - The environment's children are reparented under a PLAIN Node3D root (no script),
///     so instancing the saved scene never re-runs a build in _Ready.
///   - Nodes whose script type is in <c>builderClasses</c> build their own children at
///     runtime (TinyDoor, TvPortal, ...): the node is saved (with its exported state —
///     stamp AuthoredVisual=true BEFORE calling Dump), its subtree is not, EXCEPT
///     children named in <c>keepShallowNames</c> (animation-contract nodes like the
///     tiny door's "Leaf" and the hoop's "Rig"), which are saved empty so an authored
///     model can be nested inside them in the editor.
///   - A child that is itself an instanced scene (SceneFilePath set) is saved as an
///     instance reference, never inlined.
/// </summary>
public static class AuthoredSceneDumper
{
    public static Error Dump(
        Node3D environment,
        string outPath,
        HashSet<string> builderClasses,
        HashSet<string> keepShallowNames)
    {
        var root = new Node3D { Name = environment.Name };
        var children = new List<Node>();
        foreach (Node child in environment.GetChildren())
            children.Add(child);
        foreach (Node child in children)
        {
            environment.RemoveChild(child);
            root.AddChild(child);
        }

        OwnRecursive(root, root, builderClasses, keepShallowNames);

        var packed = new PackedScene();
        Error err = packed.Pack(root);
        if (err != Error.Ok)
        {
            GD.PrintErr($"AuthoredSceneDumper: Pack failed: {err}");
            return err;
        }
        err = ResourceSaver.Save(packed, outPath);
        if (err != Error.Ok)
            GD.PrintErr($"AuthoredSceneDumper: Save failed: {err}");
        else
            GD.Print($"AuthoredSceneDumper: saved {outPath} ({CountOwned(root, root)} nodes)");
        return err;
    }

    private static void OwnRecursive(
        Node node, Node root, HashSet<string> builderClasses, HashSet<string> keepShallowNames)
    {
        foreach (Node child in node.GetChildren())
        {
            child.Owner = root;

            // Instanced scenes stay references; their internals belong to their own file.
            if (!string.IsNullOrEmpty(child.SceneFilePath))
                continue;

            if (builderClasses.Contains(child.GetType().Name))
            {
                // The script rebuilds this subtree at runtime; save only the shallow
                // animation-contract nodes so models can be authored inside them.
                foreach (Node grandchild in child.GetChildren())
                    if (keepShallowNames.Contains(grandchild.Name))
                        grandchild.Owner = root;
                continue;
            }

            OwnRecursive(child, root, builderClasses, keepShallowNames);
        }
    }

    private static int CountOwned(Node node, Node root)
    {
        int n = 0;
        foreach (Node child in node.GetChildren())
        {
            if (child.Owner == root)
                n += 1 + CountOwned(child, root);
        }
        return n;
    }
}
