# Crimson Void

A top-down arena bullet-hell in deep space. Survive 10 levels — each ending with a unique miniboss — and beat the final boss at the end.

Built with [MonoGame](https://www.monogame.net) (C# / .NET 8). **No asset files are shipped** — every sprite (ship, UFO, coin, particles, nebulae) is generated procedurally at startup from a few math routines, and all text uses a hand-rolled 3x5 pixel-block font.

## Controls

| Action | Key |
| --- | --- |
| Move | `WASD` or arrow keys |
| Aim / Shoot | Mouse + Left Click |
| Pick upgrade | `1` / `2` / `3` on the upgrade screen |
| Start / Retry | `Space` or `Enter` |
| Quit | `Esc` |

## Run it

```bash
# Requires .NET 8 SDK
dotnet run -c Release
```

The window opens at 1280x720, vsync on.

## Gameplay loop

- Move with WASD, aim and shoot with the mouse.
- Killing enemies drops gold coins. Coins **magnet** toward you within a short radius.
- Every **5 coins**, the game pauses and shows you 3 upgrade cards. Pick one and play continues.
- Each level ends with a **miniboss** with a unique attack pattern (see below). Level 10 spawns the **FINAL BOSS** instead.

## Enemy roster

| Kind | Behavior | Unlocked |
| --- | --- | --- |
| **Grunt** | Chases, shoots single bullets infrequently | Level 1 |
| **Kamikaze** | Charges fast, arms a fuse at close range, **instakills** on hit | Level 2 |
| **Tough** | Slower but 2-3 HP, fires aimed pairs | Level 3 |
| **Sniper** | Holds at medium range, telegraphs with a dashed aim line, then fires a fast shot | Level 4 |
| **Splitter** | Slow tank that breaks into 2 Grunts on death | Level 5 |
| **Elite** | Bigger, faster, 5+ HP, 3-shot fan | Level 6 |

## Boss roster (HP scales with level)

| Level | Boss | Pattern |
| --- | --- | --- |
| 1 | **GUNNER** | Fast aimed single shots |
| 2 | **SPRAYER** | Sweeping arc across ±48° |
| 3 | **SHOTGUN** | 7-bullet cone burst, long cooldown |
| 4 | **PULSAR** | 14-bullet radial ring that slowly rotates |
| 5 | **TWIN GUNNERS** | Parallel offset double-tap |
| 6 | **BOMBARDIER** | Slow lobbed "mines" that decelerate and linger |
| 7 | **SNIPER ELITE** | Dashed-line telegraph, then a 720 px/s shot |
| 8 | **SPIRAL** | Two opposing rotating shots at 10 Hz cadence |
| 9 | **MULTIPLIER** | Aimed shots that split into 3 fragments mid-flight |
| 10 | **FINAL BOSS** | Two-phase: 5-shot fan → ring bursts + rapid triple-tap |

All bosses fly procedurally-rendered red UFOs with a glassy cyan dome and warm rim lights, drifting on a slow bob with a pulsing tractor-beam glow underneath.

## Upgrade pool

3 random options drawn per pick (capped upgrades are excluded from the pool):

- **RAPID FIRE** — fire rate ×0.75 (floor 0.045s between shots)
- **HEAVY ROUNDS** — +1 bullet damage (uncapped)
- **PIERCE** — bullets pass through +1 more enemy (uncapped)
- **MULTI SHOT** — +1 bullet per shot (twin → triple → quad → quint spread, cap 5)
- **THRUSTERS** — +20% move speed (cap 600 px/s)
- **MAX HP** — +1 max HP and full heal (uncapped)
- **BIG BULLETS** — bullet collision radius ×1.6 (cap 22 px)
- **COIN MAGNET** — stronger magnet + pickup radius (caps at 420 / 60 px)

## Notable design choices

- **One file**: all game logic lives in [`Game1.cs`](./Game1.cs) (~1300 lines). Easy to read end-to-end; no asset pipeline; no dependency on the MonoGame Content Builder for runtime.
- **Procedural textures**: built at `LoadContent` time via simple per-pixel math (`MakeCircle`, `MakeRing`, `MakeShip`, `MakeNebula`, `MakeTriangle`, `MakeCoin`, `MakeUFO`).
- **Parallax starfield**: 220 stars across 6+ depth layers, each twinkling and drifting at its own rate.
- **Coin pickups** use a soft magnet field rather than instant grab — gives feedback for moving toward them.
- **Bullet mechanics in the engine**:
  - `Drag` field — exponential velocity decay (used by Bombardier mines)
  - `SplitsRemaining` + `SplitAt` — bullets fragment mid-flight (used by Multiplier)
  - `Pierce` — bullets pass through enemies (used by the PIERCE upgrade)

## Tweakable knobs

| Field | Default | What it controls |
| --- | --- | --- |
| `ScreenW`, `ScreenH` | 1280, 720 | Window size |
| `_playerSpeed` | 260 | Base movement speed |
| `_shootInterval` | 0.16s | Time between shots |
| `_maxHp` | 5 | Starting HP |
| `MaxLevel` | 10 | Total levels |
| `CoinsPerUpgrade` | 5 | Coins required for each upgrade pick |

## License

MIT — see [../LICENSE](../LICENSE).
