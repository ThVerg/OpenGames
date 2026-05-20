# Gravity Flap

Flappy Bird, but every flap **reverses gravity** instead of giving you an upward impulse. Tap once to fall up, tap again to fall down. The sky inverts with you.

Built with [MonoGame](https://www.monogame.net) (C# / .NET 8). All visuals are generated procedurally at startup — no asset files.

## Controls

| Action | Key |
| --- | --- |
| Flap (flip gravity) | `Space` / `W` / `↑` / Left-click |
| Start / Retry | Any flap |
| Quit | `Esc` |

## Run it

```bash
# Requires .NET 8 SDK
dotnet run -c Release
```

Window opens at 900x640, vsync on.

## The twist

In classic Flappy Bird, a tap gives the bird an instantaneous upward velocity. Gravity is always pulling down.

Here, a tap **flips** the direction of gravity and zeroes the bird's vertical velocity. After a flap:
- if you were falling, you start floating up at increasing speed
- if you were rising, you start falling at increasing speed

The longer you wait between flaps, the more velocity builds up in the new direction. Quick double-taps barely move you; long pauses launch the bird across the screen. The sky gradient itself flips between blue-day and amber-sunset to reinforce the disorientation, and the bird rotates 180° to match its new "up."

## Tuning knobs

| Field | Default | Notes |
| --- | --- | --- |
| `GravityStrength` | 2000 px/s² | Per-axis acceleration; higher = punchier |
| `TerminalVy` | 720 px/s | Velocity cap so flips stay readable |
| `PipeSpacing` | 280 px | Horizontal distance between pipes |
| `PipeWidth` | 78 px | Pipe column width |
| `_scrollSpeed` | 220 px/s base, +2/score (cap 360) | Game gets faster as you score |
| Gap height | 200 − min(60, score) | Gap shrinks as you score |

## License

MIT — see [../LICENSE](../LICENSE).
