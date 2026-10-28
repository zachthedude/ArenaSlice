# ArenaSlice

**Project page:** https://zachthedude.github.io/ArenaSlice/ — interactive breakdown of the movement, AI and weapons.

Greybox FPS vertical slice for Unity 6000.5.9f1. Built-in Unity APIs only, no asset packs or extra packages.

## Run
1. Open this folder in Unity Hub (it generates ProjectSettings on first open).
2. Open any scene and press Play. `Game.cs` boots itself and builds the arena, NavMesh, player and HUD.
3. Optional check: menu **ArenaSlice > Run Self-Check** asserts the kinematics and falloff numbers.

Controls: WASD, mouse, Shift sprint, Ctrl/C crouch (while sprinting: slide), Space jump, LMB fire, RMB aim, R reload, 1/2/wheel switch weapon, Esc pause.
Debug: **F1** AI state overlay, **F2** feel layer on/off, **F3** object pooling on/off.

## Where each success criterion lives

| # | Criterion | Code | How to test it |
|---|-----------|------|----------------|
| 1 | Movement | `PlayerController.cs`: `State` enum + `NextState` switch (Walk, Sprint, Crouch, Slide, Air). Jump speed from `Kinematics.TakeoffSpeed(g, h)`, g = 20 m/s², h = 1.2 m. Slide uses friction μg·cosθ plus g·sinθ on ramps. Coyote 0.1 s, buffer 0.12 s. | Record 5 minutes. Use F1 to see the state and speed. The centre ramps test slope slides; the side overhangs (1.2 m gap) test crouch and slide. |
| 2 | Weapons | `Weapon.cs` base class (rate, magazine, reload, spread, recoil, falloff, headshots, noise). `HitscanWeapon` raycasts. `ProjectileWeapon` + `Projectile` fly with real gravity, inherit the shooter's velocity, and sweep each step so shots can't tunnel. | Add a third weapon in one sitting **without editing `Weapon.cs`**. Left for you on purpose, since it is the test of your learning goal. |
| 3 | Enemy AI | `EnemyAI.cs`: Idle, Patrol, Investigate, Search, Chase, Attack. NavMesh baked at runtime. Sight is `dot(forward, toPlayer) ≥ cos 55°` plus a raycast. Hearing comes from `EnemyAI.Hear` (sprint 14 m, walk 5 m, crouch silent, gunfire 35–45 m). | Players always see `?` or `!`. F1 shows the exact state to compare with the playtester's answer. |
| 4 | Feel | `Feel.cs`: hit-stop, shake, FOV kick, hit markers, damage numbers, scaled ×2 for headshots and ×1.5 for kills. | F2 removes the whole layer during play. |
| 5 | Performance | `ProjectileWeapon` uses `UnityEngine.Pool.ObjectPool`, warmed up at start. Feel numbers use a fixed ring buffer. | Profile a fight with F3 on, then off, and screenshot both. |
| 6 | Accessibility | Horizontal FOV slider, separate ADS sensitivity, subtitles on opaque black with the direction of the sound. Health, ammo, hit markers and AI alerts use text or shape, never colour alone. | Color Oracle plus a muted playtest. |
| 7 | Scope | One arena, 4 waves (3/4/6/8 enemies), start menu, end screen with time and best time (saved as JSON in `settings.json`). | Stopwatch five sessions, then tune `Game.waves` until a run takes 5–8 min. |

## Known ceilings (marked `ponytail:` in code)
- Sound passes through walls (straight-line radius).
- The IMGUI HUD makes a little garbage every frame. Move it to UI Toolkit if it shows up in the Profiler's GC column.
