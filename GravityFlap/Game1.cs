using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace GravityFlap;

public class Game1 : Game
{
    // ---------- Core ----------
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;

    private const int ScreenW = 900;
    private const int ScreenH = 640;

    // ---------- Textures ----------
    private Texture2D _pixel;
    private Texture2D _circle32;
    private Texture2D _bird;
    private Texture2D _birdWing;
    private Texture2D _pipeBody;
    private Texture2D _pipeCap;
    private Texture2D[] _clouds;

    // ---------- Bird ----------
    private Vector2 _birdPos;
    private float _birdVy;
    private int _gravityDir = 1;         // +1 = down, -1 = up
    private const float GravityStrength = 2000f;
    private const float TerminalVy = 720f;
    private float _wingPhase = 0f;
    private float _birdRot = 0f;          // smoothed rotation toward velocity direction

    // ---------- Pipes ----------
    private struct Pipe
    {
        public float X;
        public float GapY;
        public float GapHeight;
        public bool Scored;
    }
    private readonly List<Pipe> _pipes = new();
    private const float PipeWidth = 78f;
    private const float PipeSpacing = 280f;
    private float _scrollSpeed = 220f;

    // ---------- Game state ----------
    private enum State { Title, Playing, Dead }
    private State _state = State.Title;
    private int _score = 0;
    private int _highScore = 0;
    private float _stateTimer = 0f;

    // ---------- Background ----------
    private struct Star { public Vector2 Pos; public float Depth; public float Brightness; }
    private readonly List<Star> _stars = new();

    private struct Building
    {
        public float X;
        public float Width;
        public float Height;
        public byte WindowSeed;
        public bool Antenna;     // antenna with a blinking red light on top
        public bool Pyramid;     // pyramid-shaped roof
    }
    private readonly List<Building> _farBuildings = new();
    private readonly List<Building> _nearBuildings = new();
    private float _farScroll = 0f;
    private float _nearScroll = 0f;
    private const int GroundHeight = 56;
    private float _groundScroll = 0f;

    // ---------- Particles ----------
    private struct Particle { public Vector2 Pos, Vel; public Color Color; public float Life, MaxLife, Size; }
    private readonly List<Particle> _particles = new();

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
        Window.Title = "Gravity Flap";
    }

    protected override void Initialize()
    {
        ResetBird();
        for (int i = 0; i < 80; i++)
        {
            _stars.Add(new Star
            {
                Pos = new Vector2(_rng.Next(ScreenW), _rng.Next(ScreenH)),
                Depth = 0.2f + (float)_rng.NextDouble() * 0.8f,
                Brightness = 0.4f + (float)_rng.NextDouble() * 0.6f
            });
        }
        GenerateCity();
        base.Initialize();
    }

    private void GenerateCity()
    {
        _farBuildings.Clear();
        _nearBuildings.Clear();
        // Far layer: smaller, shorter, packed
        float x = 0f;
        while (x < ScreenW * 2)
        {
            float w = 22f + (float)_rng.NextDouble() * 28f;
            float h = 36f + (float)_rng.NextDouble() * 70f;
            _farBuildings.Add(new Building
            {
                X = x, Width = w, Height = h,
                WindowSeed = (byte)_rng.Next(256),
                Antenna = false,
                Pyramid = _rng.NextDouble() < 0.18
            });
            x += w + 1f + (float)_rng.NextDouble() * 4f;
        }
        // Near layer: taller, more space between, occasional antennas
        x = 0f;
        while (x < ScreenW * 2)
        {
            float w = 38f + (float)_rng.NextDouble() * 56f;
            float h = 70f + (float)_rng.NextDouble() * 130f;
            _nearBuildings.Add(new Building
            {
                X = x, Width = w, Height = h,
                WindowSeed = (byte)_rng.Next(256),
                Antenna = _rng.NextDouble() < 0.22,
                Pyramid = _rng.NextDouble() < 0.12
            });
            x += w + 3f + (float)_rng.NextDouble() * 9f;
        }
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _circle32 = MakeCircle(32);
        _bird = MakeBird(48);
        _birdWing = MakeWing(16, 9);
        _pipeBody = MakePipeBody(64, 32);
        _pipeCap = MakePipeCap(88, 28);
        _clouds = new[]
        {
            MakeCloud(112, 52, seed: 7),
            MakeCloud(140, 60, seed: 13),
            MakeCloud(96, 46, seed: 23),
            MakeCloud(120, 50, seed: 41),
        };
    }

    private Texture2D MakeCloud(int w, int h, int seed)
    {
        // Cartoon cumulus: layered puffs (big body + shoulders + edges + random small top humps)
        // create an irregular bumpy silhouette. Soft sagging bottom, no perfectly flat line.
        var rng = new Random(seed);
        var tex = new Texture2D(GraphicsDevice, w, h);
        var data = new Color[w * h];

        var puffs = new List<(float cx, float cy, float r)>();
        float baseY = h * 0.78f; // overall cloud baseline (bumps hang slightly below for sag)

        // BODY: one big puff in the center carries the silhouette
        puffs.Add((w * 0.50f, baseY - h * 0.32f, h * 0.46f));

        // SHOULDERS: two medium puffs flanking the body
        float shoulderJitter = (float)(rng.NextDouble() - 0.5) * h * 0.05f;
        puffs.Add((w * 0.28f, baseY - h * 0.26f + shoulderJitter, h * 0.34f + (float)rng.NextDouble() * 5f));
        puffs.Add((w * 0.72f, baseY - h * 0.26f - shoulderJitter, h * 0.32f + (float)rng.NextDouble() * 5f));

        // EDGES: small puffs at the far ends to round off the silhouette
        puffs.Add((w * 0.10f, baseY - h * 0.18f, h * 0.22f + (float)rng.NextDouble() * 4f));
        puffs.Add((w * 0.90f, baseY - h * 0.18f, h * 0.22f + (float)rng.NextDouble() * 4f));

        // TOP HUMPS: 2-3 random small bumps for extra silhouette irregularity
        int extraHumps = 2 + rng.Next(2);
        for (int i = 0; i < extraHumps; i++)
        {
            float cx = w * (0.22f + (float)rng.NextDouble() * 0.56f);
            float cy = baseY - h * (0.45f + (float)rng.NextDouble() * 0.18f);
            float r = h * 0.18f + (float)rng.NextDouble() * 4f;
            puffs.Add((cx, cy, r));
        }

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // Soft cutoff at the bottom — slight sag rather than razor flat
            if (y > baseY + 2)
            {
                data[y * w + x] = Color.Transparent;
                continue;
            }

            float maxAlpha = 0f;
            foreach (var p in puffs)
            {
                float dx = x - p.cx, dy = y - p.cy;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float a = MathHelper.Clamp(p.r - d, 0f, 1f);
                if (a > maxAlpha) maxAlpha = a;
            }

            // Tail off alpha at the very bottom edge for a gentle baseline
            if (y > baseY)
            {
                float fade = 1f - (y - baseY) / 2f;
                maxAlpha *= MathHelper.Clamp(fade, 0f, 1f);
            }

            if (maxAlpha > 0.04f)
            {
                // Gray underside strip — gives the cloud volume
                float underT = MathHelper.Clamp((y - (baseY - 9f)) / 10f, 0f, 1f);
                byte v = (byte)(255 - underT * 36);
                data[y * w + x] = new Color(v, v, v, (byte)(maxAlpha * 255));
            }
            else
            {
                data[y * w + x] = Color.Transparent;
            }
        }
        tex.SetData(data);
        return tex;
    }

    // ---------- Procedural textures ----------
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

    private Texture2D MakeBird(int size)
    {
        // Side-view bird facing RIGHT: yellow body centered in the texture, with the beak
        // sticking out PAST the body circle in front, and tail feathers out the back.
        var tex = new Texture2D(GraphicsDevice, size, size);
        var data = new Color[size * size];
        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float bodyR = size * 0.30f;
        float bellyR = size * 0.23f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - cx, dy = y - cy;
            float d = MathF.Sqrt(dx * dx + dy * dy);

            Color c = Color.Transparent;

            // ---- Body (yellow with dark outline) ----
            if (d <= bodyR)
            {
                float t = d / bodyR;
                byte r = 255;
                byte g = (byte)(215 - t * 35);
                byte b = (byte)(70 - t * 20);
                c = new Color(r, g, b);
                if (d > bodyR - 1.5f) c = new Color(110, 60, 0);
            }

            // ---- Cream belly (ellipse below body center) ----
            float bx = x - cx;
            float by = y - (cy + bodyR * 0.28f);
            float belEx = bodyR * 0.85f, belEy = bellyR * 0.65f;
            if ((bx * bx) / (belEx * belEx) + (by * by) / (belEy * belEy) < 1f && d < bodyR - 1.5f)
            {
                c = new Color(255, 250, 220);
            }

            // ---- Tail feathers (red-orange wedge extending LEFT past the body) ----
            float tailBaseX = cx - bodyR + 2f;
            float tailTipX  = cx - bodyR - 7f;
            if (x >= tailTipX && x <= tailBaseX)
            {
                float tt = (x - tailTipX) / (tailBaseX - tailTipX); // 0 at tip, 1 at base
                float halfH = tt * 7f;
                if (MathF.Abs(y - cy) < halfH)
                {
                    c = new Color(230, 110, 40);
                    if (MathF.Abs(y - cy) > halfH - 1.1f) c = new Color(110, 50, 0);
                    // 2-feather split lines for that "tail feather" look
                    float dyt = y - cy;
                    if (MathF.Abs(dyt) > 1.2f && MathF.Abs(MathF.Abs(dyt) - 3f) < 0.6f) c = new Color(160, 70, 20);
                }
            }

            // ---- Eye: bigger white sclera + dark pupil with a white catchlight ----
            float exC = cx + bodyR * 0.32f;
            float eyC = cy - bodyR * 0.28f;
            float exDx = x - exC, exDy = y - eyC;
            float eyeD = MathF.Sqrt(exDx * exDx + exDy * exDy);
            if (eyeD < 5.2f) c = Color.White;
            if (eyeD < 5.2f && eyeD > 4.2f) c = new Color(40, 30, 20); // ring
            if (eyeD < 2.8f) c = new Color(20, 20, 30);
            // catchlight
            if ((x - (exC - 0.7f)) * (x - (exC - 0.7f)) + (y - (eyC - 0.9f)) * (y - (eyC - 0.9f)) < 0.9f)
                c = Color.White;

            // ---- Beak (orange triangle, planted at the body edge, pointing forward) ----
            // Base attaches just inside the body edge for a seamless connection; tip sits
            // well in FRONT of the body circle.
            float beakBaseX = cx + bodyR - 2f;
            float beakTipX  = cx + bodyR + 9f;
            if (x >= beakBaseX && x < beakTipX)
            {
                float tBeak = (x - beakBaseX) / (beakTipX - beakBaseX); // 0 base -> 1 tip
                float beakHalfH = (1f - tBeak) * 6.5f;
                if (MathF.Abs(y - cy) < beakHalfH)
                {
                    c = new Color(255, 140, 30);
                    // dark outline edges
                    if (y < cy - beakHalfH + 1.1f || y > cy + beakHalfH - 1.1f)
                        c = new Color(180, 90, 10);
                    // mouth crease
                    if (MathF.Abs(y - cy) < 0.6f && x > beakBaseX + 1f && tBeak < 0.85f)
                        c = new Color(180, 90, 10);
                }
            }

            data[y * size + x] = c;
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakeWing(int w, int h)
    {
        // Crescent/teardrop wing — tip points RIGHT, shoulder is at the LEFT-MIDDLE.
        // Origin (set at draw time) goes at the shoulder so the wing pivots from there.
        var tex = new Texture2D(GraphicsDevice, w, h);
        var data = new Color[w * h];
        float cy = (h - 1) / 2f;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = x / (float)(w - 1);     // 0 shoulder ... 1 tip
            // Wing tapers: thickest near the shoulder, narrowing toward the tip
            float halfH = (1f - nx * 0.85f) * (h * 0.5f - 0.5f);
            float dy = y - cy;

            Color c = Color.Transparent;
            if (MathF.Abs(dy) <= halfH && nx > 0.03f && nx < 0.97f)
            {
                // top half slightly brighter; bottom half darker (gives sense of curvature)
                float t = (dy + halfH) / (halfH * 2f); // 0 top -> 1 bottom
                byte r = (byte)(235 - t * 55);
                byte g = (byte)(125 - t * 55);
                byte b = (byte)(55  - t * 25);
                c = new Color(r, g, b);

                // dark outline along edges + tip
                if (MathF.Abs(dy) > halfH - 0.9f || nx > 0.90f || nx < 0.08f)
                    c = new Color(110, 50, 0);
            }
            data[y * w + x] = c;
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakePipeBody(int w, int h)
    {
        var tex = new Texture2D(GraphicsDevice, w, h);
        var data = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = x / (float)(w - 1);
            float bell = 1f - MathF.Abs(nx - 0.5f) * 2f;
            byte g = (byte)(140 + bell * 90);
            byte r = (byte)(20 + bell * 40);
            byte b = (byte)(40 + bell * 20);
            if (x == 0 || x == w - 1) { r = 12; g = 60; b = 24; }
            data[y * w + x] = new Color(r, g, b);
        }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakePipeCap(int w, int h)
    {
        var tex = new Texture2D(GraphicsDevice, w, h);
        var data = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = x / (float)(w - 1);
            float bell = 1f - MathF.Abs(nx - 0.5f) * 2f;
            byte g = (byte)(150 + bell * 85);
            byte r = (byte)(25 + bell * 35);
            byte b = (byte)(45 + bell * 20);
            bool outline = (x < 2 || x > w - 3 || y < 2 || y > h - 3);
            if (outline) { r = 12; g = 60; b = 24; }
            data[y * w + x] = new Color(r, g, b);
        }
        tex.SetData(data);
        return tex;
    }

    // ---------- Game flow ----------
    private void ResetBird()
    {
        _birdPos = new Vector2(ScreenW * 0.30f, ScreenH * 0.45f);
        _birdVy = 0f;
        _gravityDir = 1;
        _birdRot = 0f;
        _wingPhase = 0f;
    }

    private void StartGame()
    {
        _state = State.Playing;
        _score = 0;
        _stateTimer = 0f;
        _pipes.Clear();
        _particles.Clear();
        ResetBird();
        float startX = ScreenW + 150f;
        for (int i = 0; i < 5; i++)
            _pipes.Add(NewPipe(startX + i * PipeSpacing));
    }

    private Pipe NewPipe(float x)
    {
        float gap = 200f - Math.Min(60f, _score * 1.0f);
        float topMargin = 70f;
        float bottomMargin = 60f + GroundHeight;
        float playH = ScreenH - topMargin - bottomMargin;
        float gapY = topMargin + (float)_rng.NextDouble() * (playH - gap);
        gapY += gap / 2f;
        return new Pipe { X = x, GapY = gapY, GapHeight = gap, Scored = false };
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var kb = Keyboard.GetState();
        var mouse = Mouse.GetState();

        if (kb.IsKeyDown(Keys.Escape)) Exit();

        bool flapPressed = Pressed(kb, Keys.Space) ||
                           Pressed(kb, Keys.W) ||
                           Pressed(kb, Keys.Up) ||
                           (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton != ButtonState.Pressed);

        _stateTimer += dt;

        switch (_state)
        {
            case State.Title:
                if (flapPressed) StartGame();
                break;
            case State.Playing:
                UpdatePlaying(dt, flapPressed);
                break;
            case State.Dead:
                if (flapPressed && _stateTimer > 0.6f) StartGame();
                break;
        }

        UpdateParticles(dt);

        // Parallax scroll: city always drifts, gives life to the title screen too
        _farScroll = (_farScroll + dt * 14f)  % (ScreenW * 2);
        _nearScroll = (_nearScroll + dt * 40f) % (ScreenW * 2);
        _groundScroll = (_groundScroll + dt * (_state == State.Playing ? _scrollSpeed : 60f)) % 32f;

        _prevKb = kb;
        _prevMouse = mouse;
        base.Update(gameTime);
    }

    private bool Pressed(KeyboardState kb, Keys k) => kb.IsKeyDown(k) && !_prevKb.IsKeyDown(k);

    private void UpdatePlaying(float dt, bool flap)
    {
        if (flap)
        {
            // The core twist: each flap REVERSES gravity. Velocity is zeroed for a snappy feel.
            _gravityDir *= -1;
            _birdVy = 0f;
            SpawnFlapPuff();
        }

        _birdVy += GravityStrength * _gravityDir * dt;
        _birdVy = MathHelper.Clamp(_birdVy, -TerminalVy, TerminalVy);
        _birdPos.Y += _birdVy * dt;

        _wingPhase += dt * (8f + MathF.Abs(_birdVy) * 0.02f);

        float targetRot = MathHelper.Clamp(_birdVy / 700f, -0.8f, 0.8f);
        _birdRot = MathHelper.Lerp(_birdRot, targetRot, 1f - MathF.Pow(0.001f, dt));

        for (int i = 0; i < _pipes.Count; i++)
        {
            var p = _pipes[i];
            p.X -= _scrollSpeed * dt;
            if (!p.Scored && p.X + PipeWidth / 2f < _birdPos.X)
            {
                p.Scored = true;
                _score++;
                if (_score > _highScore) _highScore = _score;
                SpawnScorePop();
            }
            _pipes[i] = p;
        }
        while (_pipes.Count > 0 && _pipes[0].X < -PipeWidth)
        {
            _pipes.RemoveAt(0);
            float lastX = _pipes.Count > 0 ? _pipes[_pipes.Count - 1].X : ScreenW;
            _pipes.Add(NewPipe(lastX + PipeSpacing));
        }

        _scrollSpeed = MathF.Min(360f, 220f + _score * 2f);

        if (_birdPos.Y < 14f || _birdPos.Y > ScreenH - GroundHeight - 8f)
        {
            Die();
            return;
        }

        float birdR = 14f;
        foreach (var p in _pipes)
        {
            float leftP = p.X - PipeWidth / 2f;
            float rightP = p.X + PipeWidth / 2f;
            if (_birdPos.X + birdR < leftP) continue;
            if (_birdPos.X - birdR > rightP) continue;

            float gapTop = p.GapY - p.GapHeight / 2f;
            float gapBot = p.GapY + p.GapHeight / 2f;
            if (_birdPos.Y - birdR < gapTop || _birdPos.Y + birdR > gapBot)
            {
                Die();
                return;
            }
        }
    }

    private void Die()
    {
        _state = State.Dead;
        _stateTimer = 0f;
        for (int i = 0; i < 30; i++)
        {
            float ang = (float)_rng.NextDouble() * MathF.Tau;
            float speed = 80f + (float)_rng.NextDouble() * 220f;
            _particles.Add(new Particle
            {
                Pos = _birdPos,
                Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * speed,
                Color = new Color(255, 200, 80),
                Life = 0.7f, MaxLife = 0.7f, Size = 3f
            });
        }
    }

    private void SpawnFlapPuff()
    {
        for (int i = 0; i < 6; i++)
        {
            float ang = (float)_rng.NextDouble() * MathF.Tau;
            float speed = 60f + (float)_rng.NextDouble() * 80f;
            _particles.Add(new Particle
            {
                Pos = _birdPos,
                Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * speed * 0.6f,
                Color = new Color(255, 240, 200),
                Life = 0.35f, MaxLife = 0.35f, Size = 2.2f
            });
        }
    }

    private void SpawnScorePop()
    {
        for (int i = 0; i < 10; i++)
        {
            float ang = (float)_rng.NextDouble() * MathF.Tau;
            float speed = 80f + (float)_rng.NextDouble() * 140f;
            _particles.Add(new Particle
            {
                Pos = _birdPos,
                Vel = new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * speed,
                Color = new Color(120, 230, 255),
                Life = 0.4f, MaxLife = 0.4f, Size = 2.5f
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

    // ---------- Drawing ----------
    protected override void Draw(GameTime gameTime)
    {
        _spriteBatch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);

        DrawBackground();
        DrawPipes();
        DrawGround();
        DrawParticles();
        DrawBird();
        DrawHud();

        if (_state == State.Title) DrawTitleOverlay();
        else if (_state == State.Dead) DrawDeathOverlay();

        _spriteBatch.End();
        base.Draw(gameTime);
    }

    private void DrawBackground()
    {
        // Sky gradient flips with gravity direction.
        bool flipped = _gravityDir < 0;
        Color topSky    = flipped ? new Color(255, 170, 110) : new Color(85, 170, 230);
        Color bottomSky = flipped ? new Color(70, 100, 160)  : new Color(255, 200, 130);

        int slabs = 30;
        int slabH = ScreenH / slabs + 1;
        for (int i = 0; i < slabs; i++)
        {
            float t = i / (float)(slabs - 1);
            var c = Color.Lerp(topSky, bottomSky, t);
            _spriteBatch.Draw(_pixel, new Rectangle(0, i * slabH, ScreenW, slabH), c);
        }

        // Stars during flipped (night) state
        if (flipped)
        {
            foreach (var s in _stars)
                _spriteBatch.Draw(_pixel, new Rectangle((int)s.Pos.X, (int)s.Pos.Y, 2, 2), Color.White * (s.Brightness * 0.6f));
        }

        // Puffy clouds — two layers at different speeds & tints for depth
        DrawClouds(flipped);

        // ---- Far city layer (atmospheric haze) ----
        Color farTint    = flipped ? new Color(45, 50, 95)  : new Color(70, 90, 130);
        Color farWindow  = flipped ? new Color(220, 220, 255) * 0.7f : new Color(255, 235, 200) * 0.55f;
        foreach (var b in _farBuildings)
        {
            float bx = b.X - _farScroll;
            DrawBuilding(bx, b, farTint, farWindow, smallWindows: true);
            DrawBuilding(bx + ScreenW * 2, b, farTint, farWindow, smallWindows: true);
        }

        // ---- Near city layer (darker, in front of far) ----
        Color nearTint   = flipped ? new Color(20, 22, 50)  : new Color(28, 36, 70);
        Color nearWindow = flipped ? new Color(180, 220, 255)        : new Color(255, 225, 130);
        foreach (var b in _nearBuildings)
        {
            float bx = b.X - _nearScroll;
            DrawBuilding(bx, b, nearTint, nearWindow, smallWindows: false);
            DrawBuilding(bx + ScreenW * 2, b, nearTint, nearWindow, smallWindows: false);
        }
    }

    private void DrawClouds(bool flipped)
    {
        // Clouds are SOLID white (not translucent). When night-flipped we tint them a cool gray.
        Color farCol  = flipped ? new Color(170, 185, 220) : Color.White;
        Color nearCol = flipped ? new Color(210, 220, 245) : Color.White;

        // Far layer — smaller, slower
        for (int i = 0; i < 5; i++)
        {
            var tex = _clouds[(i * 3) % _clouds.Length];
            float wrap = ScreenW + tex.Width + 60;
            float cx = ((i * 211 - (int)(_stateTimer * 9)) % wrap + wrap) % wrap - tex.Width;
            float cy = 30 + (i * 73) % 180;
            _spriteBatch.Draw(tex, new Vector2(cx, cy), null, farCol, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0f);
        }

        // Near layer — bigger, faster
        for (int i = 0; i < 4; i++)
        {
            var tex = _clouds[(i * 5 + 1) % _clouds.Length];
            float wrap = ScreenW + tex.Width + 80;
            float cx = ((i * 281 - (int)(_stateTimer * 22)) % wrap + wrap) % wrap - tex.Width;
            float cy = 70 + (i * 91) % 200;
            _spriteBatch.Draw(tex, new Vector2(cx, cy), null, nearCol, 0f, Vector2.Zero, 1.0f, SpriteEffects.None, 0f);
        }
    }

    private void DrawBuilding(float bx, Building b, Color tint, Color windowCol, bool smallWindows)
    {
        if (bx + b.Width < 0 || bx > ScreenW) return;
        int groundY = ScreenH - GroundHeight;
        int top = groundY - (int)b.Height;
        int bw = (int)b.Width;
        int bh = (int)b.Height;

        // body
        _spriteBatch.Draw(_pixel, new Rectangle((int)bx, top, bw, bh), tint);

        // pyramid roof on top of body
        if (b.Pyramid)
        {
            int peak = (int)Math.Min(18, b.Width * 0.45f);
            for (int ty = 0; ty < peak; ty++)
            {
                float t = ty / (float)(peak - 1);
                int slabW = (int)(b.Width * (1f - t));
                int sx = (int)bx + (bw - slabW) / 2;
                _spriteBatch.Draw(_pixel, new Rectangle(sx, top - ty - 1, slabW, 1), tint);
            }
        }

        // antenna with a blinking red aircraft warning light
        if (b.Antenna)
        {
            int aH = 14;
            _spriteBatch.Draw(_pixel, new Rectangle((int)bx + bw / 2 - 1, top - aH, 2, aH), tint);
            // blink at ~1 Hz
            bool on = ((int)(_stateTimer * 2) & 1) == 0;
            var lightCol = on ? new Color(255, 90, 80) : new Color(120, 30, 30);
            _spriteBatch.Draw(_pixel, new Rectangle((int)bx + bw / 2 - 2, top - aH - 3, 4, 3), lightCol);
        }

        // windows — fixed cell grid with a deterministic lit pattern
        int wW = smallWindows ? 2 : 3;
        int wH = smallWindows ? 2 : 4;
        int cellW = smallWindows ? 5 : 7;
        int cellH = smallWindows ? 6 : 8;
        int cols = Math.Max(1, bw / cellW);
        int rows = Math.Max(1, (bh - 6) / cellH);
        int offsetX = (bw - cols * cellW) / 2 + (cellW - wW) / 2;
        int offsetY = 5;
        for (int wy = 0; wy < rows; wy++)
        for (int wx = 0; wx < cols; wx++)
        {
            int hash = (b.WindowSeed * 73 + wx * 31 + wy * 17) & 0xFF;
            int threshold = smallWindows ? 45 : 65;
            if (hash < threshold)
            {
                int wpx = (int)bx + offsetX + wx * cellW;
                int wpy = top + offsetY + wy * cellH;
                _spriteBatch.Draw(_pixel, new Rectangle(wpx, wpy, wW, wH), windowCol);
            }
        }
    }

    private void DrawGround()
    {
        bool flipped = _gravityDir < 0;
        int groundY = ScreenH - GroundHeight;

        // dirt strip
        Color dirt  = flipped ? new Color(60, 50, 70)  : new Color(120, 90, 55);
        Color dirtD = flipped ? new Color(35, 28, 45)  : new Color(75, 55, 30);
        _spriteBatch.Draw(_pixel, new Rectangle(0, groundY + 12, ScreenW, GroundHeight - 12), dirt);
        // dirt detail rows
        for (int y = groundY + 16; y < ScreenH; y += 6)
            _spriteBatch.Draw(_pixel, new Rectangle(0, y, ScreenW, 1), dirtD);

        // grass row
        Color grass  = flipped ? new Color(60, 110, 90)  : new Color(120, 175, 70);
        Color grassD = flipped ? new Color(35, 70, 60)   : new Color(70, 120, 40);
        _spriteBatch.Draw(_pixel, new Rectangle(0, groundY, ScreenW, 12), grass);

        // little grass tufts that scroll with the foreground speed
        int tuftSpacing = 24;
        int n = ScreenW / tuftSpacing + 3;
        for (int i = 0; i < n; i++)
        {
            float gx = i * tuftSpacing - (_groundScroll % tuftSpacing);
            _spriteBatch.Draw(_pixel, new Rectangle((int)gx, groundY - 3, 2, 4), grassD);
            _spriteBatch.Draw(_pixel, new Rectangle((int)gx + 5, groundY - 2, 2, 3), grass);
        }
    }

    private void DrawPipes()
    {
        foreach (var p in _pipes)
        {
            float leftP = p.X - PipeWidth / 2f;
            float gapTop = p.GapY - p.GapHeight / 2f;
            float gapBot = p.GapY + p.GapHeight / 2f;

            int capH = 28;
            int topPipeBodyH = (int)gapTop - capH;
            if (topPipeBodyH > 0)
                _spriteBatch.Draw(_pipeBody, new Rectangle((int)leftP, 0, (int)PipeWidth, topPipeBodyH), Color.White);
            _spriteBatch.Draw(_pipeCap, new Rectangle((int)(leftP - 5), (int)gapTop - capH, (int)PipeWidth + 10, capH), Color.White);

            int bottomPipeStartY = (int)gapBot + capH;
            int bottomPipeBodyH = ScreenH - bottomPipeStartY;
            if (bottomPipeBodyH > 0)
                _spriteBatch.Draw(_pipeBody, new Rectangle((int)leftP, bottomPipeStartY, (int)PipeWidth, bottomPipeBodyH), Color.White);
            _spriteBatch.Draw(_pipeCap, new Rectangle((int)(leftP - 5), (int)gapBot, (int)PipeWidth + 10, capH), Color.White);
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

    private void DrawBird()
    {
        // The bird flips 180° when gravity is up
        float baseRot = (_gravityDir < 0) ? MathF.PI : 0f;
        float rot = baseRot + _birdRot * (_gravityDir < 0 ? -1 : 1);

        float flutter = MathF.Sin(_wingPhase) * 1.2f;
        var pos = _birdPos + new Vector2(0, flutter);

        // soft drop-shadow
        _spriteBatch.Draw(_circle32, pos, null, new Color(0, 0, 0) * 0.18f, 0f, new Vector2(16, 16), 1.4f, SpriteEffects.None, 0f);

        // body
        _spriteBatch.Draw(_bird, pos, null, Color.White, rot,
            new Vector2(_bird.Width / 2f, _bird.Height / 2f), 1f, SpriteEffects.None, 0f);

        // ---- Animated wing layered on top, pivoting at the shoulder ----
        // Shoulder offset in bird-local coordinates (back-and-up from body center)
        Vector2 shoulderLocal = new Vector2(-1.5f, -2.5f);
        float cosR = MathF.Cos(rot), sinR = MathF.Sin(rot);
        Vector2 shoulderWorld = new Vector2(
            shoulderLocal.X * cosR - shoulderLocal.Y * sinR,
            shoulderLocal.X * sinR + shoulderLocal.Y * cosR);

        // Wing swings ±~50° around the shoulder, in sync with wingPhase.
        // Bias toward swinging "down" relative to the bird, so the wing reads as flapping.
        float flapAngle = MathF.Sin(_wingPhase) * 1.0f + 0.15f;

        _spriteBatch.Draw(
            _birdWing,
            pos + shoulderWorld,
            null,
            Color.White,
            rot + flapAngle,
            new Vector2(1f, _birdWing.Height / 2f), // origin at shoulder side of the sprite
            1f,
            SpriteEffects.None,
            0f);
    }

    private void DrawHud()
    {
        if (_state == State.Playing)
        {
            DrawNumber(_score, new Vector2(ScreenW / 2f - DigitWidth(_score, 8) / 2f, 24), 8, Color.White);
        }

        DrawTextBlocks("G", new Vector2(ScreenW - 56, 24), 3, Color.White * 0.8f);
        var arrow = _gravityDir > 0 ? "DN" : "UP";
        DrawTextBlocks(arrow, new Vector2(ScreenW - 28, 24), 3, _gravityDir > 0 ? new Color(120, 200, 255) : new Color(255, 180, 120));
    }

    private void DrawTitleOverlay()
    {
        DrawTextBlocks("GRAVITY FLAP", new Vector2(ScreenW / 2f, ScreenH / 2f - 120), 8, Color.White);
        DrawTextBlocks("EACH FLAP REVERSES GRAVITY", new Vector2(ScreenW / 2f, ScreenH / 2f - 50), 3, new Color(255, 230, 150));
        DrawTextBlocks("SPACE OR CLICK TO FLAP", new Vector2(ScreenW / 2f, ScreenH / 2f), 3, Color.White * 0.85f);

        float blink = 0.6f + 0.4f * MathF.Sin(_stateTimer * 4f);
        DrawTextBlocks("PRESS ANY FLAP TO START", new Vector2(ScreenW / 2f, ScreenH / 2f + 100), 4, Color.White * blink);
    }

    private void DrawDeathOverlay()
    {
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, ScreenW, ScreenH), Color.Black * 0.45f);
        DrawTextBlocks("CRASH", new Vector2(ScreenW / 2f, ScreenH / 2f - 90), 9, new Color(255, 100, 110));
        DrawTextBlocks("SCORE", new Vector2(ScreenW / 2f - 90, ScreenH / 2f - 10), 3, Color.White);
        DrawNumber(_score, new Vector2(ScreenW / 2f + 10, ScreenH / 2f - 20), 5, Color.White);
        DrawTextBlocks("BEST", new Vector2(ScreenW / 2f - 90, ScreenH / 2f + 40), 3, Color.White * 0.7f);
        DrawNumber(_highScore, new Vector2(ScreenW / 2f + 10, ScreenH / 2f + 30), 4, new Color(255, 220, 120));

        if (_stateTimer > 0.6f)
        {
            float blink = 0.6f + 0.4f * MathF.Sin(_stateTimer * 5f);
            DrawTextBlocks("FLAP TO RETRY", new Vector2(ScreenW / 2f, ScreenH / 2f + 110), 4, Color.White * blink);
        }
    }

    // ---------- Pixel-block "font" ----------
    private static readonly Dictionary<char, string[]> Glyphs = BuildGlyphs();
    private int DigitWidth(int v, int pixelSize)
    {
        string s = v.ToString();
        int total = 0;
        foreach (var ch in s)
            if (Glyphs.TryGetValue(ch, out var rows)) total += (rows[0].Length + 1) * pixelSize;
        return total;
    }
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
    };
}
