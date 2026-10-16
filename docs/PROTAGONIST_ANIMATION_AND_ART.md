# Protagonist Art, Model Sheet, and Animation Pipeline Overhaul

## Executive Summary
This document records the complete design, authoring, rigging, sprite sheet generation, and Unity runtime animation integration for the protagonist of **Aether** (**Aethon / The Aether Ranger**).

The character replaces the former single-pose procedural placeholder sprite with a dedicated 15-state, 204-frame rigged 2D side-view animation set, accompanied by a visual model sheet, individual contact sheets for each sequence, runtime sprite sheets, and automated PlayMode verification tests.

---

## 1. Creative Direction & Character Design (Model Sheet)

### 1.1 Archetype & Visual Identity
- **Character**: Aethon / The Aether Ranger (Ancient order of Forest Sentinels and Aetherblade wielders).
- **Proportions**: Athletic heroic platformer scale (~4.5 to 5 heads tall), designed for immediate silhouette readability on 6-inch mobile screens (1.4m world height, 0.7m world width).
- **Head & Cowl**: Pointed wanderer's hood with forward brow overhang, reinforced silver-winged temple crest, and deep shadow face cavity.
- **Visor & Aether Core**: Electric luminous cyan slit (`#3AF0F8`) with brilliant ice-white energy core (`#D8F0FF`). Flares during ready stances and attacks; fades softly upon defeat.
- **Cuirass & Armor**: Weathered slate and silver-steel plate (`#5C768C`, `#94B2CA`) over dark twilight navy under-tunic (`#0E1622`). Gilded bronze solar crest buckle (`#E2AC36`) encasing the sternum aether jewel.
- **Greaves & Boots**: Knee-cop armor plates, dark indigo trousers, sturdy double-strapped leather boots (`#402E20`) with reinforced steel toe caps.
- **The Aetherblade**: Tapered runic short-claymore with leather-wrapped hilt, gilded crossguard quillons, razor silver cutting edge (`#D4EEFF`), and an electric cyan fuller conduit (`#3AF0F8`).
- **Trailing Half-Cloak**: Three-layered flowing cape draping from the shoulders in midnight navy (`#162438`) with deep inner fold shading (`#0E1622`) and an electric cyan trim hem (`#288CA0` to `#3AF0F8`). Reacts with dynamic secondary motion to running, leaping, falling, and slashing.

### 1.2 Color Palette Specifications
| Component | Hex Code | Visual Role |
| :--- | :--- | :--- |
| **Midnight Navy** | `#162438`, `#0E1622` | Cowl hood, flowing cloak, tunic |
| **Electric Cyan** | `#3AF0F8`, `#288CA0` | Luminous visor slit, sword fuller conduit, cloak hem trim |
| **Silver Steel** | `#5C768C`, `#94B2CA` | Breastplate cuirass, greaves, shoulder pauldrons |
| **Ice Highlight** | `#D4EEFF` | Blade cutting edge, metallic specular highlights |
| **Gilded Bronze** | `#E2AC36`, `#A07020` | Chest crest buckle, sword quillons, belt buckle |
| **Weathered Leather**| `#402E20`, `#261A12` | Combat boots, sword grip, utility belt |
| **Dark Ink** | `#0C1018` | Crevices, outlines, limb joint pivots |

---

## 2. Art Pipeline & Rigging Architecture

### 2.1 Environmental Limitation & Pipeline Selection
- **Blender Availability**: Blender is not installed in this headless Linux container sandbox (`which blender` returned not found; package repository mirrors are blocked by outbound firewall network allowlists).
- **Alternative Pipeline**: Rather than relying on AI-generated erratic images or static rectangles, a custom mathematical 2D skeletal rigging and rendering engine was built in Python (`tools/art/generate_player_sprites.py`) utilizing NumPy and Pillow (PIL).
- **Trade-Off**:
  - *Advantage*: Absolute pixel-level alignment, mathematically locked joint pivots, 100% deterministic color palette adherence across all 204 frames, guaranteed zero frame drift or jitter, and zero external network/cloud dependencies.
  - *Trade-Off*: 2D vector-raster rendering rather than 3D polygonal mesh rendering.

### 2.2 Skeletal Joint Hierarchy
```
Root (Pelvis X, Pelvis Y)
├── Torso / Chest (Angle, X, Y)
│   ├── Head / Hooded Cowl (Angle, Visor Glow, Temple Crest)
│   ├── Trailing Cloak Anchor (Flow X, Lift Y, Cyan Hem)
│   ├── Left (Back) Shoulder → Left Elbow → Left Wrist (Dark Tone)
│   └── Right (Front) Shoulder → Right Elbow → Right Wrist (Highlighted Tone)
│       └── Aetherblade (Hilt, Quillons, Blade Spine, Cyan Fuller, Crescent Arc VFX)
├── Left (Back) Hip → Left Knee → Left Ankle → Back Boot (Dark Tone)
└── Right (Front) Hip → Right Knee → Right Ankle → Front Boot (Highlighted Tone)
```

---

## 3. Production Animation Suites (204 Frames)

| Sequence | Frames | FPS | Loop | Description & Mechanics |
| :--- | :---: | :---: | :---: | :--- |
| **Hero Breathing Idle** | 24 | 12 | Yes | Smooth sinusoidal chest breathing, cloak drift, subtle blade rest. |
| **Hero Idle Alert Variation** | 24 | 12 | No | Head turns forward, visor flares with cyan energy, sword shifts into ready guard. |
| **Hero Walk Cycle** | 16 | 16 | Yes | Measured stride cycle, foot planting, arm swings opposing leg cadence. |
| **Hero Run Sprint Cycle** | 20 | 18 | Yes | 14-degree forward lean, deep knee drive, grounded heel-toe plants, billowing trailing cloak. |
| **Hero Run Deceleration Brake**| 8 | 20 | No | Backward lean, front foot skid brake, weight settling into idle. |
| **Hero Jump Anticipation** | 6 | 20 | No | Squat compression, knees bent, arms swinging back, cloak pooling. |
| **Hero Jump Upward Propulsion** | 10 | 20 | Yes | Vertical body stretch, legs kicked back, cloak streaming straight down. |
| **Hero Jump Apex Crest** | 8 | 20 | No | Weightless crest, legs tucked, body leveling off at arc apex. |
| **Hero Jump Downward Descent** | 10 | 20 | Yes | Aerodynamic downward orientation, legs braced, cloak billowing upward around shoulders. |
| **Hero Jump Landing Recovery** | 8 | 20 | No | Impact absorption touchdown, deep crouch squat, springing back to standing. |
| **Hero Horizontal Cleave Strike**| 16 | 20 | No | Startup draw (0-4), horizontal cleave with cyan crescent arc (5-8), recovery (9-15). |
| **Hero Rising Crescent Combo** | 16 | 20 | No | Startup low pivot (0-4), rising diagonal leap slash (5-8), flourish recovery (9-15). |
| **Hero Phase Dash Dodge** | 12 | 20 | Yes | Low-profile tuck, horizontal slipstream dash, streamlined cloak point. |
| **Hero Impact Recoil Stagger** | 10 | 20 | No | Impact stagger thrown back, head snap, arms flailing, recovering balance. |
| **Hero Defeat Collapse** | 16 | 20 | No | Knees buckle, sword drops to ground, torso slumps to rest, visor glow fades out. |

---

## 4. Unity Integration Architecture

### 4.1 Asset Structure
- **Master Model Sheet**: `Assets/Aether/Art/Characters/Player/Aether_Hero_ModelSheet.png` (1024x512)
- **Contact Sheets**: `Assets/Aether/Art/Characters/Player/ContactSheets/Hero_{Animation}_ContactSheet.png`
- **Master Sprite Sheet**: `Assets/Aether/Art/Characters/Player/Aether_Hero_SpriteSheet.png` (1536x1248, 16x13 grid)
- **Runtime Resource**: `Assets/Aether/Resources/Characters/Player/Aether_Hero_SpriteSheet.png`
- **Animation Metadata**: `Assets/Aether/Resources/Characters/Player/player_animations.json`

### 4.2 Runtime Components
1. **`PlayerAnimationCatalog.cs`**:
   - Slices the runtime master sprite sheet into 204 individual `Sprite` assets cached once upon first access.
   - Provides safe single-frame fallback to `CharacterSpriteLibrary.Player` for headless or isolated test fixtures.
   - Employs zero GC allocations during gameplay frame indexing.
2. **`PlayerAnimator.cs`**:
   - Component wired to `PlayerController`, `PlayerMotor`, `PlayerCombat`, and `PlayerHealth`.
   - Drives `SpriteRenderer.sprite` frame advancement and sets `renderer.flipX = _motor.FacingSign < 0`.
   - Synchronizes melee attack frames directly to `PlayerCombat.Phase` and `StartupProgress`.
   - Manages transitions between Idle, Walk, Run, RunStop, Airborne states, Landing, Hurt, Dodge, and Dead.
3. **`PlayerFactory.cs`**:
   - Instantiates and wires `PlayerAnimator` on the player host object alongside `PlayerFeedback` and `PlayerController`.

---

## 5. Verification & Test Coverage

### 5.1 Verification Scripts
All verification suites pass 100%:
- `verify.py`: 162 files, 267 types modelled, 0 failures, 0 warnings.
- `gameplay_runtime.py`: PASS.
- `touchlayout.py`: 24/24 device shapes sound.
- `mainmenu.py`: 714/714 checks passed, 0 problems.
- `levelcheck.py`: 8/8 traversal claims proven.
- `localization.py`: 10/10 languages verified.
- `content_advisory.py`: PASSED.
- `intro.py`: 140/140 synthetic device shapes sound.

### 5.2 PlayMode Tests
Added `PlayerAnimationWiringTests.cs` covering:
- `AnimationCatalogHasAllFifteenSequencesConfigured`: Validates all 15 clips, frame arrays, positive FPS, and non-null sprites.
- `PlayerSpawnsInIdleAnimationAndWiredCorrectly`: Verifies initial Idle state on spawn.
- `PlayerTransitionsBetweenWalkAndRunBasedOnSpeed`: Validates velocity threshold transitions between Walk, Run, and Idle.
- `PlayerTransitionsAirborneAnimationsBasedOnVerticalVelocity`: Validates JumpRise, JumpApex, and JumpFall transitions.
- `PlayerTransitionsToAttackWhenCombatStarts`: Verifies combat attack synchronization.
- `PlayerTransitionsToDeadOnDefeat`: Validates defeat collapse transition.
- `FacingSignCorrectlyFlipsSpriteRenderer`: Verifies horizontal sprite flipping without collider alteration.
