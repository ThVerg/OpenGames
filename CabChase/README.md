# CabChase

A 2D side-scrolling lane-runner that mashes Crazy Taxi (pick up fares, drop them off, make a buck) with Subway Surfers (3-lane endless dodge with a police car always on your tail).

Built with [MonoGame](https://www.monogame.net) (C# / .NET 8). All visuals are procedurally generated at startup — no asset files.

## Controls

| Action | Key |
| --- | --- |
| Switch lane (up/down) | `W` / `S` or `↑` / `↓` |
| Start / Retry | `Space` or `Enter` |
| Quit | `Esc` |

## Run it

```bash
# Requires .NET 8 SDK
dotnet run -c Release
```

Window opens at 1280x720 (landscape), vsync on. Camera is at ground level, side-on — you see your taxi from the side rolling left-to-right while the world scrolls past.

## The hook: stumble & catch

Lane changes take **0.22s** — during them you cross through two lanes at once, so it's the riskiest moment to bump into traffic.

- **Hit one car** → **stumble** for 1.4 seconds (screen shake, slowed scroll, no lane changes, red flicker, lead distance drops).
- **Hit a second car while stumbling** → **CAUGHT.** Game over.

The cops are always behind you at a visible distance (the **LEAD** bar at the top). Hits eat into your lead. The cops also slowly gain on you over time, so you can't just coast forever — you have to pick up and deliver fares to top up your lead.

## Taxi loop

1. A yellow waypoint with an **"!"** diamond marks a passenger waiting in some lane ahead.
2. Drive over the waypoint to pick them up. A **stopwatch icon** then appears further ahead in another lane.
3. Drop them off at the stopwatch for **+250 score** and **+110 lead distance** (push the cops back).
4. Missing a pickup costs `-18` lead. Letting a picked-up passenger scroll off (passenger bails) costs `-55` lead.

## Traffic

Up to **5 cars** on screen at once. Each spawns with one of three speed buckets so traffic feels varied:

- **Slow** (35% of spawns, 0.30–0.55× your speed) — drifts to the left of the screen quickly
- **Medium** (45%, 0.55–0.80×) — drifts back more slowly
- **Nearly matching** (20%, 0.80–0.95×) — barely moves relative to you

Each car also rolls for a lane change every **1.2–3.0s** with a 60% chance to actually attempt it. The AI refuses changes that would clip another car or sideswipe the player point-blank.

## The cops

The police car rides off-screen left. As your **LEAD** bar drops, the cop slides into view — when LEAD hits 0, its front bumper has reached the back of your taxi exactly, and you're busted.

## Tuning knobs

| Field | Default | Notes |
| --- | --- | --- |
| `_scrollSpeed` | 420 → 820 | Player speed in px/sec; ramps up with distance |
| `_leadDistance` | 280 start, max 360 | Visual gap between you and the cops |
| `_leadDecayRate` | 6 px/sec | Passive ground-gain by the cops |
| `LaneChangeDuration` | 0.22s | Player lane change time (you're double-vulnerable during) |
| `StumbleDuration` | 1.4s | Second-hit window after first impact |
| `CarLaneChangeDuration` | 0.55s | How long AI cars take to change lanes |
| `MaxConcurrentCars` | 5 | Hard cap on simultaneous traffic |
| Drop-off bonus | +110 lead, +250 score | Per fare delivered |
| Hit penalty | -70 lead | Per single bump |
| Missed pickup | -18 lead | Yellow waypoint scrolled past |
| Passenger bail | -55 lead | On-board fare scrolled past |

## License

MIT — see [../LICENSE](../LICENSE).
