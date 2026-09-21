# Prop physics materials (PHYS-1, 2026-09-20)

Talon, 2026-09-20: *"Make it do what the player expects: if they're placing an object on a shelf
and accidentally hit a bunch of boxes, those boxes should fall over like dominoes, and cans should
roll around."*

**The whole of ruling P4 is authored, not coded.** Friction and bounce live on the four
`PhysicsMaterial` resources here, referenced from each prefab's
`physics_material_override`; the per-body quantities a `PhysicsMaterial` cannot carry — angular
damping, linear damping, centre of mass, `can_sleep` — are native `RigidBody3D` properties
authored in the prefab `.tscn` itself. Both halves arrive: `Can.tscn`'s own header records that a
NATIVE property survives nested-`PackedScene` instancing on this build where a C# `[Export]` does
not, and `physics_material_override`, `angular_damp`, `center_of_mass` and `can_sleep` are all
native.

One resource per `PropMaterial` voice, so a prop's SOUND and its FEEL come off the same
classification and cannot disagree about what a thing is made of.

| voice | friction | bounce | what it is for |
|---|---|---|---|
| `tin` (a can) | **0.25** | 0.10 | the low number in the table, and the reason is the sentence: a can has to ROLL. It is also the only prop with any bounce at all — a tin hitting a board pings |
| `cardboard` (a cereal box) | **0.75** | 0.00 | the high number, for the opposite reason: a box shoved along a board must TRIP over its own base rather than skate on it. Friction is what turns a push into a topple |
| `produce` | 0.90 | 0.00 | fruit neither slides nor bounces. `rough` so the higher friction wins against whatever it lands on rather than being multiplied away |
| `wood` (a crate, and every unclassified prop) | 0.60 | 0.00 | between the two: a crate slides a little and stops |

The floor and the shelves author no material, so they are Godot's default friction 1.0 and the
combined figure at a contact is the prop's own — which is what makes this table read directly as
"how this object behaves on a board".

**Do not add a fifth file without a `PropMaterial` member to hang it on.** The mapping is
`Carryable.MaterialFor` / `Carryable.ResolveMaterial`, the same one that picks the sound profile in
`assets/items/`, and a physics material with no voice would be a second classification of the same
object that nothing keeps in step.
