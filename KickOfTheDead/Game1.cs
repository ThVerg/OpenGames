using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace KickOfTheDead;

public class Game1 : Game
{
    // ---------- Core ----------
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;

    private const int ScreenW = 1280;
    private const int ScreenH = 520;

    // ---------- World layout ----------
    private const int GroundY  = 470;
    private const int CeilingY = 40;

    private float _cameraX = 0f;
    private int   _levelWidth = ScreenW;       // total scrollable width of current floor
    private const float CameraLerp = 8f;       // higher = snappier
    private const float CameraEdge = 400f;     // player x-on-screen anchor

    // Platforms (world-space rectangles you can stand on)
    private readonly List<Rectangle> _platforms = new();
    // End-of-level door
    private Rectangle _door = Rectangle.Empty;
    private bool _doorOpen = false;

    // ---------- Textures ----------
    private Texture2D _pixel;
    private Texture2D _circle32;
    private Texture2D _circle64Hard;

    // ---------- Player ----------
    private Vector2 _playerPos;          // bottom-center, in world coords
    private Vector2 _playerVel;
    private bool _onGround;
    private int _facing = 1;
    private int _hp = 3;
    private int _maxHp = 3;
    private float _iFrames = 0f;
    private const int PlayerW = 36;
    private const int PlayerH = 70;
    private float _playerSpeed = 280f;
    private const float JumpVy = -800f;    // apex ≈ 160 px — tighter, more Mario-like
    private float _kickCooldown = 0f;
    private float _kickAnim = 0f;

    // ---------- Ball ----------
    private Vector2 _ballPos;            // center, world coords
    private Vector2 _ballVel;
    private bool _ballHeld = true;
    private const int BallR = 10;
    private const float BallGravity = 1700f;
    private const float BallBounce  = 0.55f;
    private const float BallMinKillVel = 320f;
    private float _ballRotation = 0f;
    private float _ballFireTime = 0f;
    private int   _ballPierceCharges = 0;
    private float _megaKickArmedFor = 0f;
    private float _multiBallArmedFor = 0f;  // while > 0, each kick fires 3 balls in a spread
    private float _speedBoostTime = 0f;

    private struct ExtraBall { public Vector2 Pos, Vel; public float Life; public float Rotation; public bool Fire; }
    private readonly List<ExtraBall> _extraBalls = new();

    private const float Gravity = 2000f;

    // ---------- Zombies ----------
    private enum ZombieKind { Shambler, Runner, Brute }
    private struct Zombie
    {
        public ZombieKind Kind;
        public Vector2 Pos;
        public Vector2 Vel;
        public int Hp;
        public int MaxHp;
        public bool OnGround;
        public float HitFlash;
        public float OnFireTime;
        public float ShoveTimer;
    }
    private readonly List<Zombie> _zombies = new();

    // ---------- Coins ----------
    private struct Coin { public Vector2 Pos; public Vector2 Vel; public float Spin; public float Life; }
    private readonly List<Coin> _coins = new();

    // ---------- Power-ups ----------
    private enum PowerUpKind { FireBall, MultiBall, MegaKick, SpeedBoost, Heart }
    private struct PowerUp { public Vector2 Pos; public Vector2 Vel; public PowerUpKind Kind; public float Life; }
    private readonly List<PowerUp> _powerUps = new();

    // ---------- Particles ----------
    private struct Particle { public Vector2 Pos, Vel; public Color Color; public float Life, MaxLife, Size; }
    private readonly List<Particle> _particles = new();

    // ---------- Floating text (HEADSHOT, score popups) ----------
    private struct FloatText { public Vector2 Pos; public Vector2 Vel; public Color Color; public float Life; public float MaxLife; public string Text; public int PixelSize; }
    private readonly List<FloatText> _floatTexts = new();

    // ---------- Game state ----------
    private enum State { Title, Playing, FloorClear, Dead, Won }
    private State _state = State.Title;
    private int _floor = 1;
    private const int MaxFloor = 5;
    private int _score = 0;
    private int _highScore = 0;
    private float _stateTimer = 0f;
    private float _floorClearTimer = 0f;

    // ---------- Boss ----------
    private struct Boss
    {
        public Vector2 Pos;
        public int Hp;
        public int MaxHp;
        public float ActionTimer;
        public int Phase;
        public float HitFlash;
        public bool Alive;
    }
    private Boss _boss;
    private struct Fireball { public Vector2 Pos, Vel; public float Life; }
    private readonly List<Fireball> _bossFireballs = new();

    // ---------- Input ----------
    private KeyboardState _prevKb;
    private MouseState _prevMouse;
    private readonly Random _rng = new();

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = ScreenW;
        _graphics.PreferredBackBufferHeight = ScreenH;
        _graphics.SynchronizeWithVerticalRetrace = true;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        Window.Title = "Kick of the Dead";
    }

    protected override void Initialize()
    {
        ResetPlayerToStart();
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

    private Texture2D MakeCircleHard(int diameter)
    {
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

    // ---------- Game flow ----------
    private void ResetPlayerToStart()
    {
        _playerPos = new Vector2(160, GroundY);
        _playerVel = Vector2.Zero;
        _onGround = true;
        _facing = 1;
        _ballHeld = true;
        _ballPos = _playerPos + new Vector2(20, -10);
        _ballVel = Vector2.Zero;
        _iFrames = 0f;
        _kickCooldown = 0f;
        _kickAnim = 0f;
        _cameraX = 0f;
    }

    private void StartGame()
    {
        _state = State.Playing;
        _floor = 1;
        _score = 0;
        _hp = _maxHp = 3;
        _playerSpeed = 280f;
        _ballFireTime = 0f;
        _ballPierceCharges = 0;
        _megaKickArmedFor = 0f;
        _multiBallArmedFor = 0f;
        _speedBoostTime = 0f;
        _extraBalls.Clear();
        _zombies.Clear();
        _powerUps.Clear();
        _coins.Clear();
        _particles.Clear();
        _floatTexts.Clear();
        _bossFireballs.Clear();
        BeginFloor(_floor);
        _stateTimer = 0f;
    }

    private void BeginFloor(int floor)
    {
        _floor = floor;
        _zombies.Clear();
        _powerUps.Clear();
        _coins.Clear();
        _bossFireballs.Clear();
        _platforms.Clear();
        _doorOpen = false;
        _floorClearTimer = 0f;

        bool bossFloor = (floor == MaxFloor);
        _levelWidth = bossFloor ? ScreenW : ScreenW * 3;

        ResetPlayerToStart();

        if (bossFloor)
        {
            _boss = new Boss
            {
                Pos = new Vector2(ScreenW - 220, GroundY - 110),
                Hp = 40, MaxHp = 40, ActionTimer = 2.5f, Phase = 0, Alive = true, HitFlash = 0f
            };
            // Boss arena platforms — above head level, within jump reach
            _platforms.Add(new Rectangle(280, GroundY - 110, 160, 20));
            _platforms.Add(new Rectangle(620, GroundY - 135, 160, 20));
            _platforms.Add(new Rectangle(960, GroundY - 110, 160, 20));
            _door = Rectangle.Empty;
        }
        else
        {
            _boss.Alive = false;
            // ---- Generate Mario-style platforms ----
            // Player head while standing is at GroundY - PlayerH (= 70 px up). Platforms sit
            // CLEARLY above head level (100..140 px up), still reachable with the 160 px jump apex.
            int numPlatforms = 5 + _rng.Next(4);
            float minX = 360f, maxX = _levelWidth - 360f;
            for (int i = 0; i < numPlatforms; i++)
            {
                float t = i / (float)(numPlatforms - 1);
                float px = MathHelper.Lerp(minX, maxX, t) + ((float)_rng.NextDouble() - 0.5f) * 120f;
                int py = GroundY - (100 + _rng.Next(40));
                int pw = 110 + _rng.Next(140);
                _platforms.Add(new Rectangle((int)px, py, pw, 20));
            }

            // ---- Door at the end of the level ----
            _door = new Rectangle(_levelWidth - 110, GroundY - 120, 70, 120);

            // ---- Pre-place zombies along the level (some on platforms!) ----
            int numZombies = 5 + floor * 2;
            for (int i = 0; i < numZombies; i++)
            {
                // ~35% spawn ON a platform; the rest on the ground.
                if (_platforms.Count > 0 && _rng.NextDouble() < 0.35)
                {
                    var plat = _platforms[_rng.Next(_platforms.Count)];
                    float sx = plat.Left + 25 + (float)_rng.NextDouble() * (plat.Width - 50);
                    AddZombie(sx, plat.Top);
                }
                else
                {
                    float t = (i + 0.5f) / numZombies;
                    float sx = MathHelper.Lerp(600f, _levelWidth - 220f, t);
                    sx += ((float)_rng.NextDouble() - 0.5f) * 80f;
                    AddZombie(sx);
                }
            }
        }

        Burst(_playerPos + new Vector2(0, -PlayerH / 2f), Color.White, 30, 220f);
    }

    private void AddZombie(float x) => AddZombie(x, GroundY);

    private void AddZombie(float x, float y)
    {
        ZombieKind kind = ZombieKind.Shambler;
        double r = _rng.NextDouble();
        if (_floor == 1) kind = ZombieKind.Shambler;
        else if (_floor == 2) kind = r < 0.65 ? ZombieKind.Shambler : ZombieKind.Runner;
        else if (_floor == 3) kind = r < 0.45 ? ZombieKind.Shambler : (r < 0.85 ? ZombieKind.Runner : ZombieKind.Brute);
        else                 kind = r < 0.30 ? ZombieKind.Shambler : (r < 0.65 ? ZombieKind.Runner : ZombieKind.Brute);
        int hp = kind switch { ZombieKind.Brute => 6, ZombieKind.Runner => 2, _ => 2 };
        _zombies.Add(new Zombie
        {
            Kind = kind,
            Pos = new Vector2(x, y),
            Vel = Vector2.Zero,
            Hp = hp, MaxHp = hp, OnGround = true
        });
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var kb = Keyboard.GetState();
        var mouse = Mouse.GetState();
        if (kb.IsKeyDown(Keys.Escape)) Exit();
        _stateTimer += dt;

        switch (_state)
        {
            case State.Title:
                if (Pressed(kb, Keys.Space) || Pressed(kb, Keys.Enter)) StartGame();
                break;
            case State.Playing:
                UpdatePlaying(dt, kb, mouse);
                break;
            case State.FloorClear:
                UpdatePlaying(dt, kb, mouse);
                _floorClearTimer += dt;
                if (_floorClearTimer > 1.2f)
                {
                    if (_floor >= MaxFloor) { _state = State.Won; _stateTimer = 0f; }
                    else { BeginFloor(_floor + 1); _state = State.Playing; }
                }
                break;
            case State.Dead:
            case State.Won:
                if (Pressed(kb, Keys.Space) || Pressed(kb, Keys.Enter)) StartGame();
                break;
        }

        UpdateParticles(dt);
        UpdateFloatTexts(dt);
        _prevKb = kb;
        _prevMouse = mouse;
        base.Update(gameTime);
    }

    private bool Pressed(KeyboardState kb, Keys k) => kb.IsKeyDown(k) && !_prevKb.IsKeyDown(k);
    private bool MousePressed(MouseState m) => m.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton != ButtonState.Pressed;

    private void UpdatePlaying(float dt, KeyboardState kb, MouseState mouse)
    {
        // ---------- Player input ----------
        float moveDir = 0f;
        if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left))  moveDir -= 1f;
        if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right)) moveDir += 1f;
        if (moveDir != 0) _facing = moveDir > 0 ? 1 : -1;

        float speed = _playerSpeed * (_speedBoostTime > 0f ? 1.45f : 1f);
        _playerVel.X = moveDir * speed;

        if ((Pressed(kb, Keys.Space) || Pressed(kb, Keys.W) || Pressed(kb, Keys.Up)) && _onGround)
        {
            _playerVel.Y = JumpVy;
            _onGround = false;
        }

        _playerVel.Y += Gravity * dt;
        if (_playerVel.Y > 1500f) _playerVel.Y = 1500f;

        // Move X then resolve, then move Y then resolve (separated-axis platform collision)
        _playerPos.X += _playerVel.X * dt;
        ResolvePlayerHorizontal();
        _playerPos.Y += _playerVel.Y * dt;
        _onGround = false;
        ResolvePlayerVertical();

        // Level bounds (left/right walls of the world)
        if (_playerPos.X - PlayerW / 2f < 0)               { _playerPos.X = PlayerW / 2f;               _playerVel.X = 0; }
        if (_playerPos.X + PlayerW / 2f > _levelWidth)     { _playerPos.X = _levelWidth - PlayerW / 2f; _playerVel.X = 0; }

        // Ground (a single floor across the whole level)
        if (_playerPos.Y >= GroundY) { _playerPos.Y = GroundY; _playerVel.Y = 0; _onGround = true; }

        // Camera follow
        float targetCamera = _playerPos.X - CameraEdge;
        _cameraX = MathHelper.Lerp(_cameraX, targetCamera, 1f - MathF.Pow(0.001f, dt * CameraLerp));
        _cameraX = MathHelper.Clamp(_cameraX, 0, MathF.Max(0, _levelWidth - ScreenW));

        // ---------- Ball update ----------
        UpdateBall(dt, mouse);
        UpdateExtraBalls(dt);

        // ---------- Power-up timers ----------
        _ballFireTime = MathF.Max(0f, _ballFireTime - dt);
        _megaKickArmedFor = MathF.Max(0f, _megaKickArmedFor - dt);
        _multiBallArmedFor = MathF.Max(0f, _multiBallArmedFor - dt);
        _speedBoostTime = MathF.Max(0f, _speedBoostTime - dt);

        // ---------- Kick input ----------
        _kickCooldown -= dt;
        _kickAnim = MathF.Max(0f, _kickAnim - dt * 4f);
        bool kickPressed = Pressed(kb, Keys.F) || MousePressed(mouse);
        if (kickPressed && _ballHeld && _kickCooldown <= 0f)
        {
            // Aim is from ball to mouse position in WORLD coordinates
            Vector2 mouseWorld = new Vector2(mouse.X + _cameraX, mouse.Y);
            Vector2 aim = mouseWorld - _ballPos;
            if (aim.LengthSquared() < 1f) aim = new Vector2(_facing, -0.3f);
            aim.Normalize();
            if (aim.Y > -0.15f) aim.Y = -0.15f;
            aim.Normalize();

            float kickPower = (_megaKickArmedFor > 0f) ? 1180f : 820f;
            _ballVel = aim * kickPower;
            _ballHeld = false;
            _kickCooldown = 0.20f;
            _kickAnim = 1f;
            if (_megaKickArmedFor > 0f)
            {
                _megaKickArmedFor = 0f;
                _ballPierceCharges = 3;
            }
            // MULTI BALL: every kick during the buff fires 2 additional balls in a spread.
            if (_multiBallArmedFor > 0f)
            {
                for (int i = 0; i < 2; i++)
                {
                    float spreadAngle = (i == 0 ? -0.30f : 0.30f);
                    Vector2 spreadAim = new Vector2(
                        aim.X * MathF.Cos(spreadAngle) - aim.Y * MathF.Sin(spreadAngle),
                        aim.X * MathF.Sin(spreadAngle) + aim.Y * MathF.Cos(spreadAngle));
                    _extraBalls.Add(new ExtraBall
                    {
                        Pos = _ballPos, Vel = spreadAim * kickPower,
                        Life = 4f, Rotation = 0f, Fire = _ballFireTime > 0f
                    });
                }
            }
        }

        // ---------- Update zombies ----------
        UpdateZombies(dt);

        // ---------- Boss ----------
        if (_floor == MaxFloor && _boss.Alive)
            UpdateBoss(dt);

        // ---------- Coins ----------
        UpdateCoins(dt);

        // ---------- Power-ups ----------
        UpdatePowerUps(dt);

        // ---------- iFrames ----------
        _iFrames = MathF.Max(0f, _iFrames - dt);

        // ---------- Door / level complete ----------
        bool noEnemies = _zombies.Count == 0 && (!_boss.Alive);
        if (!_doorOpen && noEnemies) _doorOpen = true;

        if (_doorOpen && _door.Width > 0)
        {
            // Walk through the door → next floor
            Rectangle prect = new Rectangle((int)(_playerPos.X - PlayerW / 2), (int)(_playerPos.Y - PlayerH), PlayerW, PlayerH);
            if (prect.Intersects(_door))
            {
                _state = State.FloorClear;
                _floorClearTimer = 0f;
                Burst(_playerPos + new Vector2(0, -PlayerH / 2f), new Color(255, 220, 130), 40, 280f);
            }
        }
        else if (_doorOpen && _floor == MaxFloor && !_boss.Alive)
        {
            // Boss defeated → win
            _state = State.Won;
            _stateTimer = 0f;
            if (_score > _highScore) _highScore = _score;
        }
    }

    // ---------- Platform / ground collision ----------
    private void ResolvePlayerHorizontal()
    {
        Rectangle r = new Rectangle((int)(_playerPos.X - PlayerW / 2), (int)(_playerPos.Y - PlayerH), PlayerW, PlayerH);
        foreach (var plat in _platforms)
        {
            if (!r.Intersects(plat)) continue;
            float overlapL = r.Right - plat.Left;
            float overlapR = plat.Right - r.Left;
            if (overlapL < overlapR) { _playerPos.X = plat.Left - PlayerW / 2f; }
            else                      { _playerPos.X = plat.Right + PlayerW / 2f; }
            _playerVel.X = 0;
            r = new Rectangle((int)(_playerPos.X - PlayerW / 2), (int)(_playerPos.Y - PlayerH), PlayerW, PlayerH);
        }
    }

    private void ResolvePlayerVertical()
    {
        Rectangle r = new Rectangle((int)(_playerPos.X - PlayerW / 2), (int)(_playerPos.Y - PlayerH), PlayerW, PlayerH);
        foreach (var plat in _platforms)
        {
            if (!r.Intersects(plat)) continue;
            float overlapTop = r.Bottom - plat.Top;
            float overlapBot = plat.Bottom - r.Top;
            if (overlapTop < overlapBot)
            {
                // landed on top of the platform
                _playerPos.Y = plat.Top;
                _playerVel.Y = 0;
                _onGround = true;
            }
            else
            {
                // hit the underside (jumping into it from below)
                _playerPos.Y = plat.Bottom + PlayerH;
                _playerVel.Y = MathF.Max(0, _playerVel.Y);
            }
            r = new Rectangle((int)(_playerPos.X - PlayerW / 2), (int)(_playerPos.Y - PlayerH), PlayerW, PlayerH);
        }
    }

    private bool BallBouncesOffPlatforms(ref Vector2 pos, ref Vector2 vel)
    {
        Rectangle r = new Rectangle((int)(pos.X - BallR), (int)(pos.Y - BallR), BallR * 2, BallR * 2);
        bool bounced = false;
        foreach (var plat in _platforms)
        {
            if (!r.Intersects(plat)) continue;
            float overlapL = r.Right - plat.Left;
            float overlapR = plat.Right - r.Left;
            float overlapT = r.Bottom - plat.Top;
            float overlapB = plat.Bottom - r.Top;
            float minH = MathF.Min(overlapL, overlapR);
            float minV = MathF.Min(overlapT, overlapB);
            if (minV < minH)
            {
                if (overlapT < overlapB)
                {
                    pos.Y = plat.Top - BallR;
                    vel.Y = -MathF.Abs(vel.Y) * BallBounce;
                    vel.X *= 0.92f;
                }
                else
                {
                    pos.Y = plat.Bottom + BallR;
                    vel.Y = MathF.Abs(vel.Y) * BallBounce;
                }
            }
            else
            {
                if (overlapL < overlapR) { pos.X = plat.Left - BallR;  vel.X = -MathF.Abs(vel.X) * BallBounce; }
                else                     { pos.X = plat.Right + BallR; vel.X =  MathF.Abs(vel.X) * BallBounce; }
            }
            bounced = true;
            r = new Rectangle((int)(pos.X - BallR), (int)(pos.Y - BallR), BallR * 2, BallR * 2);
        }
        return bounced;
    }

    // ---------- Ball ----------
    private void UpdateBall(float dt, MouseState mouse)
    {
        if (_ballHeld)
        {
            float bob = (MathF.Abs(_playerVel.X) > 1f) ? MathF.Sin(_stateTimer * 14f) * 2f : 0f;
            _ballPos = new Vector2(_playerPos.X + _facing * 22f, _playerPos.Y - 12f + bob);
            _ballVel = Vector2.Zero;
            _ballRotation += _playerVel.X * dt * 0.04f;
            return;
        }
        _ballVel.Y += BallGravity * dt;
        _ballVel.Y = MathF.Min(_ballVel.Y, 1500f);
        _ballPos += _ballVel * dt;

        // World boundaries
        if (_ballPos.X - BallR < 0) { _ballPos.X = BallR; _ballVel.X = -_ballVel.X * BallBounce; }
        if (_ballPos.X + BallR > _levelWidth) { _ballPos.X = _levelWidth - BallR; _ballVel.X = -_ballVel.X * BallBounce; }
        if (_ballPos.Y - BallR < CeilingY) { _ballPos.Y = CeilingY + BallR; _ballVel.Y = -_ballVel.Y * BallBounce; }
        // Ground
        if (_ballPos.Y + BallR >= GroundY)
        {
            _ballPos.Y = GroundY - BallR;
            _ballVel.Y = -_ballVel.Y * BallBounce;
            _ballVel.X *= 0.88f;
            if (MathF.Abs(_ballVel.Y) < 60f) _ballVel.Y = 0f;
        }
        BallBouncesOffPlatforms(ref _ballPos, ref _ballVel);

        _ballRotation += _ballVel.X * dt * 0.05f;
        if (_ballVel.LengthSquared() < 80f * 80f &&
            Vector2.Distance(_ballPos, _playerPos + new Vector2(0, -16)) < 30f)
        {
            _ballHeld = true;
        }
    }

    private void UpdateExtraBalls(float dt)
    {
        for (int i = _extraBalls.Count - 1; i >= 0; i--)
        {
            var b = _extraBalls[i];
            b.Vel.Y += BallGravity * dt;
            b.Vel.Y = MathF.Min(b.Vel.Y, 1500f);
            b.Pos += b.Vel * dt;
            if (b.Pos.X - BallR < 0) { b.Pos.X = BallR; b.Vel.X = -b.Vel.X * BallBounce; }
            if (b.Pos.X + BallR > _levelWidth) { b.Pos.X = _levelWidth - BallR; b.Vel.X = -b.Vel.X * BallBounce; }
            if (b.Pos.Y - BallR < CeilingY) { b.Pos.Y = CeilingY + BallR; b.Vel.Y = -b.Vel.Y * BallBounce; }
            if (b.Pos.Y + BallR >= GroundY)
            {
                b.Pos.Y = GroundY - BallR;
                b.Vel.Y = -b.Vel.Y * BallBounce;
                b.Vel.X *= 0.88f;
                if (MathF.Abs(b.Vel.Y) < 60f) b.Vel.Y = 0f;
            }
            BallBouncesOffPlatforms(ref b.Pos, ref b.Vel);
            b.Rotation += b.Vel.X * dt * 0.05f;
            b.Life -= dt;
            _extraBalls[i] = b;
            if (b.Life <= 0f) _extraBalls.RemoveAt(i);
        }
    }

    // ---------- Zombies ----------
    private void UpdateZombies(float dt)
    {
        for (int i = _zombies.Count - 1; i >= 0; i--)
        {
            var z = _zombies[i];
            float zspeed = z.Kind switch
            {
                ZombieKind.Shambler => 75f,
                ZombieKind.Runner   => 195f,
                ZombieKind.Brute    => 55f,
                _ => 80f
            };
            if (z.ShoveTimer > 0f) zspeed *= 0.25f;
            float dir = _playerPos.X < z.Pos.X ? -1f : 1f;
            z.Vel.X = dir * zspeed;
            z.Vel.Y += Gravity * dt;
            z.Vel.Y = MathF.Min(z.Vel.Y, 1500f);
            float prevY = z.Pos.Y;
            z.Pos += z.Vel * dt;

            // Ground
            if (z.Pos.Y >= GroundY) { z.Pos.Y = GroundY; z.Vel.Y = 0; z.OnGround = true; }
            else z.OnGround = false;

            // Platforms (one-way top collision so zombies can stand on them)
            if (z.Vel.Y >= 0)
            {
                float zw = ZombieWidth(z.Kind);
                foreach (var plat in _platforms)
                {
                    if (z.Pos.X + zw / 2 > plat.Left && z.Pos.X - zw / 2 < plat.Right)
                    {
                        if (prevY <= plat.Top + 1 && z.Pos.Y >= plat.Top)
                        {
                            z.Pos.Y = plat.Top;
                            z.Vel.Y = 0;
                            z.OnGround = true;
                        }
                    }
                }
            }

            if (z.Pos.X < 30) z.Pos.X = 30;
            if (z.Pos.X > _levelWidth - 30) z.Pos.X = _levelWidth - 30;

            z.ShoveTimer = MathF.Max(0f, z.ShoveTimer - dt);
            z.HitFlash = MathF.Max(0f, z.HitFlash - dt * 3f);
            if (z.OnFireTime > 0f)
            {
                z.OnFireTime -= dt;
                if ((int)(z.OnFireTime * 2f) != (int)((z.OnFireTime + dt) * 2f))
                {
                    z.Hp -= 1;
                    z.HitFlash = 1f;
                    Burst(z.Pos + new Vector2(0, -ZombieHeight(z.Kind) / 2f), new Color(255, 150, 60), 6, 160f);
                }
            }

            CheckBallHitsZombie(ref z, ref _ballPos, ref _ballVel, ref _ballPierceCharges, fire: _ballFireTime > 0f);
            for (int j = 0; j < _extraBalls.Count; j++)
            {
                var eb = _extraBalls[j];
                int dummyPierce = 0;
                Vector2 ep = eb.Pos; Vector2 ev = eb.Vel;
                CheckBallHitsZombie(ref z, ref ep, ref ev, ref dummyPierce, fire: eb.Fire);
                eb.Pos = ep; eb.Vel = ev;
                _extraBalls[j] = eb;
            }

            if (z.Hp > 0 && _iFrames <= 0f)
            {
                float zw = ZombieWidth(z.Kind), zh = ZombieHeight(z.Kind);
                Rectangle zrect = new Rectangle((int)(z.Pos.X - zw / 2), (int)(z.Pos.Y - zh), (int)zw, (int)zh);
                Rectangle prect = new Rectangle((int)(_playerPos.X - PlayerW / 2), (int)(_playerPos.Y - PlayerH), PlayerW, PlayerH);
                if (zrect.Intersects(prect))
                    HurtPlayer();
            }

            if (z.Hp <= 0)
            {
                _score += z.Kind switch
                {
                    ZombieKind.Shambler => 50,
                    ZombieKind.Runner => 75,
                    ZombieKind.Brute => 120,
                    _ => 50
                };
                Burst(z.Pos + new Vector2(0, -ZombieHeight(z.Kind) / 2f), new Color(120, 200, 90), 16, 240f);
                // ---- Drops ----
                Vector2 dropPos = z.Pos + new Vector2(0, -ZombieHeight(z.Kind) * 0.55f);
                int coinCount = z.Kind switch { ZombieKind.Brute => 3 + _rng.Next(3), ZombieKind.Runner => 1 + _rng.Next(2), _ => 1 + _rng.Next(2) };
                DropCoins(dropPos, coinCount);
                if (_rng.NextDouble() < 0.28) DropPowerUp(dropPos);
                _zombies.RemoveAt(i);
                continue;
            }
            _zombies[i] = z;
        }
    }

    private void CheckBallHitsZombie(ref Zombie z, ref Vector2 ballPos, ref Vector2 ballVel, ref int pierce, bool fire)
    {
        if (z.Hp <= 0) return;
        float zw = ZombieWidth(z.Kind), zh = ZombieHeight(z.Kind);
        Rectangle zrect = new Rectangle((int)(z.Pos.X - zw / 2), (int)(z.Pos.Y - zh), (int)zw, (int)zh);
        Vector2 closest = new Vector2(MathHelper.Clamp(ballPos.X, zrect.Left, zrect.Right),
                                       MathHelper.Clamp(ballPos.Y, zrect.Top, zrect.Bottom));
        if (Vector2.DistanceSquared(ballPos, closest) > BallR * BallR) return;

        float v = ballVel.Length();
        if (v < BallMinKillVel) return;

        // Head zone is the TOP 36% of the zombie (matches the head drawn in DrawZombies).
        // Hits inside that band count as headshots and deal double damage.
        float headBottomY = z.Pos.Y - zh * 0.64f;
        bool headshot = ballPos.Y < headBottomY;

        int dmg = headshot ? 2 : 1;
        // Mega-kick on a Brute already does 2; combine with headshot for big damage.
        if (z.Kind == ZombieKind.Brute && v > 700f) dmg += 1;

        z.Hp -= dmg;
        z.HitFlash = 1f;
        z.ShoveTimer = 0.35f;
        if (fire) z.OnFireTime = MathF.Max(z.OnFireTime, 5f);

        // Big bursts + headshot popup
        if (headshot)
        {
            Burst(ballPos, new Color(255, 230, 90), 18, 320f);
            Burst(ballPos, Color.White, 8, 200f);
            _floatTexts.Add(new FloatText
            {
                Pos = new Vector2(z.Pos.X, headBottomY - 4),
                Vel = new Vector2(0, -60f),
                Color = new Color(255, 230, 90),
                Life = 0.9f, MaxLife = 0.9f,
                Text = "HEADSHOT", PixelSize = 2
            });
            _score += 40; // bonus
        }
        else
        {
            Burst(ballPos, fire ? new Color(255, 160, 60) : Color.White, 10, 220f);
        }

        if (pierce > 0) { pierce -= 1; }
        else
        {
            // Proper reflection off the zombie's surface: reverse the velocity component along
            // the normal so the ball clearly bounces BACK rather than just slowing in place.
            // Then add a small upward kick so it doesn't immediately drop to the ground.
            Vector2 normal = ballPos - (z.Pos + new Vector2(0, -zh / 2f));
            if (normal.LengthSquared() < 1f) normal = new Vector2(0, -1);
            normal.Normalize();
            float dot = Vector2.Dot(ballVel, normal);
            // If the ball is moving INTO the zombie (dot < 0), flip that component.
            if (dot < 0) ballVel -= 2f * dot * normal;
            ballVel *= 0.75f; // energy loss on impact
            // Ensure a minimum bounce so even slow-hitting balls have visible recoil.
            float postV = ballVel.Length();
            if (postV < 260f) ballVel = (postV > 1f ? ballVel / postV : normal) * 280f;
            // Bias upward a bit so the bounce stays in play.
            if (ballVel.Y > -80f) ballVel.Y -= 120f;
        }
    }

    private float ZombieWidth(ZombieKind k) => k switch { ZombieKind.Brute => 56, ZombieKind.Runner => 32, _ => 38 };
    private float ZombieHeight(ZombieKind k) => k switch { ZombieKind.Brute => 86, ZombieKind.Runner => 64, _ => 70 };

    // ---------- Coins / power-ups ----------
    private void DropCoins(Vector2 pos, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _coins.Add(new Coin
            {
                Pos = pos,
                Vel = new Vector2(((float)_rng.NextDouble() - 0.5f) * 160f, -200f - (float)_rng.NextDouble() * 70f),
                Spin = (float)_rng.NextDouble() * MathF.Tau,
                Life = 12f
            });
        }
    }

    private void UpdateCoins(float dt)
    {
        for (int i = _coins.Count - 1; i >= 0; i--)
        {
            var c = _coins[i];
            c.Vel.Y += Gravity * 0.6f * dt;
            c.Pos += c.Vel * dt;
            // Ground
            if (c.Pos.Y > GroundY - 8) { c.Pos.Y = GroundY - 8; c.Vel.Y *= -0.35f; c.Vel.X *= 0.82f; if (MathF.Abs(c.Vel.Y) < 30) c.Vel.Y = 0; }
            // Sides
            if (c.Pos.X < 8) { c.Pos.X = 8; c.Vel.X = MathF.Abs(c.Vel.X) * 0.5f; }
            if (c.Pos.X > _levelWidth - 8) { c.Pos.X = _levelWidth - 8; c.Vel.X = -MathF.Abs(c.Vel.X) * 0.5f; }
            // Platforms (simple top-collision)
            foreach (var plat in _platforms)
            {
                if (c.Pos.X > plat.Left && c.Pos.X < plat.Right && c.Pos.Y > plat.Top && c.Pos.Y < plat.Top + 10 && c.Vel.Y > 0)
                {
                    c.Pos.Y = plat.Top;
                    c.Vel.Y = -c.Vel.Y * 0.35f;
                    c.Vel.X *= 0.82f;
                    if (MathF.Abs(c.Vel.Y) < 30) c.Vel.Y = 0;
                }
            }
            c.Spin += dt * 6f;
            c.Life -= dt;
            // Magnet pickup — coin pulls toward player when nearby
            Vector2 pp = _playerPos + new Vector2(0, -PlayerH / 2f);
            float pd = Vector2.Distance(c.Pos, pp);
            if (pd < 80f)
            {
                Vector2 to = pp - c.Pos; if (to.LengthSquared() > 1) to.Normalize();
                c.Vel = Vector2.Lerp(c.Vel, to * 340f, 0.20f);
            }
            if (pd < 24f)
            {
                _score += 25;
                Burst(c.Pos, new Color(255, 220, 80), 6, 160f);
                _coins.RemoveAt(i);
                continue;
            }
            if (c.Life <= 0) { _coins.RemoveAt(i); continue; }
            _coins[i] = c;
        }
    }

    private void DropPowerUp(Vector2 pos)
    {
        var kinds = new[] { PowerUpKind.FireBall, PowerUpKind.MultiBall, PowerUpKind.MegaKick, PowerUpKind.SpeedBoost, PowerUpKind.Heart };
        var k = kinds[_rng.Next(kinds.Length)];
        _powerUps.Add(new PowerUp
        {
            Pos = pos,
            Vel = new Vector2(((float)_rng.NextDouble() - 0.5f) * 80f, -160f),
            Kind = k,
            Life = 14f
        });
    }

    private void UpdatePowerUps(float dt)
    {
        for (int i = _powerUps.Count - 1; i >= 0; i--)
        {
            var p = _powerUps[i];
            p.Vel.Y += Gravity * 0.6f * dt;
            p.Pos += p.Vel * dt;
            if (p.Pos.Y > GroundY - 12) { p.Pos.Y = GroundY - 12; p.Vel.Y *= -0.35f; p.Vel.X *= 0.82f; if (MathF.Abs(p.Vel.Y) < 30) p.Vel.Y = 0; }
            if (p.Pos.X < 12) { p.Pos.X = 12; p.Vel.X = MathF.Abs(p.Vel.X) * 0.4f; }
            if (p.Pos.X > _levelWidth - 12) { p.Pos.X = _levelWidth - 12; p.Vel.X = -MathF.Abs(p.Vel.X) * 0.4f; }
            // Platforms (simple top-stop)
            foreach (var plat in _platforms)
            {
                if (p.Pos.X > plat.Left && p.Pos.X < plat.Right && p.Pos.Y > plat.Top && p.Pos.Y < plat.Top + 14 && p.Vel.Y > 0)
                {
                    p.Pos.Y = plat.Top - 4;
                    p.Vel.Y = 0;
                    p.Vel.X *= 0.85f;
                }
            }
            p.Life -= dt;
            Rectangle prect = new Rectangle((int)(_playerPos.X - PlayerW / 2), (int)(_playerPos.Y - PlayerH), PlayerW, PlayerH);
            if (prect.Contains((int)p.Pos.X, (int)p.Pos.Y))
            {
                ApplyPowerUp(p.Kind, p.Pos);
                _powerUps.RemoveAt(i);
                continue;
            }
            if (p.Life <= 0) { _powerUps.RemoveAt(i); continue; }
            _powerUps[i] = p;
        }
    }

    private void ApplyPowerUp(PowerUpKind kind, Vector2 pos)
    {
        switch (kind)
        {
            case PowerUpKind.FireBall:
                _ballFireTime = 10f;
                Burst(pos, new Color(255, 150, 60), 22, 220f);
                break;
            case PowerUpKind.MultiBall:
                _multiBallArmedFor = 8f;
                Burst(pos, new Color(255, 220, 120), 22, 240f);
                break;
            case PowerUpKind.MegaKick:
                _megaKickArmedFor = 6f;
                Burst(pos, new Color(255, 90, 90), 22, 240f);
                break;
            case PowerUpKind.SpeedBoost:
                _speedBoostTime = 8f;
                Burst(pos, new Color(120, 230, 255), 22, 240f);
                break;
            case PowerUpKind.Heart:
                if (_hp < _maxHp) _hp += 1;
                Burst(pos, new Color(255, 100, 130), 22, 240f);
                break;
        }
    }

    private void HurtPlayer()
    {
        _hp -= 1;
        _iFrames = 1.0f;
        Burst(_playerPos + new Vector2(0, -PlayerH / 2f), new Color(255, 80, 80), 26, 280f);
        if (_hp <= 0)
        {
            _state = State.Dead;
            _stateTimer = 0f;
            if (_score > _highScore) _highScore = _score;
        }
    }

    // ---------- Boss ----------
    private void UpdateBoss(float dt)
    {
        _boss.HitFlash = MathF.Max(0f, _boss.HitFlash - dt * 3f);
        float targetX = MathHelper.Clamp(_playerPos.X + (_boss.Phase == 1 ? -120f : 320f), 140, _levelWidth - 140);
        _boss.Pos.X = MathHelper.Lerp(_boss.Pos.X, targetX, 1f - MathF.Pow(0.001f, dt));
        _boss.Pos.Y = GroundY - 110 + MathF.Sin(_stateTimer * 2f) * 14f;

        _boss.ActionTimer -= dt;
        if (_boss.ActionTimer <= 0f)
        {
            int n = (_boss.Phase == 0) ? 3 : 5;
            float spread = 0.30f;
            var to = _playerPos + new Vector2(0, -PlayerH / 2f) - _boss.Pos;
            if (to.LengthSquared() < 1) to = new Vector2(-1, 0); else to.Normalize();
            float baseAngle = MathF.Atan2(to.Y, to.X);
            for (int i = 0; i < n; i++)
            {
                float t = (n == 1) ? 0f : (i / (float)(n - 1)) * 2f - 1f;
                float a = baseAngle + t * spread;
                _bossFireballs.Add(new Fireball
                {
                    Pos = _boss.Pos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 60f,
                    Vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * 360f,
                    Life = 4f
                });
            }
            if (_boss.Phase == 1 && _zombies.Count < 4 && _rng.NextDouble() < 0.7)
            {
                float sx = (_rng.NextDouble() < 0.5) ? 80 : _levelWidth - 80;
                AddZombie(sx);
            }
            _boss.ActionTimer = (_boss.Phase == 0) ? 1.8f : 1.3f;
        }

        if (_boss.Phase == 0 && _boss.Hp <= _boss.MaxHp / 2)
        {
            _boss.Phase = 1;
            Burst(_boss.Pos, new Color(255, 100, 60), 60, 320f);
        }

        for (int i = _bossFireballs.Count - 1; i >= 0; i--)
        {
            var f = _bossFireballs[i];
            f.Pos += f.Vel * dt;
            f.Vel.Y += BallGravity * 0.3f * dt;
            f.Life -= dt;
            if (f.Life <= 0 || f.Pos.X < -50 || f.Pos.X > _levelWidth + 50 || f.Pos.Y > ScreenH + 50) { _bossFireballs.RemoveAt(i); continue; }
            if (f.Pos.Y > GroundY - 4) { Burst(new Vector2(f.Pos.X, GroundY - 4), new Color(255, 140, 60), 14, 240f); _bossFireballs.RemoveAt(i); continue; }
            if (_iFrames <= 0f && Vector2.DistanceSquared(f.Pos, _playerPos + new Vector2(0, -PlayerH / 2f)) < (PlayerW * 0.55f) * (PlayerW * 0.55f))
            {
                HurtPlayer();
                _bossFireballs.RemoveAt(i);
                continue;
            }
            _bossFireballs[i] = f;
        }

        if (!_ballHeld)
            CheckBallHitsBoss(ref _ballPos, ref _ballVel, ref _ballPierceCharges, fire: _ballFireTime > 0f);
        for (int j = 0; j < _extraBalls.Count; j++)
        {
            var eb = _extraBalls[j];
            int dummy = 0;
            Vector2 ep = eb.Pos; Vector2 ev = eb.Vel;
            CheckBallHitsBoss(ref ep, ref ev, ref dummy, fire: eb.Fire);
            eb.Pos = ep; eb.Vel = ev;
            _extraBalls[j] = eb;
        }

        if (_boss.Hp <= 0 && _boss.Alive)
        {
            _boss.Alive = false;
            _bossFireballs.Clear();
            Burst(_boss.Pos, new Color(255, 100, 60), 100, 420f);
            _score += 1000;
        }
    }

    private void CheckBallHitsBoss(ref Vector2 ballPos, ref Vector2 ballVel, ref int pierce, bool fire)
    {
        if (!_boss.Alive) return;
        var rect = new Rectangle((int)(_boss.Pos.X - 70), (int)(_boss.Pos.Y - 70), 140, 140);
        Vector2 closest = new Vector2(MathHelper.Clamp(ballPos.X, rect.Left, rect.Right),
                                       MathHelper.Clamp(ballPos.Y, rect.Top, rect.Bottom));
        if (Vector2.DistanceSquared(ballPos, closest) > BallR * BallR) return;
        float v = ballVel.Length();
        if (v < BallMinKillVel) return;
        _boss.Hp -= (fire ? 2 : 1);
        _boss.HitFlash = 1f;
        Burst(ballPos, fire ? new Color(255, 150, 60) : Color.White, 14, 260f);
        if (pierce > 0) { pierce--; }
        else
        {
            var push = ballPos - _boss.Pos; if (push.LengthSquared() < 1) push = new Vector2(-1, 0); push.Normalize();
            ballVel = push * MathF.Max(v * 0.4f, 240f);
        }
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

    private void UpdateFloatTexts(float dt)
    {
        for (int i = _floatTexts.Count - 1; i >= 0; i--)
        {
            var ft = _floatTexts[i];
            ft.Pos += ft.Vel * dt;
            ft.Vel *= MathF.Pow(0.5f, dt * 1.4f);
            ft.Life -= dt;
            if (ft.Life <= 0) { _floatTexts.RemoveAt(i); continue; }
            _floatTexts[i] = ft;
        }
    }

    // ============================================================
    //                        DRAWING
    // ============================================================
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(20, 10, 25));

        // ---- World pass: everything moves with the camera ----
        _spriteBatch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp,
            transformMatrix: Matrix.CreateTranslation(-_cameraX, 0, 0));
        DrawBackground();
        DrawPlatforms();
        DrawDoor();
        if (_floor == MaxFloor) DrawPrincessBackground();
        DrawCoins();
        DrawPowerUps();
        DrawZombies();
        if (_floor == MaxFloor && _boss.Alive) DrawBoss();
        DrawBossFireballs();
        DrawPlayer();
        DrawAimLine();
        DrawBall();
        DrawExtraBalls();
        DrawParticles();
        DrawFloatTexts();
        _spriteBatch.End();

        // ---- Screen pass: HUD + overlays ----
        _spriteBatch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);
        DrawHud();
        if (_floor == MaxFloor && _boss.Alive) DrawBossHpBar();
        if (_state == State.Title) DrawTitleOverlay();
        else if (_state == State.Dead) DrawDeathOverlay();
        else if (_state == State.Won) DrawWonOverlay();
        else if (_state == State.FloorClear) DrawFloorClearOverlay();
        _spriteBatch.End();

        base.Draw(gameTime);
    }

    private void DrawBackground()
    {
        // Dark stone interior; gets deeper the higher you climb
        float tFloor = MathHelper.Clamp(_floor / (float)MaxFloor, 0f, 1f);
        var bg = Color.Lerp(new Color(60, 50, 70), new Color(20, 10, 30), tFloor);
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, _levelWidth, ScreenH), bg);

        // Brick wall pattern (tile across the whole level)
        int brickW = 60, brickH = 28;
        var brickLight = new Color(70, 55, 80);
        var brickDark  = new Color(35, 28, 50);
        for (int y = CeilingY; y < GroundY; y += brickH)
        {
            int rowOffset = ((y - CeilingY) / brickH) % 2 == 0 ? 0 : brickW / 2;
            for (int x = -brickW; x < _levelWidth + brickW; x += brickW)
            {
                int bx = x + rowOffset;
                _spriteBatch.Draw(_pixel, new Rectangle(bx, y, brickW - 2, brickH - 2), brickLight);
                _spriteBatch.Draw(_pixel, new Rectangle(bx, y + brickH - 4, brickW - 2, 2), brickDark);
                _spriteBatch.Draw(_pixel, new Rectangle(bx + brickW - 4, y, 2, brickH - 2), brickDark);
            }
        }

        // Wood ground
        _spriteBatch.Draw(_pixel, new Rectangle(0, GroundY, _levelWidth, ScreenH - GroundY), new Color(85, 55, 35));
        for (int x = 0; x < _levelWidth; x += 90)
            _spriteBatch.Draw(_pixel, new Rectangle(x, GroundY, 2, ScreenH - GroundY), new Color(50, 30, 20));
        _spriteBatch.Draw(_pixel, new Rectangle(0, GroundY, _levelWidth, 3), new Color(40, 22, 15));

        // Ceiling band
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, _levelWidth, CeilingY), new Color(50, 38, 60));

        // Torches every ~360 px
        for (int tx = 200; tx < _levelWidth; tx += 360)
        {
            DrawTorch(tx, CeilingY + 80);
            DrawTorch(tx + 180, CeilingY + 220);
        }
    }

    private void DrawTorch(int x, int y)
    {
        // ---- Wooden sconce (the wall bracket holding the torch) ----
        // bracket back (mounted on the wall)
        _spriteBatch.Draw(_pixel, new Rectangle(x - 6, y, 12, 4), new Color(50, 32, 20));
        // handle / stick going up out of the bracket
        _spriteBatch.Draw(_pixel, new Rectangle(x - 3, y - 18, 6, 18), new Color(80, 50, 30));
        // dark grain line down the handle
        _spriteBatch.Draw(_pixel, new Rectangle(x - 1, y - 16, 1, 14), new Color(40, 25, 15));
        // metal cap at the top of the handle (where the fire sits)
        _spriteBatch.Draw(_pixel, new Rectangle(x - 5, y - 22, 10, 5), new Color(110, 90, 70));
        _spriteBatch.Draw(_pixel, new Rectangle(x - 5, y - 23, 10, 2), new Color(160, 130, 90));

        // ---- Flame ----
        // Animated flicker affects vertical sway, slight horizontal jitter, and brightness.
        float t = _stateTimer * 7f + x * 0.13f;
        float wob = MathF.Sin(t) * 0.6f + MathF.Sin(t * 2.7f) * 0.4f;        // [-1..1]
        int wobX = (int)(MathF.Sin(t * 1.5f) * 1.2f);
        float flick = 0.85f + 0.15f * MathF.Sin(t * 2.4f);
        int flameBaseY = y - 23; // top of the cap = bottom of the flame
        // Total flame height oscillates slightly (22..28 px)
        int flameH = 24 + (int)(MathF.Sin(t) * 2f);
        int flameMaxW = 12;

        // Outer flame (deep red-orange) — teardrop pointing up
        var outerCol = new Color(220, 80, 30);
        for (int i = 0; i < flameH; i++)
        {
            float ny = i / (float)(flameH - 1);           // 0 top -> 1 bottom
            // width tapers smoothly: 0 at top, max at ~70% down, narrows again near base
            float bell = 1f - MathF.Pow(1f - ny, 2.0f);
            float baseTaper = MathHelper.Clamp(ny * 3f, 0f, 1f);
            int w = (int)(flameMaxW * bell * MathHelper.Lerp(0.55f, 1f, baseTaper));
            int yy = flameBaseY - flameH + i;
            int xx = x + wobX + (int)(wob * (1f - ny) * 1.6f);
            if (w > 0) _spriteBatch.Draw(_pixel, new Rectangle(xx - w / 2, yy, w, 1), outerCol);
        }

        // Mid flame (bright orange) — smaller, offset slightly
        var midCol = new Color(255, 160, 50) * flick;
        int midH = (int)(flameH * 0.78f);
        int midMaxW = (int)(flameMaxW * 0.66f);
        for (int i = 0; i < midH; i++)
        {
            float ny = i / (float)(midH - 1);
            float bell = 1f - MathF.Pow(1f - ny, 2.0f);
            float baseTaper = MathHelper.Clamp(ny * 3f, 0f, 1f);
            int w = (int)(midMaxW * bell * MathHelper.Lerp(0.6f, 1f, baseTaper));
            int yy = flameBaseY - midH + i;
            int xx = x + wobX + (int)(wob * (1f - ny) * 1.2f);
            if (w > 0) _spriteBatch.Draw(_pixel, new Rectangle(xx - w / 2, yy, w, 1), midCol);
        }

        // Hottest core (yellow / white) — small, near the base of the flame
        var coreCol = new Color(255, 230, 140) * flick;
        var hottestCol = new Color(255, 250, 200) * flick;
        int coreH = (int)(flameH * 0.45f);
        int coreMaxW = (int)(flameMaxW * 0.35f);
        for (int i = 0; i < coreH; i++)
        {
            float ny = i / (float)(coreH - 1);
            float bell = 1f - MathF.Pow(1f - ny, 1.8f);
            int w = (int)(coreMaxW * bell);
            int yy = flameBaseY - coreH + i;
            int xx = x + wobX;
            if (w > 0) _spriteBatch.Draw(_pixel, new Rectangle(xx - w / 2, yy, w, 1), coreCol);
            // tiny white-hot spot near the base
            if (ny > 0.65f && w > 0)
                _spriteBatch.Draw(_pixel, new Rectangle(xx - Math.Max(1, w / 3), yy, Math.Max(2, w / 2), 1), hottestCol);
        }

        // Halo glow surrounding the flame (subtle warm light)
        _spriteBatch.Draw(_circle32, new Vector2(x, flameBaseY - flameH / 2), null,
            new Color(255, 150, 60) * 0.20f, 0f, new Vector2(16, 16), 4.5f, SpriteEffects.None, 0f);

        // Tiny ember sparks that drift up occasionally (deterministic stagger by x)
        for (int i = 0; i < 2; i++)
        {
            float phase = (_stateTimer * 1.6f + i * 0.5f + x * 0.011f) % 1f;
            int ex = x + wobX + (int)(MathF.Sin((phase + i) * 8f) * 4f);
            int ey = flameBaseY - flameH - (int)(phase * 14f);
            float ea = (1f - phase);
            _spriteBatch.Draw(_pixel, new Rectangle(ex, ey, 2, 2), new Color(255, 200, 100) * ea);
        }
    }

    private void DrawPlatforms()
    {
        var top   = new Color(150, 100, 60);
        var mid   = new Color(110, 70, 40);
        var bot   = new Color(70, 45, 25);
        var edge  = new Color(35, 22, 12);
        foreach (var p in _platforms)
        {
            // wooden plank: lighter top, darker bottom, edge outline
            _spriteBatch.Draw(_pixel, new Rectangle(p.X, p.Y, p.Width, p.Height), mid);
            _spriteBatch.Draw(_pixel, new Rectangle(p.X, p.Y, p.Width, 4), top);
            _spriteBatch.Draw(_pixel, new Rectangle(p.X, p.Y + p.Height - 4, p.Width, 4), bot);
            // outline
            _spriteBatch.Draw(_pixel, new Rectangle(p.X, p.Y, p.Width, 1), edge);
            _spriteBatch.Draw(_pixel, new Rectangle(p.X, p.Y + p.Height - 1, p.Width, 1), edge);
            _spriteBatch.Draw(_pixel, new Rectangle(p.X, p.Y, 1, p.Height), edge);
            _spriteBatch.Draw(_pixel, new Rectangle(p.X + p.Width - 1, p.Y, 1, p.Height), edge);
            // plank seams
            for (int sx = p.X + 30; sx < p.X + p.Width - 5; sx += 30)
                _spriteBatch.Draw(_pixel, new Rectangle(sx, p.Y + 2, 1, p.Height - 4), edge);
        }
    }

    private void DrawDoor()
    {
        if (_door.Width <= 0) return;

        // Stone arched frame around the door
        var stoneOuter = new Color(70, 64, 80);
        var stoneInner = new Color(95, 88, 105);
        int frameThick = 8;
        int archR = _door.Width / 2 + frameThick;
        int archCx = _door.X + _door.Width / 2;
        int archCy = _door.Y; // arch curves above the door top

        // Frame side columns
        _spriteBatch.Draw(_pixel, new Rectangle(_door.X - frameThick, _door.Y, frameThick, _door.Height), stoneOuter);
        _spriteBatch.Draw(_pixel, new Rectangle(_door.X + _door.Width, _door.Y, frameThick, _door.Height), stoneOuter);
        // Stone block highlights on the columns
        for (int by = _door.Y + 8; by < _door.Y + _door.Height; by += 20)
        {
            _spriteBatch.Draw(_pixel, new Rectangle(_door.X - frameThick, by, frameThick, 2), stoneInner);
            _spriteBatch.Draw(_pixel, new Rectangle(_door.X + _door.Width, by, frameThick, 2), stoneInner);
        }
        // Arched stone top (drawn as rows of pixels with arc width)
        for (int dy = 0; dy < archR; dy++)
        {
            float t = dy / (float)archR;
            int halfW = (int)MathF.Sqrt(MathF.Max(0, archR * archR - dy * dy));
            int rowY = archCy - dy - 1;
            _spriteBatch.Draw(_pixel, new Rectangle(archCx - halfW, rowY, halfW * 2, 1), stoneOuter);
        }
        // Carve the inner door opening out of the arch
        int innerR = _door.Width / 2;
        for (int dy = 0; dy < innerR; dy++)
        {
            int halfW = (int)MathF.Sqrt(MathF.Max(0, innerR * innerR - dy * dy));
            int rowY = archCy - dy - 1;
            _spriteBatch.Draw(_pixel, new Rectangle(archCx - halfW, rowY, halfW * 2, 1), new Color(20, 12, 18));
        }

        // ---- Wooden door body ----
        var woodBase = _doorOpen ? new Color(180, 130, 70) : new Color(80, 55, 35);
        var woodDark = _doorOpen ? new Color(140, 95, 50) : new Color(50, 32, 20);
        _spriteBatch.Draw(_pixel, _door, woodBase);
        // Arched top of the door body
        for (int dy = 0; dy < innerR; dy++)
        {
            int halfW = (int)MathF.Sqrt(MathF.Max(0, innerR * innerR - dy * dy));
            _spriteBatch.Draw(_pixel, new Rectangle(archCx - halfW, archCy - dy - 1, halfW * 2, 1), woodBase);
        }
        // Vertical wood planks (5 planks across)
        int plankW = _door.Width / 5;
        for (int i = 1; i < 5; i++)
        {
            int px = _door.X + i * plankW;
            _spriteBatch.Draw(_pixel, new Rectangle(px, _door.Y - innerR + 8, 1, _door.Height + innerR - 8), woodDark);
        }
        // Horizontal iron bands
        var iron = new Color(40, 38, 50);
        var ironLight = new Color(85, 80, 95);
        int[] bandYs = { _door.Y + 14, _door.Y + _door.Height / 2 - 7, _door.Y + _door.Height - 22 };
        foreach (int by in bandYs)
        {
            _spriteBatch.Draw(_pixel, new Rectangle(_door.X, by, _door.Width, 7), iron);
            _spriteBatch.Draw(_pixel, new Rectangle(_door.X, by, _door.Width, 1), ironLight); // top highlight
            // Iron studs along the band
            for (int sx = _door.X + 6; sx < _door.X + _door.Width - 4; sx += 14)
            {
                _spriteBatch.Draw(_circle64Hard, new Vector2(sx, by + 3), null, ironLight, 0f, new Vector2(32, 32), 5f / 64f, SpriteEffects.None, 0f);
                _spriteBatch.Draw(_circle64Hard, new Vector2(sx, by + 3), null, iron,      0f, new Vector2(32, 32), 3f / 64f, SpriteEffects.None, 0f);
            }
        }

        // Big iron knocker ring + plate in the upper-middle
        int knX = archCx;
        int knY = _door.Y + _door.Height / 2 + 18;
        _spriteBatch.Draw(_circle64Hard, new Vector2(knX, knY), null, ironLight, 0f, new Vector2(32, 32), 14f / 64f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, new Vector2(knX, knY), null, iron,      0f, new Vector2(32, 32), 12f / 64f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, new Vector2(knX, knY), null, woodBase,  0f, new Vector2(32, 32), 8f  / 64f, SpriteEffects.None, 0f);
        // ring at the bottom of the plate
        _spriteBatch.Draw(_circle64Hard, new Vector2(knX, knY + 12), null, ironLight, 0f, new Vector2(32, 32), 8f / 64f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, new Vector2(knX, knY + 12), null, iron,      0f, new Vector2(32, 32), 6f / 64f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, new Vector2(knX, knY + 12), null, woodBase,  0f, new Vector2(32, 32), 3f / 64f, SpriteEffects.None, 0f);

        // Glow when open
        if (_doorOpen)
        {
            float pulse = 0.6f + 0.4f * MathF.Sin(_stateTimer * 5f);
            _spriteBatch.Draw(_circle32, new Vector2(archCx, _door.Y + _door.Height / 2f), null,
                new Color(255, 220, 130) * (0.55f * pulse), 0f, new Vector2(16, 16), 6f, SpriteEffects.None, 0f);
        }
    }

    private void DrawPrincessBackground()
    {
        // Centered in the boss arena
        int cx = ScreenW / 2 + 220;
        int cy = CeilingY + 130;
        int cw = 90, ch = 130;
        _spriteBatch.Draw(_pixel, new Rectangle(cx - cw / 2 - 4, cy - 6, cw + 8, ch + 12), new Color(20, 12, 18));
        _spriteBatch.Draw(_pixel, new Rectangle(cx - cw / 2, cy, cw, ch), new Color(40, 25, 35));
        for (int bx = cx - cw / 2; bx <= cx + cw / 2; bx += 12)
            _spriteBatch.Draw(_pixel, new Rectangle(bx, cy, 3, ch), new Color(80, 60, 90));
        _spriteBatch.Draw(_pixel, new Rectangle(cx - cw / 2, cy, cw, 4), new Color(80, 60, 90));
        _spriteBatch.Draw(_pixel, new Rectangle(cx - cw / 2, cy + ch - 4, cw, 4), new Color(80, 60, 90));
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 20, cy + 50, 40, 60), new Color(255, 140, 200));
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 14, cy + 30, 28, 30), new Color(255, 200, 220));
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 18, cy + 22, 36, 18), new Color(250, 220, 130));
        _spriteBatch.Draw(_circle32, new Vector2(cx, cy + 32), null, new Color(255, 220, 200), 0f, new Vector2(16, 16), 0.55f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 10, cy + 18, 20, 5), new Color(255, 215, 60));
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 9, cy + 14, 4, 4), new Color(255, 215, 60));
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 2, cy + 13, 4, 5), new Color(255, 215, 60));
        _spriteBatch.Draw(_pixel, new Rectangle(cx + 5, cy + 14, 4, 4), new Color(255, 215, 60));
    }

    private void DrawZombies()
    {
        foreach (var z in _zombies)
        {
            float w = ZombieWidth(z.Kind), h = ZombieHeight(z.Kind);
            int x = (int)(z.Pos.X - w / 2);
            int y = (int)(z.Pos.Y - h);

            _spriteBatch.Draw(_pixel, new Rectangle(x - 4, (int)z.Pos.Y - 4, (int)w + 8, 6), new Color(0, 0, 0) * 0.35f);

            var skin = z.Kind switch
            {
                ZombieKind.Brute  => new Color(70, 110, 70),
                ZombieKind.Runner => new Color(110, 140, 80),
                _ => new Color(95, 130, 80)
            };
            var shirt = z.Kind switch
            {
                ZombieKind.Brute  => new Color(60, 50, 40),
                ZombieKind.Runner => new Color(150, 50, 50),
                _ => new Color(80, 60, 40)
            };

            int legW = (int)(w * 0.32f);
            int legH = (int)(h * 0.32f);
            _spriteBatch.Draw(_pixel, new Rectangle(x + 4, y + (int)h - legH, legW, legH), new Color(50, 35, 25));
            _spriteBatch.Draw(_pixel, new Rectangle(x + (int)w - legW - 4, y + (int)h - legH, legW, legH), new Color(50, 35, 25));
            int torsoY = y + (int)(h * 0.35f);
            int torsoH = (int)(h * 0.36f);
            _spriteBatch.Draw(_pixel, new Rectangle(x, torsoY, (int)w, torsoH), shirt);
            _spriteBatch.Draw(_pixel, new Rectangle(x + 2, torsoY + torsoH - 6, (int)w - 4, 6), new Color(40, 25, 15));
            int armW = (int)(w * 0.20f);
            int armH = (int)(h * 0.30f);
            float armOff = (_playerPos.X < z.Pos.X ? -1 : 1) * 4f;
            _spriteBatch.Draw(_pixel, new Rectangle(x - 2 + (int)armOff, torsoY + 4, armW, armH), shirt);
            _spriteBatch.Draw(_pixel, new Rectangle(x + (int)w - armW + 2 + (int)armOff, torsoY + 4, armW, armH), shirt);
            _spriteBatch.Draw(_pixel, new Rectangle(x - 2 + (int)armOff, torsoY + 4 + armH - 4, armW, 6), skin);
            _spriteBatch.Draw(_pixel, new Rectangle(x + (int)w - armW + 2 + (int)armOff, torsoY + 4 + armH - 4, armW, 6), skin);
            int headD = (int)(w * 0.78f);
            _spriteBatch.Draw(_pixel, new Rectangle(x + (int)w / 2 - headD / 2, y, headD, (int)(h * 0.36f)), skin);
            int eyeY = y + (int)(h * 0.13f);
            int eyeSize = z.Kind == ZombieKind.Brute ? 4 : 3;
            int eyeOff = (int)(w * 0.18f);
            _spriteBatch.Draw(_pixel, new Rectangle(x + (int)w / 2 - eyeOff - eyeSize / 2, eyeY, eyeSize, eyeSize), new Color(255, 60, 60));
            _spriteBatch.Draw(_pixel, new Rectangle(x + (int)w / 2 + eyeOff - eyeSize / 2, eyeY, eyeSize, eyeSize), new Color(255, 60, 60));
            int mouthY = y + (int)(h * 0.24f);
            _spriteBatch.Draw(_pixel, new Rectangle(x + (int)w / 2 - 5, mouthY, 10, 3), new Color(40, 10, 10));

            if (z.HitFlash > 0.05f)
                _spriteBatch.Draw(_pixel, new Rectangle(x - 2, y - 2, (int)w + 4, (int)h + 4), Color.White * (z.HitFlash * 0.45f));

            if (z.OnFireTime > 0f)
            {
                for (int i = 0; i < 3; i++)
                {
                    float fx = z.Pos.X + ((float)_rng.NextDouble() - 0.5f) * w;
                    float fy = z.Pos.Y - h * (0.2f + (float)_rng.NextDouble() * 0.7f);
                    _spriteBatch.Draw(_circle32, new Vector2(fx, fy), null, new Color(255, 140, 60) * 0.7f, 0f, new Vector2(16, 16), 0.6f, SpriteEffects.None, 0f);
                }
            }

            if (z.MaxHp > 1)
            {
                int pipsY = y - 10;
                for (int i = 0; i < z.MaxHp; i++)
                {
                    int px = (int)(z.Pos.X - (z.MaxHp * 8 - 2) / 2 + i * 8);
                    _spriteBatch.Draw(_pixel, new Rectangle(px, pipsY, 6, 4), i < z.Hp ? new Color(120, 230, 120) : new Color(50, 50, 60));
                }
            }
        }
    }

    private void DrawPlayer()
    {
        int x = (int)(_playerPos.X - PlayerW / 2);
        int y = (int)(_playerPos.Y - PlayerH);

        _spriteBatch.Draw(_pixel, new Rectangle(x - 4, (int)_playerPos.Y - 4, PlayerW + 8, 6), new Color(0, 0, 0) * 0.4f);

        bool show = _iFrames <= 0f || ((int)(_iFrames * 18f) % 2 == 0);
        if (!show) return;

        int legW = 10, legH = 22;
        _spriteBatch.Draw(_pixel, new Rectangle(x + 4, y + PlayerH - legH, legW, legH), new Color(40, 30, 20));
        _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW - legW - 4, y + PlayerH - legH, legW, legH), new Color(40, 30, 20));
        _spriteBatch.Draw(_pixel, new Rectangle(x + 4, y + PlayerH - legH, legW, 6), Color.White);
        _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW - legW - 4, y + PlayerH - legH, legW, 6), Color.White);
        _spriteBatch.Draw(_pixel, new Rectangle(x + 2, y + (int)(PlayerH * 0.55f), PlayerW - 4, (int)(PlayerH * 0.18f)), Color.White);
        _spriteBatch.Draw(_pixel, new Rectangle(x + 2, y + (int)(PlayerH * 0.25f), PlayerW - 4, (int)(PlayerH * 0.32f)), new Color(220, 60, 60));
        _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW / 2 - 2, y + (int)(PlayerH * 0.35f), 4, 8), Color.White);
        int armW = 7, armH = 22;
        _spriteBatch.Draw(_pixel, new Rectangle(x - 2, y + (int)(PlayerH * 0.27f), armW, armH), new Color(220, 60, 60));
        _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW - armW + 2, y + (int)(PlayerH * 0.27f), armW, armH), new Color(220, 60, 60));
        _spriteBatch.Draw(_pixel, new Rectangle(x - 2, y + (int)(PlayerH * 0.27f) + armH - 6, armW, 6), new Color(240, 200, 170));
        _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW - armW + 2, y + (int)(PlayerH * 0.27f) + armH - 6, armW, 6), new Color(240, 200, 170));
        int headD = (int)(PlayerW * 0.78f);
        _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW / 2 - headD / 2, y, headD, (int)(PlayerH * 0.28f)), new Color(240, 200, 170));
        _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW / 2 - headD / 2, y, headD, 6), new Color(80, 50, 30));
        int eyeY = y + 10;
        if (_facing > 0)
        {
            _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW / 2 - 2, eyeY, 2, 2), new Color(30, 30, 40));
            _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW / 2 + 6, eyeY, 2, 2), new Color(30, 30, 40));
        }
        else
        {
            _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW / 2 - 8, eyeY, 2, 2), new Color(30, 30, 40));
            _spriteBatch.Draw(_pixel, new Rectangle(x + PlayerW / 2, eyeY, 2, 2), new Color(30, 30, 40));
        }

        if (_speedBoostTime > 0f)
            _spriteBatch.Draw(_circle32, _playerPos, null, new Color(120, 230, 255) * 0.35f, 0f, new Vector2(16, 16), 2.5f, SpriteEffects.None, 0f);

        if (_megaKickArmedFor > 0f)
            _spriteBatch.Draw(_circle32, _playerPos + new Vector2(0, -PlayerH / 2f), null, new Color(255, 100, 100) * 0.35f, 0f, new Vector2(16, 16), 3.0f, SpriteEffects.None, 0f);
    }

    private void DrawAimLine()
    {
        if (!_ballHeld || _state != State.Playing) return;
        var mouse = Mouse.GetState();
        Vector2 mouseWorld = new Vector2(mouse.X + _cameraX, mouse.Y);
        Vector2 aim = mouseWorld - _ballPos;
        if (aim.LengthSquared() < 1f) return;
        aim.Normalize();
        if (aim.Y > -0.15f) aim.Y = -0.15f;
        aim.Normalize();

        // Very short directional preview — just a hint of angle. No landing reticle, so
        // where the ball ends up is up to your gut.
        float kickPower = (_megaKickArmedFor > 0f) ? 1180f : 820f;
        Vector2 simPos = _ballPos;
        Vector2 simVel = aim * kickPower;
        float simDt = 1f / 40f;
        const int Steps = 25;
        Color baseCol = (_megaKickArmedFor > 0f) ? new Color(255, 110, 110) : new Color(255, 220, 130);
        for (int i = 1; i <= Steps; i++)
        {
            simVel.Y += BallGravity * simDt;
            simPos += simVel * simDt;
            float a = 1f - (i - 1) / (float)Steps;
            a *= a;
            int sz = (i == 1) ? 5 : 3;
            _spriteBatch.Draw(_pixel, new Rectangle((int)simPos.X - sz / 2, (int)simPos.Y - sz / 2, sz, sz),
                baseCol * (0.75f * a));
        }
    }

    private void DrawBall() => DrawBallAt(_ballPos, _ballRotation, _ballFireTime > 0f);
    private void DrawBallAt(Vector2 pos, float rotation, bool fire)
    {
        if (fire)
        {
            float wob = 0.85f + 0.15f * MathF.Sin(_stateTimer * 20f);
            _spriteBatch.Draw(_circle32, pos, null, new Color(255, 140, 60) * (0.55f * wob), 0f, new Vector2(16, 16), 1.5f, SpriteEffects.None, 0f);
        }
        _spriteBatch.Draw(_circle64Hard, pos, null, new Color(20, 20, 25), rotation, new Vector2(32, 32), (BallR * 2f + 4f) / 64f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, pos, null, Color.White, rotation, new Vector2(32, 32), (BallR * 2f) / 64f, SpriteEffects.None, 0f);
        int blobs = 5;
        for (int i = 0; i < blobs; i++)
        {
            float a = rotation + i * MathF.Tau / blobs;
            Vector2 off = new Vector2(MathF.Cos(a), MathF.Sin(a)) * BallR * 0.55f;
            _spriteBatch.Draw(_circle64Hard, pos + off, null, new Color(20, 20, 25), rotation, new Vector2(32, 32), 4f / 64f, SpriteEffects.None, 0f);
        }
        _spriteBatch.Draw(_circle64Hard, pos, null, new Color(20, 20, 25), rotation, new Vector2(32, 32), 4f / 64f, SpriteEffects.None, 0f);
    }

    private void DrawExtraBalls()
    {
        foreach (var b in _extraBalls) DrawBallAt(b.Pos, b.Rotation, b.Fire);
    }

    private void DrawCoins()
    {
        foreach (var c in _coins)
        {
            float scaleX = 0.55f + MathF.Abs(MathF.Cos(c.Spin)) * 0.45f;
            // outer glow
            _spriteBatch.Draw(_circle32, c.Pos, null, new Color(255, 215, 80) * 0.35f, 0f, new Vector2(16, 16), 1.2f, SpriteEffects.None, 0f);
            // outline ring
            _spriteBatch.Draw(_circle64Hard, c.Pos, null, new Color(150, 100, 20), 0f, new Vector2(32, 32),
                new Vector2(scaleX * 14f / 64f, 14f / 64f), SpriteEffects.None, 0f);
            // gold body
            _spriteBatch.Draw(_circle64Hard, c.Pos, null, new Color(255, 200, 60), 0f, new Vector2(32, 32),
                new Vector2(scaleX * 11f / 64f, 11f / 64f), SpriteEffects.None, 0f);
            // bright highlight
            _spriteBatch.Draw(_circle64Hard, c.Pos + new Vector2(-1f, -1f), null, new Color(255, 240, 180), 0f, new Vector2(32, 32),
                new Vector2(scaleX * 5f / 64f, 5f / 64f), SpriteEffects.None, 0f);
        }
    }

    private void DrawPowerUps()
    {
        foreach (var p in _powerUps)
        {
            float pulse = 0.85f + 0.15f * MathF.Sin(_stateTimer * 6f);
            float bob = MathF.Sin(_stateTimer * 4f + p.Pos.X * 0.05f) * 2f;
            Color baseCol = p.Kind switch
            {
                PowerUpKind.FireBall => new Color(255, 140, 60),
                PowerUpKind.MultiBall => new Color(255, 220, 120),
                PowerUpKind.MegaKick => new Color(255, 90, 90),
                PowerUpKind.SpeedBoost => new Color(120, 230, 255),
                PowerUpKind.Heart => new Color(255, 110, 140),
                _ => Color.White
            };

            // Glowing badge backdrop (dark rounded disc with a bright ring)
            int badgeR = 18;
            _spriteBatch.Draw(_circle32, p.Pos, null, baseCol * (0.45f * pulse), 0f, new Vector2(16, 16), 2.2f, SpriteEffects.None, 0f);
            _spriteBatch.Draw(_circle64Hard, p.Pos + new Vector2(0, bob), null, baseCol, 0f, new Vector2(32, 32), (badgeR * 2f) / 64f, SpriteEffects.None, 0f);
            _spriteBatch.Draw(_circle64Hard, p.Pos + new Vector2(0, bob), null, new Color(25, 18, 30), 0f, new Vector2(32, 32), (badgeR * 2f - 6f) / 64f, SpriteEffects.None, 0f);

            int sx = (int)p.Pos.X, sy = (int)(p.Pos.Y + bob);

            switch (p.Kind)
            {
                case PowerUpKind.FireBall:
                    // Proper flame icon: teardrop with bright core
                    DrawSmallFlame(sx, sy + 5, baseCol);
                    break;

                case PowerUpKind.MultiBall:
                    // Three little soccer-ish balls in a triangle
                    DrawMiniBall(sx - 6, sy + 3);
                    DrawMiniBall(sx + 6, sy + 3);
                    DrawMiniBall(sx,     sy - 5);
                    break;

                case PowerUpKind.MegaKick:
                    // Lightning bolt icon (zigzag pixels)
                    DrawLightningBolt(sx, sy, Color.White);
                    break;

                case PowerUpKind.SpeedBoost:
                    // Double-chevron arrow pointing right
                    DrawSpeedChevrons(sx, sy, Color.White);
                    break;

                case PowerUpKind.Heart:
                    DrawHeartIcon(sx, sy, 16, Color.White);
                    break;
            }
        }
    }

    private void DrawSmallFlame(int cx, int cy, Color outerCol)
    {
        // Mini teardrop flame, 14 px tall, drawn upward from (cx, cy).
        int flameH = 14;
        int maxW = 8;
        for (int i = 0; i < flameH; i++)
        {
            float ny = i / (float)(flameH - 1);
            float bell = 1f - MathF.Pow(1f - ny, 2.0f);
            float baseTaper = MathHelper.Clamp(ny * 3f, 0f, 1f);
            int w = (int)(maxW * bell * MathHelper.Lerp(0.55f, 1f, baseTaper));
            int yy = cy - flameH + i;
            if (w > 0) _spriteBatch.Draw(_pixel, new Rectangle(cx - w / 2, yy, w, 1), outerCol);
        }
        // Inner bright yellow core
        int innerH = 8;
        int innerMaxW = 4;
        var inner = new Color(255, 230, 140);
        for (int i = 0; i < innerH; i++)
        {
            float ny = i / (float)(innerH - 1);
            float bell = 1f - MathF.Pow(1f - ny, 2.0f);
            int w = (int)(innerMaxW * bell);
            int yy = cy - innerH + i;
            if (w > 0) _spriteBatch.Draw(_pixel, new Rectangle(cx - w / 2, yy, w, 1), inner);
        }
    }

    private void DrawMiniBall(int cx, int cy)
    {
        _spriteBatch.Draw(_circle64Hard, new Vector2(cx, cy), null, new Color(25, 18, 30), 0f, new Vector2(32, 32), 8f / 64f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, new Vector2(cx, cy), null, Color.White, 0f, new Vector2(32, 32), 6f / 64f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 1, cy - 1, 2, 2), new Color(25, 18, 30));
    }

    private void DrawLightningBolt(int cx, int cy, Color color)
    {
        // Approximate a zigzag bolt with 3 rectangle segments.
        // Top-right slash -> mid-left -> bottom-right point.
        // Each rectangle is angled visually by stacking small horizontal pixel strips.
        // Strip 1: upper-right diagonal
        for (int i = 0; i < 6; i++)
        {
            int x = cx + 2 - i;
            int y = cy - 8 + i;
            _spriteBatch.Draw(_pixel, new Rectangle(x, y, 3, 1), color);
        }
        // Strip 2: lower-left jag
        for (int i = 0; i < 5; i++)
        {
            int x = cx - 3 + i;
            int y = cy - 3 + i;
            _spriteBatch.Draw(_pixel, new Rectangle(x, y, 3, 1), color);
        }
        // Strip 3: lower diagonal to bottom-right
        for (int i = 0; i < 6; i++)
        {
            int x = cx - 1 + i;
            int y = cy + 2 + i;
            _spriteBatch.Draw(_pixel, new Rectangle(x, y, 2, 1), color);
        }
    }

    private void DrawSpeedChevrons(int cx, int cy, Color color)
    {
        // Two right-pointing chevrons stacked
        for (int row = 0; row < 2; row++)
        {
            int yBase = cy - 4 + row * 8;
            // Top half of the chevron
            for (int i = 0; i < 4; i++)
                _spriteBatch.Draw(_pixel, new Rectangle(cx - 6 + i, yBase + i, 3, 1), color);
            // Bottom half
            for (int i = 0; i < 4; i++)
                _spriteBatch.Draw(_pixel, new Rectangle(cx - 6 + i, yBase + 6 - i, 3, 1), color);
        }
    }

    private void DrawBoss()
    {
        int cx = (int)_boss.Pos.X;
        int cy = (int)_boss.Pos.Y;
        _spriteBatch.Draw(_circle32, new Vector2(cx, GroundY - 4), null, new Color(0, 0, 0) * 0.35f, 0f, new Vector2(16, 16), 4f, SpriteEffects.None, 0f);
        float pulse = 0.85f + 0.15f * MathF.Sin(_stateTimer * 3f);
        _spriteBatch.Draw(_circle32, _boss.Pos, null, new Color(255, 60, 60) * (0.35f * pulse), 0f, new Vector2(16, 16), 7f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, _boss.Pos, null, new Color(80, 40, 90), 0f, new Vector2(32, 32), 130f / 64f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, _boss.Pos + new Vector2(0, -8), null, new Color(110, 50, 130), 0f, new Vector2(32, 32), 110f / 64f, SpriteEffects.None, 0f);
        for (int s = -1; s <= 1; s += 2)
        {
            int hx = cx + s * 50;
            int hy = cy - 40;
            for (int i = 0; i < 14; i++)
            {
                float t = i / 13f;
                int sx = hx + s * (int)(t * 10f);
                int sy = hy - i * 3;
                int w = (int)((1f - t) * 10);
                _spriteBatch.Draw(_pixel, new Rectangle(sx, sy, w, 3), new Color(40, 20, 30));
            }
        }
        int eyeY = cy - 16;
        _spriteBatch.Draw(_circle32, new Vector2(cx - 20, eyeY), null, new Color(255, 60, 60), 0f, new Vector2(16, 16), 0.6f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle32, new Vector2(cx + 20, eyeY), null, new Color(255, 60, 60), 0f, new Vector2(16, 16), 0.6f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 22, eyeY - 1, 4, 4), new Color(50, 0, 0));
        _spriteBatch.Draw(_pixel, new Rectangle(cx + 18, eyeY - 1, 4, 4), new Color(50, 0, 0));
        _spriteBatch.Draw(_pixel, new Rectangle(cx - 26, cy + 12, 52, 6), new Color(20, 0, 0));
        for (int i = 0; i < 7; i++)
        {
            int tx = cx - 24 + i * 8;
            _spriteBatch.Draw(_pixel, new Rectangle(tx, cy + 12, 4, 6), Color.White);
        }
        if (_boss.HitFlash > 0.05f)
            _spriteBatch.Draw(_circle64Hard, _boss.Pos, null, Color.White * (_boss.HitFlash * 0.5f), 0f, new Vector2(32, 32), 130f / 64f, SpriteEffects.None, 0f);
    }

    private void DrawBossHpBar()
    {
        int bw = 700, bh = 14;
        int bx = (ScreenW - bw) / 2;
        int by = 60;
        _spriteBatch.Draw(_pixel, new Rectangle(bx - 2, by - 2, bw + 4, bh + 4), new Color(15, 15, 20));
        _spriteBatch.Draw(_pixel, new Rectangle(bx, by, bw, bh), new Color(40, 20, 30));
        float frac = MathHelper.Clamp(_boss.Hp / (float)_boss.MaxHp, 0f, 1f);
        _spriteBatch.Draw(_pixel, new Rectangle(bx, by, (int)(bw * frac), bh), new Color(255, 80, 80));
        DrawTextBlocks("DEMON LORD", new Vector2(ScreenW / 2f, by - 18), 3, Color.White);
    }

    private void DrawBossFireballs()
    {
        foreach (var f in _bossFireballs)
        {
            _spriteBatch.Draw(_circle32, f.Pos, null, new Color(255, 100, 40) * 0.55f, 0f, new Vector2(16, 16), 1.4f, SpriteEffects.None, 0f);
            _spriteBatch.Draw(_circle32, f.Pos, null, new Color(255, 200, 120), 0f, new Vector2(16, 16), 0.6f, SpriteEffects.None, 0f);
            _spriteBatch.Draw(_circle32, f.Pos, null, Color.White, 0f, new Vector2(16, 16), 0.3f, SpriteEffects.None, 0f);
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

    private void DrawFloatTexts()
    {
        foreach (var ft in _floatTexts)
        {
            float a = MathHelper.Clamp(ft.Life / ft.MaxLife, 0f, 1f);
            DrawTextBlocks(ft.Text, ft.Pos, ft.PixelSize, ft.Color * a);
        }
    }

    private void DrawHud()
    {
        // HP hearts top-left — clean icon shape (two bumps + V-point), uniformly spaced
        for (int i = 0; i < _maxHp; i++)
        {
            int hx = 40 + i * 30;
            int hy = 40;
            bool full = i < _hp;
            var fill = full ? new Color(245, 70, 90) : new Color(55, 55, 65);
            var outline = full ? new Color(120, 10, 30) : new Color(28, 28, 35);
            // outline first, slightly larger
            DrawHeartIcon(hx, hy, 20, outline);
            DrawHeartIcon(hx, hy, 16, fill);
            // small white shine on the upper-left bump when full
            if (full)
                _spriteBatch.Draw(_pixel, new Rectangle(hx - 5, hy - 4, 3, 2), new Color(255, 200, 210));
        }

        DrawTextBlocks("SCORE", new Vector2(ScreenW - 110, 22), 3, new Color(220, 230, 255));
        DrawNumber(_score, new Vector2(ScreenW - 130, 38), 5, Color.White);

        // Floor indicator under the score
        DrawTextBlocks($"FLOOR {_floor}", new Vector2(ScreenW - 110, 84), 3, new Color(220, 180, 240));

        // Active power-up icons top-left under hearts
        int pux = 30, puy = 80;
        if (_ballFireTime > 0f)      { DrawPowerUpIcon(pux, puy, new Color(255, 140, 60), _ballFireTime / 10f); pux += 44; }
        if (_megaKickArmedFor > 0f)  { DrawPowerUpIcon(pux, puy, new Color(255, 90, 90),  _megaKickArmedFor / 6f); pux += 44; }
        if (_speedBoostTime > 0f)    { DrawPowerUpIcon(pux, puy, new Color(120, 230, 255), _speedBoostTime / 8f); pux += 44; }
        if (_multiBallArmedFor > 0f) { DrawPowerUpIcon(pux, puy, new Color(255, 220, 120), _multiBallArmedFor / 8f); }

        // Door open hint
        if (_doorOpen && _floor < MaxFloor)
        {
            float pulse = 0.6f + 0.4f * MathF.Sin(_stateTimer * 5f);
            DrawTextBlocks("HEAD TO THE DOOR", new Vector2(ScreenW / 2f, 30), 3, new Color(255, 220, 120) * pulse);
        }
    }

    private void DrawHeartIcon(int cx, int cy, int size, Color color)
    {
        // size = total width/height of the heart icon, in pixels.
        // Built from two top bumps (circles) and a V-shape converging to a point.
        int bumpR = Math.Max(2, (int)(size * 0.30f));
        int bumpDx = bumpR;
        int bumpY = cy - size / 4;

        // Two top bumps
        _spriteBatch.Draw(_circle64Hard, new Vector2(cx - bumpDx, bumpY), null, color, 0f, new Vector2(32, 32),
            (bumpR * 2f) / 64f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle64Hard, new Vector2(cx + bumpDx, bumpY), null, color, 0f, new Vector2(32, 32),
            (bumpR * 2f) / 64f, SpriteEffects.None, 0f);

        // V-shape: full width at bump level, tapers to a single-pixel point at the bottom
        int vTop = bumpY;
        int vBot = cy + size / 2;
        int leftStart = cx - bumpDx - bumpR;
        int rightStart = cx + bumpDx + bumpR;
        int vHeight = Math.Max(1, vBot - vTop);
        for (int i = 0; i < vHeight; i++)
        {
            float t = i / (float)(vHeight - 1);
            int leftX = (int)MathHelper.Lerp(leftStart, cx, t);
            int rightX = (int)MathHelper.Lerp(rightStart, cx, t);
            _spriteBatch.Draw(_pixel, new Rectangle(leftX, vTop + i, Math.Max(1, rightX - leftX), 1), color);
        }
    }

    private void DrawPowerUpIcon(int x, int y, Color c, float frac)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(x - 2, y - 2, 36, 36), new Color(20, 20, 30));
        _spriteBatch.Draw(_pixel, new Rectangle(x, y, 32, 32), c);
        _spriteBatch.Draw(_pixel, new Rectangle(x, y + 28, (int)(32 * MathHelper.Clamp(frac, 0f, 1f)), 4), Color.White);
    }

    private void DrawTitleOverlay()
    {
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), Color.Black * 0.55f);
        DrawTextBlocks("KICK OF THE DEAD", new Vector2(ScreenW / 2f, 110), 8, new Color(255, 200, 90));
        DrawTextBlocks("RESCUE THE PRINCESS", new Vector2(ScreenW / 2f, 180), 4, new Color(255, 140, 200));
        DrawTextBlocks("A D MOVE   SPACE JUMP", new Vector2(ScreenW / 2f, 250), 3, Color.White * 0.9f);
        DrawTextBlocks("CLICK OR F TO KICK   AIM WITH MOUSE", new Vector2(ScreenW / 2f, 285), 3, Color.White * 0.9f);
        DrawTextBlocks("KILL ZOMBIES FOR COINS", new Vector2(ScreenW / 2f, 320), 3, new Color(255, 220, 80));
        float blink = 0.6f + 0.4f * MathF.Sin(_stateTimer * 4f);
        DrawTextBlocks("PRESS SPACE", new Vector2(ScreenW / 2f, 430), 5, Color.White * blink);
    }

    private void DrawDeathOverlay()
    {
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), Color.Black * 0.55f);
        DrawTextBlocks("THE PRINCESS WAITS", new Vector2(ScreenW / 2f, 130), 5, new Color(255, 140, 200));
        DrawTextBlocks("GAME OVER", new Vector2(ScreenW / 2f, 210), 9, new Color(255, 80, 80));
        DrawTextBlocks("SCORE", new Vector2(ScreenW / 2f - 90, 300), 3, Color.White);
        DrawNumber(_score, new Vector2(ScreenW / 2f + 0, 292), 5, Color.White);
        DrawTextBlocks("BEST", new Vector2(ScreenW / 2f - 90, 350), 3, Color.White * 0.7f);
        DrawNumber(_highScore, new Vector2(ScreenW / 2f + 0, 342), 4, new Color(200, 220, 255));
        if (_stateTimer > 0.7f)
        {
            float blink = 0.6f + 0.4f * MathF.Sin(_stateTimer * 5f);
            DrawTextBlocks("SPACE TO RETRY", new Vector2(ScreenW / 2f, 450), 4, Color.White * blink);
        }
    }

    private void DrawWonOverlay()
    {
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), Color.Black * 0.55f);
        DrawTextBlocks("VICTORY", new Vector2(ScreenW / 2f, 120), 10, new Color(255, 220, 90));
        DrawTextBlocks("THE PRINCESS IS SAVED", new Vector2(ScreenW / 2f, 210), 4, new Color(255, 140, 200));
        DrawTextBlocks("SCORE", new Vector2(ScreenW / 2f - 90, 300), 3, Color.White);
        DrawNumber(_score, new Vector2(ScreenW / 2f + 0, 292), 5, Color.White);
        if (_stateTimer > 0.7f)
        {
            float blink = 0.6f + 0.4f * MathF.Sin(_stateTimer * 5f);
            DrawTextBlocks("SPACE TO PLAY AGAIN", new Vector2(ScreenW / 2f, 450), 4, Color.White * blink);
        }
    }

    private void DrawFloorClearOverlay()
    {
        float t = MathHelper.Clamp(_floorClearTimer / 1.2f, 0f, 1f);
        float pulse = 0.6f + 0.4f * MathF.Sin(_stateTimer * 8f);
        DrawTextBlocks("FLOOR CLEARED", new Vector2(ScreenW / 2f, 180), 6, new Color(120, 230, 160) * pulse);
        DrawTextBlocks("CLIMBING THE TOWER", new Vector2(ScreenW / 2f, 250), 3, Color.White * 0.8f);
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
