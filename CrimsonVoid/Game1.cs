using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CrimsonVoid;

public class Game1 : Game
{
    // ---------- Core ----------
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;

    private const int ScreenW = 1280;
    private const int ScreenH = 720;

    // The one and only enemy / bullet color
    private static readonly Color RedColor = new Color(255, 70, 90);
    private static readonly Color CoinColor = new Color(255, 215, 90);

    // ---------- Procedural textures ----------
    private Texture2D _pixel;
    private Texture2D _circle32;
    private Texture2D _ring48;
    private Texture2D _triangle24;
    private Texture2D _ship32;
    private Texture2D _nebula128;
    private Texture2D _coin16;
    private Texture2D _ufo;

    // ---------- Starfield / nebula ----------
    private struct Star { public Vector2 Pos; public float Depth; public float Brightness; public float Twinkle; }
    private readonly List<Star> _stars = new();
    private struct Nebula { public Vector2 Pos; public Vector2 Vel; public Color Tint; public float Size; }
    private readonly List<Nebula> _nebulae = new();
    private Vector2 _camDrift;

    // ---------- Player ----------
    private Vector2 _playerPos;
    private float _playerSpeed = 260f;
    private int _hp = 5;
    private int _maxHp = 5;
    private float _shootCooldown = 0f;
    private float _shootInterval = 0.16f; // mutable, shrinks with RAPID FIRE
    private int _bulletDamage = 1;
    private int _bulletPierce = 0;        // extra enemies bullet can pass through
    private int _bulletCount = 1;         // 1, 2 = twin, 3+ = spread
    private float _bulletRadius = 4f;     // collision radius added on top of enemy radius
    private float _magnetRadius = 110f;
    private float _pickupRadius = 22f;
    private float _iFrames = 0f;

    // ---------- Bullets ----------
    private struct Bullet { public Vector2 Pos, Vel; public float Life; public int Damage; public int Pierce; }
    private readonly List<Bullet> _bullets = new();

    // ---------- Enemies ----------
    private enum EnemyKind { Grunt, Tough, Elite, Sniper, Kamikaze, Splitter, MiniBoss, Boss }
    private struct Enemy
    {
        public EnemyKind Kind;
        public Vector2 Pos, Vel;
        public int Hp;
        public int MaxHp;
        public float Wobble;
        public float Radius;        // collision radius
        public float DrawScale;     // sprite scale multiplier
        public float OrbitAngle;    // for bosses
        public float AttackTimer;   // boss attack cadence
        public int Phase;           // boss phase (0..)
    }
    private readonly List<Enemy> _enemies = new();

    // ---------- Enemy bullets (boss attacks) ----------
    private struct EBullet
    {
        public Vector2 Pos, Vel;
        public float Life;
        public float Radius;
        public float Drag;            // 0 = none. If >0 & <1, applied as Vel *= Drag^dt each frame.
        public int SplitsRemaining;   // # of times the bullet should split into 3 at SplitAt life
        public float SplitAt;         // remaining-life threshold to trigger split
    }
    private readonly List<EBullet> _ebullets = new();
    private static readonly Color EBulletColor = new Color(255, 150, 60);

    // Boss tracking — at most one boss alive at a time
    private int _bossIndex = -1;
    private bool _bossSpawnedThisLevel = false;

    // ---------- Coins ----------
    private struct Coin { public Vector2 Pos, Vel; public float Spin; public float Life; }
    private readonly List<Coin> _coins = new();

    // ---------- Particles ----------
    private struct Particle { public Vector2 Pos, Vel; public Color Color; public float Life, MaxLife, Size; }
    private readonly List<Particle> _particles = new();

    // ---------- Upgrades ----------
    private int _coinsTotal = 0;
    private int _upgradeTier = 0;        // how many upgrades applied
    private const int CoinsPerUpgrade = 5;
    private string _upgradeBanner = "";
    private float _upgradeBannerTime = 0f;

    // Upgrade pool (all stackable — selection logic prunes ones at cap)
    private static readonly string[] UpgradePool = {
        "RAPID FIRE",
        "HEAVY ROUNDS",
        "PIERCE",
        "MULTI SHOT",
        "THRUSTERS",
        "MAX HP",
        "BIG BULLETS",
        "COIN MAGNET",
    };
    private readonly string[] _offerNames = new string[3];
    private int _upgradesPending = 0;

    // ---------- Game state ----------
    private enum State { Title, Playing, UpgradeChoice, Dead, Won }
    private State _state = State.Title;
    private int _score = 0;
    private int _level = 0;
    private const int MaxLevel = 10;
    private float _waveTimer = 0f;
    private int _enemiesThisLevel = 0;
    private int _enemiesSpawnedThisLevel = 0;
    private float _spawnTimer = 0f;
    private float _levelClearTimer = 0f;
    private readonly Random _rng = new();

    // ---------- Input edge detection ----------
    private KeyboardState _prevKb;
    private MouseState _prevMouse;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreferredBackBufferWidth = ScreenW;
        _graphics.PreferredBackBufferHeight = ScreenH;
        _graphics.SynchronizeWithVerticalRetrace = true;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        Window.Title = "Crimson Void";
    }

    protected override void Initialize()
    {
        _playerPos = new Vector2(ScreenW / 2f, ScreenH / 2f);
        BuildStarfield();
        BuildNebulae();
        base.Initialize();
    }

    private void BuildStarfield()
    {
        _stars.Clear();
        for (int i = 0; i < 220; i++)
        {
            _stars.Add(new Star
            {
                Pos = new Vector2(_rng.Next(ScreenW), _rng.Next(ScreenH)),
                Depth = 0.15f + (float)_rng.NextDouble() * 0.85f,
                Brightness = 0.4f + (float)_rng.NextDouble() * 0.6f,
                Twinkle = (float)_rng.NextDouble() * MathF.Tau
            });
        }
    }

    private void BuildNebulae()
    {
        _nebulae.Clear();
        Color[] tints = {
            new Color(80, 40, 160),
            new Color(30, 80, 160),
            new Color(140, 50, 110),
            new Color(40, 90, 130),
        };
        for (int i = 0; i < 5; i++)
        {
            var ang = (float)_rng.NextDouble() * MathF.Tau;
            _nebulae.Add(new Nebula
            {
                Pos = new Vector2(_rng.Next(ScreenW), _rng.Next(ScreenH)),
                Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (4f + (float)_rng.NextDouble() * 6f),
                Tint = tints[_rng.Next(tints.Length)],
                Size = 2.2f + (float)_rng.NextDouble() * 2.0f
            });
        }
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _circle32 = MakeCircle(32);
        _ring48 = MakeRing(48, 4);
        _triangle24 = MakeTriangle(24);
        _ship32 = MakeShip(32);
        _nebula128 = MakeNebula(128);
        _coin16 = MakeCoin(16);
        _ufo = MakeUFO(180, 110);
    }

    // ---------- Procedural texture helpers ----------
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

    private Texture2D MakeRing(int diameter, int thickness)
    {
        var tex = new Texture2D(GraphicsDevice, diameter, diameter);
        var data = new Color[diameter * diameter];
        float r = diameter / 2f - 0.5f;
        for (int y = 0; y < diameter; y++)
        for (int x = 0; x < diameter; x++)
        {
            float dx = x - r, dy = y - r;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            float dist = MathF.Abs(d - (r - thickness * 0.5f));
            float a = MathHelper.Clamp(1f - dist / (thickness * 0.5f), 0f, 1f);
            data[y * diameter + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakeShip(int size)
    {
        var tex = new Texture2D(GraphicsDevice, size, size);
        var data = new Color[size * size];
        float cx = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float ny = y / (float)(size - 1);
            float dx = (x - cx) / cx;
            float hullHalf = 0.10f + ny * 0.30f;
            bool inHull = MathF.Abs(dx) <= hullHalf && ny > 0.05f && ny < 0.95f;
            float wingY = ny - 0.55f;
            float wingHalfWidth = MathF.Max(0f, 0.55f - MathF.Abs(wingY) * 2.5f);
            bool inWing = ny > 0.45f && ny < 0.85f && MathF.Abs(dx) <= wingHalfWidth + 0.05f && MathF.Abs(dx) > hullHalf - 0.02f;
            bool canopy = ny > 0.20f && ny < 0.40f && MathF.Abs(dx) < 0.10f;
            Color c = Color.Transparent;
            if (inHull || inWing) c = new Color(220, 230, 255);
            if (canopy) c = new Color(120, 200, 255);
            data[y * size + x] = c;
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakeNebula(int size)
    {
        var tex = new Texture2D(GraphicsDevice, size, size);
        var data = new Color[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - r, dy = y - r;
            float d = MathF.Sqrt(dx * dx + dy * dy) / r;
            float a = MathHelper.Clamp(1f - d, 0f, 1f);
            a = a * a;
            data[y * size + x] = new Color(1f, 1f, 1f, a * 0.55f);
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakeTriangle(int size)
    {
        var tex = new Texture2D(GraphicsDevice, size, size);
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float ty = y / (float)(size - 1);
            float halfWidth = ty * 0.5f;
            float cx = x / (float)(size - 1) - 0.5f;
            bool inside = MathF.Abs(cx) <= halfWidth && ty > 0.05f;
            data[y * size + x] = inside ? Color.White : Color.Transparent;
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakeUFO(int w, int h)
    {
        // Classic flying saucer: wide disc + glassy dome on top + warm rim lights.
        // Colors are baked in so we draw with Color.White at the call site.
        var tex = new Texture2D(GraphicsDevice, w, h);
        var data = new Color[w * h];

        float cx = w / 2f;
        float discCy = h * 0.62f;
        float discAx = w * 0.47f;
        float discAy = h * 0.13f;
        float domeCy = h * 0.40f;
        float domeAx = w * 0.22f;
        float domeAy = h * 0.30f;

        // Precompute rim light positions
        int numLights = 9;
        var lights = new (float lx, float ly)[numLights];
        for (int i = 0; i < numLights; i++)
        {
            float t = i / (float)(numLights - 1);
            // sweep across the bottom of the disc
            float ang = MathHelper.Lerp(MathF.PI + 0.25f, MathF.PI * 2f - 0.25f, t);
            lights[i] = (cx + MathF.Cos(ang) * discAx * 0.92f,
                         discCy + MathF.Sin(ang) * discAy * 0.92f);
        }

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Color color = Color.Transparent;

            // ----- Disc (saucer body) -----
            float ddx = (x - cx) / discAx;
            float ddy = (y - discCy) / discAy;
            float dDisc = ddx * ddx + ddy * ddy;
            if (dDisc < 1f)
            {
                // top-to-bottom shading: lighter on top of disc, darker underside
                float topness = (y - (discCy - discAy)) / (discAy * 2f); // 0 top -> 1 bottom
                topness = MathHelper.Clamp(topness, 0f, 1f);
                byte r = (byte)(210 - topness * 90);
                byte g = (byte)(55 + topness * 5);
                byte b = (byte)(65 + topness * 10);
                color = new Color(r, g, b);

                // dark equator stripe for definition
                float stripe = MathF.Abs(y - discCy);
                if (stripe < 1.5f) color = new Color(60, 12, 18);
            }

            // ----- Dome (glassy cyan) -----
            float dox = (x - cx) / domeAx;
            float doy = (y - domeCy) / domeAy;
            float dDome = dox * dox + doy * doy;
            if (dDome < 1f && y < domeCy + domeAy * 0.45f)
            {
                float ny = MathHelper.Clamp((y - (domeCy - domeAy)) / (domeAy * 1.4f), 0f, 1f);
                byte r = (byte)(110 + (1f - ny) * 110);
                byte g = (byte)(190 + (1f - ny) * 50);
                byte b = (byte)(230 + (1f - ny) * 25);
                color = new Color(r, g, b);

                // sparkle highlight near top-left of dome
                float hx = x - (cx - domeAx * 0.35f);
                float hy = y - (domeCy - domeAy * 0.45f);
                if (hx * hx + hy * hy < 9f) color = new Color(255, 255, 255);
            }

            // ----- Rim lights -----
            for (int i = 0; i < numLights; i++)
            {
                float lx = x - lights[i].lx;
                float ly = y - lights[i].ly;
                float d2 = lx * lx + ly * ly;
                if (d2 < 6f) { color = new Color(255, 235, 130); break; }
                if (d2 < 14f && color.A > 0) { color = Color.Lerp(color, new Color(255, 220, 110), 0.5f); }
            }

            data[y * w + x] = color;
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakeCoin(int size)
    {
        // Solid disc with an inner highlight ring — drawn white so we can tint at runtime.
        var tex = new Texture2D(GraphicsDevice, size, size);
        var data = new Color[size * size];
        float r = size / 2f - 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - r, dy = y - r;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            float ndist = d / r; // 0 center -> 1 edge
            if (ndist > 1f) { data[y * size + x] = Color.Transparent; continue; }
            // outer rim slightly darker, inner core white
            float inner = MathHelper.Clamp(1f - ndist * 1.1f, 0f, 1f);
            byte rim = (byte)(160 + inner * 95);
            data[y * size + x] = new Color(rim, rim, rim, (byte)255);
        }
        tex.SetData(data);
        return tex;
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var kb = Keyboard.GetState();
        var mouse = Mouse.GetState();

        if (kb.IsKeyDown(Keys.Escape)) Exit();

        switch (_state)
        {
            case State.Title:
                if (Pressed(kb, Keys.Space) || Pressed(kb, Keys.Enter))
                    StartGame();
                break;
            case State.Playing:
                UpdatePlaying(dt, kb, mouse);
                break;
            case State.UpgradeChoice:
                if (Pressed(kb, Keys.D1) || Pressed(kb, Keys.NumPad1)) ApplyChosenUpgrade(0);
                else if (Pressed(kb, Keys.D2) || Pressed(kb, Keys.NumPad2)) ApplyChosenUpgrade(1);
                else if (Pressed(kb, Keys.D3) || Pressed(kb, Keys.NumPad3)) ApplyChosenUpgrade(2);
                break;
            case State.Dead:
            case State.Won:
                if (Pressed(kb, Keys.Space) || Pressed(kb, Keys.Enter))
                    StartGame();
                break;
        }

        UpdateParticles(dt);
        UpdateSpaceBackground(dt);
        _upgradeBannerTime = MathF.Max(0f, _upgradeBannerTime - dt);

        _prevKb = kb;
        _prevMouse = mouse;
        base.Update(gameTime);
    }

    private bool Pressed(KeyboardState kb, Keys k) => kb.IsKeyDown(k) && !_prevKb.IsKeyDown(k);

    private void StartGame()
    {
        _state = State.Playing;
        _maxHp = 5;
        _hp = _maxHp;
        _score = 0;
        _level = 0;
        _waveTimer = 0f;
        _enemiesThisLevel = 0;
        _enemiesSpawnedThisLevel = 0;
        _spawnTimer = 0f;
        _levelClearTimer = 0f;
        _playerPos = new Vector2(ScreenW / 2f, ScreenH / 2f);

        // reset upgrades
        _coinsTotal = 0;
        _upgradeTier = 0;
        _shootInterval = 0.16f;
        _bulletDamage = 1;
        _bulletPierce = 0;
        _bulletCount = 1;
        _bulletRadius = 4f;
        _magnetRadius = 110f;
        _pickupRadius = 22f;
        _playerSpeed = 260f;
        _upgradeBanner = "";
        _upgradeBannerTime = 0f;
        _upgradesPending = 0;

        _bullets.Clear();
        _enemies.Clear();
        _ebullets.Clear();
        _coins.Clear();
        _particles.Clear();
        _bossIndex = -1;
        _bossSpawnedThisLevel = false;
        BeginLevel(1);
    }

    // Every level now ends with a boss (final level uses the BIG boss, others mini).
    private bool IsBossLevel(int lvl) => true;

    private void BeginLevel(int levelNum)
    {
        _level = levelNum;
        // Longer levels: more rank-and-file spawns plus the level-end boss.
        _enemiesThisLevel = 10 + levelNum * 4;
        _enemiesSpawnedThisLevel = 0;
        _waveTimer = 2.8f;
        _spawnTimer = 0f;
        _levelClearTimer = 0f;
        _bossIndex = -1;
        _bossSpawnedThisLevel = false;
        if (levelNum > 1 && levelNum % 2 == 1 && _hp < _maxHp) _hp++;
        Burst(_playerPos, Color.White, 30, 220f);
    }

    private void UpdatePlaying(float dt, KeyboardState kb, MouseState mouse)
    {
        // ----- Movement -----
        Vector2 move = Vector2.Zero;
        if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up)) move.Y -= 1;
        if (kb.IsKeyDown(Keys.S) || kb.IsKeyDown(Keys.Down)) move.Y += 1;
        if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left)) move.X -= 1;
        if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right)) move.X += 1;
        if (move != Vector2.Zero)
        {
            move.Normalize();
            _playerPos += move * _playerSpeed * dt;
        }
        _playerPos.X = MathHelper.Clamp(_playerPos.X, 24, ScreenW - 24);
        _playerPos.Y = MathHelper.Clamp(_playerPos.Y, 24, ScreenH - 24);

        // ----- Shoot -----
        _shootCooldown -= dt;
        if (mouse.LeftButton == ButtonState.Pressed && _shootCooldown <= 0f)
        {
            var aim = new Vector2(mouse.X, mouse.Y) - _playerPos;
            if (aim.LengthSquared() > 1f)
            {
                aim.Normalize();
                FireBullets(aim);
                _shootCooldown = _shootInterval;
            }
        }

        // ----- Bullets -----
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            var b = _bullets[i];
            b.Pos += b.Vel * dt;
            b.Life -= dt;
            if (b.Life <= 0 || b.Pos.X < -20 || b.Pos.X > ScreenW + 20 || b.Pos.Y < -20 || b.Pos.Y > ScreenH + 20)
            {
                _bullets.RemoveAt(i);
                continue;
            }
            _bullets[i] = b;
        }

        // ----- Level / spawning -----
        _waveTimer -= dt;
        if (_waveTimer <= 0f && _enemiesSpawnedThisLevel < _enemiesThisLevel)
        {
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f)
            {
                SpawnEnemy();
                _enemiesSpawnedThisLevel++;
                _spawnTimer = MathF.Max(0.40f, 1.15f - _level * 0.075f);
            }
        }

        // On boss levels: once normal spawns are done, give a beat then spawn the boss.
        if (IsBossLevel(_level) && !_bossSpawnedThisLevel && _enemiesSpawnedThisLevel >= _enemiesThisLevel)
        {
            // wait until the screen has at most a few stragglers
            if (_enemies.Count <= 1)
            {
                SpawnBossForLevel();
            }
        }

        bool levelObjectivesDone = _enemiesSpawnedThisLevel >= _enemiesThisLevel
                                   && _enemies.Count == 0
                                   && _waveTimer <= 0f
                                   && (!IsBossLevel(_level) || _bossSpawnedThisLevel);
        if (levelObjectivesDone)
        {
            _levelClearTimer += dt;
            if (_levelClearTimer > 1.6f)
            {
                if (_level >= MaxLevel)
                {
                    _state = State.Won;
                    Burst(_playerPos, Color.White, 120, 360f);
                }
                else
                {
                    BeginLevel(_level + 1);
                }
            }
        }

        // ----- Enemy update + collisions -----
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            var e = _enemies[i];
            var to = _playerPos - e.Pos;
            float dEnemyPlayer = to.Length();
            if (dEnemyPlayer > 0.01f) to /= dEnemyPlayer;
            e.Wobble += dt * 3f;

            // ---- Per-kind AI ----
            if (e.Kind == EnemyKind.Boss || e.Kind == EnemyKind.MiniBoss)
            {
                // bosses fly in from off-screen, then orbit the player at medium range
                bool entering = e.Pos.Y < 90f;
                if (entering)
                {
                    e.Vel = new Vector2(0, 120f);
                }
                else
                {
                    float orbitRadius = e.Kind == EnemyKind.Boss ? 280f : 240f;
                    float orbitSpeed = e.Kind == EnemyKind.Boss ? 0.9f : 1.1f;
                    e.OrbitAngle += dt * orbitSpeed;
                    var target = _playerPos + new Vector2(MathF.Cos(e.OrbitAngle), MathF.Sin(e.OrbitAngle)) * orbitRadius;
                    var seek = target - e.Pos;
                    if (seek.LengthSquared() > 1f) seek.Normalize();
                    e.Vel = seek * 140f;
                }
                e.Pos += e.Vel * dt;

                // Attack cadence
                if (!entering)
                {
                    e.AttackTimer -= dt;
                    if (e.AttackTimer <= 0f)
                    {
                        FireBossAttack(ref e);
                    }
                }
            }
            else if (e.Kind == EnemyKind.Sniper)
            {
                // approach until in range, then hold and charge a shot
                float approachSpd = 95f;
                if (dEnemyPlayer > 380f) { e.Vel = to * approachSpd; }
                else if (dEnemyPlayer < 240f) { e.Vel = -to * 75f; }
                else { e.Vel = Vector2.Zero; }
                e.Pos += e.Vel * dt;

                e.AttackTimer -= dt;
                if (e.Phase == 0)
                {
                    if (e.AttackTimer <= 0f && dEnemyPlayer < 460f)
                    {
                        FireSniperShot(e.Pos, to);
                        e.Phase = 1;
                        e.AttackTimer = 2.4f;
                    }
                }
                else
                {
                    if (e.AttackTimer <= 0f) { e.Phase = 0; e.AttackTimer = 1.2f; }
                }
            }
            else if (e.Kind == EnemyKind.Kamikaze)
            {
                // chase HARD — they don't shoot, so their threat is closing distance
                float speed = 230f + _level * 14f; // L1=244, L5=300, L10=370 (player base = 260)
                e.Vel = to * speed;
                e.Pos += e.Vel * dt;

                if (e.Phase == 0 && dEnemyPlayer < 110f)
                {
                    e.Phase = 1;
                    e.AttackTimer = 0.40f; // fuse
                }
                if (e.Phase == 1)
                {
                    e.AttackTimer -= dt;
                    if (e.AttackTimer <= 0f)
                    {
                        // Detonate: big burst + AOE INSTAKILL (bypasses iFrames)
                        Burst(e.Pos, new Color(255, 180, 80), 70, 360f);
                        if (Vector2.DistanceSquared(_playerPos, e.Pos) < 80f * 80f)
                        {
                            Burst(_playerPos, new Color(255, 180, 80), 60, 320f);
                            _hp = 0;
                            _state = State.Dead;
                            Burst(_playerPos, RedColor, 80, 320f);
                        }
                        e.Hp = 0;
                    }
                }
            }
            else
            {
                float speed = 60f + _level * 8f;
                if (e.Kind == EnemyKind.Tough) speed *= 0.85f;
                if (e.Kind == EnemyKind.Elite) speed *= 1.15f;
                if (e.Kind == EnemyKind.Splitter) speed *= 0.70f;
                var perp = new Vector2(-to.Y, to.X) * MathF.Sin(e.Wobble) * 0.35f;
                e.Vel = (to + perp) * speed;
                e.Pos += e.Vel * dt;

                // Minion fire — only when they have line of sight at a reasonable range
                e.AttackTimer -= dt;
                if (e.AttackTimer <= 0f && dEnemyPlayer > 90f && dEnemyPlayer < 720f)
                {
                    FireMinionAttack(ref e, to);
                }
            }

            // bullet collisions (pierce supported)
            float hitR = e.Radius + _bulletRadius;
            float hitR2 = hitR * hitR;
            for (int j = _bullets.Count - 1; j >= 0; j--)
            {
                var b = _bullets[j];
                if (Vector2.DistanceSquared(b.Pos, e.Pos) < hitR2)
                {
                    e.Hp -= b.Damage;
                    Burst(b.Pos, RedColor, 6, 160f);
                    if (b.Pierce <= 0)
                    {
                        _bullets.RemoveAt(j);
                    }
                    else
                    {
                        b.Pierce--;
                        _bullets[j] = b;
                    }
                    if (e.Hp <= 0) break;
                }
            }

            // player collision — Kamikaze bypasses iFrames AND instakills
            if (e.Hp > 0)
            {
                float touchR = e.Radius + 12f;
                bool touching = Vector2.DistanceSquared(_playerPos, e.Pos) < touchR * touchR;
                if (touching && e.Kind == EnemyKind.Kamikaze)
                {
                    // Instakill — explosion big enough to be unmistakable
                    Burst(e.Pos, new Color(255, 180, 80), 90, 420f);
                    Burst(_playerPos, new Color(255, 180, 80), 60, 320f);
                    _hp = 0;
                    e.Hp = 0;
                    _state = State.Dead;
                    Burst(_playerPos, RedColor, 80, 320f);
                }
                else if (touching && _iFrames <= 0f)
                {
                    _hp--;
                    _iFrames = 1.0f;
                    Burst(_playerPos, Color.White, 40, 280f);
                    // bosses don't die from contact, just nudge back
                    if (e.Kind == EnemyKind.Boss || e.Kind == EnemyKind.MiniBoss)
                    {
                        var away = e.Pos - _playerPos;
                        if (away.LengthSquared() > 1f) { away.Normalize(); e.Pos += away * 24f; }
                    }
                    else
                    {
                        e.Hp = 0;
                    }
                    if (_hp <= 0)
                    {
                        _state = State.Dead;
                        Burst(_playerPos, RedColor, 80, 320f);
                    }
                }
            }

            if (e.Hp <= 0)
            {
                int score; int coinDrops;
                switch (e.Kind)
                {
                    case EnemyKind.Tough:    score = 25;  coinDrops = e.MaxHp + 1; break;
                    case EnemyKind.Elite:    score = 75;  coinDrops = 6;           break;
                    case EnemyKind.Sniper:   score = 60;  coinDrops = 3;           break;
                    case EnemyKind.Kamikaze: score = 30;  coinDrops = 2;           break;
                    case EnemyKind.Splitter: score = 50;  coinDrops = 3;           break;
                    case EnemyKind.MiniBoss: score = 500; coinDrops = 7;           break;
                    case EnemyKind.Boss:     score = 2000; coinDrops = 18;          break;
                    default:                 score = 10;  coinDrops = 1;           break;
                }
                _score += score;
                Burst(e.Pos, RedColor, e.Kind >= EnemyKind.MiniBoss ? 80 : 18, e.Kind >= EnemyKind.MiniBoss ? 360f : 220f);
                for (int c = 0; c < coinDrops; c++) DropCoin(e.Pos);

                // Splitter: spawn 2 grunts at our position with outward velocity.
                if (e.Kind == EnemyKind.Splitter)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        AddEnemy(EnemyKind.Grunt, e.Pos);
                        var spawned = _enemies[_enemies.Count - 1];
                        float ang = (float)_rng.NextDouble() * MathF.Tau;
                        spawned.Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 130f;
                        _enemies[_enemies.Count - 1] = spawned;
                    }
                }

                if (i == _bossIndex) _bossIndex = -1;
                _enemies.RemoveAt(i);
                // boss-index shift correction
                if (_bossIndex > i) _bossIndex--;
                continue;
            }
            _enemies[i] = e;
        }

        // ----- Enemy bullets (boss fire) -----
        for (int i = _ebullets.Count - 1; i >= 0; i--)
        {
            var eb = _ebullets[i];
            if (eb.Drag > 0f && eb.Drag < 1f)
                eb.Vel *= MathF.Pow(eb.Drag, dt);
            eb.Pos += eb.Vel * dt;
            eb.Life -= dt;
            if (eb.Life <= 0 || eb.Pos.X < -30 || eb.Pos.X > ScreenW + 30 || eb.Pos.Y < -30 || eb.Pos.Y > ScreenH + 30)
            {
                _ebullets.RemoveAt(i);
                continue;
            }
            // Splitting: when life crosses threshold, replace with 3 fragments
            if (eb.SplitsRemaining > 0 && eb.Life <= eb.SplitAt)
            {
                var dir = eb.Vel;
                float spd = dir.Length();
                if (spd > 1f) dir /= spd;
                float fragSpeed = MathF.Max(160f, spd * 0.9f);
                for (int k = -1; k <= 1; k++)
                {
                    var nd = Rotate(dir, k * MathHelper.ToRadians(22f));
                    _ebullets.Add(new EBullet
                    {
                        Pos = eb.Pos, Vel = nd * fragSpeed,
                        Life = 2.2f, Radius = MathF.Max(5f, eb.Radius * 0.7f),
                        SplitsRemaining = 0, Drag = 0
                    });
                }
                _ebullets.RemoveAt(i);
                continue;
            }
            // player hit?
            if (_iFrames <= 0f && Vector2.DistanceSquared(eb.Pos, _playerPos) < (eb.Radius + 12f) * (eb.Radius + 12f))
            {
                _hp--;
                _iFrames = 0.9f;
                Burst(_playerPos, EBulletColor, 30, 260f);
                _ebullets.RemoveAt(i);
                if (_hp <= 0)
                {
                    _state = State.Dead;
                    Burst(_playerPos, RedColor, 80, 320f);
                }
                continue;
            }
            _ebullets[i] = eb;
        }

        // ----- Coins -----
        for (int i = _coins.Count - 1; i >= 0; i--)
        {
            var c = _coins[i];
            c.Spin += dt * 4f;
            // brief initial scatter then magnet toward player when close
            var toP = _playerPos - c.Pos;
            float dist = toP.Length();
            if (dist < _magnetRadius && dist > 0.01f)
            {
                toP /= dist;
                float pull = MathHelper.Lerp(380f, 90f, dist / _magnetRadius);
                c.Vel = Vector2.Lerp(c.Vel, toP * pull, 0.18f);
            }
            else
            {
                c.Vel *= MathF.Pow(0.5f, dt * 3.5f); // settle
            }
            c.Pos += c.Vel * dt;
            c.Life -= dt;

            if (dist < _pickupRadius)
            {
                _coins.RemoveAt(i);
                CollectCoin(c.Pos);
                continue;
            }
            if (c.Life <= 0)
            {
                _coins.RemoveAt(i);
                continue;
            }
            _coins[i] = c;
        }

        _iFrames -= dt;
    }

    private void FireBullets(Vector2 aim)
    {
        const float bulletSpeed = 760f;
        // perpendicular for parallel offset / spread base
        var perp = new Vector2(-aim.Y, aim.X);

        if (_bulletCount <= 1)
        {
            SpawnBullet(_playerPos + aim * 18f, aim * bulletSpeed);
        }
        else if (_bulletCount == 2)
        {
            // twin parallel
            SpawnBullet(_playerPos + aim * 18f + perp * 8f, aim * bulletSpeed);
            SpawnBullet(_playerPos + aim * 18f - perp * 8f, aim * bulletSpeed);
        }
        else
        {
            // odd-count spread (3+) — symmetric around aim
            int n = _bulletCount;
            float spreadDeg = 18f; // total spread for outermost rays
            for (int i = 0; i < n; i++)
            {
                float t = (n == 1) ? 0f : (i / (float)(n - 1)) * 2f - 1f; // -1..1
                float ang = t * MathHelper.ToRadians(spreadDeg);
                var dir = Rotate(aim, ang);
                SpawnBullet(_playerPos + dir * 18f, dir * bulletSpeed);
            }
        }
    }

    private void SpawnBullet(Vector2 pos, Vector2 vel)
    {
        _bullets.Add(new Bullet { Pos = pos, Vel = vel, Life = 1.4f, Damage = _bulletDamage, Pierce = _bulletPierce });
    }

    private static Vector2 Rotate(Vector2 v, float rad)
    {
        float c = MathF.Cos(rad), s = MathF.Sin(rad);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private Vector2 EdgeSpawnPos()
    {
        int side = _rng.Next(4);
        return side switch
        {
            0 => new Vector2(_rng.Next(ScreenW), -20),
            1 => new Vector2(ScreenW + 20, _rng.Next(ScreenH)),
            2 => new Vector2(_rng.Next(ScreenW), ScreenH + 20),
            _ => new Vector2(-20, _rng.Next(ScreenH))
        };
    }

    private void SpawnEnemy()
    {
        // Weighted spawn pool with per-level unlocks. Grunts dominate early; archetypes
        // get heavier weight as levels progress.
        int wGrunt    = Math.Max(20, 100 - _level * 8);
        int wTough    = _level >= 3 ? 25 + _level * 2 : 0;
        int wKamikaze = _level >= 2 ? 18 + _level : 0;
        int wSniper   = _level >= 4 ? 14 + _level : 0;
        int wSplitter = _level >= 5 ? 12 + _level : 0;
        int wElite    = _level >= 6 ? 8  + _level * 2 : 0;

        int sum = wGrunt + wTough + wKamikaze + wSniper + wSplitter + wElite;
        int roll = _rng.Next(sum);
        EnemyKind kind;
        if (roll < wGrunt) kind = EnemyKind.Grunt;
        else if ((roll -= wGrunt) < wTough) kind = EnemyKind.Tough;
        else if ((roll -= wTough) < wKamikaze) kind = EnemyKind.Kamikaze;
        else if ((roll -= wKamikaze) < wSniper) kind = EnemyKind.Sniper;
        else if ((roll -= wSniper) < wSplitter) kind = EnemyKind.Splitter;
        else kind = EnemyKind.Elite;

        AddEnemy(kind, EdgeSpawnPos());
    }

    private void AddEnemy(EnemyKind kind, Vector2 pos)
    {
        int hp; float radius, scale;
        switch (kind)
        {
            case EnemyKind.Tough:    hp = 2 + (_level >= 7 ? 1 : 0); radius = 18f; scale = 1.25f; break;
            case EnemyKind.Elite:    hp = 5 + (_level / 3);          radius = 22f; scale = 1.6f;  break;
            case EnemyKind.Sniper:   hp = _level >= 6 ? 3 : 2;       radius = 16f; scale = 1.30f; break;
            case EnemyKind.Kamikaze: hp = 1;                         radius = 13f; scale = 1.05f; break;
            case EnemyKind.Splitter: hp = 3 + (_level >= 8 ? 1 : 0); radius = 20f; scale = 1.50f; break;
            case EnemyKind.MiniBoss: hp = 140 + _level * 70;         radius = 40f; scale = 3.0f;  break;
            case EnemyKind.Boss:     hp = 450 + _level * 50;         radius = 54f; scale = 4.2f;  break;
            default:                 hp = 1;   radius = 14f; scale = 1.0f;  break;
        }

        // Stagger initial attack timers so the player isn't shot the instant enemies spawn.
        float initialFireDelay = kind switch
        {
            EnemyKind.Grunt    => 2.5f + (float)_rng.NextDouble() * 2.5f,
            EnemyKind.Tough    => 2.0f + (float)_rng.NextDouble() * 2.0f,
            EnemyKind.Elite    => 1.6f + (float)_rng.NextDouble() * 1.4f,
            EnemyKind.Sniper   => 1.4f,   // charge time before first shot
            EnemyKind.Kamikaze => 999f,   // no fuse until it gets close
            EnemyKind.Splitter => 2.5f + (float)_rng.NextDouble() * 2.0f,
            EnemyKind.MiniBoss => 2.0f,
            EnemyKind.Boss     => 2.5f,
            _ => 1.5f
        };

        _enemies.Add(new Enemy
        {
            Kind = kind,
            Pos = pos,
            Vel = Vector2.Zero,
            Hp = hp,
            MaxHp = hp,
            Wobble = (float)_rng.NextDouble() * MathF.Tau,
            Radius = radius,
            DrawScale = scale,
            OrbitAngle = (float)_rng.NextDouble() * MathF.Tau,
            AttackTimer = initialFireDelay,
            Phase = 0,
        });
    }

    private void SpawnBossForLevel()
    {
        var kind = (_level == MaxLevel) ? EnemyKind.Boss : EnemyKind.MiniBoss;
        // dramatic entrance from the top, drift in
        AddEnemy(kind, new Vector2(ScreenW / 2f, -80f));
        _bossIndex = _enemies.Count - 1;
        _bossSpawnedThisLevel = true;
        // arrival flash
        Burst(new Vector2(ScreenW / 2f, 80f), RedColor, 80, 360f);
    }

    private void FireSniperShot(Vector2 from, Vector2 toPlayerDir)
    {
        // Fast, accurate, single shot — distinct pinkish bullet via radius/color in draw
        _ebullets.Add(new EBullet { Pos = from + toPlayerDir * 16f, Vel = toPlayerDir * 620f, Life = 2.5f, Radius = 7f });
    }

    private void FireMinionAttack(ref Enemy e, Vector2 toPlayerDir)
    {
        switch (e.Kind)
        {
            case EnemyKind.Grunt:
            {
                _ebullets.Add(new EBullet { Pos = e.Pos + toPlayerDir * 14f, Vel = toPlayerDir * 240f, Life = 4f, Radius = 7f });
                e.AttackTimer = 3.2f + (float)_rng.NextDouble() * 1.4f;
                break;
            }
            case EnemyKind.Tough:
            {
                // pair of slightly-spread shots
                for (int i = -1; i <= 1; i += 2)
                {
                    var dir = Rotate(toPlayerDir, i * MathHelper.ToRadians(7f));
                    _ebullets.Add(new EBullet { Pos = e.Pos + dir * 16f, Vel = dir * 270f, Life = 4f, Radius = 8f });
                }
                e.AttackTimer = 2.4f + (float)_rng.NextDouble() * 0.8f;
                break;
            }
            case EnemyKind.Elite:
            {
                // 3-shot aimed fan
                for (int i = -1; i <= 1; i++)
                {
                    var dir = Rotate(toPlayerDir, i * MathHelper.ToRadians(11f));
                    _ebullets.Add(new EBullet { Pos = e.Pos + dir * 18f, Vel = dir * 310f, Life = 4f, Radius = 9f });
                }
                e.AttackTimer = 1.5f + (float)_rng.NextDouble() * 0.7f;
                break;
            }
        }
    }

    private void FireBossAttack(ref Enemy boss)
    {
        var to = _playerPos - boss.Pos;
        if (to.LengthSquared() < 1f) to = new Vector2(0, 1);
        to.Normalize();

        if (boss.Kind == EnemyKind.MiniBoss)
        {
            switch (_level)
            {
                case 1: PatternGunner(ref boss, to); break;
                case 2: PatternSprayer(ref boss, to); break;
                case 3: PatternShotgun(ref boss, to); break;
                case 4: PatternPulsar(ref boss, to); break;
                case 5: PatternTwinGunners(ref boss, to); break;
                case 6: PatternBombardier(ref boss, to); break;
                case 7: PatternSniperElite(ref boss, to); break;
                case 8: PatternSpiral(ref boss, to); break;
                case 9: PatternMultiplier(ref boss, to); break;
                default: PatternGunner(ref boss, to); break;
            }
        }
        else // final Boss
        {
            // phase change at half HP
            if (boss.Phase == 0 && boss.Hp <= boss.MaxHp / 2) boss.Phase = 1;

            if (boss.Phase == 0)
            {
                // 5-shot aimed fan
                const float speed = 340f;
                for (int i = -2; i <= 2; i++)
                {
                    var dir = Rotate(to, i * MathHelper.ToRadians(9f));
                    _ebullets.Add(new EBullet { Pos = boss.Pos + dir * 40f, Vel = dir * speed, Life = 3.5f, Radius = 9f });
                }
                boss.AttackTimer = 1.1f;
            }
            else
            {
                // alternating: ring burst, then aimed double-tap
                if ((Environment.TickCount / 1100) % 2 == 0)
                {
                    int n = 14;
                    const float speed = 240f;
                    float angle0 = (float)_rng.NextDouble() * MathF.Tau;
                    for (int i = 0; i < n; i++)
                    {
                        float a = angle0 + i * MathF.Tau / n;
                        var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                        _ebullets.Add(new EBullet { Pos = boss.Pos + dir * 40f, Vel = dir * speed, Life = 3.5f, Radius = 8f });
                    }
                    boss.AttackTimer = 1.6f;
                }
                else
                {
                    for (int k = 0; k < 2; k++)
                    {
                        var dir = Rotate(to, (k == 0 ? -1 : 1) * MathHelper.ToRadians(6f));
                        _ebullets.Add(new EBullet { Pos = boss.Pos + dir * 40f, Vel = dir * 420f, Life = 3.5f, Radius = 9f });
                    }
                    boss.AttackTimer = 0.55f;
                }
            }
        }
    }

    // ---------- Per-level miniboss patterns ----------
    private static string MiniBossName(int level) => level switch
    {
        1 => "GUNNER",
        2 => "SPRAYER",
        3 => "SHOTGUN",
        4 => "PULSAR",
        5 => "TWIN GUNNERS",
        6 => "BOMBARDIER",
        7 => "SNIPER ELITE",
        8 => "SPIRAL",
        9 => "MULTIPLIER",
        _ => "MINIBOSS"
    };

    // L1 — straight aimed shots at a fast cadence
    private void PatternGunner(ref Enemy b, Vector2 to)
    {
        _ebullets.Add(new EBullet { Pos = b.Pos + to * 36f, Vel = to * 340f, Life = 3.5f, Radius = 9f });
        b.AttackTimer = 0.50f;
    }

    // L2 — sweeping arc around the aim vector (use Wobble as sweep phase)
    private void PatternSprayer(ref Enemy b, Vector2 to)
    {
        b.Wobble += 0.55f;
        float sweep = MathF.Sin(b.Wobble) * MathHelper.ToRadians(48f);
        var dir = Rotate(to, sweep);
        _ebullets.Add(new EBullet { Pos = b.Pos + dir * 36f, Vel = dir * 300f, Life = 3.5f, Radius = 9f });
        b.AttackTimer = 0.18f;
    }

    // L3 — wide cone burst (forces lateral dodge), long cooldown
    private void PatternShotgun(ref Enemy b, Vector2 to)
    {
        int n = 7;
        for (int i = 0; i < n; i++)
        {
            float t = (i / (float)(n - 1)) * 2f - 1f;
            var dir = Rotate(to, t * MathHelper.ToRadians(38f));
            _ebullets.Add(new EBullet { Pos = b.Pos + dir * 36f, Vel = dir * 290f, Life = 3.5f, Radius = 9f });
        }
        b.AttackTimer = 1.7f;
    }

    // L4 — radial ring burst, slowly rotating each shot
    private void PatternPulsar(ref Enemy b, Vector2 to)
    {
        int n = 14;
        float a0 = b.Wobble;
        b.Wobble += MathF.PI / 12f;
        for (int i = 0; i < n; i++)
        {
            float a = a0 + i * MathF.Tau / n;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            _ebullets.Add(new EBullet { Pos = b.Pos + dir * 38f, Vel = dir * 230f, Life = 3.5f, Radius = 8f });
        }
        b.AttackTimer = 1.5f;
    }

    // L5 — two parallel guns, offset on either side
    private void PatternTwinGunners(ref Enemy b, Vector2 to)
    {
        var perp = new Vector2(-to.Y, to.X);
        for (int side = -1; side <= 1; side += 2)
        {
            var origin = b.Pos + perp * (side * 26f);
            _ebullets.Add(new EBullet { Pos = origin + to * 16f, Vel = to * 340f, Life = 3.5f, Radius = 9f });
        }
        b.AttackTimer = 0.40f;
    }

    // L6 — slow lobbed "mines" that decelerate to a near-stop and linger
    private void PatternBombardier(ref Enemy b, Vector2 to)
    {
        for (int i = -1; i <= 1; i++)
        {
            var dir = Rotate(to, i * MathHelper.ToRadians(18f));
            _ebullets.Add(new EBullet
            {
                Pos = b.Pos + dir * 36f,
                Vel = dir * 360f,
                Life = 5.0f,
                Radius = 14f,
                Drag = 0.18f
            });
        }
        b.AttackTimer = 1.6f;
    }

    // L7 — single fast accurate shot. Telegraph drawn in DrawEnemies via AttackTimer.
    private void PatternSniperElite(ref Enemy b, Vector2 to)
    {
        _ebullets.Add(new EBullet { Pos = b.Pos + to * 40f, Vel = to * 720f, Life = 2.4f, Radius = 7f });
        b.AttackTimer = 2.0f;
    }

    // L8 — continuously rotating dual shots create a spiral
    private void PatternSpiral(ref Enemy b, Vector2 to)
    {
        b.Wobble += 0.45f;
        var dir = new Vector2(MathF.Cos(b.Wobble), MathF.Sin(b.Wobble));
        _ebullets.Add(new EBullet { Pos = b.Pos + dir * 32f, Vel = dir * 290f, Life = 3.5f, Radius = 8f });
        var neg = -dir;
        _ebullets.Add(new EBullet { Pos = b.Pos + neg * 32f, Vel = neg * 290f, Life = 3.5f, Radius = 8f });
        b.AttackTimer = 0.10f;
    }

    // L9 — aimed shots that split into 3 fragments after ~1s of flight
    private void PatternMultiplier(ref Enemy b, Vector2 to)
    {
        int n = 4;
        for (int i = 0; i < n; i++)
        {
            float t = (i / (float)(n - 1)) * 2f - 1f;
            var dir = Rotate(to, t * MathHelper.ToRadians(11f));
            _ebullets.Add(new EBullet
            {
                Pos = b.Pos + dir * 38f, Vel = dir * 270f,
                Life = 3.5f, Radius = 11f,
                SplitsRemaining = 1, SplitAt = 2.4f  // splits ~1.1s after firing
            });
        }
        b.AttackTimer = 1.3f;
    }

    private void DropCoin(Vector2 pos)
    {
        float ang = (float)_rng.NextDouble() * MathF.Tau;
        float spd = 60f + (float)_rng.NextDouble() * 90f;
        _coins.Add(new Coin
        {
            Pos = pos,
            Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * spd,
            Spin = (float)_rng.NextDouble() * MathF.Tau,
            Life = 12f
        });
    }

    private void CollectCoin(Vector2 pos)
    {
        _coinsTotal++;
        _score += 5;
        Burst(pos, CoinColor, 8, 200f);
        // Queue an upgrade choice for each tier threshold crossed
        while (_coinsTotal >= (_upgradeTier + 1) * CoinsPerUpgrade)
        {
            _upgradeTier++;
            _upgradesPending++;
        }
        if (_upgradesPending > 0 && _state == State.Playing)
            OfferUpgrades();
    }

    private bool IsUpgradeAtCap(string name)
    {
        return name switch
        {
            "RAPID FIRE"  => _shootInterval <= 0.046f,
            "MULTI SHOT"  => _bulletCount >= 5,
            "THRUSTERS"   => _playerSpeed >= 600f,
            "BIG BULLETS" => _bulletRadius >= 20f,
            "COIN MAGNET" => _magnetRadius >= 360f,
            _ => false
        };
    }

    private void OfferUpgrades()
    {
        // Build a fresh pool excluding ones at cap.
        var pool = new List<string>();
        foreach (var u in UpgradePool) if (!IsUpgradeAtCap(u)) pool.Add(u);
        // Defensive fallback so we always have at least one option
        if (pool.Count == 0) { pool.Add("HEAVY ROUNDS"); pool.Add("PIERCE"); pool.Add("MAX HP"); }

        // Fisher-Yates
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        for (int i = 0; i < 3; i++)
            _offerNames[i] = pool[i % pool.Count];

        _state = State.UpgradeChoice;
    }

    private void ApplyChosenUpgrade(int index)
    {
        if (index < 0 || index >= 3) return;
        string name = _offerNames[index];
        switch (name)
        {
            case "RAPID FIRE":   _shootInterval = MathF.Max(0.045f, _shootInterval * 0.75f); break;
            case "HEAVY ROUNDS": _bulletDamage += 1; break;
            case "PIERCE":       _bulletPierce += 1; break;
            case "MULTI SHOT":   _bulletCount = Math.Min(5, _bulletCount + 1); break;
            case "THRUSTERS":    _playerSpeed = MathF.Min(600f, _playerSpeed * 1.20f); break;
            case "MAX HP":       _maxHp += 1; _hp = _maxHp; break;
            case "BIG BULLETS":  _bulletRadius = MathF.Min(22f, _bulletRadius * 1.6f); break;
            case "COIN MAGNET":  _magnetRadius = MathF.Min(420f, _magnetRadius * 1.4f);
                                  _pickupRadius = MathF.Min(60f, _pickupRadius * 1.2f); break;
        }
        _upgradeBanner = name;
        _upgradeBannerTime = 2.2f;
        Burst(_playerPos, CoinColor, 40, 260f);

        _upgradesPending--;
        if (_upgradesPending > 0) OfferUpgrades();
        else _state = State.Playing;
    }

    private static string UpgradeDescription(string name) => name switch
    {
        "RAPID FIRE"  => "FASTER FIRE RATE",
        "HEAVY ROUNDS" => "PLUS ONE DAMAGE",
        "PIERCE"      => "BULLETS PIERCE MORE",
        "MULTI SHOT"  => "ONE EXTRA BULLET",
        "THRUSTERS"   => "MOVE FASTER",
        "MAX HP"      => "PLUS ONE MAX HP",
        "BIG BULLETS" => "LARGER PROJECTILES",
        "COIN MAGNET" => "STRONGER PICKUP PULL",
        _ => ""
    };

    private void Burst(Vector2 pos, Color c, int count, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            float ang = (float)_rng.NextDouble() * MathF.Tau;
            float s = speed * (0.4f + (float)_rng.NextDouble() * 0.8f);
            float life = 0.4f + (float)_rng.NextDouble() * 0.5f;
            _particles.Add(new Particle
            {
                Pos = pos,
                Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * s,
                Color = c,
                Life = life,
                MaxLife = life,
                Size = 2f + (float)_rng.NextDouble() * 2f
            });
        }
    }

    private void UpdateSpaceBackground(float dt)
    {
        _camDrift += new Vector2(8f, 4f) * dt;
        for (int i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            s.Twinkle += dt * (1.5f + s.Depth * 2.0f);
            _stars[i] = s;
        }
        for (int i = 0; i < _nebulae.Count; i++)
        {
            var n = _nebulae[i];
            n.Pos += n.Vel * dt;
            float m = 200f;
            if (n.Pos.X < -m) n.Pos.X = ScreenW + m;
            if (n.Pos.X > ScreenW + m) n.Pos.X = -m;
            if (n.Pos.Y < -m) n.Pos.Y = ScreenH + m;
            if (n.Pos.Y > ScreenH + m) n.Pos.Y = -m;
            _nebulae[i] = n;
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Pos += p.Vel * dt;
            p.Vel *= MathF.Pow(0.5f, dt * 2.2f);
            p.Life -= dt;
            if (p.Life <= 0)
            {
                _particles.RemoveAt(i);
                continue;
            }
            _particles[i] = p;
        }
    }

    // ---------- Drawing ----------
    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(4, 5, 12));
        _spriteBatch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);

        DrawSpace();

        if (_state == State.Playing || _state == State.UpgradeChoice || _state == State.Dead || _state == State.Won)
        {
            DrawParticles();
            DrawCoins();
            DrawBullets();
            DrawEnemyBullets();
            DrawEnemies();
            DrawPlayer();
            DrawHud();
        }

        if (_state == State.Title) DrawTitle();
        else if (_state == State.UpgradeChoice) DrawUpgradeChoice();
        else if (_state == State.Dead) DrawDead();
        else if (_state == State.Won) DrawWon();

        _spriteBatch.End();
        base.Draw(gameTime);
    }

    private void DrawSpace()
    {
        foreach (var n in _nebulae)
        {
            _spriteBatch.Draw(_nebula128, n.Pos, null, n.Tint, 0f, new Vector2(64, 64), n.Size, SpriteEffects.None, 0f);
            _spriteBatch.Draw(_nebula128, n.Pos, null, Color.Lerp(n.Tint, Color.White, 0.25f) * 0.6f, 0f, new Vector2(64, 64), n.Size * 0.55f, SpriteEffects.None, 0f);
        }
        for (int i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            Vector2 pos = s.Pos + _camDrift * s.Depth;
            pos.X = ((pos.X % ScreenW) + ScreenW) % ScreenW;
            pos.Y = ((pos.Y % ScreenH) + ScreenH) % ScreenH;
            float twinkle = 0.75f + 0.25f * MathF.Sin(s.Twinkle);
            float alpha = s.Brightness * twinkle;
            float size = 0.6f + s.Depth * 1.4f;
            int sz = (int)MathF.Ceiling(size);
            _spriteBatch.Draw(_pixel, new Rectangle((int)pos.X, (int)pos.Y, sz, sz), Color.White * alpha);
        }
    }

    private void DrawParticles()
    {
        foreach (var p in _particles)
        {
            float a = p.Life / p.MaxLife;
            var c = p.Color * a;
            float s = p.Size;
            _spriteBatch.Draw(_circle32, p.Pos, null, c, 0f, new Vector2(16, 16), s / 16f, SpriteEffects.None, 0f);
        }
    }

    private void DrawCoins()
    {
        float t = gameTimeSecondsApprox;
        foreach (var c in _coins)
        {
            float pulse = 0.85f + 0.15f * MathF.Sin(t * 6f + c.Spin);
            // outer glow
            _spriteBatch.Draw(_circle32, c.Pos, null, CoinColor * 0.35f, 0f, new Vector2(16, 16), 1.1f, SpriteEffects.None, 0f);
            // squashing x-scale to look like a spinning coin
            float xs = MathF.Abs(MathF.Cos(c.Spin)) * 0.7f + 0.3f;
            _spriteBatch.Draw(_coin16, c.Pos, null, CoinColor * pulse, 0f, new Vector2(8, 8), new Vector2(xs, 1f), SpriteEffects.None, 0f);
            // bright inner highlight
            _spriteBatch.Draw(_coin16, c.Pos, null, new Color(255, 255, 200) * 0.9f, 0f, new Vector2(8, 8), new Vector2(xs * 0.55f, 0.6f), SpriteEffects.None, 0f);
        }
    }

    private void DrawBullets()
    {
        // Visual radius scales with the upgraded collision radius
        float vr = MathF.Max(4f, _bulletRadius);
        foreach (var b in _bullets)
        {
            _spriteBatch.Draw(_circle32, b.Pos, null, RedColor * 0.4f, 0f, new Vector2(16, 16), vr / 6.5f, SpriteEffects.None, 0f);
            _spriteBatch.Draw(_circle32, b.Pos, null, RedColor,       0f, new Vector2(16, 16), vr / 12f,  SpriteEffects.None, 0f);
            _spriteBatch.Draw(_circle32, b.Pos, null, Color.White * 0.7f, 0f, new Vector2(16, 16), vr / 24f, SpriteEffects.None, 0f);
        }
    }

    private void DrawEnemies()
    {
        float t = gameTimeSecondsApprox;
        foreach (var e in _enemies)
        {
            float rotation = MathF.Atan2(e.Vel.Y, e.Vel.X) + MathF.PI / 2f;
            // boss visuals — UFO!
            if (e.Kind == EnemyKind.MiniBoss || e.Kind == EnemyKind.Boss)
            {
                bool isFinal = e.Kind == EnemyKind.Boss;
                float ufoScale = isFinal ? 1.55f : 1.0f;
                float bob = MathF.Sin(t * 1.6f + e.OrbitAngle) * 4f;
                float tilt = MathF.Sin(t * 0.9f + e.OrbitAngle) * 0.10f; // gentle wobble
                var drawPos = e.Pos + new Vector2(0, bob);

                // menacing red aura behind the saucer
                float aurapulse = 0.85f + 0.15f * MathF.Sin(t * 4f + e.OrbitAngle);
                _spriteBatch.Draw(_circle32, drawPos, null, RedColor * (0.35f * aurapulse), 0f, new Vector2(16, 16),
                    e.DrawScale * 2.0f, SpriteEffects.None, 0f);
                _spriteBatch.Draw(_circle32, drawPos, null, RedColor * (0.45f * aurapulse), 0f, new Vector2(16, 16),
                    e.DrawScale * 1.3f, SpriteEffects.None, 0f);

                // tractor beam: warm pulsing glow under the saucer
                float beamPulse = 0.55f + 0.35f * MathF.Sin(t * 5f + e.OrbitAngle);
                var beamOffset = new Vector2(0, _ufo.Height * 0.35f * ufoScale);
                _spriteBatch.Draw(_circle32, drawPos + beamOffset, null,
                    new Color(255, 220, 140) * (0.55f * beamPulse), 0f, new Vector2(16, 16),
                    _ufo.Width * ufoScale / 32f * 0.55f, SpriteEffects.None, 0f);
                _spriteBatch.Draw(_circle32, drawPos + beamOffset, null,
                    new Color(255, 240, 200) * (0.5f * beamPulse), 0f, new Vector2(16, 16),
                    _ufo.Width * ufoScale / 32f * 0.30f, SpriteEffects.None, 0f);

                // L7 SNIPER ELITE telegraph — dashed line at player during charge windup
                if (e.Kind == EnemyKind.MiniBoss && _level == 7 && e.AttackTimer > 0f && e.AttackTimer < 0.7f)
                {
                    var dir = _playerPos - drawPos;
                    float dist = dir.Length();
                    if (dist > 1f)
                    {
                        dir /= dist;
                        int steps = (int)(dist / 14f);
                        int phase = (int)(t * 10f);
                        float hot = 1f - e.AttackTimer / 0.7f;
                        var dotCol = Color.Lerp(new Color(255, 120, 120), new Color(255, 255, 200), hot) * (0.6f + 0.4f * hot);
                        for (int k = 3; k < steps - 1; k++)
                        {
                            if (((k + phase) & 1) == 0) continue;
                            var p = drawPos + dir * (k * 14f);
                            _spriteBatch.Draw(_pixel, new Rectangle((int)p.X - 1, (int)p.Y - 1, 3, 3), dotCol);
                        }
                    }
                }

                // the UFO sprite itself
                _spriteBatch.Draw(_ufo, drawPos, null, Color.White, tilt,
                    new Vector2(_ufo.Width / 2f, _ufo.Height / 2f), ufoScale, SpriteEffects.None, 0f);

                // final boss: extra angry ring around it
                if (isFinal)
                {
                    _spriteBatch.Draw(_ring48, drawPos, null,
                        new Color(255, 80, 80) * (0.6f + 0.4f * MathF.Sin(t * 3f)),
                        t * 0.4f, new Vector2(24, 24), _ufo.Width * ufoScale / 48f * 1.05f,
                        SpriteEffects.None, 0f);
                }
                continue;
            }

            // ----- Sniper: pink-magenta tint + dashed aim line while charging -----
            if (e.Kind == EnemyKind.Sniper)
            {
                var sniperCol = new Color(255, 100, 180);
                // dashed aim line while charging
                if (e.Phase == 0)
                {
                    var dir = _playerPos - e.Pos;
                    float dist = dir.Length();
                    if (dist > 1f)
                    {
                        dir /= dist;
                        int steps = (int)(dist / 14f);
                        int phase = (int)(t * 8f);
                        // dim as the timer counts down (gets hotter when about to fire)
                        float hot = MathHelper.Clamp(1f - e.AttackTimer / 1.4f, 0f, 1f);
                        var dotCol = Color.Lerp(new Color(255, 120, 180), new Color(255, 255, 200), hot) * (0.5f + 0.5f * hot);
                        for (int k = 2; k < steps - 1; k++)
                        {
                            if (((k + phase) & 1) == 0) continue;
                            var p = e.Pos + dir * (k * 14f);
                            _spriteBatch.Draw(_pixel, new Rectangle((int)p.X - 1, (int)p.Y - 1, 3, 3), dotCol);
                        }
                    }
                }
                _spriteBatch.Draw(_circle32, e.Pos, null, sniperCol * 0.35f, 0f, new Vector2(16, 16), 1.6f, SpriteEffects.None, 0f);
                _spriteBatch.Draw(_triangle24, e.Pos, null, sniperCol, rotation, new Vector2(12, 12), e.DrawScale, SpriteEffects.None, 0f);
                continue;
            }

            // ----- Kamikaze: orange triangle + frantic pulse when armed -----
            if (e.Kind == EnemyKind.Kamikaze)
            {
                var kamiCol = new Color(255, 150, 60);
                float pulseRate = (e.Phase == 1) ? 24f : 5f;
                float pulse = 0.65f + 0.35f * MathF.Sin(t * pulseRate + e.Wobble);
                float haloS = (e.Phase == 1) ? 2.6f : 1.6f;
                _spriteBatch.Draw(_circle32, e.Pos, null, kamiCol * (0.45f * pulse), 0f, new Vector2(16, 16), haloS, SpriteEffects.None, 0f);
                // bright inner core blinks when armed
                if (e.Phase == 1)
                    _spriteBatch.Draw(_circle32, e.Pos, null, new Color(255, 230, 200) * pulse, 0f, new Vector2(16, 16), 0.9f, SpriteEffects.None, 0f);
                _spriteBatch.Draw(_triangle24, e.Pos, null, kamiCol, rotation, new Vector2(12, 12), e.DrawScale, SpriteEffects.None, 0f);
                continue;
            }

            // ----- Splitter: purple-pink with 2 orbiting mini-triangles -----
            if (e.Kind == EnemyKind.Splitter)
            {
                var splCol = new Color(220, 80, 160);
                _spriteBatch.Draw(_circle32, e.Pos, null, splCol * 0.32f, 0f, new Vector2(16, 16), 2.0f, SpriteEffects.None, 0f);
                _spriteBatch.Draw(_triangle24, e.Pos, null, splCol, rotation, new Vector2(12, 12), e.DrawScale, SpriteEffects.None, 0f);
                // two mini-triangles orbiting it (telegraph what's about to spawn)
                for (int k = 0; k < 2; k++)
                {
                    float a = t * 2.5f + k * MathF.PI;
                    var off = new Vector2(MathF.Cos(a), MathF.Sin(a)) * 20f;
                    _spriteBatch.Draw(_triangle24, e.Pos + off, null, splCol * 0.85f, rotation, new Vector2(12, 12), 0.65f, SpriteEffects.None, 0f);
                }
                continue;
            }

            // ----- Grunt / Tough / Elite — original red rendering -----
            var ebackAng = rotation - MathF.PI / 2f + MathF.PI;
            var eback = new Vector2(MathF.Cos(ebackAng), MathF.Sin(ebackAng));
            _spriteBatch.Draw(_circle32, e.Pos + eback * 10f, null, RedColor * 0.35f, 0f, new Vector2(16, 16), 0.8f, SpriteEffects.None, 0f);
            float haloScale = e.Kind == EnemyKind.Elite ? 2.0f : (e.Kind == EnemyKind.Tough ? 1.7f : 1.4f);
            _spriteBatch.Draw(_circle32, e.Pos, null, RedColor * 0.25f, 0f, new Vector2(16, 16), haloScale, SpriteEffects.None, 0f);
            if (e.Kind == EnemyKind.Elite)
                _spriteBatch.Draw(_ring48, e.Pos, null, new Color(255, 180, 120) * 0.8f, 0f, new Vector2(24, 24), 0.9f, SpriteEffects.None, 0f);

            _spriteBatch.Draw(_triangle24, e.Pos, null, RedColor, rotation, new Vector2(12, 12), e.DrawScale, SpriteEffects.None, 0f);
        }
    }

    private void DrawEnemyBullets()
    {
        foreach (var b in _ebullets)
        {
            _spriteBatch.Draw(_circle32, b.Pos, null, EBulletColor * 0.45f, 0f, new Vector2(16, 16), b.Radius / 12f, SpriteEffects.None, 0f);
            _spriteBatch.Draw(_circle32, b.Pos, null, EBulletColor, 0f, new Vector2(16, 16), b.Radius / 22f, SpriteEffects.None, 0f);
            _spriteBatch.Draw(_circle32, b.Pos, null, Color.White * 0.7f, 0f, new Vector2(16, 16), b.Radius / 45f, SpriteEffects.None, 0f);
        }
    }

    private void DrawPlayer()
    {
        if (_state == State.Dead) return;
        var mouse = Mouse.GetState();
        var aim = new Vector2(mouse.X, mouse.Y) - _playerPos;
        float rot = (aim.LengthSquared() > 1f) ? MathF.Atan2(aim.Y, aim.X) + MathF.PI / 2f : 0f;

        var back = -new Vector2(MathF.Sin(rot), -MathF.Cos(rot));
        float flicker = 0.8f + 0.2f * MathF.Sin(gameTimeSecondsApprox * 30f);
        var thrustPos = _playerPos + back * 16f;
        _spriteBatch.Draw(_circle32, thrustPos, null, new Color(255, 160, 80) * (0.6f * flicker), 0f, new Vector2(16, 16), 1.1f, SpriteEffects.None, 0f);
        _spriteBatch.Draw(_circle32, thrustPos + back * 6f, null, Color.White * (0.35f * flicker), 0f, new Vector2(16, 16), 0.6f, SpriteEffects.None, 0f);

        // subtle red aura
        _spriteBatch.Draw(_ring48, _playerPos, null, RedColor * 0.45f, 0f, new Vector2(24, 24), 1f, SpriteEffects.None, 0f);

        bool show = _iFrames <= 0f || ((int)(_iFrames * 20f) % 2 == 0);
        if (show)
        {
            var bodyTint = new Color(230, 230, 240);
            _spriteBatch.Draw(_ship32, _playerPos, null, bodyTint, rot, new Vector2(16, 16), 1f, SpriteEffects.None, 0f);
        }
    }

    private void DrawHud()
    {
        // HP pips
        for (int i = 0; i < _maxHp; i++)
        {
            var r = new Rectangle(20 + i * 22, 20, 18, 18);
            _spriteBatch.Draw(_pixel, r, i < _hp ? new Color(240, 70, 90) : new Color(60, 60, 70));
        }

        // Coin counter (top-right)
        _spriteBatch.Draw(_coin16, new Vector2(ScreenW - 140, 28), null, CoinColor, 0f, new Vector2(8, 8), 1.3f, SpriteEffects.None, 0f);
        DrawNumber(_coinsTotal, new Vector2(ScreenW - 120, 20), 4, CoinColor);

        // Progress to next upgrade — small bar
        int coinsForNext = (_upgradeTier + 1) * CoinsPerUpgrade;
        int coinsIntoTier = _coinsTotal - _upgradeTier * CoinsPerUpgrade;
        float frac = MathHelper.Clamp(coinsIntoTier / (float)CoinsPerUpgrade, 0f, 1f);
        int barX = ScreenW - 220, barY = 52, barW = 200, barH = 6;
        _spriteBatch.Draw(_pixel, new Rectangle(barX, barY, barW, barH), new Color(40, 40, 55));
        _spriteBatch.Draw(_pixel, new Rectangle(barX, barY, (int)(barW * frac), barH), CoinColor);

        // Score (bottom-left)
        DrawNumber(_score, new Vector2(20, ScreenH - 40), 4, Color.White);

        // Level X/10 (bottom-right)
        DrawTextBlocks("LEVEL", new Vector2(ScreenW - 170, ScreenH - 28), 3, new Color(200, 220, 255));
        DrawNumber(_level, new Vector2(ScreenW - 110, ScreenH - 40), 4, new Color(200, 220, 255));
        DrawTextBlocks("/", new Vector2(ScreenW - 70, ScreenH - 28), 3, new Color(140, 160, 200));
        DrawNumber(MaxLevel, new Vector2(ScreenW - 55, ScreenH - 40), 4, new Color(140, 160, 200));

        // Level intro banner
        if (_waveTimer > 0f && _enemiesSpawnedThisLevel == 0)
        {
            float t = MathHelper.Clamp(_waveTimer / 2.8f, 0f, 1f);
            float pulse = 0.7f + 0.3f * MathF.Sin(gameTimeSecondsApprox * 4f);
            var bannerCol = Color.White * (t * pulse);
            DrawTextBlocks("LEVEL", new Vector2(ScreenW / 2f, ScreenH / 2f - 140), 8, bannerCol);
            DrawNumber(_level, new Vector2(ScreenW / 2f - 30, ScreenH / 2f - 40), 14, bannerCol);
        }

        // Level cleared banner
        if (_levelClearTimer > 0f && _enemies.Count == 0 && _level < MaxLevel)
        {
            float pulse = 0.7f + 0.3f * MathF.Sin(gameTimeSecondsApprox * 6f);
            DrawTextBlocks("CLEARED", new Vector2(ScreenW / 2f, ScreenH / 2f - 40), 7, new Color(120, 230, 160) * pulse);
        }

        // Boss HP bar (top center)
        if (_bossIndex >= 0 && _bossIndex < _enemies.Count)
        {
            var b = _enemies[_bossIndex];
            float bossFrac = MathHelper.Clamp(b.Hp / (float)b.MaxHp, 0f, 1f);
            int bw = 720, bh = 14;
            int bx = (ScreenW - bw) / 2;
            int by = 60;
            _spriteBatch.Draw(_pixel, new Rectangle(bx - 2, by - 2, bw + 4, bh + 4), new Color(20, 20, 30));
            _spriteBatch.Draw(_pixel, new Rectangle(bx, by, bw, bh), new Color(50, 16, 22));
            _spriteBatch.Draw(_pixel, new Rectangle(bx, by, (int)(bw * bossFrac), bh), RedColor);
            string label = b.Kind == EnemyKind.Boss ? "FINAL BOSS" : MiniBossName(_level);
            DrawTextBlocks(label, new Vector2(ScreenW / 2f, by - 18), 3, Color.White);
        }

        // Upgrade banner — flies up and fades
        if (_upgradeBannerTime > 0f && _upgradeBanner.Length > 0)
        {
            float t = _upgradeBannerTime / 2.2f;
            float rise = (1f - t) * 60f;
            float fade = MathF.Min(1f, t * 2.2f);
            DrawTextBlocks("UPGRADE", new Vector2(ScreenW / 2f, 120 - rise), 4, CoinColor * fade);
            DrawTextBlocks(_upgradeBanner, new Vector2(ScreenW / 2f, 170 - rise), 6, Color.White * fade);
        }
    }

    private float gameTimeSecondsApprox => (float)Environment.TickCount / 1000f;

    private Color RedPulse(float t)
    {
        // Red-leaning pulse for titles. No green/blue palette anywhere.
        float p = 0.5f + 0.5f * MathF.Sin(t * 1.5f);
        return Color.Lerp(new Color(255, 80, 90), new Color(255, 200, 90), p);
    }

    private void DrawTitle()
    {
        float t = gameTimeSecondsApprox;
        DrawTextBlocks("CRIMSON VOID", new Vector2(ScreenW / 2f, ScreenH / 2f - 140), 10, RedPulse(t));
        DrawTextBlocks("WASD MOVE   MOUSE AIM/SHOOT", new Vector2(ScreenW / 2f, ScreenH / 2f - 20), 4, Color.White * 0.9f);
        DrawTextBlocks("COLLECT COINS TO UNLOCK UPGRADES", new Vector2(ScreenW / 2f, ScreenH / 2f + 30), 4, CoinColor * 0.95f);
        DrawTextBlocks("SURVIVE 10 LEVELS", new Vector2(ScreenW / 2f, ScreenH / 2f + 80), 4, new Color(255, 200, 120));
        float blink = 0.6f + 0.4f * MathF.Sin(t * 4f);
        DrawTextBlocks("PRESS SPACE", new Vector2(ScreenW / 2f, ScreenH / 2f + 170), 5, Color.White * blink);
    }

    private void DrawUpgradeChoice()
    {
        // Dim the field but keep it visible
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), new Color(0, 0, 0) * 0.55f);

        // Title
        DrawTextBlocks("CHOOSE AN UPGRADE", new Vector2(ScreenW / 2f, 130), 6, CoinColor);
        if (_upgradesPending > 1)
            DrawTextBlocks("X" + _upgradesPending + " PENDING", new Vector2(ScreenW / 2f, 200), 3, Color.White * 0.7f);

        int cw = 280, ch = 320, gap = 40;
        int totalW = cw * 3 + gap * 2;
        int x0 = (ScreenW - totalW) / 2;
        int cardY = (ScreenH - ch) / 2 + 30;

        for (int i = 0; i < 3; i++)
        {
            int cx = x0 + i * (cw + gap);
            var rect = new Rectangle(cx, cardY, cw, ch);

            // panel
            _spriteBatch.Draw(_pixel, rect, new Color(18, 22, 38));
            // border
            int b = 3;
            var border = new Color(120, 150, 220);
            _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, b), border);
            _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - b, rect.Width, b), border);
            _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, b, rect.Height), border);
            _spriteBatch.Draw(_pixel, new Rectangle(rect.Right - b, rect.Y, b, rect.Height), border);

            // big key number
            DrawTextBlocks((i + 1).ToString(), new Vector2(cx + cw / 2f, cardY + 60), 10, CoinColor);

            // upgrade name
            DrawTextBlocks(_offerNames[i], new Vector2(cx + cw / 2f, cardY + 180), 4, Color.White);

            // description
            DrawTextBlocks(UpgradeDescription(_offerNames[i]), new Vector2(cx + cw / 2f, cardY + 240), 2, new Color(180, 200, 230));
        }

        DrawTextBlocks("PRESS 1 2 OR 3", new Vector2(ScreenW / 2f, cardY + ch + 40), 3, Color.White * 0.75f);
    }

    private void DrawWon()
    {
        var overlay = new Color(0, 0, 0) * 0.55f;
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), overlay);
        DrawTextBlocks("VICTORY", new Vector2(ScreenW / 2f, ScreenH / 2f - 80), 9, RedPulse(gameTimeSecondsApprox));
        DrawTextBlocks("SCORE", new Vector2(ScreenW / 2f - 90, ScreenH / 2f + 10), 4, Color.White);
        DrawNumber(_score, new Vector2(ScreenW / 2f + 10, ScreenH / 2f + 0), 5, Color.White);
        float blink = 0.6f + 0.4f * MathF.Sin(gameTimeSecondsApprox * 4f);
        DrawTextBlocks("SPACE TO PLAY AGAIN", new Vector2(ScreenW / 2f, ScreenH / 2f + 90), 4, Color.White * blink);
    }

    private void DrawDead()
    {
        var overlay = new Color(0, 0, 0) * 0.55f;
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), overlay);
        DrawTextBlocks("DESTROYED", new Vector2(ScreenW / 2f, ScreenH / 2f - 60), 8, new Color(255, 90, 110));
        DrawTextBlocks("SCORE", new Vector2(ScreenW / 2f - 90, ScreenH / 2f + 10), 4, Color.White);
        DrawNumber(_score, new Vector2(ScreenW / 2f + 10, ScreenH / 2f + 0), 5, Color.White);
        float blink = 0.6f + 0.4f * MathF.Sin(gameTimeSecondsApprox * 4f);
        DrawTextBlocks("SPACE TO RETRY", new Vector2(ScreenW / 2f, ScreenH / 2f + 90), 4, Color.White * blink);
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
            if (Glyphs.TryGetValue(ch, out var rows))
                totalW += (rows[0].Length + 1) * pixelSize;
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

    private static Dictionary<char, string[]> BuildGlyphs()
    {
        return new Dictionary<char, string[]>
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
        };
    }
}
