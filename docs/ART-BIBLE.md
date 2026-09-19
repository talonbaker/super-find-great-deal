# Art Bible

> The living style authority for this game. Code cites this file by section number as a
> canonical reference (`AvatarVisual.cs`, `CheckerFloor.gdshader`, `VoiceSpeaker.cs`);
> those citations are load-bearing, so **section numbers are stable** — add subsections,
> don't renumber.
>
> **This is a working reference, not a contract** (Talon's brief, 2026-08-20, §12). It is a
> current best guess and it will be wrong at times. **When Talon says a documented rule should
> change, the agent updates this file rather than defending its existing text.** Do not push
> back on a direction change by citing what the bible currently says. Section numbers are the
> one thing that does not move — code cites them.
>
> **Direction (2026-08-20):** *A mundane world, with absurdity in specific accents.* The world
> reads **boring by default** — desaturated naturals, greys and beiges and browns and greens,
> nothing that looks synthetic or designed — because "boring" is not an aesthetic here, it is a
> **budget**. The environment occupies most of the screen most of the time, so every unit of
> saturation the landscape spends is one a flower, a bug, a fire or a creature cannot.
> Absurdity lives in **small, specific, tagged accent zones** — a brown-furred creature with a
> bright red nose — modelled on real biological warning colouration, applied per *asset* rather
> than per category, never spread evenly, and never as a second competing palette. There are no
> two opposing teams of assets: one consistent mundane world, with bright accents as a rare
> curated layer on top. **If everything gets an accent, nothing reads as special.**
>
> **Shading is flat/toon** (Talon's ruling, 2026-08-20 evening): flat colour, banded light, rim
> light where an accent has to survive flat lighting. Weathering and growth are tinted masks
> over vertex colour, never photoreal texture sets. *"Maybe we will turn a direction in the
> future"* — so nothing gets built that would make a later PBR turn expensive, and equally
> nothing hedges the toon work.
>
> **All style validation happens under flat, neutral midday daylight** (brief §2): no fog, no
> weather, no dramatic shadow play, no grading. Geometry, palette and material work are judged
> on their own merits; lighting mood does not get to do the work. Atmospheric extremes come
> after the baseline is locked, through the §8 concept review.
>
> The per-category **triangle, material, texture and draw-call budgets** derived from that
> direction live in `docs/ENVIRONMENT-ASSET-CONTRACT.md` §1–§1.3, with the arithmetic that
> produced them. They are not repeated here.
>
> *Superseded 2026-08-20: the previous direction line, "**Warm minimalism**," which described a
> pastel creature-sorting game and had outlived it by two pivots. The restraint it asked for
> survives; the pastel world it was restraining does not.*
>
> The rationale and rejected alternatives of that **older** direction live in the companion
> spec `docs/superpowers/specs/2026-07-18-visual-style-direction-design.md`. The current
> direction's source is `docs/agents/2026-08-20-STYLE-brief-from-talon.md` — Talon's brief,
> verbatim — and where the two disagree the brief wins.

---

## §1 — Philosophy & pillars

**Nothing in this file is a rule and nothing in it gates anything.** It records what the game
has looked like and why, so a new surface can be made to belong. Try things that contradict
it — that is how the direction moves, and this file follows the game rather than fencing it.

Three pillars. When they pull against each other, the earlier one is usually the one to keep.

1. **Legible first.** Gameplay that does not read instantly tends to be the thing players
   complain about without knowing why. Motion wants a ground reference (§3); silhouettes want
   to separate from the background at a glance. Style bought with legibility debt is usually
   the trade that gets reversed later.
2. **Restraint with intent.** Minimal is the baseline; every addition — a saturated
   accent, a bloom, a tinted light — reads best when it earns its place by pointing attention
   or adding character. Decoration for its own sake is usually the first thing to cut. "Nothing extra" was never
   wrong; it was just unfinished.
3. **Time, love & care.** The world should feel like someone who valued the experience
   built it. A cohesive palette, a creature with a voice, a zone lit with warmth — these
   are the difference between "a physics demo" and "a place."

Performance is the constraint that shapes all three: Forward+, no textures **[superseded
2026-07-29 by §4.4's budgeted texture contract; *re-tightened* 2026-08-20 by the flat/toon
ruling — the world is vertex-coloured and there is now exactly **one** texture in the whole
game, the shared decal atlas. §4.4 is the authority and it now has numbers instead of
blanks]**, one custom shader family, post-processing gated to where it pays. See §7.

**A fourth constraint, added 2026-08-20 and stated as a pillar because it is one.** The
priority order above is about what the player *sees*; underneath it sits what the machine can
*afford*, and on this project that is **draw calls before triangles**. Six player bodies spend
roughly a third of the entire draw-call budget before a tree is drawn
(`ENVIRONMENT-ASSET-CONTRACT.md` §1.2). Every rule in this file that looks like asceticism —
one material per unique appearance, one shader family, shared atlases, no per-object texture
sets — is that arithmetic, not taste. **Low-end laptops outrank visual ambition**, and that is
Talon's own ordering.

---

## §2 — Palette system

> **Restated 2026-08-20 from Talon's brief §1.** The system is now **one mundane world
> palette** with **warning-coloured accents on specific tagged zones**, and **Meridian is
> UI-only**. The three-way split below is the current shape:
>
> | Palette | What it is | Where it may appear |
> |---|---|---|
> | **World** (§2.2, §2.4) | desaturated naturals — greys, beiges, browns, greens. Nothing saturated, nothing that reads as synthetic or designed | terrain, foliage, most props, and the **base** colouring of creatures |
> | **Accent** (§2.3) | saturated *warning* colours — reds, yellows, blues — modelled on poison frogs, wasps, venomous berries | **small tagged zones only**: hazards, points of interest, one deliberate absurd flourish per asset. Carried in `COLOR_0.a` (`ENVIRONMENT-ASSET-CONTRACT.md` §6.2) |
> | **Meridian** (§2.1) | the saturated UI token set in `resources/UITheme.tres` | **UI chrome only.** It is no longer the source of world hero accents |
>
> **The accent is per-asset, not per-category.** A creature is not "the mundane one" or "the
> bright one" — a single creature is almost entirely natural-toned with one or two loud accent
> zones. Do not build two visually opposing teams of assets.
>
> **Where this contradicts §2.1–§2.3 below, this block wins.** Those subsections were written
> for a per-zone hero-accent scheme pulled from Meridian tokens; the mechanism survives (one
> temperature, at most two accents, accents are signals not decoration), the *source* of the
> colours does not. §2.4 is the current landscape authority and it is governed by brief §1.

The world is built from a **neutral base** with **per-zone accent schemes** layered on
top. This keeps the whole game coherent while letting each area own a mood.

### §2.1 — Two palettes, do not mix them up

The game runs **two distinct color sets**. They serve different jobs and cross-using them is what
made the two mints confusing the first time:

- **Meridian accents** — the *saturated* UI/hero-accent tokens defined in
  `resources/UITheme.tres`. **UI only, from 2026-08-20** — interactive highlights, screen
  chrome, HUD state. They are **no longer** the source of zone hero colours or emissive world
  accents; a world accent comes from the accent palette above, which is warning colouration
  rather than brand colour. Never recolor these ad hoc — pull the token.

  | Token | Hex | RGB (linear-ish, as authored) |
  |---|---|---|
  | Mint / Teal | `#35C2AC` | `0.208, 0.761, 0.675` |
  | Teal-hi | `#9BE9DC` | `0.608, 0.914, 0.863` |
  | Blush (peach) | `#F7BDA2` | `0.969, 0.741, 0.635` |
  | Orange | `#F26A2B` | `0.949, 0.416, 0.169` |
  | Panel | `#12201C` | `0.071, 0.125, 0.110` |
  | Surface | `#0E1514` | `0.055, 0.082, 0.078` |

- **Character pastel family (§4.1)** — the *soft* tint set players and creatures wear.
  Lower saturation, higher value. Not for UI chrome.

The two "mints" and two "blushes" are deliberately different: the UI mint is a
saturated teal signal; the avatar mint is a soft mint dumpling. Keep them separate.

### §2.2 — The neutral base

Environment structure (decks, walls, framing) sits in **warm-neutral greys** —
`0.58–0.72` value, a faint warm bias (R ≥ G ≥ B by a hair). Neutrals are the canvas;
they read best not out-shouting an accent. This is why the current level's structural
primitives read as "quiet" — that is deliberate.

### §2.3 — Per-zone accent schemes

Each functional zone picks **one temperature (warm OR cool)** and **1–2 hero accents**.
A zone's hero accent is the color a player's eye should go to (the objective, the
danger, the goal). Rules:

- One temperature per zone. A warm zone leans orange/blush; a cool zone leans
  mint/teal/sky. Don't split a zone down the middle.
- Max **two** hero accents per zone. Three is gaudy — the line we don't cross.
- Hero accents are pulled from Meridian tokens (§2.1) so the whole game stays in one
  family even as zones differ.
- The ground/legibility layer (§3) is exempt — it is functional, not decorative.

A zone whose colour-coded objective markers read before the palette does is a hero-accent use: the colour is a gameplay signal first, palette member second.

---

### §2.4 — The landscape palette: the world is the floor, not the show

> **PROVISIONAL — added 2026-08-20 at Talon's direction, and expected to change.** Every hex
> below is a **tiebreaker-weight** value: a starting point measured to be internally consistent,
> not a decision anyone has looked at on a screen. **The discipline in "The three dials" is
> heavy-weight; the specific colours are not.** Nudge a hex freely. Raise the saturation ceiling
> only deliberately, and only knowing what it costs. Nothing here has been seen rendered — it was
> authored in a cloud session with no GPU — so treat the first headed look as the real review.
>
> **Still provisional as of 2026-08-20 evening, and now governed by the brief's §1.** Talon's
> Visual Style brief arrived after this section was written and says the same thing in its own
> words — *"desaturated, natural tones only — greys, beiges, browns, greens. Nothing saturated,
> nothing that reads as synthetic or designed"* — so the dials below stand, with brief §1 as
> the authority they answer to rather than the morning conversation they were derived from.
> **Two things the brief adds that this section did not have:** the accent palette is
> specifically **warning colouration** (poison frogs, wasps, venomous berries) and it is
> applied **per asset on tagged zones**, not per category. Nothing here is repealed by that;
> the "events" column of the three dials is what the accent palette now means.

Talon, 2026-08-20, in his own words:

> "I would like muted tones and natural colors. I would like the colors of the environment to not
> be very intrusive. They to be dull and a little bit boring, even — but this is to let the
> interesting elements stand out more like the sun, perhaps through the trees, or like the
> characters, or the flowers that might bloom at times, or the bugs."

**The principle, stated so it survives an argument.** "Boring" is not an aesthetic here; it is a
**budget**. Attention is finite and the environment occupies most of the screen most of the time.
Every unit of saturation the landscape spends is one a flower cannot. So the landscape is not
being *starved* — it is **paying for** the sun through the trees, the bug in the net, the one
bloom in a clearing. When someone proposes making a rock prettier, the question is never "is this
nicer?" — it is "what does this cost the thing it stands behind?"

This is the same argument canon already makes (see `docs/CANON.md` §1.9 — light and saturation belong to events, not the environment) — §2.4 turns it into numbers so it can be checked rather than felt.

#### The three dials

Contrast in this game is deliberately **split by channel**: the landscape separates itself by
**value**, and events separate themselves by **saturation**. That split is what makes a
low-saturation world legible instead of muddy, and it is colourblind-safe by construction.

| Dial | Landscape | Events (creatures, fire, glow, blooms) |
|---|---|---|
| **Saturation** | **≤ 0.37 HSV.** This is the ceiling and it is the load-bearing one. | **≥ 0.44.** |
| **Value** | **0.18 – 0.48** in daylight. Dark, close together, boring on purpose. | **≥ 0.55.** |
| **Hue** | Two wedges only: **greens 75°–145°** and **earths 25°–55°**. Nothing else. | Anything. Warm is the house style (§3). |

**The gap between 0.48 and 0.55 is not slack — it is the point.** A dead band between the
landscape's ceiling and an event's floor is what makes a firefly read as an event rather than as a
bright leaf. Do not fill it. If a landscape surface needs to sit at 0.52, the honest question is
whether it is landscape.

**Hue discipline is what makes it read as "natural" rather than "grey".** Real landscapes are not
desaturated *uniformly* — they are desaturated within a **narrow hue family**, which is why a
forest reads as rich while measuring dull. Two wedges, clustered but never identical, is the whole
trick. A blue-grey rock in a green-brown wood reads as a prop, not as stone.

#### The palette

Authored sRGB, consistent with `docs/ENVIRONMENT-ASSET-CONTRACT.md` §2 (which stays the machine
authority for the five it already lists). H/S/V measured, not estimated.

**Shipped and in use:**

| Name | Hex | H | S | V | Job |
|---|---|---|---|---|---|
| foliage | `#415D47` | 133° | 0.301 | 0.365 | canopy mass, the single largest colour in the game |
| understory | `#3B513B` | 120° | 0.272 | 0.318 | shrub, the layer at eye height |
| bark | `#554336` | 25° | 0.365 | 0.333 | conifer trunk — **at the saturation ceiling; do not push it** |
| shallow water | `#26342A` | 137° | 0.269 | 0.204 | the lake edge. **The LAKE only — the river has its own pair, below** |
| grass tip | `#6B8736` | 81° | **0.600** | 0.529 | **breaks both dials — see the finding below** |

**Proposed additions — nothing in this block has been rendered:**

| Name | Hex | H | S | V | Job |
|---|---|---|---|---|---|
| stone, dry | `#5A5B57` | 75° | 0.044 | 0.357 | rock. Near-neutral with a green bias so it belongs to the wood rather than sitting on it |
| stone, wet / shadowed | `#43443F` | 72° | 0.074 | 0.267 | the same rock in shade or at the waterline |
| earth, trodden | `#4A4036` | 30° | 0.270 | 0.290 | paths, bare ground, the dirt a camp wears into the floor |
| forest litter | `#52483A` | 35° | 0.293 | 0.322 | dead needles and leaf fall — the floor between the trees |
| dry grass | `#7A7550` | 53° | 0.344 | 0.478 | late-season and sun-bleached growth. **At the value ceiling** |
| weathered timber | `#6A6155` | 34° | 0.198 | 0.416 | deadfall, stumps, planks, anything built and left out |
| deep shade | `#2A2E24` | 84° | 0.217 | 0.180 | the floor under closed canopy. The darkest surface in the game |
| distant haze | `#57635C` | 145° | 0.121 | 0.388 | what the far treeline fades into. **Depth is a colour, not a fog density** |
| birch bark | `#B8B2AA` | 34° | 0.076 | **0.722** | **the one sanctioned exception — see below** |
| river shallow, silted | `#454038` | 37° | 0.188 | 0.271 | the river over its shelf, where the bed is still faintly readable |
| river deep | `#080706` | 32° | 0.237 | **0.030** | the river channel. **Breaks the value floor on purpose — see below** |

#### Water is two rows now, and the river is not the lake

*Added 2026-08-21 by WATER-1, from Talon's water brief the same day: he asked for a **murky,
gloomy, scary dark brown or grey river**, in "neutral colors natural plain and kind of boring
nature colors."*

`shallow water #26342A` is at hue **137°** — a tannic green. That is the right colour for a
wooded lake with leaf litter on the bed, and the lake keeps it. It is the wrong colour for a
river, which carries **silt**, and silt is brown-grey. So water is two entries, not one, and the
two river rows above sit in the **earths wedge (25°–55°)** as tonal kin to `earth, trodden`
(30°) and `forest litter` (35°) — the river reads as the ground it is running over, which is
what makes it read as silty rather than as a coloured plane.

Both hues were checked against the wedge before they were committed, because §2.4 already
contains one row that violates its own (`stone, wet` is 72°, outside 75°–145°) and a second
would stop the wedge meaning anything. 37° and 32° are both inside 25°–55°. Both saturations
(0.188, 0.237) are under the 0.37 ceiling, and both sit *below* the earth they answer to —
silt suspended in water is greyer than the dirt it came from.

**`river deep` breaks the 0.18 value floor, at V 0.030, and that is deliberate.** Three
reasons, in order:

1. **It is not an albedo anybody sees flat-lit.** It is the far endpoint of a Beer-Lambert
   extinction ramp — the colour the water converges to when no light comes back. §2.4's value
   band governs *surfaces the light lands on*; this is the absence of a returned signal.
2. **It is the design.** WATER-1's brief is that the player *cannot confirm what is a few feet
   down*, and that the uncertainty is the dread rather than the gradient. A deep colour inside
   0.18–0.48 is a legible readout of the channel floor, which is the opposite of the ask.
3. **The precedent already ships.** The lake's own `deep_col` has been V 0.022 since
   2026-08-08 and predates this table; the river's is *lighter* than that, not darker. This
   row records an existing exception rather than opening a new one.

The nearest 8-bit hex is given above for the table's sake, but **the authored value is float
sRGB (0.0295, 0.0262, 0.0225)** — at this value 8-bit quantisation cannot carry the hue, and
the shader takes floats. `assets/materials/water/river_surface.tres` is the machine authority.

#### Three things worth arguing about, named rather than buried

1. **`grass tip` breaks both dials and it currently ships.** At S 0.600 it is *twice* the
   environment ceiling, and at V 0.529 it sits inside the dead band. It is the only shipped
   landscape colour that does. There is a real defence — it is a *tip* colour on thin blades, so
   its screen area is a fraction of its apparent presence, and grass genuinely is the most
   saturated thing in a real meadow. But it is a **standing exception, not a precedent**, and it
   should be looked at directly the first time the Material Lab is opened. If the meadow reads
   loud next to the trees, this is the number.

2. **Birch is deliberately allowed to break the value dial, and the whole species depends on it.**
   Birch was chosen for the roster precisely because it separates **on value** against a dark
   forest rather than on geometry — a pale trunk is the entire read at 144 triangles. So
   `#B8B2AA` at V 0.722 sits far above every other landscape surface, on purpose.
   **The rule that keeps it honest: birch bark is the highest-value landscape surface in the game
   and nothing else may go above it.** Its licence comes from its *shape* — a thin vertical of
   small screen area. A large pale surface with the same value would be a wall you have to look
   past, which §3 already forbids.

3. **The sun through the trees is bought here, not lit here.** A warm shaft reads as warm because
   the foliage it crosses is desaturated; against a saturated green canopy the same shaft fights
   the surface instead of landing on it. **The muting is the light's budget.** This is why §2.4
   is a palette rule and not a lighting rule, and why raising the environment's saturation would
   quietly cost the atmosphere work already shipped (§7, the fire registry, the ward union, the
   light shafts) without touching a single light.

#### How to check yourself

Four questions, in order. Any "no" is a finding, not a preference.

1. **Does every landscape surface sit at S ≤ 0.37 and V ≤ 0.48?** Two sanctioned exceptions exist
   — `grass tip` and `birch bark` — and both are named above. A third is a decision, not a nudge.
2. **Do its hues fall in 75°–145° or 25°–55°?** A colour outside both wedges is either an event or
   a mistake, and you should be able to say which.
3. **Squint at the frame. Does anything in the landscape compete with the brightest thing in it?**
   If the answer is a rock, the rock is wrong.
4. **Take the events out — no fire, no glow, no creatures. Is it boring?** *It should be.* A frame
   that is interesting with nothing happening in it will be noisy the moment something does. This
   is the check most likely to be failed by someone doing good work.

#### What is most likely to change, and what would trigger it

- **Every hex**, once anyone sees them rendered. They were derived for internal consistency in a
  session with no GPU. **Trigger: the first time Talon opens `MaterialLab`** — its palette row
  shows each colour twice, plain sRGB against the same colour delivered the way a `.glb` delivers
  it, so a pipeline lie shows up as a mismatched swatch before any of this is judged as art.
- **The saturation ceiling itself**, if the world reads as grey rather than as muted. That failure
  looks like: hues too close together, values too close together, and *not enough* value spread
  between the layers. **The fix is more value separation, not more saturation** — try that first.
- **Season and weather**, which do not exist yet and will want their own drift (a wet wood is
  darker and *more* saturated; a dry one is paler and yellower). §2.4 describes one condition
  today and does not pretend otherwise.
- **The night palette.** These are daylight values. Night is currently produced by the lighting
  systems acting on these surfaces, not by a second colour set, and whether that holds is
  unanswered.
- **`stone` and `birch bark` were derived by an asset packet, not chosen by Talon.** They are in
  this table because this direction settles what they should be, but the specific hexes are the
  weakest entries here.

---

## §3 — Color & legibility

*Cited by `CheckerFloor.gdshader` as "Bible §3/§4".*

**The legibility rule:** motion needs a ground reference. The checker floor
(`CheckerFloor.gdshader`, two flat greens) exists so a player can read speed, direction,
and distance travelled at a glance. This is functional rather than stylistic, and it is the
thing in this file most worth protecting: a floor treatment that costs motion legibility tends
to cost more than it gives back.

Corollaries:
- Interactive objects (scrap, props, sort bins, enemies) separating from the floor and from
  each other in **value** rather than only hue is what keeps the read colorblind-safe.
- **Danger reads warm on objects and accents** (hazards, a warm-lit threat
  marker); safe/goal *objects* read cool where possible. This rule scopes to
  **objects/accents only** — it does **not** govern ambient or environment temperature,
  which `LEVEL-BIBLE.md` §2.2 owns (there, danger *zones* read cool/dark/enclosed and safe
  *zones* read warm/open). The two are not in conflict once scoped: a warm-accent threat
  standing against a cool danger zone is the intended composition — the accent pops hardest
  precisely because it contrasts the zone — not a contradiction. **[reconciliation C1,
  decided 2026-07-22 — see `docs/superpowers/audits/2026-07-20-fable-grand-review.md`
  §Part C.]**
- A hero accent on a large static surface the player has to look *past* tends to work against
  itself. Accents mark what matters; a wall is not what matters.

---

## §4 — Materials & shading

*Cited by `AvatarVisual.cs` ("Bible §4: one material per unique COLOR") and
`CheckerFloor.gdshader` ("Bible §3/§4").*

### §4.1 — The pastel family

*Cited by `AvatarVisual.cs` as "the Art Bible §4.1 pastel family."*

The soft tint set worn by players and (where fitting) creatures. Six colors; the server
deals one per joining player by spawn-index round-robin, so no two of six players match:

| Name | Hex | RGB |
|---|---|---|
| Mint | `#9ED4B8` | `0.62, 0.83, 0.72` |
| Blush | `#FABFD1` | `0.98, 0.75, 0.82` |
| Butter | `#FAE68C` | `0.98, 0.90, 0.55` |
| Sky | `#99CCE6` | `0.60, 0.80, 0.90` |
| Lilac | `#D1BDEB` | `0.82, 0.74, 0.92` |
| Coral | `#FFB39E` | `1.00, 0.70, 0.62` |

Derived shades follow `AvatarVisual.BuildAppearance`: belly `Lightened(0.45)`, chin
`Lightened(0.15)`, feet `Darkened(0.12)`, stem `Darkened(0.15)`. Face details keep the
artist's authored colors and are not player-tinted.

### §4.2 — Material sharing

One `StandardMaterial3D` per unique **color**, shared across every instance in the scene
(the pattern `AvatarVisual.MatCache` already uses). The palette is small by design, so
the cache stays tiny. A fresh material per instance is what breaks batching, which is the
reason the pattern exists.

### §4.3 — The toon world shader family (the one shader family)

> **Rewritten 2026-08-20.** Talon ruled flat/toon for the whole world, which promotes what
> used to be an optional creature treatment into **the** shading model. What this section
> described before — a per-surface "painted character" gradient ramp, applied *where a surface
> wants painted warmth*, over structural neutrals left on `StandardMaterial3D` — is retired as
> a *selective* treatment. It was never wrong; it is now universal, which is a different rule.

**One shader family renders the world, and it is `toon_world`.** Not a treatment applied where
something wants warmth — the default, applied to everything, with `StandardMaterial3D` as the
exception rather than the base. Its implementation is STYLE-2's
(`resources/shaders/toon_world.gdshader` + `resources/materials/toon/ToonWorld.tres`); this
section states **intent**, never uniforms, because a bible that names uniforms goes stale the
first time somebody renames one.

What the family is for, in the order the look depends on it:

1. **Stepped diffuse from the sun.** Light lands in bands, not a gradient. The band boundary
   is the read — it is what makes a facet a facet, which is the whole reason
   `ENVIRONMENT-ASSET-CONTRACT.md` §1.1 can derive a triangle budget from a pixel size.
2. **Flat ambient.** Shadowed sides are one value, not an ambient-occlusion study. Depth comes
   from §7's haze and from value separation (§2.4's three dials), never from shading detail.
3. **A rim term keyed to the accent mask.** Brief §3 asks for rim lighting *specifically so
   accent colours still read under flat lighting*. So the rim is not a global silhouette-pop
   effect: it is strongest exactly where `COLOR_0.a` says an accent zone is, which is what
   keeps a red nose on a brown creature legible at noon without raising the world's saturation
   to compete with it.
4. **Weathering and growth as tinted masks over vertex colour.** Brief §6's layered material
   approach — base colour, weathering/age, environmental growth/grime — arrives as two
   per-vertex dials (`TEXCOORD_0.x` and `.y`, contract §6.2) modulated by procedural breakup in
   the shader. Never a photoreal texture set. This is what makes an asset read "lived in"
   without a single texture fetch.
5. **Per-instance tint preserved, MultiMesh-safe, no textures.** The family has to survive
   being the material on a 900-instance MultiMesh, because that is how the world is placed
   (contract §8). Anything that breaks batching breaks the draw-call budget.
6. **Depth haze as a uniform, defaulting to 0** — §7 and brief §5. Off until the §8 concept
   review picks a direction.

**One exception, and it is the only textured material in the game:** `toon_decal`
(`resources/shaders/toon_decal.gdshader` + `ToonDecal.tres`) — alpha-scissor, depth-biased,
sampling the single shared decal atlas. Contract §6.3 is its authority. Two shader variants
total: one world, one decal.

**The gradient ramp is not dead, it is subsumed.** A 1D ramp lookup on N·L *is* a stepped
diffuse when the ramp has steps in it, and it is still cheaper than the `StandardMaterial3D`
lighting path it replaces. Whether the bands come from a ramp texture or from arithmetic is
STYLE-2's call and not a bible-level decision — except in one respect: **a ramp texture would
be a second texture**, and §4.4's answer is that there is one. Prefer the arithmetic.

> **Presentation-profile note:** the data-driven presentation system
> (`PresentationProfile` / `EventResponse`) drives **event SFX and particle puffs**
> (including `PuffColor`), **not** body albedo/material. A creature's body color comes
> from its GLB materials (or `AvatarVisual`-style tinting), authored against this
> section — not from its presentation `.tres`.

### §4.4 — The texture contract

*Added 2026-07-29, superseding §4.3's texture ban and §1's no-textures clause. Decided
by Talon; the rationale and rejected alternatives live in
`docs/superpowers/specs/2026-07-29-texture-contract-and-environment-research-design.md`.*

**Textures are a first-class tool wherever they improve the read.** The ban dated from
the pastel creature-sorting game; a place that has been lived in lives in material history —
grime gradients, sun-faded siding, the one wrong surface among ordinary ones — and flat
colour cannot say any of it. What replaces the ban is a **budgeted contract**, not a
free-for-all: the load-bearing truth under the old rule survives — what hurts later is
never "textures," it is unmanaged variety. The standards are declared here, before mass
asset authoring, while the whole neighbourhood is still CC0 placeholder.

- **Atlases and trim sheets.** Architecture and props draw from **shared trim sheets and
  atlases** rather than one bespoke texture set per object. §4.2's sharing rule extends to
  textured materials unchanged: one material per unique **appearance**, shared across
  every instance, rather than a fresh material per instance. The Kenney placeholder kit (one
  shared atlas across the whole neighbourhood) is the de facto proof of the workflow.
- **The wear layer.** Lived-in surfaces are built from three tools, cheapest-shared
  first: a small library of **shared tileable greyscale grunge masks**, tinted
  per-material in shader; **decals** for specific story marks (the stain that is *about*
  something); **vertex paint** as the per-instance wear-intensity dial, so one wall
  material reads fresh on one house and tired on the next without a second material.
- **The five blanks, answered 2026-08-20 (STYLE-1).** They were named here on 2026-07-29 and
  left open because nothing could fill them honestly. Talon's flat/toon ruling makes four of
  them trivial and the fifth arithmetic. Each answer carries its derivation; none is a
  preference.

  | Was blank | Answer | Derivation |
  |---|---|---|
  | **Texel-density standard (px/m)** | **n/a** for every world asset's albedo. **128 px/m** for the one decal atlas | The world is vertex-coloured — there is no albedo texture to have a density. 128 px/m is the surface-wear spec's "large exteriors & terrain detail" row and the number the atlas is sized by (`ENVIRONMENT-ASSET-CONTRACT.md` §3) |
  | **Grunge-mask library size and contents** | **zero masks.** Weathering and growth ride in `TEXCOORD_0.x` / `.y` as per-vertex dials, broken up procedurally in the shader | A shared greyscale grunge library was the cheapest way to get lived-in surfaces *when the shading model needed a texture fetch to express them*. Toon does not. Contract §6.2 |
  | **VRAM budget** against the 4 GB floor card | **≈ 1.4 MB of texture**, total, for the whole game world | One 1024 × 1024 BPTC page is 1.0 MB, plus ~⅓ for mipmaps. There is no second texture (contract §6.3). Meshes, the terrain splat and UI are separate and unchanged |
  | **Decal budget per scene** | **12 draw calls**, LOD0 range only | It is a line in `ENVIRONMENT-ASSET-CONTRACT.md` §1.2's allotment, and a decal primitive is a second draw call per instance. Past the LOD0 boundary (§1.3) the decal is not worth its call |
  | **Shader-variant count** | **two**: one world (`toon_world`), one decal (`toon_decal`) | §4.3. Every world surface is one material family; the decal atlas is the single exception. A third variant needs the same justification a second material would |

  **The blanks' original filling source** —
  `docs/superpowers/research/surface-wear-RESEARCH-RESULT.md` — was written for a textured PBR
  world and is now background reading rather than an input. The texel-density row is the one
  thing still taken from it.
- **Perf guardrails, restated.** GTX 970 / Forward+ floor; draw-call budget **350**,
  ceiling **600** (`PerfHud.cs:78`); 2–6 players. Import settings are part of the
  contract: **desktop compression only (s3tc/bptc)**. `etc2_astc` is a mobile format and has
  already crept into one `.import` in this tree once — worth knowing it is the thing to look
  for when textures come out wrong. And the pillars are
  untouched: **§1's priority order and §3's legibility rule win over any texture** — a
  texture that costs motion legibility loses, same as any other treatment.

*Scope note. Current bite: this contract governs zero real assets — the whole
neighbourhood is CC0 placeholder, and every budget above is a named blank. First real
test: the first non-placeholder textured asset batch, authored against the contract.
What passing looks like: that batch ships without a re-texture pass, and the
draw-call/VRAM numbers hold on the floor card.*

---

## §5 — Sound & voice

*Cited by `VoiceSpeaker.cs` / `VoiceRoute` as "Bible §5."*

### §5.1 — Voice character

Two routes (see `VoiceRoute`):
- **Proximity** — positional, distance falloff (`UnitSize 6`, `MaxDistance 24 m`,
  inverse-distance). The default social voice.
- **PA** — the intercom: no falloff, routed through the PA bus (light overdrive + hard
  lowpass + boxy reverb) — "a voice through a speaker in a creepy building." Its whole
  character is three stock bus effects; bus DSP runs once per audio block regardless of
  listener count. Best juice-per-cost in the game.

### §5.2 — Creature voice

Every creature has a **voice**: a short periodic positional bark (a "hello," a bleat, a
chitter) that gives it presence and doubles as a **solo proximity-voice test rig** —
walking toward/away from a barking creature exercises the same spatial falloff real
player voice uses.

Rules:
- The bark plays through an `AudioStreamPlayer3D` configured to the **same attenuation
  profile as `VoiceSpeaker` proximity** (`UnitSize 6`, `MaxDistance 24`, inverse-distance,
  voice output bus) so it is a faithful proxy for real voice, not merely "a 3D sound."
- The **trigger is server-decided and replicated** (it rides the creature's existing
  state/position broadcast), so a solo player also exercises the networked trigger path.
- A creature's bark clip + cadence are authored data (a `Bark` `ActorEvent` on its
  presentation `.tres`, with the spatial routing handled by a shared `CreatureVoice`
  helper on the enemy base — see the enemy roster spec §Architecture).
- The bark does **not** ride the Opus voice relay. That path's unique coverage
  (codec/relay/jitter) is already proven headlessly by `VoiceTestSender` +
  `Run-VoiceTest.ps1`; duplicating it on creatures would change the voice wire contract
  and poke the security-reviewed relay for no new coverage. (Considered and deferred —
  see the roster spec.)
- **Idle barks are for standalone/ambient creatures only. Family vocalizations are
  state-driven only** — a sound from a Family member (young or Parent) is always meaningful
  (waking, threatening, calming, crying out), never ambient presence noise. This is what
  keeps `LEVEL-BIBLE.md` §8.3's always-meaningful audio channel true for Families
  specifically without banning idle barks project-wide: if a Parent bleated idly, "she made
  a sound" would stop meaning "she is waking," diluting the game's single most important
  audio signal. Family sounds map to the discrete replicated tier events (the B2 aggro
  tiers), never to a periodic idle timer. **[reconciliation C3, decided 2026-07-22 — see
  `docs/superpowers/audits/2026-07-20-fable-grand-review.md` §Part C.]**

### §5.3 — Event SFX

Footsteps, bumps, grabs, deaths, etc. are authored per creature/prop as `EventResponse`
entries (synthesized `Sfx` palette or a `CustomSound` stream) with matching particle
puffs. Keep the synthesized palette as the default; reach for `CustomSound` only when a
creature needs a signature noise (the bark usually does).

---

## §6 — Character construction

Creatures are **chunky, stylized, silhouette-first**: round volumes, stumpy limbs, readable
big-shape reads at distance. A creature
reads best identifiable from its silhouette alone — before color, before animation.

- **Proportions:** big head / big defining feature, small limbs, a bottom-heavy or
  otherwise distinctive mass. No realistic anatomy.
- **Rigging:** rigid-part construction, single-bone weights, per `docs/BLENDER-EXPORT.md`.
  ~9 bones is the biped/quadruped norm for the shipped creatures — not a hard cap.
  Chain creatures (snake/slug), many-limbed creatures (spider), and boneless creatures
  (ghost/gas) follow different bone counts and topologies entirely; see that doc's
  "Non-humanoid topologies" section. Every bone — or, for a boneless creature, nothing
  — is covered by the creature's ragdoll `.tres` where one applies.
- **Expressiveness is body language**, not facial rigging: squash/stretch, lean, waddle,
  tail/appendage lag, tumble.

### §6.3 — Silhouette fillers

*Cited by `AvatarVisual.cs` as "Bible §6.3."*

Where two primitive volumes would read as "two balls stuck together," add a **filler**
piece in the **same body color** so the volume reads as one continuous mass (the
the shipped avatar's `BackFiller`/`CheekL` fillers). Fillers are not separately tinted; they exist
to make the silhouette flush, not to add a shape.

### §6.4 — Animation posture: hybrid, procedural-first

*Added 2026-07-21 from `docs/superpowers/research/animation-pipeline-RESEARCH-RESULT.md`
(PR #29). Subsections §6.4–§6.8 are append-only, like everything in §6 — section numbers
are load-bearing.*

**[decided 2026-07-22 — all §6.4–§6.8 research defaults accepted wholesale (Q3,
`docs/superpowers/status/2026-07-22-blocker-decisions-Q1-Q10.md`)]** The default
animation posture is **hybrid, procedural-first**. Every creature receives the shared procedural body-language
layer as its foundation — the §6 verb set the shipped avatar already has (`AvatarVisual.cs`:
squash/stretch spring, waddle cadence, lean-into-velocity, appendage lag, carry bob, idle
breath, blink, fidget). Authored clips are **role-driven exceptions**, reserved for what
procedural cannot say: signature telegraphs (Wake, Threaten, CalmDown), carried-state
reads, and any pose that is a design statement. This inverts the clip economics — a
one-family first playable needs roughly half a dozen authored clips instead of ~18.

- **Code owns the verbs; data owns the parameters.** One shared implementation, two
  backends: `Node3D` transforms for rigid-node creatures (exists), a `SkeletonModifier3D`
  subclass for skinned generated creatures (new). The Visual class hides which kind a
  creature is. Per-creature parameters (amplitudes, frequencies, role-to-bone mappings,
  per-verb enables) live in a **sibling resource to `PresentationProfile`**
  (`BodyLanguageProfile.tres`) — **[decided 2026-07-22, Q3]** —
  drafted by the pipeline with flagged placeholders, tuned by hand, never auto-tuned
  (the ragdoll-draft discipline). The v1 verb inventory is exactly that shipped list;
  tumble stays the ragdoll's job — **[decided 2026-07-22, Q3]**.
- **Runtime pattern: `AnimationPlayer` + a `SetState` switch** (the `GoatVisual`
  precedent), not `AnimationTree` — **[decided 2026-07-22, Q3]**.
  Loops are forced to `LoopModeEnum.Linear` at load (glTF imports everything play-once),
  with an `AllLoopClipsLooping()`-style regression guard per Visual.
- **Clips are presentation rather than movement.** In-place: no root motion, no horizontal root-bone (`Hips`) translation. The wire stays `(Vector3 position, int state)` and clip names do not
  appear on it. Foot-slide is handled by scaling playback rate against actual speed, which is
  cheaper than re-authoring.
- **Crossfade defaults — codified from shipped precedent (`HoarderVisual.cs` blend
  times), decided 2026-07-22 (Q3 + Q4):** locomotion transitions blend at **0.15 s**;
  telegraph transitions **cut at 0 s** with an authored anticipation pose. These are
  **default templates, not invariants** (Q4): they apply unless a specific creature's
  design calls for something different, and a family may override them in its own spec —
  no lockdown. Per-transition data (`Play(name, customBlend)`), not a global tween.
  Easing a telegraph reads as mushy and undermines pose-to-pose chunkiness.
- **Mid-state entry (late join / reconnect):** clips get entered mid-state by late joiners — loops always are; for one-shots the rule is **per-clip class [decided
  2026-07-22 — animation research Q1]**: run-critical one-shots (telegraphs) enter at
  their **end pose** — a late joiner lands on the truthful current situation with no
  phantom wind-up (a desynced telegraph lies about the fairness window; a pop is only
  cosmetic); cosmetic one-shots may play-from-start. **Elapsed-time-in-state stays off
  the wire in v1** — time-reconstruction is a v2 escalation only if playtests condemn a
  specific clip's end-pose pop.
- **Zero-clip creatures are acceptable for ambient and young roles** — the shipped avatar is
  the proof — **[decided 2026-07-22, Q3]**.
- Auto-animation tooling is not a pipeline stage and is **ruled out as a generation
  path, with exactly one named fallback: Cascadeur [decided 2026-07-22 — Q2]**. Nothing
  retargets onto the custom `Hips`-rooted vocabulary (generation tools impose their own
  rig; Mixamo/Krikey are biped-only); Cascadeur survives because it keys on *our* rig
  regardless of bone names. Reached for only when a specific one-shot needs
  physically-plausible weighted motion that chunky hand-keying cannot sell, on a
  quadruped in its supported (cat/dog-like, alpha-grade) class. Everything else stays
  hand-keyed.

### §6.5 — Clip sets derive from role, and every role has a death path

*Decision A2 applied (2026-07-21), amended 2026-07-22 (Q1): defeatability is expressed
as **capability flags** (`can_be_killed`, `can_be_trapped`, `can_be_stunned`,
`can_be_evaded`) declared per family, none mandatory. **A role has a death path iff its
family declares `can_be_killed: true`** — the death animation maps to `can_be_killed`.
Talon's stated bar, verbatim: "We don't need fancy death animations for everything."
The cheapest acceptable presentation per role is the default.*

| Role | Authored clips (cheapest acceptable) | Death path |
|---|---|---|
| Standalone ambient | **0** — procedural idle/waddle | pose-snapshot → ragdoll; zero authored death clips |
| Family young | **0–2**: `Limp` (single authored pose — near-mandatory; a legible tier change) + `Struggle` **[decided 2026-07-22, Q3: procedural wiggle, not a clip]** | short `Death` clip → ragdoll, or direct pose-snapshot → ragdoll (iff `can_be_killed`) |
| Family parent | **3–5**: `Wake`, `Threaten`, `CalmDown` authored (run-critical legibility, the visual siblings of the roar); likely one `Pursue` loop | iff `can_be_killed: true` (`BEHAVIOR-BIBLE.md` §9.5): at most one short `Death` clip → ragdoll |
| Player | **0** (shipped); Downed runs on the ragdoll | Dead per `BEHAVIOR-BIBLE.md` §10.1 |
| Boneless (no skeleton) | material/shader-parameter tracks only | **bespoke presentation** (e.g. shader dissolve, per `BLENDER-EXPORT.md`) — no ragdoll pipeline |

- **Skeletal death is "short clip → ragdoll" at most, and "ragdoll only" is fine.** The
  ragdoll *is* the death animation for most creatures; a short authored clip in front of
  it is reserved for creatures whose death is a design beat.
- **Parent telegraphs are the authored-clip priority** for the first playable —
  **[decided 2026-07-22, Q3]**.
- **Low key counts are the default, not an economy measure.** Two-keyframe pose loops
  are legitimate at this style level (Overgrowth ships walk cycles from four poses;
  Minecraft from zero). A dense-keyed `Struggle` reads *worse* than two alternating
  extremes with snappy interpolation.
- **The death path is declared by the capability flag.** `can_be_killed: true` → the
  role's death presentation is in the clip plan; `false` or absent → no death clip, by
  declaration. A family spec still carrying the retired `can_be_defeated` field is a
  schema gap to report, not to translate on the fly (`/author-clips` enforces this).
- **Recover-from-ragdoll (Downed → standing): pose-lerp + stylized squash-stretch pop,
  zero authored get-up clips** — **[decided 2026-07-22, Q3]**.

**No harvest / results-screen animation contract exists, deliberately (decision D2).**
Harvest's purpose is undecided (YAGNI) and the word "harvest" itself may change. There is
no tally pose, no celebrating-young clip, no results-screen presentation contract — here
or anywhere — and a future agent finding that "gap" should read the decision log, not
helpfully fill it. Creature-side clips (Death, Walk, telegraphs) are unaffected by D2.

### §6.6 — Carried creatures: struggle is a clip, not a simulation

The B1 rule, adopted per the grand review's agent-executable amendment list (PR #24): a
carried creature's resistance is **presentation** —
cosmetic, client-local — driven by the server's discrete tier events (cried out, went
limp, broke free), rather than by physics simulation of the struggle. **This is a default
template, not an invariant (Q4, 2026-07-22):** it applies unless a specific creature's
design calls for something different, and a family may override it in its own spec.

- The carried creature's Visual switches to its carried-state set (`Struggle` ↔ `Limp`)
  on the replicated tier events. Collision and AI presentation are off while attached.
- **Anchor contract:** player avatars have no skeleton, so the anchor is the named
  `Node3D` carry anchor `SandboxAvatar` already maintains; a carried creature is
  reparented **client-side** from replicated "carried-by X" state, and its world position
  is not independently replicated while carried. If a creature ever carries something,
  `BoneAttachment3D` on a named bone is the skeletal equivalent. **One shared anchor
  [decided 2026-07-22 — Q3]**: grip-vs-cradle read differences are a per-item pose/offset
  field on the carriable (the Lethal Company pattern — one hold system carries scrap,
  corpses, and a struggling Maneater baby), rather than a second anchor node — a second
  anchor is a new per-item authoring axis for the carried-state bug class to hide in.
- `AvatarVisual.CarryBobOffset` already rides held things on the carrier's gait (bob on
  footfall, sway with roll, breath at a stand) — a carried young is alive in the arms
  for free, between its own animation beats.
- The carrier jostle-pulse on struggle is **v1-out [decided 2026-07-22 — Q4]**: shipped
  carry games sell "it's fighting me" through the carried body's own state, and this game's
  carried creature has the body-language layer for exactly that. Trigger to revisit:
  playtests show players missing struggle-tier changes from the carried body alone.

### §6.7 — Bone-placement validation gates every authored clip

Generated rigs place bones by bounding-box arithmetic; on the Coilwrack fixture the
root bone (`Hips`) owns 8,838 vertices while `Spine1` owns 132. **Procedural animation degrades
gracefully under bad placement — it can be amplitude-tuned at runtime; authored clips do
not degrade, they encode.** A `Walk` keyed against a bad pivot is wrong in every frame
forever and can only be fixed by re-rigging *and re-authoring*.

Therefore: **before any clip is keyed on a creature, its bone placement passes automated
validation.** The evidence artifact is `conform-report.json`'s `vertices_per_bone` table
(already emitted); the balance heuristic (a bone owning an order of magnitude more or
fewer vertices than its topological siblings) is the gate, and it becomes a formal
`conform.py` step for any creature slated for authored clips **[decided 2026-07-22, Q3]**.

*Amended 2026-07-22 (Q8–Q10): this was originally a human sign-off gate. Manual in-engine
sign-off gates are removed — the pipeline runs on its own validation, and placement
problems that slip through surface downstream, get root-caused, and fix the heuristic or
the rigging step that produced them. A failed validation still hard-stops keying: the
path is re-rig → re-conform → re-validate, rather than keying it anyway and tuning later.*

Clip authoring also inherits the ragdoll's pose discipline (every pose in every clip is
a potential ragdoll start pose): clips are authored against the **same rest pose the
`RagdollProfile` spans were tuned on** — the rest pose is a shared contract artifact,
named in the spec, not a Blender convenience; authored rotations stay comfortably inside
the profile's swing spans, hardest for *end poses* of one-shots (a `Stagger` that ends
antipodal is a NaN delivered later); the procedural layer's amplitude caps are part of
the same contract. Enforcement is **staged [decided 2026-07-22 — Q5]**: **v1 =
checklist plus a narrow automated singularity guard** — a validate-time check flagging
any keyed pose within epsilon of the 180° swing-twist singularity (the deferred-NaN
case) — deliberately *not* a naive full-span lint, because a naive swing-twist scan
mishandles exactly the singular pose it exists to catch and converts author discipline
into false confidence. v2 = a full per-frame swing-twist-vs-span scan built only on a
singularity-handling decomposition (port dtecta/theorangeduck rather than writing one),
END poses of one-shots first. The guard ships before any authored one-shot does.

### §6.8 — Validation contract for clips (the gate-8 replacement design)

Design only — implementation belongs to `/validate-asset` (`validate.py`) and
`/compile-spec` (schema), not this document. Recorded here because the current gate 8
fails 100% of the time by construction, and *a gate that cannot currently pass must say
why in its own verdict, or it trains blindness* (2026-07-21 validation run, Finding 6).

- **8a — spec-internal map (keep, FAIL-able today):** state↔clip 1:1 both directions;
  PascalCase; `looping_clips ⊆ clips`.
- **8b — clip presence in the clip-owning file:** requires the spec to declare
  `animation.authoring` ∈ `{authored, procedural, hybrid}` (ideally per clip), and the
  file-topology decision so the gate knows which glb to read. Missing authored/hybrid
  clips → **FAIL**; `procedural` → **"N/A — clipless by declaration"**, a distinct
  verdict counted separately from PASS and SKIP; pre-authoring pipeline state → **"N/A —
  pre-authoring"**, reason printed. The four-verdict scheme and the
  `animation.authoring` field are **adopted with one amendment [decided 2026-07-22 —
  animation research Q6]**: every `N/A` requires a non-empty, machine-checked `reason`
  that references the declaration licensing it (`authoring: procedural` or an explicit
  clipless role); the validate summary carries an **aggregate N/A count with a
  threshold** so creeping N/A is itself visible; `SKIP` is reserved for
  not-evaluated-this-run. An unaudited N/A is just a silent skip — the guard is the
  point. File topology — single-file, one skinned glb owning mesh + rig + clips,
  existing split files grandfathered — is **[decided 2026-07-22, Q3 wholesale]**.
- **8c — role-derived required sets:** parent → `Threaten`+`CalmDown` required; young →
  `Struggle`+`Limp` required *in the declared set*; `can_be_killed: true` puts the
  role's death path in the required set. Strictness is **per clip class [decided
  2026-07-22 — Q7]**: **carried states FAIL from day one** (the shipped bug class;
  negative fixture trivial — WARN here would re-enable the original bug); **telegraphs
  WARN with a dated ratchet** promoting to FAIL on a committed date (zero authored
  clips exist yet — strict-day-one would redline every parent and train blindness;
  warn-forever is forbidden, the date is the point); **death path FAILs iff
  `can_be_killed: true` is declared** (enforcing the author's own declaration), else
  N/A. Unifying rule: FAIL immediately where a negative fixture exists and the failure
  is a known shipped bug class; dated ratchet where the required set is real but no
  assets exist yet.
- **8d — loop modes are checked at runtime, not in the glb.** glTF always imports
  play-once; the meaningful check is the per-Visual `AllLoopClipsLooping()` self-test
  after load, a review-checklist item, not a glb gate.

---

## §7 — Lighting & post-processing

> **Amended 2026-08-20 from Talon's brief. Three changes, and one thing this section may not
> do.**
>
> **1. Depth haze is IN** (brief §5). A subtle fog/fade that increases with distance,
> separating foreground from midground from background *even under flat lighting*, and
> naturally justifying reduced detail farther from camera. It is the depth cue this look needs
> most: with a desaturated palette and banded shading, colour and shading detail are both
> unavailable as separation channels, so haze and **silhouette value contrast** are what is
> left. Every layer needs a shape that reads clearly against what is behind it, **through value
> and silhouette, not colour** — the base palette is desaturated across the board, so colour
> cannot carry it. `toon_world` carries haze as a uniform defaulting to **0** (§4.3); it turns
> on when the §8 review says so.
>
> **2. Colour grading is DEFERRED, not cut.** The "gentle global grade, one LUT, whole game"
> bullet below stands as a *proposal*. Brief §2 is explicit that validation happens under flat
> neutral midday with no grading tricks, precisely so geometry and palette are judged on their
> own merits — so the grade cannot be turned on before the **§8 concept review** picks a
> lighting direction. Turning it on early is how lighting mood ends up doing the work that the
> assets were supposed to do.
>
> **3. Real-time shadows from sun, moon and fire are OUT** (brief §13). They were quoted as the
> most expensive rendering cost in the game and are not worth it. **What actually changes is
> one light:** sun and moon shadows have been off since PR #55, so the only real shadow caster
> left in the game is the campfire's `OmniLight3D` (`CampfireVisuals.cs:33-40`) — that omni is
> the cut. Fire must still *feel* alive without it: flicker, colour and intensity animation,
> glow, particles. The freed budget is reinvested, and the confirmed priority target is **water
> rendering** — depth/murk cues, distortion, murky fog colour, so crossing water feels
> genuinely daunting — but the reallocation is **not exclusive to water** and other uses are
> open. Sequenced with the water category, not with the current rock work.
>
> **What this section may not do:** resolve `THRILL-BIBLE.md` §6.2's night ambient floor. That
> is Talon's and it is not settled by inference from anything here.

Deliberate tools, **off by default**, on where they pay:

- **Per-zone tinted lighting.** A zone's key/fill light may carry a subtle tint toward
  the zone's temperature (§2.3) — warm zones warmer, cool zones cooler. Subtle: a nudge,
  not a disco.
- **Bloom / glow — emissive-only.** A single post pass, thresholded so **only emissive
  hero accents bloom** (a glowing bin edge, a hazard), with structural neutrals left out of
  it.
  This is the cheapest, highest-impact "authored" cue we have.
- **Color grading.** Gentle global grade on top of the existing filmic tonemap — a small
  warm lift in shadows, mild contrast. One LUT/curve, whole game.
- **No depth-of-field, no motion blur, no SSR/SSAO-heavy passes.** DoF in particular is
  rejected: it fights legibility (§3) and costs frame time in a fast networked game.

Post effects work best as named, measured choices rather than arriving because the engine
defaults them on.

---

## §8 — Creature visual language

> **The roster below is TIDE-era text and the creature category revalidates it** (noted
> 2026-08-20). Ram Goat, Hoarder, Guardian, Skittish, Bumbler and Curlpin come from a tidepool
> game with a harvest-risk loop; the canon in `docs/CANON.md` has since moved twice, and no
> creature roster is settled — the creature table is explicitly empty pending Talon. **The
> rules underneath the roster are what survive:** distinct palette slot per family, silhouette
> that reads as its temperament, kinship legible on sight, danger reads warm and prey reads
> pale. Those get re-derived, not deleted, when the creature category goes through brief §7's
> stage list on its own terms. **Do not build toward a named creature in this list.**

**Each creature owns a distinct palette slot and silhouette**, so it reads as its own species
at a glance and by temperament — before colour, before animation.

Shared rules: chunky-stylized (§6), one gradient-ramp material family (§4.3), a voice (§5.2),
and §3's value separation.

**Palette-slot rule (reconciliation C2, 2026-07-22):** no two *families* share a palette slot;
within a family, sharing is the point. A standalone creature is a family of one and still owns
its slot. The earlier "no two creatures share a slot" wording forbade the kinship read and is
superseded.

**Kinship legibility (C2):** a player who has never met a given family can pair its young with
its parent **on sight**, from shared palette slot and silhouette echo alone — no label, no
prior encounter. Shared palette plus silhouette echo *is* the kinship mechanism; the
`spec-variants` juvenile-proportion table is the silhouette half of it. Worth validating a
young and a parent as a **pair, shown together, unlabeled**, rather than only solo.

**Bestiary membership is fluid (Q5, 2026-07-22).** Creatures can be added, removed or shelved
without retroactive bible edits and without an approval gate.

---

## §9 — UI (Meridian extension)

> **Unchanged by the 2026-08-20 style brief, and that is now explicit** (§2's restatement).
> Meridian is **UI-only**: the world's mundane-plus-warning-accent palette governs everything
> the player looks *at*, and Meridian governs the chrome they look *through*. The two are no
> longer expected to meet, which removes the old friction where a zone's hero accent had to be
> a UI token. The phrase "warm the minimalism" below quotes the retired *Warm minimalism*
> direction (see the header); the four bullets it introduces are still what to do.

The UI theme is **Meridian** (`resources/UITheme.tres`): dark teal-tinted base, Sora +
JetBrains Mono, three accents (mint/teal, blush, orange), filled primary buttons. We
**extend it, not replace it.** "Warm the minimalism" here means:

- Lean harder on the accents already defined: colored `SectionLabel`s, more use of orange
  and blush for state (danger, highlights, secondary actions) rather than everything
  living on mint.
- Optional **simple line-icons** to break the current zero-imagery flatness — flat,
  single-weight, accent-tinted, stopping short of illustrative clutter.
- Keep the dark base and the two type families. No new fonts.
- Every screen keeps a clear focal hierarchy: one primary action, colored; everything
  else quiet.

The goal: the UI stops reading as "unfinished flat" and starts reading as "deliberately
restrained" — the same shift the whole game is making.

---

*Reference authority. To change the direction, edit the companion spec first, then
reflect the decision here. Section numbers are load-bearing — see the header.*
