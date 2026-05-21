# Kick of the Dead

A 2D side-view platformer where a star footballer kicks his way up a zombie-infested tower to rescue a princess. Your soccer ball is your only weapon — kick it, bounce it off walls, and chain hits across rooms full of undead.

Built with [MonoGame](https://www.monogame.net) (C# / .NET 8). All visuals are procedurally generated at startup — no asset files.

## Controls

| Action | Key |
| --- | --- |
| Move | `A` / `D` or `←` / `→` |
| Jump | `Space` / `W` / `↑` |
| Aim | Mouse |
| Kick | Left-click or `F` |
| Start / Retry | `Space` or `Enter` |
| Quit | `Esc` |

## Run it

```bash
# Requires .NET 8 SDK
dotnet run -c Release
```

Window opens at 1280x720 landscape, vsync on.

## The hook

You don't have a sword or a gun — you have a soccer ball. Hold it at your feet, aim with the mouse, and kick to launch the ball with full physics. It arcs, hits zombies hard enough to drop them, bounces off walls, and rolls to a stop where you have to go retrieve it. A slow ball won't damage anything — only a kick that's still moving past **320 px/s** kills.

## Tower

Five floors of escalating zombie chaos. Each floor is a single-screen arena; clear all zombies → next floor.

- **Floor 1**: shamblers only — learn the kick
- **Floor 2**: shamblers + runners
- **Floor 3**: introduces **Brutes** (3 HP, knock the ball back)
- **Floor 4**: dense mix
- **Floor 5**: the **DEMON LORD** boss + princess rescue

## Zombies

| Kind | HP | Speed | Notes |
| --- | --- | --- | --- |
| **Shambler** | 1 | slow | basic, dies in one kick |
| **Runner** | 1 | fast | charges you, harder to dodge |
| **Brute** | 3 | very slow | bigger, more HP. Mega-kicks land 2 dmg |

## Power-ups

Killed zombies sometimes drop power-ups (~22% chance).

| Icon | Effect | Duration |
| --- | --- | --- |
| 🔥 **Fire Ball** | Ball is on fire — sets zombies aflame (DOT) | 10s |
| ✦ **Multi-Ball** | Spawn 2 extra balls that bounce around damaging zombies | 7s |
| ❗ **Mega Kick** | Your next kick is much harder and pierces through 3 zombies | 6s window |
| » **Speed Boost** | +45% movement speed | 8s |
| ❤ **Heart** | Restore 1 HP |

## Boss: Demon Lord

Floats around the room shooting fans of fireballs at you. At half HP it enters phase 2 — denser 5-shot fans and summons Runners. Knock 40 HP off with your ball to free the princess.

## Tuning knobs

| Field | Default | Notes |
| --- | --- | --- |
| `Gravity` | 2000 px/s² | World gravity |
| `BallGravity` | 1700 px/s² | Ball-specific |
| `BallBounce` | 0.55 | Velocity preserved per bounce |
| `BallMinKillVel` | 320 px/s | Slower hits don't damage |
| `JumpVy` | -780 px/s | Player jump impulse |
| `_playerSpeed` | 280 px/s | Base movement |
| Brute HP | 3 | Two-HP for the others (1 shot kill) |
| Drop rate | 22% | Per zombie killed |

## License

MIT — see [../LICENSE](../LICENSE).
