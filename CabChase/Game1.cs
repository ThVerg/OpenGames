using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CabChase;

public class Game1 : Game
{
    // ---------- Core ----------
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;

    private const int ScreenW = 1280;
    private const int ScreenH = 720;

    // ---------- Layout (side-scrolling) ----------
    // Lanes are stacked vertically. Lane 0 is top, lane 2 is bottom.
    private const int LaneCount = 3;
    private const int LaneTop = 320;
    private const int LaneHeight = 86;      // y spacing between lanes (vertical pitch)
    private const int CarH = 70;
    private const int CarW = 140;
    private float LaneY(int lane) => LaneTop + lane * LaneHeight;

    // ---------- Textures ----------
    private Texture2D _pixel;
    private Texture2D _circle32;
    private Texture2D _circle64Hard;  // hard-edged disc used for wheels (no soft halo)

    // ---------- Scrolling ----------
    private float _scrollSpeed = 420f;       // pixels/sec — base
    private float _roadScroll = 0f;
    private float _bgScrollFar = 0f;
    private float _bgScrollNear = 0f;
    private float _distance = 0f;

    // ---------- Player ----------
    private const float PlayerX = 280f;
    private int _lane = 1;
    private float _laneFY;                   // current player Y (interpolated)
    private float _laneSrcY, _laneDstY;
    private float _laneTransition = 0f;
    private const float LaneChangeDuration = 0.22f;
    private float _stumbleTime = 0f;
    private const float StumbleDuration = 1.4f;
    private float _shakeAmount = 0f;

    // ---------- Police ----------
    private const float PoliceX = 100f;
    private float _leadDistance = 280f;
    private const float MaxLead = 360f;
    private float _leadDecayRate = 6f;
    private float _policeLightPhase = 0f;
    // Police is visually "lagging" if lead is high (off-screen left).
    // We render it at PoliceX - (1 - leadFrac) * 200, so as lead approaches 0 it slides into view.

    // ---------- Other cars ----------
    private struct Car
    {
        public int Lane;
        public float X;
        public float LaneFY;             // interpolated y (during lane changes)
        public Color Color;
        public Color WindowTint;
        public float Speed;              // fraction of player speed: 0.3 (slow) .. 0.95 (nearly matching)
        public float LaneChangeTimer;    // seconds remaining in active lane change (0 = idle)
        public float LaneSrcY, LaneDstY;
        public float NextDecisionTime;   // seconds until next AI lane-change roll
    }
    private const float CarLaneChangeDuration = 0.55f;
    private const int MaxConcurrentCars = 5; // hard cap on simultaneous traffic
    private readonly List<Car> _cars = new();
    private float _carSpawnTimer = 0f;

    // ---------- Fare ----------
    private enum FareState { WaitingPickup, OnBoard }
    private FareState _fareState = FareState.WaitingPickup;
    private int _fareLane = 1;
    private float _fareX = ScreenW + 200f;

    // ---------- Buildings (parallax background) ----------
    private struct Building { public float X; public float Width; public float Height; public Color Color; public byte WindowSeed; public int Style; }
    private readonly List<Building> _bgFar = new();
    private readonly List<Building> _bgNear = new();
    private float _bgSpawnFarTimer = 0f;
    private float _bgSpawnNearTimer = 0f;

    // ---------- Particles ----------
    private struct Particle { public Vector2 Pos, Vel; public Color Color; public float Life, MaxLife, Size; }
    private readonly List<Particle> _particles = new();

    // ---------- Game state ----------
    private enum State { Title, Playing, Caught }
    private State _state = State.Title;
    private int _score = 0;
    private int _highScore = 0;
    private int _passengersDelivered = 0;
    private float _stateTimer = 0f;

    // ---------- Input ----------
    private KeyboardState _prevKb;
    private readonly Random _rng = new();

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = ScreenW;
        _graphics.PreferredBackBufferHeight = ScreenH;
        _graphics.SynchronizeWithVerticalRetrace = true;
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        IsFixedTimeStep = false;
        Window.Title = "CabChase";
    }

    protected override void Initialize()
    {
        _laneFY = LaneY(_lane);
        // Seed some buildings so the scene isn't empty on start
        for (int i = 0; i < 8; i++) SeedBuilding(_bgFar, far: true, _rng.Next(ScreenW));
        for (int i = 0; i < 6; i++) SeedBuilding(_bgNear, far: false, _rng.Next(ScreenW));
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _circle32 = MakeCircle(32);
        _circle64Hard = MakeCircleHard(64);
    }

    private Texture2D MakeCircleHard(int diameter)
    {
        // Fully opaque inside, fully transparent outside — no anti-aliased fade.
        // Used for wheels so they don't have a translucent gray halo bleeding into the body.
        var tex = new Texture2D(GraphicsDevice, diameter, diameter);
        var data = new Color[diameter * diameter];
        float r = diameter / 2f - 0.5f;
        for (int y = 0; y < diameter; y++)
        for (int x = 0; x < diameter; x++)
        {
            float dx = x - r, dy = y - r;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            data[y * diameter + x] = d <= r ? Color.White : Color.Transparent;
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakeCircle(int diameter)
    {
        var tex = new Texture2D(GraphicsDevice, diameter, diameter);
        var data = new Color[diameter * diameter];
        float r = diameter / 2f - 0.5f;
        for (int y = 0; y < diameter; y++)
        for (int x = 0; x < diameter; x++)
        {
            float dx = x - r, dy = y - r;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            float edge = MathHelper.Clamp(1f - (d / r), 0f, 1f);
            byte alpha = (byte)(255 * MathF.Min(1f, edge * 2.4f));
            data[y * diameter + x] = new Color((byte)255, (byte)255, (byte)255, alpha);
        }
        tex.SetData(data);
        return tex;
    }

    // ---------- Game flow ----------
    private void StartGame()
    {
        _state = State.Playing;
        _scrollSpeed = 420f;
        _lane = 1;
        _laneFY = LaneY(_lane);
        _laneTransition = 0f;
        _stumbleTime = 0f;
        _shakeAmount = 0f;
        _leadDistance = 280f;
        _distance = 0f;
        _score = 0;
        _passengersDelivered = 0;
        _fareState = FareState.WaitingPickup;
        _fareLane = _rng.Next(LaneCount);
        _fareX = ScreenW + 200f;
        _cars.Clear();
        _particles.Clear();
        _carSpawnTimer = 0.6f;
        _stateTimer = 0f;
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.Escape)) Exit();
        _stateTimer += dt;

        switch (_state)
        {
            case State.Title:
                if (Pressed(kb, Keys.Space) || Pressed(kb, Keys.Enter)) StartGame();
                break;
            case State.Playing:
                UpdatePlaying(dt, kb);
                break;
            case State.Caught:
                if (Pressed(kb, Keys.Space) || Pressed(kb, Keys.Enter)) StartGame();
                break;
        }
        UpdateParticles(dt);
        UpdateBackground(dt);
        _prevKb = kb;
        base.Update(gameTime);
    }

    private bool Pressed(KeyboardState kb, Keys k) => kb.IsKeyDown(k) && !_prevKb.IsKeyDown(k);

    private void UpdatePlaying(float dt, KeyboardState kb)
    {
        // ---- Lane change input (up/down for vertical lanes) ----
        bool wantUp   = Pressed(kb, Keys.W) || Pressed(kb, Keys.Up);
        bool wantDown = Pressed(kb, Keys.S) || Pressed(kb, Keys.Down);
        if ((wantUp || wantDown) && _laneTransition <= 0f && _stumbleTime <= 0f)
        {
            int target = _lane + (wantDown ? 1 : -1);
            if (target >= 0 && target < LaneCount)
            {
                _laneSrcY = _laneFY;
                _laneDstY = LaneY(target);
                _laneTransition = LaneChangeDuration;
                _lane = target;
            }
        }
        if (_laneTransition > 0f)
        {
            _laneTransition -= dt;
            float t = 1f - MathHelper.Clamp(_laneTransition / LaneChangeDuration, 0f, 1f);
            float eased = t * t * (3f - 2f * t);
            _laneFY = MathHelper.Lerp(_laneSrcY, _laneDstY, eased);
            if (_laneTransition <= 0f) { _laneTransition = 0f; _laneFY = _laneDstY; }
        }

        // ---- Scrolling speeds up with distance ----
        _scrollSpeed = MathF.Min(820f, 420f + _distance * 0.008f);
        float effectiveScroll = _stumbleTime > 0f ? _scrollSpeed * 0.55f : _scrollSpeed;
        _roadScroll = (_roadScroll + effectiveScroll * dt) % 60f;
        _distance += effectiveScroll * dt;
        _score = (int)_distance / 10;

        // ---- Lead distance: cops slowly catch up ----
        _leadDistance -= _leadDecayRate * dt;
        _leadDistance = MathHelper.Clamp(_leadDistance, 0f, MaxLead);
        if (_leadDistance <= 0f) { Catch(); return; }

        // ---- Spawn cars ----
        _carSpawnTimer -= dt;
        if (_carSpawnTimer <= 0f)
        {
            SpawnCar();
            _carSpawnTimer = MathHelper.Lerp(0.95f, 0.45f, MathHelper.Clamp(_distance / 8000f, 0f, 1f));
        }

        // ---- Move, AI, and collide cars ----
        for (int i = _cars.Count - 1; i >= 0; i--)
        {
            var c = _cars[i];

            // Speed-relative motion: a car going at fraction c.Speed of the player's speed
            // drifts left on screen at (1 - c.Speed) * effectiveScroll.
            c.X -= effectiveScroll * (1f - c.Speed) * dt;

            // Lane-change interpolation
            if (c.LaneChangeTimer > 0f)
            {
                c.LaneChangeTimer -= dt;
                float t = 1f - MathHelper.Clamp(c.LaneChangeTimer / CarLaneChangeDuration, 0f, 1f);
                float eased = t * t * (3f - 2f * t);
                c.LaneFY = MathHelper.Lerp(c.LaneSrcY, c.LaneDstY, eased);
                if (c.LaneChangeTimer <= 0f) { c.LaneChangeTimer = 0f; c.LaneFY = c.LaneDstY; }
            }
            else
            {
                // AI lane-change decision: only roll while on-screen and not stumble-paused
                c.NextDecisionTime -= dt;
                if (c.NextDecisionTime <= 0f && c.X > -CarW && c.X < ScreenW)
                {
                    // 60% chance to actually attempt a change
                    if (_rng.NextDouble() < 0.60)
                    {
                        // pick a valid adjacent lane (prefer to stay in bounds)
                        int dir = (_rng.NextDouble() < 0.5) ? -1 : 1;
                        int target = c.Lane + dir;
                        if (target < 0 || target >= LaneCount) target = c.Lane - dir;

                        if (target != c.Lane && target >= 0 && target < LaneCount)
                        {
                            // Temporarily write c back so IsLaneSafeForCar sees up-to-date state
                            _cars[i] = c;
                            if (IsLaneSafeForCar(i, target))
                            {
                                c.LaneSrcY = c.LaneFY;
                                c.LaneDstY = LaneY(target);
                                c.LaneChangeTimer = CarLaneChangeDuration;
                                c.Lane = target;
                            }
                        }
                    }
                    c.NextDecisionTime = 1.2f + (float)_rng.NextDouble() * 1.8f;
                }
            }

            _cars[i] = c;

            // off-screen left, or far off-screen right (cars that accelerated to clear a blockade)
            if (c.X < -CarW - 30 || c.X > ScreenW + 200) { _cars.RemoveAt(i); continue; }

            // Collision uses interpolated LaneFY (so mid-change cars can clip you)
            bool xOverlap = c.X < PlayerX + CarW && c.X + CarW > PlayerX;
            float pyTop = _laneFY, pyBot = _laneFY + CarH;
            float cyTop = c.LaneFY, cyBot = c.LaneFY + CarH;
            bool yOverlap = pyTop < cyBot && pyBot > cyTop;
            if (xOverlap && yOverlap)
            {
                HandleHit();
                _cars.RemoveAt(i);
                if (_state == State.Caught) return;
                continue;
            }
        }

        // ---- Fare ----
        _fareX -= effectiveScroll * dt;
        if (_fareX < -120f)
        {
            // Missed pickup: small penalty. Missed drop-off (passenger bails): bigger penalty.
            float penalty = (_fareState == FareState.OnBoard) ? 55f : 18f;
            if (_fareState == FareState.OnBoard)
            {
                _fareState = FareState.WaitingPickup;
                Burst(new Vector2(PlayerX + CarW / 2f, _laneFY + CarH / 2f), new Color(220, 80, 80), 24, 240f);
            }
            _fareLane = _rng.Next(LaneCount);
            _fareX = ScreenW + 180f;
            _leadDistance = MathF.Max(_leadDistance - penalty, 0f);
            if (_leadDistance <= 0f) { Catch(); return; }
        }
        // pickup / dropoff hit (slightly generous touch box)
        bool fXOverlap = _fareX < PlayerX + CarW && _fareX + 80 > PlayerX;
        float fyTop = LaneY(_fareLane), fyBot = LaneY(_fareLane) + CarH;
        bool fYOverlap = _laneFY < fyBot && _laneFY + CarH > fyTop;
        if (fXOverlap && fYOverlap)
        {
            if (_fareState == FareState.WaitingPickup)
            {
                _fareState = FareState.OnBoard;
                _fareLane = _rng.Next(LaneCount);
                _fareX = ScreenW + 180f + (float)_rng.NextDouble() * 280f;
                Burst(new Vector2(PlayerX + CarW / 2f, _laneFY + CarH / 2f), new Color(255, 200, 80), 18, 220f);
            }
            else
            {
                _passengersDelivered++;
                _score += 250;
                _leadDistance = MathF.Min(MaxLead, _leadDistance + 110f);
                Burst(new Vector2(PlayerX + CarW / 2f, _laneFY + CarH / 2f), new Color(120, 230, 255), 28, 280f);
                _fareState = FareState.WaitingPickup;
                _fareLane = _rng.Next(LaneCount);
                _fareX = ScreenW + 180f;
            }
        }

        // ---- Stumble timer ----
        if (_stumbleTime > 0f)
        {
            _stumbleTime -= dt;
            if (_stumbleTime <= 0f) _stumbleTime = 0f;
        }
        if (_shakeAmount > 0f) _shakeAmount = MathF.Max(0f, _shakeAmount - dt * 18f);
        _policeLightPhase += dt * 7f;
    }

    private void HandleHit()
    {
        if (_stumbleTime > 0f) { Catch(); return; }
        _stumbleTime = StumbleDuration;
        _shakeAmount = 14f;
        _leadDistance -= 70f;
        Burst(new Vector2(PlayerX + CarW / 2f, _laneFY + CarH / 2f), new Color(255, 230, 120), 24, 280f);
        if (_leadDistance <= 0f) Catch();
    }

    private void Catch()
    {
        _state = State.Caught;
        _stateTimer = 0f;
        if (_score > _highScore) _highScore = _score;
        _shakeAmount = 28f;
        Burst(new Vector2(PlayerX + CarW / 2f, _laneFY + CarH / 2f), new Color(255, 90, 90), 70, 360f);
        _leadDistance = 0f;
    }

    private void SpawnCar()
    {
        // Hard cap: never have more cars than lanes-minus-one — guarantees one open lane.
        if (_cars.Count >= MaxConcurrentCars) return;

        int lane = _rng.Next(LaneCount);
        // avoid stacking cars too close in the same lane
        foreach (var c in _cars)
            if (c.Lane == lane && c.X > ScreenW - 80) return;
        // keep the fare lane clear of obstacles near the fare itself
        if (lane == _fareLane && MathF.Abs(_fareX - (ScreenW + 100f)) < 200f) return;

        var colors = new[] {
            new Color(180, 70, 70),  new Color(60, 90, 160),  new Color(70, 140, 90),
            new Color(160, 160, 160), new Color(80, 80, 90),  new Color(180, 140, 70),
            new Color(190, 100, 140), new Color(70, 130, 150), new Color(140, 90, 50)
        };
        // Pick a speed bucket so we get a mix of fast/slow traffic:
        //   slow lane (drifts back fast)   – 35%   speed 0.30..0.55
        //   medium pace (drifts back slowly) – 45%   speed 0.55..0.80
        //   nearly matching the player     – 20%   speed 0.80..0.95
        double r = _rng.NextDouble();
        float speed;
        if (r < 0.35) speed = 0.30f + (float)_rng.NextDouble() * 0.25f;
        else if (r < 0.80) speed = 0.55f + (float)_rng.NextDouble() * 0.25f;
        else speed = 0.80f + (float)_rng.NextDouble() * 0.15f;

        float laneY = LaneY(lane);
        _cars.Add(new Car
        {
            Lane = lane,
            X = ScreenW + 30f + (float)_rng.NextDouble() * 60f,
            LaneFY = laneY,
            Color = colors[_rng.Next(colors.Length)],
            WindowTint = new Color(140, 170, 210),
            Speed = speed,
            LaneChangeTimer = 0f,
            LaneSrcY = laneY,
            LaneDstY = laneY,
            // first lane-change attempt 2..5s after spawn
            NextDecisionTime = 2f + (float)_rng.NextDouble() * 3f
        });
    }

    private bool IsLaneSafeForCar(int carIndex, int targetLane)
    {
        var c = _cars[carIndex];
        float carCenterX = c.X + CarW / 2f;

        // Avoid swerving into the player at point-blank range.
        // Convert player's interpolated Y back to a lane comparison: if target Y is close to player's Y,
        // and the car overlaps player's X column, refuse.
        float targetY = LaneY(targetLane);
        if (MathF.Abs(targetY - _laneFY) < LaneHeight * 0.7f &&
            MathF.Abs(c.X - PlayerX) < CarW * 1.4f) return false;

        // Avoid swerving into another car.
        for (int i = 0; i < _cars.Count; i++)
        {
            if (i == carIndex) continue;
            var other = _cars[i];
            // Treat the target lane as occupied if any other car is in it OR transitioning into/out of it
            bool otherInTargetLane = other.Lane == targetLane ||
                                      (other.LaneChangeTimer > 0f && MathF.Abs(other.LaneFY - targetY) < LaneHeight * 0.6f);
            if (otherInTargetLane && MathF.Abs(other.X - c.X) < CarW * 1.6f) return false;
        }
        return true;
    }

    private void Burst(Vector2 pos, Color c, int count, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            float ang = (float)_rng.NextDouble() * MathF.Tau;
            float s = speed * (0.4f + (float)_rng.NextDouble() * 0.8f);
            float life = 0.45f + (float)_rng.NextDouble() * 0.5f;
            _particles.Add(new Particle
            {
                Pos = pos,
                Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * s,
                Color = c, Life = life, MaxLife = life,
                Size = 2f + (float)_rng.NextDouble() * 2.5f
            });
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Pos += p.Vel * dt;
            p.Vel *= MathF.Pow(0.5f, dt * 2.5f);
            p.Life -= dt;
            if (p.Life <= 0) { _particles.RemoveAt(i); continue; }
            _particles[i] = p;
        }
    }

    private void UpdateBackground(float dt)
    {
        // Far layer scrolls at 30% of road speed; near layer at 75%.
        float baseSpeed = _state == State.Playing ? _scrollSpeed : 360f;
        if (_stumbleTime > 0f) baseSpeed *= 0.55f;
        _bgScrollFar  += baseSpeed * 0.30f * dt;
        _bgScrollNear += baseSpeed * 0.75f * dt;

        for (int i = _bgFar.Count - 1; i >= 0; i--)
        {
            var b = _bgFar[i]; b.X -= baseSpeed * 0.30f * dt; _bgFar[i] = b;
            if (b.X + b.Width < -20) _bgFar.RemoveAt(i);
        }
        for (int i = _bgNear.Count - 1; i >= 0; i--)
        {
            var b = _bgNear[i]; b.X -= baseSpeed * 0.75f * dt; _bgNear[i] = b;
            if (b.X + b.Width < -20) _bgNear.RemoveAt(i);
        }

        _bgSpawnFarTimer -= dt;
        if (_bgSpawnFarTimer <= 0f) { SeedBuilding(_bgFar, far: true, ScreenW + 20); _bgSpawnFarTimer = 0.4f + (float)_rng.NextDouble() * 0.4f; }
        _bgSpawnNearTimer -= dt;
        if (_bgSpawnNearTimer <= 0f) { SeedBuilding(_bgNear, far: false, ScreenW + 20); _bgSpawnNearTimer = 0.55f + (float)_rng.NextDouble() * 0.7f; }
    }

    private void SeedBuilding(List<Building> list, bool far, int x)
    {
        var farPal = new[] {
            new Color(80, 90, 130), new Color(95, 90, 120), new Color(75, 100, 130),
            new Color(110, 90, 110), new Color(85, 95, 115)
        };
        var nearPal = new[] {
            new Color(50, 55, 80), new Color(65, 55, 70), new Color(45, 60, 75),
            new Color(70, 60, 50), new Color(50, 65, 65)
        };
        var b = new Building
        {
            X = x,
            Width = (far ? 90 : 130) + _rng.Next(60),
            Height = (far ? 110 : 170) + _rng.Next(far ? 70 : 110),
            Color = (far ? farPal : nearPal)[_rng.Next(5)],
            WindowSeed = (byte)_rng.Next(256),
            Style = _rng.Next(3)
        };
        list.Add(b);
    }

    // ---------- Drawing ----------
    protected override void Draw(GameTime gameTime)
    {
        Vector2 shake = Vector2.Zero;
        if (_shakeAmount > 0.01f)
        {
            shake = new Vector2(
                ((float)_rng.NextDouble() - 0.5f) * _shakeAmount,
                ((float)_rng.NextDouble() - 0.5f) * _shakeAmount);
        }

        _spriteBatch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp,
            transformMatrix: Matrix.CreateTranslation(shake.X, shake.Y, 0));

        DrawSky();
        DrawBuildings(_bgFar);
        DrawBuildings(_bgNear);
        DrawSidewalks();
        DrawRoad();
        DrawFare();
        DrawCars();
        DrawPlayer();
        DrawPolice();
        DrawParticles();
        DrawHud();

        if (_state == State.Title) DrawTitleOverlay();
        else if (_state == State.Caught) DrawCaughtOverlay();

        _spriteBatch.End();
        base.Draw(gameTime);
    }

    private void DrawSky()
    {
        // Dusk gradient over the whole screen — the road covers the bottom half later.
        Color top = new Color(40, 50, 95);
        Color bot = new Color(220, 130, 140);
        int slabs = 30;
        int slabH = ScreenH / slabs + 1;
        for (int i = 0; i < slabs; i++)
        {
            float t = i / (float)(slabs - 1);
            var c = Color.Lerp(top, bot, t);
            _spriteBatch.Draw(_pixel, new Rectangle(0, i * slabH, ScreenW, slabH), c);
        }
    }

    private void DrawBuildings(List<Building> list)
    {
        // Buildings sit ON the ground line just above the road. They scroll with the parallax.
        int groundY = LaneTop - 24; // upper edge of road area
        foreach (var b in list)
        {
            int x = (int)b.X;
            int y = groundY - (int)b.Height;
            _spriteBatch.Draw(_pixel, new Rectangle(x, y, (int)b.Width, (int)b.Height), b.Color);
            // dark outline
            var ol = new Color(15, 15, 25);
            _spriteBatch.Draw(_pixel, new Rectangle(x, y, (int)b.Width, 2), ol);
            _spriteBatch.Draw(_pixel, new Rectangle(x, y, 2, (int)b.Height), ol);
            _spriteBatch.Draw(_pixel, new Rectangle(x + (int)b.Width - 2, y, 2, (int)b.Height), ol);
            // pyramid / antenna top variation
            if (b.Style == 1)
            {
                int peak = Math.Min(20, (int)b.Width / 2);
                for (int ty = 0; ty < peak; ty++)
                {
                    float t = ty / (float)(peak - 1);
                    int slabW = (int)(b.Width * (1f - t));
                    int sx = x + ((int)b.Width - slabW) / 2;
                    _spriteBatch.Draw(_pixel, new Rectangle(sx, y - ty - 1, slabW, 1), b.Color);
                }
            }
            else if (b.Style == 2)
            {
                int aH = 16;
                _spriteBatch.Draw(_pixel, new Rectangle(x + (int)b.Width / 2 - 1, y - aH, 2, aH), b.Color);
                bool on = ((int)(_stateTimer * 2) & 1) == 0;
                _spriteBatch.Draw(_pixel, new Rectangle(x + (int)b.Width / 2 - 2, y - aH - 3, 4, 3),
                    on ? new Color(255, 90, 80) : new Color(120, 30, 30));
            }
            // windows
            int cw = 12, ch = 14;
            int cols = (int)b.Width / cw - 1;
            int rows = (int)b.Height / ch - 1;
            int ox = ((int)b.Width - cols * cw) / 2;
            int oy = ((int)b.Height - rows * ch) / 2;
            for (int wy = 0; wy < rows; wy++)
            for (int wx = 0; wx < cols; wx++)
            {
                int hash = (b.WindowSeed * 73 + wx * 31 + wy * 17) & 0xFF;
                bool lit = hash < 70;
                var wcol = lit ? new Color(255, 220, 130) : new Color(35, 40, 60);
                _spriteBatch.Draw(_pixel, new Rectangle(x + ox + wx * cw, y + oy + wy * ch, cw - 4, ch - 5), wcol);
            }
        }
    }

    private void DrawSidewalks()
    {
        // Sidewalk just above the road
        int sidewalkY = LaneTop - 24;
        var sidewalk = new Color(110, 110, 120);
        var sidewalkD = new Color(70, 70, 80);
        _spriteBatch.Draw(_pixel, new Rectangle(0, sidewalkY, ScreenW, 24), sidewalk);
        // dashes
        int spacing = 40;
        int sOff = (int)(_bgScrollNear % spacing);
        for (int x = -sOff; x < ScreenW; x += spacing)
            _spriteBatch.Draw(_pixel, new Rectangle(x, sidewalkY + 8, 18, 2), sidewalkD);

        // Lower sidewalk (below the road) — small strip
        int lowerY = LaneTop + LaneCount * LaneHeight + 6;
        if (lowerY < ScreenH)
        {
            _spriteBatch.Draw(_pixel, new Rectangle(0, lowerY, ScreenW, ScreenH - lowerY), new Color(95, 95, 105));
            for (int x = -sOff; x < ScreenW; x += spacing)
                _spriteBatch.Draw(_pixel, new Rectangle(x, lowerY + 14, 16, 2), sidewalkD);
        }
    }

    private void DrawRoad()
    {
        var asphalt = new Color(36, 36, 44);
        var asphaltDark = new Color(26, 26, 32);
        int roadTop = LaneTop;
        int roadHeight = LaneCount * LaneHeight + 6;
        _spriteBatch.Draw(_pixel, new Rectangle(0, roadTop, ScreenW, roadHeight), asphalt);
        // subtle horizontal banding
        for (int x = (int)(-_roadScroll); x < ScreenW; x += 14)
            _spriteBatch.Draw(_pixel, new Rectangle(x, roadTop, 1, roadHeight), asphaltDark);

        // Solid edges (top and bottom of the road)
        _spriteBatch.Draw(_pixel, new Rectangle(0, roadTop - 2, ScreenW, 3), Color.White);
        _spriteBatch.Draw(_pixel, new Rectangle(0, roadTop + roadHeight - 1, ScreenW, 3), Color.White);

        // Dashed yellow lane separators between adjacent lanes
        var stripe = new Color(245, 220, 90);
        for (int s = 1; s < LaneCount; s++)
        {
            int sy = LaneTop + s * LaneHeight - 2;
            int dashSpacing = 60;
            int sOff = (int)(_roadScroll % dashSpacing);
            for (int x = -sOff; x < ScreenW; x += dashSpacing)
                _spriteBatch.Draw(_pixel, new Rectangle(x, sy, 32, 4), stripe);
        }
    }

    // ---- Side-view car renderer with a proper car silhouette ----
    // direction: +1 = facing right
    private void DrawSideCar(float fx, float fy, int w, int h, Color body, Color window, Color outline,
                             bool isTaxi, bool isPolice, int direction = +1)
    {
        // Bounding box: (fx, fy) is the top-left of an h-tall box; the wheels stick out below.
        int x = (int)fx;
        int y = (int)fy;

        // ---- Wheels ----
        int wheelD = (int)(h * 0.42f);
        int wheelY = y + h - wheelD;                 // wheel top
        int wheelXFront = x + (int)(w * 0.14f);
        int wheelXRear  = x + (int)(w * 0.86f) - wheelD;

        // ---- Body dimensions ----
        int bodyTop = y + (int)(h * 0.46f);          // belt line — top of lower body
        int bodyBot = wheelY + wheelD / 2;           // body bottom at wheel center
        int bodyLeft = x + 3;
        int bodyRight = x + w - 3;

        // ---- Cabin (windshield slants forward, rear window slants back) ----
        int cabinTop = y + 4;
        int cabinBot = bodyTop;
        // Choose slant amounts depending on facing direction.
        int frontSlant = (int)(w * 0.16f);
        int rearSlant  = (int)(w * 0.12f);
        int cabinFrontBot, cabinFrontTop, cabinRearBot, cabinRearTop;
        if (direction > 0)
        {
            cabinFrontBot = x + (int)(w * 0.22f);
            cabinRearBot  = x + (int)(w * 0.84f);
            cabinFrontTop = cabinFrontBot + frontSlant;
            cabinRearTop  = cabinRearBot  - rearSlant;
        }
        else
        {
            cabinRearBot  = x + (int)(w * 0.16f);
            cabinFrontBot = x + (int)(w * 0.78f);
            cabinRearTop  = cabinRearBot  + rearSlant;
            cabinFrontTop = cabinFrontBot - frontSlant;
        }
        int cabinTopL = Math.Min(cabinFrontTop, cabinRearTop);
        int cabinTopR = Math.Max(cabinFrontTop, cabinRearTop);
        int cabinBotL = Math.Min(cabinFrontBot, cabinRearBot);
        int cabinBotR = Math.Max(cabinFrontBot, cabinRearBot);

        // ---- Shadow ----
        _spriteBatch.Draw(_pixel, new Rectangle(x - 6, bodyBot + 6, w + 12, 10), new Color(0, 0, 0) * 0.40f);

        // ---- Lower body slab ----
        _spriteBatch.Draw(_pixel, new Rectangle(bodyLeft, bodyTop, bodyRight - bodyLeft, bodyBot - bodyTop), body);

        // Subtle body shading: slightly darker band at the bottom of the body
        var bodyDark = ScaleColor(body, 0.78f);
        _spriteBatch.Draw(_pixel, new Rectangle(bodyLeft, bodyBot - 6, bodyRight - bodyLeft, 6), bodyDark);
        // top highlight (a thin lighter band just under the belt line)
        var bodyLight = ScaleColor(body, 1.15f);
        _spriteBatch.Draw(_pixel, new Rectangle(bodyLeft, bodyTop, bodyRight - bodyLeft, 3), bodyLight);

        // ---- Trapezoidal cabin (slanted windshield + rear window) ----
        for (int cy = cabinTop; cy <= cabinBot; cy++)
        {
            float t = (cy - cabinTop) / (float)Math.Max(1, cabinBot - cabinTop); // 0 top -> 1 bot
            int left  = (int)MathHelper.Lerp(cabinTopL, cabinBotL, t);
            int right = (int)MathHelper.Lerp(cabinTopR, cabinBotR, t);
            _spriteBatch.Draw(_pixel, new Rectangle(left, cy, right - left, 1), body);
        }

        // ---- Windows (inset trapezoid, split by a central B-pillar) ----
        int winInsetY = 4;
        int winInsetX = 4;
        for (int cy = cabinTop + winInsetY; cy <= cabinBot - 3; cy++)
        {
            float t = (cy - cabinTop) / (float)Math.Max(1, cabinBot - cabinTop);
            int left  = (int)MathHelper.Lerp(cabinTopL, cabinBotL, t) + winInsetX;
            int right = (int)MathHelper.Lerp(cabinTopR, cabinBotR, t) - winInsetX;
            int mid = (left + right) / 2;
            // front window
            _spriteBatch.Draw(_pixel, new Rectangle(left, cy, mid - 3 - left, 1), window);
            // rear window
            _spriteBatch.Draw(_pixel, new Rectangle(mid + 3, cy, right - (mid + 3), 1), window);
        }
        // B-pillar between front and rear windows (vertical bar)
        int pillarX = (cabinTopL + cabinTopR) / 2 - 2;
        _spriteBatch.Draw(_pixel, new Rectangle(pillarX, cabinTop + winInsetY, 4, cabinBot - cabinTop - winInsetY - 2), body);

        // ---- Door line + door handle on lower body ----
        int doorX = (bodyLeft + bodyRight) / 2;
        _spriteBatch.Draw(_pixel, new Rectangle(doorX - 1, bodyTop + 4, 2, bodyBot - bodyTop - 8), outline);
        // handle
        int handleY = bodyTop + (bodyBot - bodyTop) / 2 - 2;
        _spriteBatch.Draw(_pixel, new Rectangle(doorX - 12, handleY, 10, 3), outline);

        // ---- Side mirror — small dark bump near the front edge of the cabin top, sticking out forward ----
        if (direction > 0)
        {
            _spriteBatch.Draw(_pixel, new Rectangle(cabinFrontBot - 6, cabinBot - 6, 5, 5), outline);
        }
        else
        {
            _spriteBatch.Draw(_pixel, new Rectangle(cabinFrontBot + 1, cabinBot - 6, 5, 5), outline);
        }

        // ---- Wheel arches: thin dark fenders around each wheel (hard-edged disc, no halo) ----
        float archScale = (wheelD * 1.12f) / 64f;
        _spriteBatch.Draw(_circle64Hard, new Vector2(wheelXFront + wheelD / 2f, wheelY + wheelD / 2f), null, outline,
            0f, new Vector2(32, 32), archScale, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, new Vector2(wheelXRear + wheelD / 2f, wheelY + wheelD / 2f), null, outline,
            0f, new Vector2(32, 32), archScale, SpriteEffects.None, 0f);

        // ---- Wheels (tire + hubcap + spoke cross) ----
        DrawWheel(wheelXFront, wheelY, wheelD);
        DrawWheel(wheelXRear, wheelY, wheelD);

        // ---- Headlight + tail light (housing-style with bright inner) ----
        int lightH = (int)(h * 0.13f);
        int lightW = 7;
        int lightY = bodyTop + (bodyBot - bodyTop) / 2 - lightH / 2;
        int headX = direction > 0 ? bodyRight - lightW : bodyLeft;
        int tailX = direction > 0 ? bodyLeft         : bodyRight - lightW;
        // headlight: white-yellow core, outline frame
        _spriteBatch.Draw(_pixel, new Rectangle(headX - 1, lightY - 1, lightW + 2, lightH + 2), outline);
        _spriteBatch.Draw(_pixel, new Rectangle(headX, lightY, lightW, lightH), new Color(255, 240, 180));
        _spriteBatch.Draw(_pixel, new Rectangle(headX + 1, lightY + lightH / 2 - 1, lightW - 2, 2), Color.White);
        // tail light: red-orange housing
        _spriteBatch.Draw(_pixel, new Rectangle(tailX - 1, lightY - 1, lightW + 2, lightH + 2), outline);
        _spriteBatch.Draw(_pixel, new Rectangle(tailX, lightY, lightW, lightH), new Color(220, 60, 50));
        _spriteBatch.Draw(_pixel, new Rectangle(tailX + 1, lightY + 2, lightW - 2, 2), new Color(255, 140, 110));

        // ---- Bumpers — short dark strips extending slightly past the body at the very bottom ----
        int bumperH = 5;
        int bumperY = bodyBot - bumperH;
        var bumperCol = new Color(25, 25, 30);
        _spriteBatch.Draw(_pixel, new Rectangle(x, bumperY, w, bumperH), bumperCol);
        // chrome highlight along the bumper
        _spriteBatch.Draw(_pixel, new Rectangle(x + 2, bumperY + 1, w - 4, 1), new Color(160, 160, 170));

        // ---- Roof accessory: taxi sign ----
        if (isTaxi)
        {
            var signRect = new Rectangle((cabinTopL + cabinTopR) / 2 - 22, cabinTop - 12, 44, 12);
            _spriteBatch.Draw(_pixel, signRect, new Color(255, 235, 80));
            _spriteBatch.Draw(_pixel, new Rectangle(signRect.X, signRect.Y, signRect.Width, 2), outline);
            _spriteBatch.Draw(_pixel, new Rectangle(signRect.X, signRect.Bottom - 2, signRect.Width, 2), outline);
            _spriteBatch.Draw(_pixel, new Rectangle(signRect.X, signRect.Y, 2, signRect.Height), outline);
            _spriteBatch.Draw(_pixel, new Rectangle(signRect.Right - 2, signRect.Y, 2, signRect.Height), outline);
            // dark T-A-X-I letterforms approximated with small blocks
            int letterY = signRect.Y + 3;
            int lx = signRect.X + 5;
            DrawTextBlocks("TAXI", new Vector2(signRect.X + signRect.Width / 2f, letterY + 2), 1, new Color(30, 30, 30));
            // checker squares as little flair on either side
            _spriteBatch.Draw(_pixel, new Rectangle(signRect.X - 6, signRect.Y + 2, 4, 8), new Color(30, 30, 30));
            _spriteBatch.Draw(_pixel, new Rectangle(signRect.Right + 2, signRect.Y + 2, 4, 8), new Color(30, 30, 30));
        }

        // ---- Roof accessory: police light bar ----
        if (isPolice)
        {
            int barW = (int)(w * 0.50f);
            int barX = (cabinTopL + cabinTopR) / 2 - barW / 2;
            int barY = cabinTop - 9;
            _spriteBatch.Draw(_pixel, new Rectangle(barX - 1, barY - 1, barW + 2, 11), outline);
            _spriteBatch.Draw(_pixel, new Rectangle(barX, barY, barW, 9), new Color(20, 20, 30));
            bool phase = ((int)_policeLightPhase & 1) == 0;
            var red  = phase ? new Color(255, 60, 60) : new Color(100, 0, 0);
            var blue = phase ? new Color(40, 0, 80)  : new Color(80, 130, 255);
            _spriteBatch.Draw(_pixel, new Rectangle(barX + 1, barY + 1, barW / 2 - 1, 7), red);
            _spriteBatch.Draw(_pixel, new Rectangle(barX + barW / 2, barY + 1, barW / 2 - 1, 7), blue);

            // door panel: black-white checker strip across the lower body
            int stripH = 16;
            int stripY = bodyTop + (bodyBot - bodyTop) / 2 - stripH / 2;
            for (int dx = bodyLeft + 8; dx < bodyRight - 8; dx += 10)
            {
                bool dark = (((dx - bodyLeft) / 10) & 1) == 0;
                _spriteBatch.Draw(_pixel, new Rectangle(dx, stripY, 10, stripH / 2), dark ? new Color(30, 30, 35) : Color.White);
                _spriteBatch.Draw(_pixel, new Rectangle(dx, stripY + stripH / 2, 10, stripH / 2), dark ? Color.White : new Color(30, 30, 35));
            }
        }

        // ---- Body outline ----
        // lower body outline (top + bottom)
        _spriteBatch.Draw(_pixel, new Rectangle(bodyLeft, bodyTop - 1, bodyRight - bodyLeft, 2), outline);
        _spriteBatch.Draw(_pixel, new Rectangle(bodyLeft, bumperY - 1, bodyRight - bodyLeft, 2), outline);
        // front fender (curved corner) — short slanted line
        _spriteBatch.Draw(_pixel, new Rectangle(bodyLeft - 1, bodyTop, 2, bodyBot - bodyTop), outline);
        _spriteBatch.Draw(_pixel, new Rectangle(bodyRight - 1, bodyTop, 2, bodyBot - bodyTop), outline);

        // cabin outline (windshield + rear window slants)
        for (int cy = cabinTop; cy <= cabinBot; cy++)
        {
            float t = (cy - cabinTop) / (float)Math.Max(1, cabinBot - cabinTop);
            int left  = (int)MathHelper.Lerp(cabinTopL, cabinBotL, t);
            int right = (int)MathHelper.Lerp(cabinTopR, cabinBotR, t);
            _spriteBatch.Draw(_pixel, new Rectangle(left - 1, cy, 2, 1), outline);
            _spriteBatch.Draw(_pixel, new Rectangle(right - 1, cy, 2, 1), outline);
        }
        // roof outline
        _spriteBatch.Draw(_pixel, new Rectangle(cabinTopL - 1, cabinTop, cabinTopR - cabinTopL + 2, 2), outline);
    }

    private void DrawWheel(int x, int y, int d)
    {
        var tire = new Color(22, 22, 28);
        var rim  = new Color(150, 150, 160);
        var hub  = new Color(70, 70, 80);
        int cx = x + d / 2;
        int cy = y + d / 2;

        // tire (outer black) — hard-edged so it doesn't fade into the body color
        _spriteBatch.Draw(_circle64Hard, new Vector2(cx, cy), null, tire,
            0f, new Vector2(32, 32), d / 64f, SpriteEffects.None, 0f);
        // rim (silver) — 62% of tire diameter
        float rimDiameter = d * 0.62f;
        _spriteBatch.Draw(_circle64Hard, new Vector2(cx, cy), null, rim,
            0f, new Vector2(32, 32), rimDiameter / 64f, SpriteEffects.None, 0f);

        // Spokes (cross) — constrained inside the rim
        int spokeLen = Math.Max(2, (int)(rimDiameter) - 4);
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 1, cy - spokeLen / 2, 2, spokeLen), hub);
        _spriteBatch.Draw(_pixel, new Rectangle(cx - spokeLen / 2, cy - 1, spokeLen, 2), hub);

        // hub center cap
        _spriteBatch.Draw(_circle64Hard, new Vector2(cx, cy), null, hub,
            0f, new Vector2(32, 32), (d * 0.20f) / 64f, SpriteEffects.None, 0f);
    }

    private static Color ScaleColor(Color c, float f)
    {
        return new Color(
            (byte)MathHelper.Clamp(c.R * f, 0, 255),
            (byte)MathHelper.Clamp(c.G * f, 0, 255),
            (byte)MathHelper.Clamp(c.B * f, 0, 255));
    }

    private void DrawCars()
    {
        foreach (var c in _cars)
        {
            DrawSideCar(c.X, c.LaneFY, CarW, CarH, c.Color, c.WindowTint, new Color(15, 15, 20),
                isTaxi: false, isPolice: false, direction: +1);
        }
    }

    private void DrawPlayer()
    {
        var body = new Color(255, 200, 30);
        var window = new Color(140, 180, 220);
        var outline = new Color(40, 30, 0);
        DrawSideCar(PlayerX, _laneFY, CarW, CarH, body, window, outline,
            isTaxi: true, isPolice: false, direction: +1);
        // stumble flicker
        if (_stumbleTime > 0f)
        {
            float a = (_stumbleTime / StumbleDuration) * 0.4f;
            _spriteBatch.Draw(_pixel, new Rectangle((int)PlayerX - 8, (int)_laneFY - 12, CarW + 16, CarH + 24), new Color(255, 100, 100) * a);
        }
    }

    private void DrawPolice()
    {
        // Police starts far off-screen at full lead, and at zero lead its FRONT BUMPER
        // touches the taxi's REAR bumper exactly.
        float leadFrac = MathHelper.Clamp(_leadDistance / MaxLead, 0f, 1f);
        float startX = -CarW * 1.5f;         // off-screen left when at full lead
        float endX   = PlayerX - CarW;       // front of cop car touching back of taxi (player's left edge)
        float px = MathHelper.Lerp(startX, endX, 1f - leadFrac);
        float py = _laneFY;
        var body = new Color(245, 245, 250);
        var window = new Color(120, 150, 190);
        var outline = new Color(20, 20, 30);
        DrawSideCar(px, py, CarW, CarH, body, window, outline,
            isTaxi: false, isPolice: true, direction: +1);
    }

    private void DrawFare()
    {
        if (_fareX < -200 || _fareX > ScreenW + 400) return;
        float y = LaneY(_fareLane);
        float pulse = 0.6f + 0.4f * MathF.Sin(_stateTimer * 6f);

        if (_fareState == FareState.WaitingPickup)
        {
            // little person standing in the lane center
            float cx = _fareX + 40;
            float cy = y + CarH * 0.55f;
            // shadow
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - 10, (int)(y + CarH - 6), 22, 6), new Color(0, 0, 0) * 0.35f);
            // body
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - 8, (int)cy - 12, 16, 26), new Color(60, 60, 80));
            // head
            _spriteBatch.Draw(_circle32, new Vector2(cx, cy - 22), null, new Color(230, 200, 160), 0f, new Vector2(16, 16), 0.45f, SpriteEffects.None, 0f);
            // bobbing yellow diamond above
            float bob = MathF.Sin(_stateTimer * 6f) * 3f;
            DrawDiamond((int)cx, (int)(cy - 44 + bob), 18, new Color(255, 215, 60));
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - 1, (int)(cy - 50 + bob), 2, 6), new Color(40, 30, 0));
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - 1, (int)(cy - 42 + bob), 2, 2), new Color(40, 30, 0));
            // glow
            _spriteBatch.Draw(_circle32, new Vector2(cx, cy - 4), null, new Color(255, 215, 80) * (0.35f * pulse), 0f, new Vector2(16, 16), 3f, SpriteEffects.None, 0f);
        }
        else
        {
            // ---- Stopwatch destination marker (purely visual; the clock hand spins) ----
            float cx = _fareX + 40;
            float cy = y + CarH * 0.25f;
            int dia = 44;
            var faceCol = new Color(245, 245, 235);
            var rimCol  = new Color(40, 40, 50);
            var accent  = new Color(255, 215, 60);

            // pole / post under the watch
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - 2, (int)cy + dia / 2, 3, 50), rimCol);

            // stopwatch stem (top crown)
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - 4, (int)cy - dia / 2 - 6, 8, 5), rimCol);
            // little "button" on top of the crown
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - 3, (int)cy - dia / 2 - 9, 6, 3), accent);

            // dark outer ring + cream face (hard discs so there's no soft halo)
            _spriteBatch.Draw(_circle64Hard, new Vector2(cx, cy), null, rimCol,
                0f, new Vector2(32, 32), dia / 64f, SpriteEffects.None, 0f);
            _spriteBatch.Draw(_circle64Hard, new Vector2(cx, cy), null, faceCol,
                0f, new Vector2(32, 32), (dia - 6) / 64f, SpriteEffects.None, 0f);

            // 12 / 3 / 6 / 9 tick marks
            int faceR = (dia - 6) / 2;
            int tickLen = 4;
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - 1, (int)cy - faceR + 2, 2, tickLen), rimCol);
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - 1, (int)cy + faceR - tickLen - 2, 2, tickLen), rimCol);
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx - faceR + 2, (int)cy - 1, tickLen, 2), rimCol);
            _spriteBatch.Draw(_pixel, new Rectangle((int)cx + faceR - tickLen - 2, (int)cy - 1, tickLen, 2), rimCol);

            // sweeping seconds hand — spinning so the watch reads as "ticking"
            float angle = _stateTimer * 2.4f; // radians; ~one full rev every 2.6s
            float handLen = faceR - 4;
            int steps = (int)handLen;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                int hx = (int)(cx + MathF.Sin(angle) * handLen * t);
                int hy = (int)(cy - MathF.Cos(angle) * handLen * t);
                _spriteBatch.Draw(_pixel, new Rectangle(hx - 1, hy - 1, 2, 2), new Color(220, 50, 50));
            }
            // hand pivot dot in the center
            _spriteBatch.Draw(_circle64Hard, new Vector2(cx, cy), null, rimCol,
                0f, new Vector2(32, 32), 5f / 64f, SpriteEffects.None, 0f);

            // glow underneath
            _spriteBatch.Draw(_circle32, new Vector2(cx, cy + 20), null,
                new Color(255, 215, 80) * (0.35f * pulse), 0f, new Vector2(16, 16), 3.5f, SpriteEffects.None, 0f);
        }
    }

    private void DrawDiamond(int cx, int cy, int size, Color c)
    {
        for (int yo = -size / 2; yo <= size / 2; yo++)
        {
            int halfW = size / 2 - Math.Abs(yo);
            _spriteBatch.Draw(_pixel, new Rectangle(cx - halfW, cy + yo, halfW * 2 + 1, 1), c);
        }
    }

    private void DrawParticles()
    {
        foreach (var p in _particles)
        {
            float a = p.Life / p.MaxLife;
            var c = p.Color * a;
            _spriteBatch.Draw(_circle32, p.Pos, null, c, 0f, new Vector2(16, 16), p.Size / 16f, SpriteEffects.None, 0f);
        }
    }

    private void DrawHud()
    {
        DrawTextBlocks("SCORE", new Vector2(76, 26), 3, new Color(220, 230, 255));
        DrawNumber(_score, new Vector2(20, 42), 5, Color.White);

        DrawTextBlocks("FARES", new Vector2(ScreenW - 92, 26), 3, new Color(220, 230, 255));
        DrawNumber(_passengersDelivered, new Vector2(ScreenW - 60, 42), 5, new Color(255, 220, 120));

        // Lead bar top-center
        int bw = 320, bh = 14;
        int bx = (ScreenW - bw) / 2;
        int by = 30;
        _spriteBatch.Draw(_pixel, new Rectangle(bx - 2, by - 2, bw + 4, bh + 4), new Color(15, 15, 20));
        _spriteBatch.Draw(_pixel, new Rectangle(bx, by, bw, bh), new Color(40, 40, 50));
        float frac = MathHelper.Clamp(_leadDistance / MaxLead, 0f, 1f);
        Color barCol = frac < 0.25f ? new Color(255, 80, 80) : frac < 0.5f ? new Color(255, 200, 80) : new Color(120, 230, 120);
        _spriteBatch.Draw(_pixel, new Rectangle(bx, by, (int)(bw * frac), bh), barCol);
        DrawTextBlocks("LEAD", new Vector2(ScreenW / 2f, by + bh + 14), 2, Color.White * 0.85f);

        if (_stumbleTime > 0f)
        {
            float t = _stumbleTime / StumbleDuration;
            float pulse = 0.6f + 0.4f * MathF.Sin(_stateTimer * 30f);
            DrawTextBlocks("STUMBLING", new Vector2(ScreenW / 2f, 90), 3, new Color(255, 100, 100) * (t * pulse));
        }
    }

    private void DrawTitleOverlay()
    {
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), Color.Black * 0.45f);
        DrawTextBlocks("CABCHASE", new Vector2(ScreenW / 2f, 280), 10, new Color(255, 215, 60));
        DrawTextBlocks("W S TO SWITCH LANES", new Vector2(ScreenW / 2f, 400), 3, Color.White * 0.85f);
        float blink = 0.6f + 0.4f * MathF.Sin(_stateTimer * 4f);
        DrawTextBlocks("PRESS SPACE", new Vector2(ScreenW / 2f, 500), 5, Color.White * blink);
    }

    private void DrawCaughtOverlay()
    {
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), Color.Black * 0.55f);
        DrawTextBlocks("BUSTED", new Vector2(ScreenW / 2f, 240), 11, new Color(255, 80, 80));
        DrawTextBlocks("SCORE", new Vector2(ScreenW / 2f - 90, 340), 3, Color.White);
        DrawNumber(_score, new Vector2(ScreenW / 2f + 0, 332), 5, Color.White);
        DrawTextBlocks("FARES", new Vector2(ScreenW / 2f - 90, 390), 3, new Color(255, 220, 120));
        DrawNumber(_passengersDelivered, new Vector2(ScreenW / 2f + 0, 382), 5, new Color(255, 220, 120));
        DrawTextBlocks("BEST", new Vector2(ScreenW / 2f - 90, 440), 3, Color.White * 0.7f);
        DrawNumber(_highScore, new Vector2(ScreenW / 2f + 0, 432), 4, new Color(200, 220, 255));
        if (_stateTimer > 0.7f)
        {
            float blink = 0.6f + 0.4f * MathF.Sin(_stateTimer * 5f);
            DrawTextBlocks("SPACE TO RETRY", new Vector2(ScreenW / 2f, 560), 4, Color.White * blink);
        }
    }

    // ---------- Pixel-block "font" ----------
    private static readonly Dictionary<char, string[]> Glyphs = BuildGlyphs();
    private void DrawTextBlocks(string text, Vector2 centerPos, int pixelSize, Color color)
    {
        text = text.ToUpperInvariant();
        int totalW = 0;
        foreach (var ch in text)
        {
            if (ch == ' ') { totalW += 4 * pixelSize; continue; }
            if (Glyphs.TryGetValue(ch, out var rows)) totalW += (rows[0].Length + 1) * pixelSize;
        }
        float x = centerPos.X - totalW / 2f;
        float y = centerPos.Y - 3 * pixelSize;
        foreach (var ch in text)
        {
            if (ch == ' ') { x += 4 * pixelSize; continue; }
            if (!Glyphs.TryGetValue(ch, out var rows)) continue;
            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                for (int c = 0; c < row.Length; c++)
                {
                    if (row[c] == '#')
                        _spriteBatch.Draw(_pixel, new Rectangle((int)(x + c * pixelSize), (int)(y + r * pixelSize), pixelSize, pixelSize), color);
                }
            }
            x += (rows[0].Length + 1) * pixelSize;
        }
    }
    private void DrawNumber(int value, Vector2 topLeft, int pixelSize, Color color)
    {
        string s = value.ToString();
        float x = topLeft.X;
        foreach (var ch in s)
        {
            if (!Glyphs.TryGetValue(ch, out var rows)) continue;
            for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++)
                if (rows[r][c] == '#')
                    _spriteBatch.Draw(_pixel, new Rectangle((int)(x + c * pixelSize), (int)(topLeft.Y + r * pixelSize), pixelSize, pixelSize), color);
            x += (rows[0].Length + 1) * pixelSize;
        }
    }
    private static Dictionary<char, string[]> BuildGlyphs() => new()
    {
        ['0'] = new[] { "###", "# #", "# #", "# #", "###" },
        ['1'] = new[] { " # ", "## ", " # ", " # ", "###" },
        ['2'] = new[] { "###", "  #", "###", "#  ", "###" },
        ['3'] = new[] { "###", "  #", "###", "  #", "###" },
        ['4'] = new[] { "# #", "# #", "###", "  #", "  #" },
        ['5'] = new[] { "###", "#  ", "###", "  #", "###" },
        ['6'] = new[] { "###", "#  ", "###", "# #", "###" },
        ['7'] = new[] { "###", "  #", "  #", "  #", "  #" },
        ['8'] = new[] { "###", "# #", "###", "# #", "###" },
        ['9'] = new[] { "###", "# #", "###", "  #", "###" },
        ['A'] = new[] { "###", "# #", "###", "# #", "# #" },
        ['B'] = new[] { "## ", "# #", "## ", "# #", "## " },
        ['C'] = new[] { "###", "#  ", "#  ", "#  ", "###" },
        ['D'] = new[] { "## ", "# #", "# #", "# #", "## " },
        ['E'] = new[] { "###", "#  ", "###", "#  ", "###" },
        ['F'] = new[] { "###", "#  ", "###", "#  ", "#  " },
        ['G'] = new[] { "###", "#  ", "# #", "# #", "###" },
        ['H'] = new[] { "# #", "# #", "###", "# #", "# #" },
        ['I'] = new[] { "###", " # ", " # ", " # ", "###" },
        ['J'] = new[] { "###", "  #", "  #", "# #", "###" },
        ['K'] = new[] { "# #", "## ", "#  ", "## ", "# #" },
        ['L'] = new[] { "#  ", "#  ", "#  ", "#  ", "###" },
        ['M'] = new[] { "# #", "###", "###", "# #", "# #" },
        ['N'] = new[] { "# #", "###", "###", "###", "# #" },
        ['O'] = new[] { "###", "# #", "# #", "# #", "###" },
        ['P'] = new[] { "###", "# #", "###", "#  ", "#  " },
        ['Q'] = new[] { "###", "# #", "# #", "###", "  #" },
        ['R'] = new[] { "###", "# #", "###", "## ", "# #" },
        ['S'] = new[] { "###", "#  ", "###", "  #", "###" },
        ['T'] = new[] { "###", " # ", " # ", " # ", " # " },
        ['U'] = new[] { "# #", "# #", "# #", "# #", "###" },
        ['V'] = new[] { "# #", "# #", "# #", "# #", " # " },
        ['W'] = new[] { "# #", "# #", "###", "###", "# #" },
        ['X'] = new[] { "# #", "# #", " # ", "# #", "# #" },
        ['Y'] = new[] { "# #", "# #", " # ", " # ", " # " },
        ['Z'] = new[] { "###", "  #", " # ", "#  ", "###" },
        ['/'] = new[] { "  #", "  #", " # ", "#  ", "#  " },
        ['-'] = new[] { "   ", "   ", "###", "   ", "   " },
        ['!'] = new[] { " # ", " # ", " # ", "   ", " # " },
    };
}
