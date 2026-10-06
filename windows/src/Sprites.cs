// Pixel-art mascots, drawn in code. Three bodies (monkey Xing, mini pig, Claude spark) play the same poses;
// accessories (hat, glasses, headphones, scarf, crown, umbrella) are drawn on top of any body.
// Stage is 40x44: an 8px headroom band on top for hats/umbrella, then the old 40x36 scene.
// All animation cycles loop in Frames (48) ticks, so poses can be pre-rendered or exported as sprite sheets.
using System;
using System.Collections.Generic;

namespace XingPixel
{
    // Pixel grid with a logical y-offset: logical y can go negative (headroom) down to -OffY.
    class PixGrid
    {
        public readonly int W, H, OffY;
        public readonly uint[] P;   // 0xAARRGGBB, 0 = transparent
        public PixGrid(int w, int h) : this(w, h, 0) { }
        public PixGrid(int w, int h, int offY) { W = w; H = h; OffY = offY; P = new uint[w * h]; }
        public uint this[int x, int y]
        {
            get { y += OffY; return x < 0 || y < 0 || x >= W || y >= H ? 0u : P[y * W + x]; }
            set { y += OffY; if (x >= 0 && y >= 0 && x < W && y < H) P[y * W + x] = value; }
        }
    }

    class Layer { public PixGrid G; public int X, Y; public Layer(PixGrid g, int x, int y) { G = g; X = x; Y = y; } }

    static class Sprites
    {
        public const int Frames = 48, StageW = 40, StageH = 44, Top = 8, Fps = 9;
        public const int HomeFrames = 48;   // "home" is a one-shot: cheer with the bindle, then walk off the right edge

        public static readonly string[] Skins = { "monkey", "pig", "spark" };
        public static readonly string[] Accessories = { "none", "santa", "glasses", "headphones", "scarf", "crown", "umbrella", "cap", "bow", "flower" };
        public static readonly string[] Stages = { "baby", "teen", "adult", "guru" };
        public static readonly string[] Backdrops = { "none", "beach", "space", "forest", "office" };

        // Global look switches (set by the app before rendering; part of its frame-cache key).
        public static string Stage = "adult";
        public static bool Dirt, Flies;
        static bool PhonesGreen;   // the "spotify" pose wears green headphones
        public static int Rank = 1;   // level: drives the rank gear (badge, belt, golden fur, aura, halo)
        public static bool AuraOn = true;   // off while exporting bodies for macOS (the aura ships as separate overlay strips)
        // Levels where his body looks different; every other level looks like the closest one below.
        public static readonly int[] Looks = { 1, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20 };
        public static int AuraCount(int lvl) { return lvl >= 17 ? 4 : lvl >= 13 ? 3 : lvl >= 12 ? 2 : 0; }
        public static void SetStage(string stage)
        {
            Stage = stage;
            if (stage == "guru") { F = 0xFF8F8A84; F2 = 0xFFA5A09A; FR = 0xFFD8D4CC; B = 0xFFE2DED6; }
            else { F = 0xFF6F655B; F2 = 0xFF857B70; FR = 0xFFA39888; B = 0xFFBDB3A5; }
        }
        public static string StageForLevel(int level) { return level <= 2 ? "baby" : level <= 5 ? "teen" : level <= 9 ? "adult" : "guru"; }
        public static readonly string[] Poses = {
            "idle", "work", "wave", "think", "run", "happy", "sleep", "milk", "read", "banana", "error", "love", "type", "done", "wait",
            "home", "gone", "lunch", "bash", "stretch", "water",
            "git_commit", "git_push", "git_pull", "git_fetch", "git_merge", "git_conflict", "git_rebase", "git_branch", "git_stash",
            "git_log", "git_diff", "git_status", "git_reset", "git_tag", "git_cherry", "git_pr", "git_ci",
            "walk", "sit", "music", "jump", "secret", "full", "plan", "bath", "levelup", "spotify", "slot" };

        // git palette
        const uint BOX = 0xFFB98A55, BOX2 = 0xFF8E6538, TAPE = 0xFFD8B47A, STAMP = 0xFFD8443C, ROCKET = 0xFFE9E4DA, FLAME = 0xFFF29A3C,
                   BR_A = 0xFF62B37A, BR_B = 0xFFA87FE0, BR_M = 0xFF6C9BE8, LEAF = 0xFF5FAF5A, POT = 0xFFB0603A, CAN = 0xFF7AA0C8,
                   CHEST = 0xFF8B5A2B, CHEST2 = 0xFFC9962A, PAPER = 0xFFF1E9D8, MINUS = 0xFFE5645A, PLUS = 0xFF5FBF6A,
                   BROOM = 0xFFC9A44C, DUST = 0xFFB8B4AC, CHERRY = 0xFFC8303C, SAND = 0xFFE8C77A, GLASSF = 0xFF9FB8C8;

        // palette
        static uint F = 0xFF6F655B, F2 = 0xFF857B70, FR = 0xFFA39888, B = 0xFFBDB3A5;
        const uint O = 0xFF2B2420,
                   K = 0xFFD69E99, K2 = 0xFFEAC0B8, K3 = 0xFFA86F6C, E = 0xFF1C1714, Wh = 0xFFFFFFFF,
                   T = 0xFFF3E6BF, Y = 0xFFF2C94C, Y2 = 0xFFC99A1E, PK = 0xFFE86A8E, INK = 0xFF6C74D9,
                   DK = 0xFF26303A, SCR = 0xFF32404F, SC = 0xFF7FD1A8, SL = 0xFF8A93D6, R = 0xFFE5645A,
                   CL = 0xFFD97757, CL2 = 0xFFF0A07E, CLO = 0xFF7A3420,
                   MW = 0xFFF4F1EA, MG = 0xFFD9D4C8, MGR = 0xFF3FA866, MGR2 = 0xFF2C7D4B, ST = 0xFFE9E4DA, BUB = 0xFF8FA8E8,
                   PAGE = 0xFFF1E9D8, BOOK = 0xFF6B4A3A, LINE = 0xFF9A8F82,
                   WOOD = 0xFF8B5A2B, CLOTH = 0xFFD9534F, CLOTH2 = 0xFFFFF4E6, BOWL = 0xFFEDE6D6, BOWLB = 0xFF4A78C2,
                   NOOD = 0xFFF2D27A, BROTH = 0xFFC98A3D, STEAM = 0xFFB8B4AC, CHOP = 0xFFB5835A,
                   // pig
                   PO = 0xFF6B4A48, PB = 0xFFF6ECE8, PS = 0xFFE8D6D0, PE = 0xFFF2A7B0, PI = 0xFFE58C99, PN = 0xFFF4A2B2,
                   PN2 = 0xFFE88A9C, PND = 0xFFA85A6A, PEY = 0xFF2A1E1E, PBL = 0xFFF7BAC3, PHF = 0xFFC9A9A2,
                   // accessories
                   HAT = 0xFFD8443C, FUR = 0xFFF7F3EC, GLS = 0xFF15171A, HP = 0xFF3B3F48, HPC = 0xFF5A6070,
                   SCF = 0xFFC73E3A, GOLD = 0xFFF2C14C, GOLD2 = 0xFFC9962A, UMB = 0xFF4F7FD6, UMB2 = 0xFF3A62B0,
                   GLASS = 0xFFCFEAF7, WATER = 0xFF6CB4E0,
                   SLOT = 0xFFB8BEC8, SLOT2 = 0xFFE4E8EE, SLOT3 = 0xFF7C8390, REELBG = 0xFFF4F1EA,
                   SPG = 0xFF1DB954, SPG2 = 0xFF15803A, VINYL = 0xFF1A1A1C, VINYL2 = 0xFF3A3A40, DECK = 0xFF2E3238, DECK2 = 0xFF4A505A,
                   CAP = 0xFF3B6FC4, CAP2 = 0xFF2A548F, BEARD = 0xFFEDEAE4, MUD = 0xFF5E4632, PETAL = 0xFFF7F3EC;

        // ---------- primitives (logical coordinates; grids may have headroom above y=0) ----------
        static int Rnd(double v) { return (int)Math.Floor(v + 0.5); }
        static void Ell(PixGrid g, double cx, double cy, double rx, double ry, uint c)
        {
            for (int ry0 = 0; ry0 < g.H; ry0++)
            {
                int y = ry0 - g.OffY;
                for (int x = 0; x < g.W; x++)
                {
                    double a = (x + .5 - cx) / rx, b = (y + .5 - cy) / ry;
                    if (a * a + b * b <= 1) g.P[ry0 * g.W + x] = c;
                }
            }
        }
        static void Px(PixGrid g, double x, double y, uint c) { g[Rnd(x), Rnd(y)] = c; }
        static void Rect(PixGrid g, double x, double y, int w, int h, uint c)
        {
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Px(g, x + i, y + j, c);
        }
        static void Pat(PixGrid g, double x, double y, string[] rows, uint c)
        {
            for (int j = 0; j < rows.Length; j++)
                for (int i = 0; i < rows[j].Length; i++) if (rows[j][i] == '#') Px(g, x + i, y + j, c);
        }
        static void Pat2(PixGrid g, double x, double y, string[] rows, uint a, uint b)   // 'e' -> a, 'i' -> b
        {
            for (int j = 0; j < rows.Length; j++)
                for (int i = 0; i < rows[j].Length; i++) { char ch = rows[j][i]; if (ch == 'e') Px(g, x + i, y + j, a); else if (ch == 'i') Px(g, x + i, y + j, b); }
        }
        static void Line(PixGrid g, double x0, double y0, double x1, double y1, uint c)
        {
            int n = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
            for (int i = 0; i <= n; i++) { double t = n == 0 ? 0 : (double)i / n; Px(g, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, c); }
        }
        static void Outline(PixGrid g, uint col)
        {
            var o = new uint[g.P.Length];
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                {
                    if (g.P[y * g.W + x] != 0) continue;
                    bool n = (x + 1 < g.W && g.P[y * g.W + x + 1] != 0) || (x > 0 && g.P[y * g.W + x - 1] != 0) ||
                             (y + 1 < g.H && g.P[(y + 1) * g.W + x] != 0) || (y > 0 && g.P[(y - 1) * g.W + x] != 0);
                    if (n) o[y * g.W + x] = col;
                }
            for (int i = 0; i < o.Length; i++) if (o[i] != 0) g.P[i] = o[i];
        }
        static void Texture(PixGrid g, uint body, uint fleck, int mul)
        {
            for (int ry = 0; ry < g.H; ry++)
                for (int x = 0; x < g.W; x++)
                {
                    int y = ry - g.OffY;
                    if (g.P[ry * g.W + x] == body && (x * 7 + y * 5) % mul == 0) g.P[ry * g.W + x] = fleck;
                }
        }
        static PixGrid BodyGrid() { return new PixGrid(32, 32 + Top, Top); }
        static string[] Mirror(string[] rows) { var r = new string[rows.Length]; for (int i = 0; i < rows.Length; i++) { var a = rows[i].ToCharArray(); Array.Reverse(a); r[i] = new string(a); } return r; }

        // ---------- pose parameters ----------
        class MP
        {
            public string Eyes = "open", Mouth = "smile", Acc = "none";
            public int Dx, Dy, Lx, Ly, FootL, FootR;
            public bool Back, Puff, EarFlop, Sit;
            public int Swing;   // Sit: how far the dangling legs swing
            public double[][] ArmsBack = { new[] { 7, 23, 2.5, 3.5 }, new[] { 25, 23, 2.5, 3.5 } };
            public double[][] ArmsFront = new double[0][];
            public Action<PixGrid, int, int> Prop;
            public Action<PixGrid> Glyph;
        }
        static readonly double[] ArmL = { 7, 23, 2.5, 3.5 };
        static double[] A(double x, double y, double rx, double ry) { return new[] { x, y, rx, ry }; }

        // Where a body's head sits, for accessories.
        class Anchor { public int Top, EyeY, EyeL, EyeR, NeckY; }
        static Anchor AnchorFor(string skin)
        {
            if (skin == "pig") return new Anchor { Top = 6, EyeY = 13, EyeL = 11, EyeR = 19, NeckY = 21 };
            if (skin == "spark") return new Anchor { Top = 4, EyeY = 14, EyeL = 12, EyeR = 19, NeckY = 23 };
            return new Anchor { Top = 4, EyeY = 15, EyeL = 12, EyeR = 19, NeckY = 21 };
        }

        static void Eyes(PixGrid g, string t, int L, int R2, int Yy, uint ink)
        {
            switch (t)
            {
                case "open": Rect(g, L, Yy, 2, 2, ink); Rect(g, R2, Yy, 2, 2, ink); Px(g, L, Yy, Wh); Px(g, R2, Yy, Wh); break;
                case "dot": Rect(g, L, Yy, 2, 2, ink); Rect(g, R2, Yy, 2, 2, ink); break;
                case "closed": Rect(g, L, Yy + 1, 2, 1, ink); Rect(g, R2, Yy + 1, 2, 1, ink); break;
                case "happy": foreach (var x in new[] { L, R2 }) { Px(g, x - 1, Yy + 1, ink); Rect(g, x, Yy, 2, 1, ink); Px(g, x + 2, Yy + 1, ink); } break;
                case "bliss": foreach (var x in new[] { L, R2 }) { Px(g, x - 1, Yy, ink); Rect(g, x, Yy + 1, 2, 1, ink); Px(g, x + 2, Yy, ink); } break;
                case "o": Rect(g, L - 1, Yy - 1, 3, 3, ink); Px(g, L, Yy, Wh); Rect(g, R2, Yy - 1, 3, 3, ink); Px(g, R2 + 1, Yy, Wh); break;
                case "heart": foreach (var x in new[] { L - 1, R2 }) Pat(g, x, Yy - 1, new[] { "#.#", "###", ".#." }, PK); break;
                case "focus": Rect(g, L - 1, Yy + 1, 3, 1, ink); Rect(g, R2, Yy + 1, 3, 1, ink); Px(g, L - 1, Yy, ink); Px(g, R2 + 2, Yy, ink); break;
            }
        }
        static void Mouth(PixGrid g, string t, int X, int Yy, uint lip, uint ink)
        {
            switch (t)
            {
                case "smile": Px(g, X - 2, Yy, lip); Rect(g, X - 1, Yy + 1, 3, 1, lip); Px(g, X + 2, Yy, lip); break;
                case "flat": Rect(g, X - 1, Yy, 3, 1, lip); break;
                case "o": Rect(g, X - 1, Yy, 2, 2, ink); break;
                case "open": Rect(g, X - 2, Yy, 4, 2, ink); break;
                case "chew": Rect(g, X - 1, Yy, 3, 1, ink); break;
                case "pucker": Rect(g, X - 1, Yy, 3, 2, lip); break;
                case "grin": Rect(g, X - 3, Yy, 7, 2, T); Px(g, X - 1, Yy + 1, ink); Px(g, X + 1, Yy + 1, ink); Rect(g, X - 3, Yy + 2, 7, 1, lip); break;
            }
        }

        // ---------- accessories (drawn before the outline so they get one too) ----------
        static void DrawAcc(PixGrid g, string skin, string acc, int dx, int dy, bool back)
        {
            var a = AnchorFor(skin); int top = a.Top + dy, eye = a.EyeY + dy, neck = a.NeckY + dy;
            switch (acc)
            {
                case "santa":
                    for (int r = 0; r < 6; r++) Rect(g, 10 + r + r / 2 + dx, top - 1 - r, Math.Max(1, 13 - r * 2), 1, HAT);
                    Ell(g, 24 + dx, top - 7, 1.6, 1.6, FUR);
                    Rect(g, 9 + dx, top, 15, 2, FUR);
                    break;
                case "glasses":
                    if (back) break;
                    Rect(g, a.EyeL - 2 + dx, eye - 1, 5, 3, GLS); Rect(g, a.EyeR - 1 + dx, eye - 1, 5, 3, GLS);
                    Rect(g, a.EyeL + 3 + dx, eye - 1, a.EyeR - a.EyeL - 4, 1, GLS);
                    Px(g, a.EyeL - 1 + dx, eye - 1, Wh); Px(g, a.EyeR + dx, eye - 1, Wh);
                    break;
                case "headphones":
                    for (int x = 6; x <= 26; x++)
                    {
                        double t = (x - 16) / 10.5; int y = Rnd(top + 1 - Math.Sqrt(Math.Max(0, 1 - t * t)) * 4);
                        Px(g, x + dx, y, PhonesGreen ? SPG2 : HP); Px(g, x + dx, y + 1, PhonesGreen ? SPG2 : HP);
                    }
                    Ell(g, 5 + dx, eye - 1, 2.6, 3.6, PhonesGreen ? SPG : HPC); Ell(g, 27 + dx, eye - 1, 2.6, 3.6, PhonesGreen ? SPG : HPC);
                    break;
                case "scarf":
                    Rect(g, 9 + dx, neck, 15, 2, SCF);
                    for (int x = 10; x < 24; x += 3) Px(g, x + dx, neck, FUR);
                    Rect(g, 20 + dx, neck + 2, 3, 4, SCF); Px(g, 21 + dx, neck + 3, FUR);
                    break;
                case "crown":
                    Rect(g, 11 + dx, top - 2, 11, 2, GOLD);
                    Pat(g, 11 + dx, top - 5, new[] { "#....#....#", "#...###...#", "##.#####.##" }, GOLD);
                    Px(g, 16 + dx, top - 1, R); Px(g, 13 + dx, top - 1, Y2); Px(g, 19 + dx, top - 1, Y2);
                    break;
                case "cap":
                    Ell(g, 16 + dx, top + 1, 8.5, 4, CAP); Rect(g, 7 + dx, top + 1, 19, 2, CAP);
                    Rect(g, 16 + dx, top + 2, 12, 2, CAP2); Px(g, 16 + dx, top - 3, PETAL);
                    break;
                case "bow":
                    Pat(g, 19 + dx, top - 1, new[] { "##...##", "###.###", "#######", "###.###", "##...##" }, PK); Rect(g, 22 + dx, top, 1, 3, 0xFFB8456A);
                    break;
                case "flower":
                    for (int i = 0; i < 5; i++) { double ang = i * Math.PI * 2 / 5; Ell(g, 7 + dx + Math.Cos(ang) * 2.2, top + 4 + Math.Sin(ang) * 2.2, 1.5, 1.5, PETAL); }
                    Ell(g, 7 + dx, top + 4, 1.4, 1.4, Y);
                    break;
                case "umbrella":
                    for (int x = 3; x <= 29; x++)
                    {
                        double t = (x - 16) / 13.0; int h = Rnd(Math.Sqrt(Math.Max(0, 1 - t * t)) * 5);
                        for (int y = 0; y <= h; y++) Px(g, x + dx, top - 3 - y, (x / 4) % 2 == 0 ? UMB : UMB2);
                    }
                    Line(g, 16 + dx, top - 2, 16 + dx, top + 1, DK);
                    break;
            }
        }


        // ---------- rank gear: what he earns with levels (drawn under the face, so the guru beard covers the badge) ----------
        public static readonly uint[] BeltColors = { 0xFFF4F1EA, 0xFFF2C94C, 0xFF62B37A, 0xFF4F7FD6, 0xFF8B5A2B, 0xFF1F1B18 };
        public static readonly string[] BeltNames = { "белый", "жёлтый", "зелёный", "синий", "коричневый", "чёрный" };
        public static int BeltIndex(int lvl) { return lvl < 4 ? -1 : Math.Min(BeltColors.Length - 1, lvl - 4); }
        static void RankGear(PixGrid g, int dx, int dy, bool back)
        {
            if (Rank >= 15) Texture(g, F, GOLD, 11);   // legend: golden flecks in the fur
            int bi = BeltIndex(Rank);
            if (bi >= 0)
            {
                uint c = BeltColors[bi];
                for (int x = 6; x <= 26; x++)
                    for (int y = 27; y <= 28; y++) if (g[x + dx, y + dy] != 0) Px(g, x + dx, y + dy, Rank >= 10 && x % 4 == 0 ? GOLD : c);
                if (!back) { Rect(g, 15 + dx, 26 + dy, 3, 3, c); Px(g, 14 + dx, 29 + dy, c); Px(g, 14 + dx, 30 + dy, c); Px(g, 18 + dx, 29 + dy, c); Px(g, 18 + dx, 30 + dy, c); }
            }
            if (Rank >= 3 && !back)   // staff badge on a lanyard
            {
                Line(g, 12 + dx, 21 + dy, 14 + dx, 23 + dy, CAP); Line(g, 20 + dx, 21 + dy, 18 + dx, 23 + dy, CAP);
                Rect(g, 14 + dx, 23 + dy, 5, 3, PAGE); Rect(g, 15 + dx, 24 + dy, 3, 1, Rank >= 7 ? GOLD2 : CAP);
            }
        }
        static void Halo(PixGrid g, double cx, double cy)
        {
            Ell(g, cx, cy, 6.5, 2, GOLD); Ell(g, cx, cy, 4.5, 0.9, 0);
            Px(g, cx - 4, cy - 1, 0xFFFFF2B8);
        }
        // Aura sparkles orbiting him from level 12 (more at 13 and 17); stage coordinates.
        public static void Aura(int k, List<Layer> back, List<Layer> front)
        {
            int n = AuraCount(Rank);
            for (int i = 0; i < n; i++)
            {
                double a = k * 2 * Math.PI / 48 + i * 2 * Math.PI / n;
                int ph = (k + i * 5) % 8; if (ph > 5) continue;
                var sp = new PixGrid(5, 5); uint c = i % 2 == 0 ? GOLD : 0xFFFFF2B8;
                Px(sp, 2, 2, Wh);
                if (ph >= 1 && ph <= 4) { Px(sp, 1, 2, c); Px(sp, 3, 2, c); Px(sp, 2, 1, c); Px(sp, 2, 3, c); }
                if (ph == 2 || ph == 3) { Px(sp, 0, 2, c); Px(sp, 4, 2, c); Px(sp, 2, 0, c); Px(sp, 2, 4, c); }
                (Math.Sin(a) < 0 ? back : front).Add(new Layer(sp, Rnd(18 + Math.Cos(a) * 17), Rnd(22 + Math.Sin(a) * 9 - 6)));
            }
        }

        // ---------- slot machine reels ----------
        // Six 5x5 symbols stacked on a strip (1px gap); a reel window shows 7 rows of it, centre symbol in rows 1..5.
        public static readonly string[] SlotSymbols = { "seven", "cherry", "banana", "bar", "spark", "bug" };
        public const int SlotCell = 6, SlotReelH = 7;
        static readonly int[] SlotReelLX = { 5, 11, 17 };                  // body-grid x of the reel windows (y 22..28)
        public static readonly int[] SlotReelX = { 9, 15, 21 };           // the same windows on the 40x44 stage
        public const int SlotReelY = 32;
        public static PixGrid SlotStrip()
        {
            var g = new PixGrid(5, SlotCell * SlotSymbols.Length);
            for (int i = 0; i < SlotSymbols.Length; i++)
            {
                int y = i * SlotCell;
                switch (SlotSymbols[i])
                {
                    case "seven": Pat(g, 0, y, new[] { "#####", "....#", "...#.", "..#..", "..#.." }, R); break;
                    case "cherry": Pat2(g, 0, y, new[] { "..ee.", ".e..e", "ii.ii", "ii.ii", "....." }, LEAF, R); break;
                    case "banana": Pat2(g, 0, y, new[] { "....e", "...ie", "..ii.", "iii..", ".i..." }, Y2, Y); break;
                    case "bar": Pat2(g, 0, y, new[] { ".....", "iiiii", "ieeei", "iiiii", "....." }, Wh, 0xFF1F1B18); break;
                    case "spark": Pat(g, 0, y, new[] { "..#..", "#.#.#", ".###.", "#.#.#", "..#.." }, CL); break;
                    case "bug": Pat2(g, 0, y, new[] { "e...e", ".iii.", "iiiii", ".iii.", "i.i.i" }, DK, 0xFF7A5FB0); break;
                }
            }
            return g;
        }
        static PixGrid slotStrip;
        // The three reels at the given positions (in symbols; fractional while spinning) as a stage-sized overlay.
        public static byte[] SlotReels(double[] pos)
        {
            if (slotStrip == null) slotStrip = SlotStrip();
            var g = new PixGrid(StageW, StageH); int H = slotStrip.H;
            for (int r = 0; r < 3; r++)
                for (int row = 0; row < SlotReelH; row++)
                {
                    int src = (int)Math.Floor(pos[r] * SlotCell) + row - 1; src = ((src % H) + H) % H;
                    bool edge = row == 0 || row == SlotReelH - 1;
                    for (int x = 0; x < 5; x++)
                    {
                        uint c = slotStrip[x, src]; if (c == 0) c = REELBG;
                        if (edge) c = 0xFF000000 | (((c >> 16) & 0xFF) * 3 / 4) << 16 | (((c >> 8) & 0xFF) * 3 / 4) << 8 | ((c & 0xFF) * 3 / 4);
                        g[SlotReelX[r] + x, SlotReelY + row] = c;
                    }
                }
            return ToBgra(g);
        }

        // ---------- "gone": he went home — a little house with a lit window, smoke and a moon (stage coordinates) ----------
        static PixGrid House(int k)
        {
            var g = new PixGrid(StageW, StageH);
            const uint WALL = 0xFFC9A27A, WALL2 = 0xFFB08A63, ROOF = 0xFFB0503A, ROOF2 = 0xFF8E3C2C, LIT = 0xFFF2D27A, LIT2 = 0xFFE8B54A;
            Rect(g, 25, 17, 3, 7, 0xFF7A4A3A);                                          // chimney
            for (int r = 0; r < 9; r++) Rect(g, 10 + r, 27 - r, 22 - 2 * r, 1, r % 3 == 0 ? ROOF2 : ROOF);   // roof
            Rect(g, 12, 28, 18, 13, WALL); Rect(g, 12, 28, 18, 1, WALL2);              // walls
            Rect(g, 14, 31, 6, 5, LIT); Rect(g, 14, 33, 6, 1, LIT2); Rect(g, 16, 31, 1, 5, WALL2);   // lit window
            if (k % 48 < 30) { Rect(g, 18, 33, 2, 2, 0xFF5A4A3E); Px(g, 18, 32, 0xFF5A4A3E); }  // his silhouette at the window
            Rect(g, 22, 33, 5, 8, WOOD); Px(g, 25, 37, GOLD);                            // door
            Outline(g, O);
            for (int i = 0; i < 3; i++)                                                  // smoke puffs drifting up
            {
                int t = (k + i * 16) % 48;
                Ell(g, 26.5 + Math.Sin(t * 0.3) * 1.2 + t * 0.08, 15 - t / 4.0, 0.7 + t / 60.0, 0.6 + t / 70.0, t < 30 ? 0xFF9A968E : 0xFF6E6A64);
            }
            Ell(g, 7, 9, 3, 3, Y); Ell(g, 8.3, 8, 2.6, 2.6, 0);                         // moon
            foreach (var st in new[] { new[] { 15, 5 }, new[] { 22, 2 }, new[] { 35, 8 } })
                if ((k / 6 + st[0]) % 4 != 0) Px(g, st[0], st[1], 0xFFEDE6C8);
            return g;
        }

        // ---------- bodies ----------
        static PixGrid Body(string skin, MP p)
        {
            if (skin == "pig") return Pig(p);
            if (skin == "spark") return Spark(p);
            return Monkey(p);
        }

        static PixGrid Monkey(MP p)
        {
            var g = BodyGrid(); int dx = p.Dx, dy = p.Dy;
            if (p.Sit)
            {
                // Legs hang straight down over the edge and swing; feet are the paler soles.
                Ell(g, 12 + dx + p.Swing * 0.5, 28, 2, 3.4, F); Ell(g, 20 + dx - p.Swing * 0.5, 28, 2, 3.4, F);
                Ell(g, 12 + dx + p.Swing, 31, 2.2, 1.4, K3); Ell(g, 20 + dx - p.Swing, 31, 2.2, 1.4, K3);
            }
            else { Ell(g, 12 + dx + p.FootL, 29, 3, 2, F); Ell(g, 20 + dx + p.FootR, 29, 3, 2, F); }
            Ell(g, 16 + dx, 23 + dy, 9, 7, F);
            foreach (var a in p.ArmsBack) Ell(g, a[0] + dx, a[1] + dy, a[2], a[3], F);
            Ell(g, 16 + dx, 24 + dy, 5, 4, B);
            Ell(g, 5.5 + dx, 14 + dy, 2.2, 2.6, K3); Ell(g, 26.5 + dx, 14 + dy, 2.2, 2.6, K3);
            Ell(g, 16 + dx, 13 + dy, 11, 9.5, F);
            for (double a = Math.PI * 1.05; a < Math.PI * 1.95; a += 0.32)
                Ell(g, 16 + dx + Math.Cos(a) * 10.3, 13 + dy + Math.Sin(a) * 8.6, 1.8, 1.8, F);
            Texture(g, F, F2, 9);
            RankGear(g, dx, dy, p.Back);
            if (p.Back) { Ell(g, 16 + dx, 12 + dy, 7, 5, F2); Texture(g, F, F2, 9); }
            else
            {
                Ell(g, 16 + dx, 17 + dy, 6, 4.6, K);
                if (p.Puff) { Ell(g, 11 + dx, 19 + dy, 2, 1.6, K2); Ell(g, 21 + dx, 19 + dy, 2, 1.6, K2); }
                Ell(g, 16 + dx, 10.5 + dy, 9.2, 3.8, FR);
                for (int x = 8; x <= 24; x += 3) { Px(g, x + dx, 14 + dy, FR); Px(g, x + 1 + dx, 14 + dy, FR); }
                Ell(g, 16 + dx, 19 + dy, 3.5, 2.2, K2); Px(g, 15 + dx, 18 + dy, K3); Px(g, 17 + dx, 18 + dy, K3);
                if (Stage == "guru")
                {
                    Ell(g, 16 + dx, 24 + dy, 5.5, 3.6, BEARD); Px(g, 13 + dx, 27 + dy, BEARD); Px(g, 16 + dx, 28 + dy, BEARD); Px(g, 19 + dx, 27 + dy, BEARD);
                    Rect(g, 10 + dx, 13 + dy, 4, 1, BEARD); Rect(g, 19 + dx, 13 + dy, 4, 1, BEARD);
                }
                Eyes(g, p.Eyes, 12 + dx + p.Lx, 19 + dx + p.Lx, 15 + dy + p.Ly, E); Mouth(g, p.Mouth, 16 + dx, 20 + dy, K3, E);
            }
            if (Stage == "baby") Pat(g, 14 + dx, 0 + dy, new[] { ".##.", "#..#", "..#.", ".#.." }, F);
            if (Dirt) foreach (var d in new[] { new[] { 9, 9 }, new[] { 22, 7 }, new[] { 11, 25 }, new[] { 21, 27 }, new[] { 25, 18 } }) { Rect(g, d[0] + dx, d[1] + dy, 2, 1, MUD); Px(g, d[0] + 1 + dx, d[1] + 1 + dy, MUD); }
            if (p.Prop != null) p.Prop(g, dx, dy);
            foreach (var a in p.ArmsFront) Ell(g, a[0] + dx, a[1] + dy, a[2], a[3], F);
            DrawAcc(g, "monkey", p.Acc, dx, dy, p.Back);
            if (Rank >= 20) Halo(g, 16 + dx, -2 + dy);
            Outline(g, O);
            if (p.Glyph != null) p.Glyph(g);
            return g;
        }

        static readonly string[] EarUp = { "e.....", "ee....", "eie...", "eiie..", "eiiee.", ".eeeee" };
        static readonly string[] EarFlop = { "......", "eeee..", "eiiee.", ".eiiee", "..eeee", "......" };

        static PixGrid Pig(MP p)
        {
            var g = BodyGrid(); int dx = p.Dx, dy = p.Dy;
            bool flop = p.EarFlop || p.Eyes == "o";
            int legL = Math.Max(0, -p.FootL), legR = Math.Max(0, p.FootR - 1);
            Rect(g, 9 + dx, 26, 3, 4, PS); Rect(g, 20 + dx, 26, 3, 4, PS);
            Rect(g, 12 + dx, 26 - legL, 3, 5, PB); Rect(g, 17 + dx, 26 - legR, 3, 5, PB);
            Rect(g, 12 + dx, 30 - legL, 3, 1, PHF); Rect(g, 17 + dx, 30 - legR, 3, 1, PHF); Rect(g, 9 + dx, 29, 3, 1, PHF); Rect(g, 20 + dx, 29, 3, 1, PHF);
            Ell(g, 16 + dx, 22 + dy, 9, 6, PB); Ell(g, 16 + dx, 25 + dy, 6, 2.5, PS);
            var ear = flop ? EarFlop : EarUp;
            Pat2(g, 5 + dx, 4 + dy, ear, PE, PI); Pat2(g, 21 + dx, 4 + dy, Mirror(ear), PE, PI);
            Ell(g, 16 + dx, 14 + dy, 10, 8.5, PB);
            for (double a = Math.PI * 1.1; a < Math.PI * 1.9; a += 0.4) Ell(g, 16 + dx + Math.Cos(a) * 9.6, 14 + dy + Math.Sin(a) * 8, 1.6, 1.6, PB);
            Texture(g, PB, PS, 11);
            if (p.Back)
            {
                Ell(g, 16 + dx, 22 + dy, 3, 2, PS);
                Pat2(g, 15 + dx + (p.Dx % 2), 19 + dy, new[] { ".ee.", "e..e", "..e.", ".e.." }, PE, PE);
            }
            else
            {
                Ell(g, 9 + dx, 17 + dy, 1.8, 1.2, PBL); Ell(g, 23 + dx, 17 + dy, 1.8, 1.2, PBL);
                int sy = 18 + dy;
                Ell(g, 16 + dx, sy, 4.2, 2.8, PN); Rect(g, 13 + dx, sy - 2, 7, 1, PN2);
                Rect(g, 14 + dx, sy - 1, 1, 2, PND); Rect(g, 18 + dx, sy - 1, 1, 2, PND);
                Eyes(g, p.Eyes, 11 + dx + p.Lx, 19 + dx + p.Lx, 13 + dy + p.Ly, PEY);
                Mouth(g, p.Mouth == "grin" ? "open" : p.Mouth, 16 + dx, sy + 3, PND, PND);
            }
            if (p.Prop != null) p.Prop(g, dx, dy);
            foreach (var a in p.ArmsFront) { Ell(g, a[0] + dx, a[1] + dy, 2.2, 2, PB); Rect(g, a[0] - 1 + dx, a[1] + 1 + dy, 3, 1, PHF); }
            DrawAcc(g, "pig", p.Acc, dx, dy, p.Back);
            Outline(g, PO);
            if (p.Glyph != null) p.Glyph(g);
            return g;
        }

        // Claude-ish spark with a face: an 8-ray star body, little legs and stubby arms.
        static PixGrid Spark(MP p)
        {
            var g = BodyGrid(); int dx = p.Dx, dy = p.Dy;
            Rect(g, 12 + dx + p.FootL / 2, 25, 2, 5, CL); Rect(g, 18 + dx + p.FootR / 2, 25, 2, 5, CL);
            double cx = 16 + dx, cy = 15 + dy;
            for (int ry = 0; ry < g.H; ry++)
            {
                int y = ry - g.OffY;
                for (int x = 0; x < g.W; x++)
                {
                    double ddx = x + .5 - cx, ddy = y + .5 - cy, r = Math.Sqrt(ddx * ddx + ddy * ddy), a = Math.Atan2(ddy, ddx);
                    if (r <= 9.5 + 2.6 * Math.Cos(8 * a)) g.P[ry * g.W + x] = CL;
                }
            }
            if (p.Back) Ell(g, cx, cy, 4, 3, 0xFFC4653F);
            else
            {
                Ell(g, cx - 3, cy - 4, 3, 2, CL2);
                Eyes(g, p.Eyes, 12 + dx + p.Lx, 19 + dx + p.Lx, 14 + dy + p.Ly, 0xFF2A1C1C);
                Mouth(g, p.Mouth, 16 + dx, 19 + dy, CLO, 0xFF2A1C1C);
            }
            if (p.Prop != null) p.Prop(g, dx, dy);
            foreach (var a in p.ArmsBack) Ell(g, a[0] + dx, a[1] + dy, 1.8, 2.4, CL);
            foreach (var a in p.ArmsFront) Ell(g, a[0] + dx, a[1] + dy, 2, 2, CL);
            DrawAcc(g, "spark", p.Acc, dx, dy, p.Back);
            Outline(g, CLO);
            if (p.Glyph != null) p.Glyph(g);
            return g;
        }

        // Claude buddy: a little spark creature that runs around (own drawing, not the official logo).
        static PixGrid Buddy(int k, int dir, uint color)
        {
            var g = new PixGrid(11, 10);
            Pat(g, 0, 1, new[] { "....#.#....", "..#.###.#..", "...#####...", ".#########.", "..#######..", "...#####..." }, color);
            Rect(g, 4, 3, 3, 1, 0xFFFFFFFF & (color | 0xFF404040));
            int ex = dir > 0 ? 1 : -1; Px(g, 4 + ex, 5, E); Px(g, 6 + ex, 5, E);
            if (k % 2 == 1) { Px(g, 3, 7, color); Px(g, 7, 8, color); Px(g, 3, 8, color); }
            else { Px(g, 4, 8, color); Px(g, 6, 7, color); Px(g, 7, 8, color); }
            Outline(g, Darken(color));
            return g;
        }
        static uint Darken(uint c) { uint r = (c >> 16) & 255, gg = (c >> 8) & 255, b = c & 255; return 0xFF000000 | (r * 45 / 100 << 16) | (gg * 45 / 100 << 8) | (b * 45 / 100); }
        public static readonly uint[] SessionColors = { CL, 0xFF6C9BE8, 0xFF62B37A, 0xFFA87FE0, 0xFFE0C04F };

        // Little helper (one per running subagent): a tiny version of the current body.
        static PixGrid Mini(string skin, int k, int dir)
        {
            var g = new PixGrid(10, 10);
            int hop = k % 2;
            if (skin == "pig")
            {
                Ell(g, 5, 5 - hop, 3.6, 3, PB); Px(g, 2, 2 - hop, PE); Px(g, 7, 2 - hop, PE);
                Ell(g, 5 + dir, 6 - hop, 1.5, 1, PN); Px(g, 4 + dir, 4 - hop, PEY); Px(g, 6 + dir, 4 - hop, PEY);
                Px(g, 3, 9, PS); Px(g, 7, 9 - hop, PS); Outline(g, PO);
            }
            else if (skin == "spark")
            {
                Pat(g, 1, 1 - hop, new[] { "...#....", ".#.#.#..", "..###...", "#######.", "..###...", ".#.#.#.." }, SessionColors[0]);
                Px(g, 3 + dir, 4 - hop, E); Px(g, 5 + dir, 4 - hop, E); Px(g, 3, 8, CL); Px(g, 5, 8 - hop, CL); Outline(g, CLO);
            }
            else
            {
                Ell(g, 5, 5 - hop, 3.6, 3.2, F); Ell(g, 5 + dir, 6 - hop, 2.2, 1.6, K);
                Px(g, 4 + dir, 5 - hop, E); Px(g, 6 + dir, 5 - hop, E); Rect(g, 2, 3 - hop, 6, 1, FR);
                Px(g, 3, 9, F); Px(g, 7, 9 - hop, F); Outline(g, O);
            }
            return g;
        }

        static PixGrid Sleeping(int k)
        {
            var g = BodyGrid(); int br = k % 16 < 8 ? 0 : 1;
            Ell(g, 21, 25 - br * 0.5, 10, 5 + br * 0.4, F); Ell(g, 28, 28, 3, 2, F); Ell(g, 10, 22, 8.5, 7.5, F); Ell(g, 3.5, 20, 2, 2.4, K3);
            Texture(g, F, F2, 9); Ell(g, 11, 24, 4.8, 3.6, K); Ell(g, 10, 19, 7, 3, FR);
            Rect(g, 8, 23, 2, 1, E); Rect(g, 13, 23, 2, 1, E); Ell(g, 11, 26, 2.5, 1.4, K2); Ell(g, 14, 28, 2.2, 2, F);
            Outline(g, O); GlyphZ(g, k);
            return g;
        }

        // Milk through a straw: cheeks hollow/puff, a sip runs up the straw, the bottle empties.
        static PixGrid MonkeyMilk(int k, string acc)
        {
            var g = BodyGrid(); int cyc = k % 48; bool suck = k % 6 < 3; int level = Math.Min(5, cyc / 8); int dy = k % 16 < 8 ? 0 : 1;
            Ell(g, 12, 29, 3, 2, F); Ell(g, 20, 29, 3, 2, F);
            Ell(g, 16, 23 + dy, 9, 7, F); Ell(g, 16, 24 + dy, 5, 4, B);
            Ell(g, 5.5, 14 + dy, 2.2, 2.6, K3); Ell(g, 26.5, 14 + dy, 2.2, 2.6, K3);
            Ell(g, 16, 13 + dy, 11, 9.5, F);
            for (double a = Math.PI * 1.05; a < Math.PI * 1.95; a += 0.32) Ell(g, 16 + Math.Cos(a) * 10.3, 13 + dy + Math.Sin(a) * 8.6, 1.8, 1.8, F);
            Texture(g, F, F2, 9);
            Ell(g, 16, 17 + dy, 6, 4.6, K);
            if (suck) { Rect(g, 11, 18 + dy, 1, 2, K3); Rect(g, 21, 18 + dy, 1, 2, K3); }
            else { Ell(g, 11, 19 + dy, 2, 1.6, K2); Ell(g, 21, 19 + dy, 2, 1.6, K2); }
            Ell(g, 16, 10.5 + dy, 9.2, 3.8, FR);
            for (int x = 8; x <= 24; x += 3) { Px(g, x, 14 + dy, FR); Px(g, x + 1, 14 + dy, FR); }
            Ell(g, 16, 19 + dy, 3.5, 2.2, K2); Px(g, 15, 18 + dy, K3); Px(g, 17, 18 + dy, K3);
            Eyes(g, "bliss", 12, 19, 15 + dy, E);
            Mouth(g, "pucker", 16, 20 + dy, K3, E);
            MilkProp(k)(g, 0, 0);
            Ell(g, 11, 26, 2.4, 2.2, F); Ell(g, 21, 26, 2.4, 2.2, F);
            DrawAcc(g, "monkey", acc, 0, dy, false);
            Outline(g, O);
            if (cyc > 38) { int t = cyc - 38; Px(g, 23, 10 - t / 2, BUB); if (t > 3) Px(g, 25, 6 - t / 3, BUB); }
            return g;
        }

        // ---------- props & glyphs ----------
        static Action<PixGrid, int, int> MilkProp(int k)
        {
            return (g, dx, dy) => {
                int level = Math.Min(5, (k % 48) / 8); bool suck = k % 6 < 3; int bx = 12, by = 23;
                Rect(g, bx + 1, by - 1, 6, 1, MGR2); Rect(g, bx, by, 8, 8, MW); Rect(g, bx + 1, by + 1, 6, level, MG);
                Rect(g, bx, by + 3, 8, 2, MGR); Px(g, bx + 2, by + 3, Wh); Px(g, bx + 5, by + 4, Wh);
                Rect(g, 16, by - 3, 1, 3, ST);
                if (suck) Px(g, 16, by - 1 - (k % 3), Wh);
            };
        }
        static Action<PixGrid, int, int> Glass(int k)
        {
            return (g, dx, dy) => {
                int lift = k % 12 < 6 ? 0 : 2, level = Math.Min(4, (k % 48) / 12);
                Rect(g, 14 + dx, 21 + dy - lift, 5, 7, GLASS); Rect(g, 15 + dx, 23 + dy - lift + level, 3, 4 - level, WATER);
                Px(g, 14 + dx, 22 + dy - lift, Wh);
            };
        }
        static Action<PixGrid, int, int> Laptop(int k)
        {
            return (g, dx, dy) => {
                Rect(g, 8 + dx, 20 + dy, 16, 9, DK); Rect(g, 9 + dx, 21 + dy, 14, 7, SCR);
                int[][] L = { new[] { 5, 2 }, new[] { 8, 0 }, new[] { 3, 3 }, new[] { 6, 1 }, new[] { 4, 4 } };
                for (int i = 0; i < 3; i++) { var l = L[(i + k) % 5]; Rect(g, 10 + l[1] + dx, 22 + i * 2 + dy, l[0], 1, i == 1 ? Y : SC); }
            };
        }
        static Action<PixGrid, int, int> LaptopSide(int k)
        {
            return (g, dx, dy) => { Rect(g, 15 + dx, 22 + dy, 14, 2, 0xFF1E252D); Rect(g, 25 + dx, 13 + dy, 3, 10, DK); Rect(g, 26 + dx, 14 + dy, 1, 8, SCR); Px(g, 26 + dx, 14 + dy + (k % 8), SC); };
        }
        static Action<PixGrid, int, int> Book(int k)
        {
            return (g, dx, dy) => {
                Rect(g, 8 + dx, 21 + dy, 16, 8, BOOK); Rect(g, 9 + dx, 21 + dy, 6, 7, PAGE); Rect(g, 17 + dx, 21 + dy, 6, 7, PAGE);
                if (k % 12 < 2) Rect(g, 15 + dx, 19 + dy, 4, 9, PAGE);
                Rect(g, 10 + dx, 23 + dy, 4, 1, LINE); Rect(g, 18 + dx, 23 + dy, 4, 1, LINE); Rect(g, 10 + dx, 25 + dy, 3, 1, LINE); Rect(g, 18 + dx, 25 + dy, 4, 1, LINE);
            };
        }
        static Action<PixGrid, int, int> Banana(int k)
        {
            return (g, dx, dy) => {
                int left = 3 - (k % 24) / 6;
                string[] rows = { "...##", "..##.", ".##..", "##...", "##...", ".#..." };
                var part = new string[rows.Length - (3 - left)]; Array.Copy(rows, 3 - left, part, 0, part.Length);
                Pat(g, 20 + dx, 15 + dy + (3 - left), part, Y); Px(g, 20 + dx, 20 + dy, Y2);
            };
        }
        static void Bindle(PixGrid g, int dx, int dy)
        {
            Line(g, 24 + dx, 21 + dy, 29 + dx, 8 + dy, WOOD);
            Ell(g, 29 + dx, 6 + dy, 3.6, 3.2, CLOTH);
            Px(g, 28 + dx, 5 + dy, CLOTH2); Px(g, 30 + dx, 7 + dy, CLOTH2); Px(g, 27 + dx, 7 + dy, CLOTH2);
            Px(g, 29 + dx, 2 + dy, CLOTH); Px(g, 28 + dx, 2 + dy, CLOTH);
        }
        static Action<PixGrid, int, int> Lunch(int k, double cy, bool lift)
        {
            return (g, dx, dy) => {
                Ell(g, 16 + dx, 26 + dy, 8, 4, BOWL); Rect(g, 9 + dx, 27 + dy, 15, 1, BOWLB);
                Ell(g, 16 + dx, 24.5 + dy, 6.5, 1.5, BROTH);
                Px(g, 13 + dx, 24 + dy, NOOD); Px(g, 15 + dx, 25 + dy, NOOD); Px(g, 18 + dx, 24 + dy, NOOD); Px(g, 19 + dx, 25 + dy, NOOD);
                Line(g, 26 + dx, cy - 3 + dy, 16 + dx, cy + dy, CHOP);
                Line(g, 26 + dx, cy - 1 + dy, 16 + dx, cy + 1 + dy, CHOP);
                if (lift) for (int i = 1; i <= 3; i++) Px(g, 16 + dx, cy + 1 + i + dy, NOOD);
            };
        }
        // Black terminal in front of him: dim old output, a "$ command" being typed, blinking cursor.
        static Action<PixGrid, int, int> Terminal(int k)
        {
            return (g, dx, dy) => {
                const uint FRAME = 0xFF3A4350, BLACK = 0xFF0E1114, GREEN = 0xFF5FE08A, DIM = 0xFF2F6B45, PROMPT = 0xFFF2C94C;
                Rect(g, 5 + dx, 22 + dy, 22, 9, FRAME); Rect(g, 6 + dx, 23 + dy, 20, 7, BLACK);
                int cyc = k % 24, scroll = (k / 24) % 3;
                int[] old = { 9, 5, 12, 7, 10, 4 };
                Rect(g, 7 + dx, 24 + dy, old[scroll], 1, DIM);
                Px(g, 7 + dx, 26 + dy, PROMPT);
                int typed = Math.Min(13, cyc);
                for (int i = 0; i < typed; i++) if (i % 4 != 3) Px(g, 9 + i + dx, 26 + dy, GREEN);
                if (cyc >= 14) Rect(g, 7 + dx, 28 + dy, Math.Min(18, 3 + (cyc - 14) * 2), 1, GREEN);
                if (k % 4 < 2) Px(g, 9 + typed + dx, 26 + dy, 0xFFFFFFFF);
            };
        }
        static void Steam(PixGrid g, int k)
        {
            for (int w = 0; w < 2; w++)
            {
                int x = w == 0 ? 6 : 26, p = (k + w * 6) % 12;
                for (int i = 0; i < 3; i++) Px(g, x + ((p + i) % 4 < 2 ? 0 : 1), 22 - p / 2 - i * 2, STEAM);
            }
        }
        static void GlyphZ(PixGrid g, int k)
        {
            int s = k % 16, y = s / 2;
            Pat(g, 21, 9 - y / 2, new[] { "###", "..#", ".#.", "#..", "###" }, INK);
            if (s > 5) Pat(g, 26, 5 - y / 3, new[] { "##", ".#", "#.", "##" }, INK);
        }
        static readonly string[] Heart = { "#.#", "###", ".#." }, Plus = { ".#.", "###", ".#." };

        // ---------- git props ----------
        static void Box(PixGrid g, int x, int y, int w, int h)
        {
            Rect(g, x, y, w, h, BOX); Rect(g, x, y, w, 1, BOX2); Rect(g, x + w / 2 - 1, y, 2, h, TAPE);
        }

        // Git poses: each returns the pose's MP (props drawn in body space, glyphs on top).
        static MP GitPose(string name, int k)
        {
            int a = k % 2;
            switch (name)
            {
                case "git_commit":   // stamps a box; the green tick appears after the stamp hits
                {
                    int ph = k % 12; int sy = ph < 6 ? 12 + ph : 18 - (ph - 6); bool hit = ph >= 5 && ph <= 7;
                    return new MP { Eyes = hit ? "happy" : "focus", Mouth = "flat", ArmsBack = new[] { ArmL },
                        Prop = (g, dx, dy) => {
                            Box(g, 9 + dx, 23 + dy, 14, 7);
                            if (ph >= 6) Pat(g, 13 + dx, 25 + dy, new[] { "....#", "#..#.", ".##.." }, PLUS);
                            Rect(g, 22 + dx, sy + dy, 2, 5, WOOD); Rect(g, 20 + dx, sy + 5 + dy, 6, 2, STAMP);
                        },
                        ArmsFront = new[] { A(23, sy + 2, 2.2, 2) },
                        Glyph = g => { if (hit) { Px(g, 7, 21, Y); Px(g, 26, 21, Y); Px(g, 8, 19, Y); Px(g, 25, 19, Y); } } };
                }
                case "git_push":     // launches a rocket up to origin
                {
                    int ph = k % 24; double ry = ph < 6 ? 16 : 16 - (ph - 6) * 1.6;
                    return new MP { Eyes = ph < 6 ? "focus" : "happy", Mouth = ph < 6 ? "flat" : "open", ArmsBack = new double[0][],
                        ArmsFront = ph < 6 ? new[] { A(22, 20, 2.2, 2), A(10, 22, 2.2, 2) } : new[] { A(4, 10, 2.3, 4), A(28, 10, 2.3, 4) },
                        Glyph = g => {
                            int x = 24, y = Rnd(ry);
                            Pat(g, x, y, new[] { ".#.", "###", "###", "###", "#.#" }, ROCKET); Px(g, x + 1, y + 2, BR_M);
                            if (ph >= 6) { Px(g, x + 1, y + 5, FLAME); if (k % 2 == 0) Px(g, x + 1, y + 6, Y); Px(g, x, y + 5 + a, FLAME); Px(g, x + 2, y + 6 - a, FLAME); }
                        } };
                }
                case "git_pull":     // hauls a box down on a rope
                {
                    int ph = k % 24; double by = -6 + Math.Min(ph, 16) * 1.3;
                    return new MP { Eyes = "focus", Mouth = ph >= 16 ? "smile" : "flat", ArmsBack = new double[0][], Dy = a,
                        ArmsFront = new[] { A(23, 8 + (a == 0 ? 0 : 2), 2.2, 2.4), A(24, 14 - (a == 0 ? 0 : 2), 2.2, 2.4) },
                        Glyph = g => { int y = Rnd(by); Line(g, 24, -8, 24, y, WOOD); Box(g, 21, y, 7, 5); Pat(g, 23, y + 1, new[] { ".#.", "###", ".#." }, BR_M); } };
                }
                case "git_fetch":    // binoculars, scanning left and right
                {
                    int lx = (k / 6) % 2 == 0 ? -1 : 1;
                    return new MP { Eyes = "dot", Mouth = "o", Lx = lx, ArmsBack = new double[0][], ArmsFront = new[] { A(10 + lx, 18, 2.2, 2), A(22 + lx, 18, 2.2, 2) },
                        Prop = (g, dx, dy) => { Ell(g, 13 + lx + dx, 15 + dy, 2.6, 2.6, GLS); Ell(g, 20 + lx + dx, 15 + dy, 2.6, 2.6, GLS); Rect(g, 15 + lx + dx, 15 + dy, 3, 1, GLS);
                                                Px(g, 12 + lx + dx, 14 + dy, BR_M); Px(g, 19 + lx + dx, 14 + dy, BR_M); } };
                }
                case "git_merge":    // two coloured branches braid into one in his paws
                {
                    int j = (k % 24) / 3;
                    return new MP { Eyes = "focus", Mouth = "flat", ArmsBack = new double[0][], ArmsFront = new[] { A(13, 24, 2.2, 2), A(19, 24, 2.2, 2) },
                        Glyph = g => {
                            Line(g, 6, 2, 15, 18, BR_A); Line(g, 26, 2, 17, 18, BR_B);
                            Rect(g, 16, 18, 1, Math.Min(8, j + 1), BR_M);
                            Ell(g, 6, 2, 1.5, 1.5, BR_A); Ell(g, 26, 2, 1.5, 1.5, BR_B); if (j > 5) Ell(g, 16, 27, 1.6, 1.6, BR_M);
                        } };
                }
                case "git_conflict": // pulls his fur while two arrows clash overhead
                {
                    int ph = k % 12, c = ph < 6 ? ph : 12 - ph;
                    return new MP { Dx = k % 4 < 2 ? -1 : 1, Eyes = "o", Mouth = "open", ArmsBack = new double[0][],
                        ArmsFront = new[] { A(8, 8 + a, 2.3, 3), A(24, 9 - a, 2.3, 3) },
                        Glyph = g => {
                            Pat(g, 4 + c, -5, new[] { "#..", "##.", "###", "##.", "#.." }, R); Pat(g, 26 - c, -5, new[] { "..#", ".##", "###", ".##", "..#" }, BR_M);
                            if (c >= 5) { Px(g, 15, -7, Y); Px(g, 17, -7, Y); Px(g, 16, -8, Y); Px(g, 16, -2, Y); }
                        } };
                }
                case "git_rebase":   // juggles three commit dots
                {
                    return new MP { Eyes = "dot", Ly = -1, Mouth = "flat", ArmsBack = new double[0][], ArmsFront = new[] { A(9, 22 - a, 2.2, 2), A(23, 21 + a, 2.2, 2) },
                        Glyph = g => {
                            uint[] col = { BR_A, BR_B, BR_M };
                            for (int i = 0; i < 3; i++) { double t = ((k + i * 8) % 24) / 24.0 * 2 * Math.PI; Ell(g, 16 + 11 * Math.Cos(t), 4 - 6 * Math.Abs(Math.Sin(t)), 1.6, 1.6, col[i]); }
                        } };
                }
                case "git_branch":   // waters a sprout (a new branch grows)
                {
                    int grow = Math.Min(6, (k % 48) / 6);
                    return new MP { Eyes = "happy", Mouth = "smile", ArmsBack = new[] { ArmL },
                        Prop = (g, dx, dy) => {
                            Rect(g, 4 + dx, 26, 6, 4, POT); Rect(g, 4 + dx, 26, 6, 1, 0xFF8A4A2C);
                            Line(g, 7 + dx, 25, 7 + dx, 25 - grow, LEAF); if (grow > 2) { Px(g, 6 + dx, 23, LEAF); Px(g, 8 + dx, 22, LEAF); } if (grow > 4) { Px(g, 5 + dx, 20, LEAF); Px(g, 9 + dx, 19, LEAF); }
                            Rect(g, 19 + dx, 20 + dy, 6, 4, CAN); Line(g, 19 + dx, 20 + dy, 14 + dx, 17 + dy, CAN);
                        },
                        ArmsFront = new[] { A(23, 21, 2.2, 2) },
                        Glyph = g => { if (k % 4 < 2) { Px(g, 12, 18, WATER); Px(g, 11, 20, WATER); } else { Px(g, 12, 19, WATER); Px(g, 11, 21, WATER); } } };
                }
                case "git_stash":    // tucks a box into a chest, lid closes
                {
                    int ph = k % 24; int by = ph < 10 ? 14 + ph : 24; bool shut = ph >= 14;
                    return new MP { Eyes = shut ? "happy" : "dot", Ly = 1, Mouth = shut ? "smile" : "flat", ArmsBack = new double[0][],
                        Prop = (g, dx, dy) => {
                            Rect(g, 8 + dx, 25, 16, 6, CHEST); Rect(g, 8 + dx, 27, 16, 1, CHEST2); Px(g, 16 + dx, 28, CHEST2);
                            if (!shut) { Box(g, 12 + dx, by, 8, 5); Rect(g, 8 + dx, 20, 16, 2, CHEST); } else Rect(g, 8 + dx, 23, 16, 2, CHEST);
                        },
                        ArmsFront = shut ? new[] { A(9, 22, 2.2, 2), A(23, 22, 2.2, 2) } : new[] { A(11, by + 2, 2.2, 2), A(21, by + 2, 2.2, 2) } };
                }
                case "git_log":      // unrolls a long scroll of history
                {
                    int len = Math.Min(12, (k % 36) / 3);
                    return new MP { Eyes = "dot", Ly = 1, Mouth = "flat", ArmsBack = new double[0][], ArmsFront = new[] { A(10, 21, 2.2, 2), A(22, 21, 2.2, 2) },
                        Prop = (g, dx, dy) => {
                            Rect(g, 11 + dx, 21 + dy, 10, 2 + len, PAPER); Rect(g, 10 + dx, 20 + dy, 12, 2, TAPE);
                            for (int i = 0; i < len; i += 2) { Px(g, 12 + dx, 23 + i + dy, BR_M); Rect(g, 14 + dx, 23 + i + dy, 5 - (i % 3), 1, LINE); }
                        } };
                }
                case "git_diff":     // magnifying glass over a page of red/green lines
                {
                    int mx = 12 + ((k / 4) % 6);
                    return new MP { Eyes = "dot", Ly = 1, Lx = mx > 14 ? 1 : -1, Mouth = "flat", ArmsBack = new[] { ArmL },
                        Prop = (g, dx, dy) => {
                            Rect(g, 8 + dx, 22 + dy, 16, 8, PAPER);
                            for (int i = 0; i < 3; i++) Rect(g, 10 + dx, 23 + i * 2 + dy, 10 - i * 2, 1, i % 2 == 0 ? PLUS : MINUS);
                            Ell(g, mx + dx, 25 + dy, 3, 3, GLASSF); Ell(g, mx + dx, 25 + dy, 2, 2, 0xFFDDEFF7); Line(g, mx + 2 + dx, 28 + dy, mx + 4 + dx, 30 + dy, WOOD);
                        },
                        ArmsFront = new[] { A(mx + 4, 30, 2.2, 1.8) } };
                }
                case "git_status":   // clipboard: ticks appear one by one
                {
                    int ticks = Math.Min(3, (k % 32) / 8);
                    return new MP { Eyes = "dot", Ly = 1, Mouth = "flat", ArmsBack = new[] { ArmL }, ArmsFront = new[] { A(23, 25 + a, 2.2, 2) },
                        Prop = (g, dx, dy) => {
                            Rect(g, 9 + dx, 20 + dy, 12, 11, BOX2); Rect(g, 10 + dx, 21 + dy, 10, 9, PAPER); Rect(g, 13 + dx, 19 + dy, 4, 2, GLS);
                            for (int i = 0; i < 3; i++) { Rect(g, 14 + dx, 23 + i * 2 + dy, 5, 1, LINE); if (i < ticks) Pat(g, 11 + dx, 22 + i * 2 + dy, new[] { "..#", "##." }, PLUS); else Px(g, 11 + dx, 23 + i * 2 + dy, LINE); }
                        } };
                }
                case "git_reset":    // sweeps with a broom, dust flies
                {
                    int sw = (k / 3) % 4; int bx = new[] { 6, 9, 12, 9 }[sw];
                    return new MP { Eyes = "focus", Mouth = "flat", Dy = a, ArmsBack = new double[0][], ArmsFront = new[] { A(bx + 8, 18, 2.2, 2), A(bx + 9, 22, 2.2, 2) },
                        Prop = (g, dx, dy) => { Line(g, bx + 10 + dx, 16, bx + 4 + dx, 28, WOOD); Rect(g, bx + dx, 27, 7, 3, BROOM); },
                        Glyph = g => { for (int i = 0; i < 3; i++) Px(g, 2 + ((k + i * 3) % 6), 27 - i * 2 - (k % 3), DUST); } };
                }
                case "git_tag":      // sticks a yellow price tag on a box
                {
                    int ph = k % 16; int tx = ph < 8 ? 26 - ph : 18;
                    return new MP { Eyes = ph >= 8 ? "happy" : "focus", Mouth = "smile", ArmsBack = new[] { ArmL },
                        Prop = (g, dx, dy) => { Box(g, 8 + dx, 23 + dy, 12, 7); Pat(g, tx + dx, 22 + dy, new[] { ".###", "####", ".###" }, Y); Px(g, tx + 1 + dx, 23 + dy, GLS); },
                        ArmsFront = new[] { A(tx + 4, 24, 2.2, 2) } };
                }
                case "git_cherry":   // plucks a cherry from a branch above
                {
                    int ph = k % 24; bool got = ph >= 10;
                    return new MP { Eyes = got ? "happy" : "dot", Ly = got ? 0 : -1, Mouth = got ? "grin" : "o", ArmsBack = new[] { ArmL },
                        ArmsFront = new[] { got ? A(21, 21, 2.2, 2) : A(26, 6 + (ph % 2), 2.2, 3) },
                        Glyph = g => {
                            Line(g, 18, -6, 31, -2, WOOD); Px(g, 22, -4, LEAF); Px(g, 23, -5, LEAF);
                            Line(g, 27, -3, 27, 1, LEAF); Ell(g, 26, 2, 1.6, 1.6, CHERRY);
                            if (!got) { Line(g, 29, -2, 29, 2, LEAF); Ell(g, 29, 3, 1.6, 1.6, CHERRY); } else { Ell(g, 22, 19, 1.6, 1.6, CHERRY); }
                        } };
                }
                case "git_pr":       // launches a paper plane
                {
                    int ph = k % 24; double px = ph < 6 ? 22 : 22 + (ph - 6) * 0.9, py = ph < 6 ? 16 : 16 - (ph - 6) * 1.1;
                    return new MP { Eyes = ph < 6 ? "focus" : "happy", Mouth = ph < 6 ? "flat" : "open", ArmsBack = new[] { ArmL },
                        ArmsFront = new[] { ph < 6 ? A(22, 18, 2.2, 2) : A(26, 10, 2.3, 4) },
                        Glyph = g => { Pat(g, Rnd(px), Rnd(py), new[] { "#....", "###..", "#####", "..#.." }, PAPER); if (ph > 6) { Px(g, Rnd(px) - 2, Rnd(py) + 4, DUST); Px(g, Rnd(px) - 4, Rnd(py) + 6, DUST); } } };
                }
                case "git_ci":       // waits on CI: hourglass flips every cycle
                {
                    int ph = k % 24; bool flip = ph >= 20; int sand = Math.Min(4, ph / 5);
                    return new MP { Eyes = "dot", Lx = (k / 8) % 2 == 0 ? -1 : 1, Mouth = "flat", ArmsBack = new double[0][], ArmsFront = new[] { A(10, 23, 2.2, 2), A(22, 23, 2.2, 2) },
                        Prop = (g, dx, dy) => {
                            int x = 12 + dx, y = 19 + dy;
                            Rect(g, x, y, 8, 1, WOOD); Rect(g, x, y + 10, 8, 1, WOOD);
                            Pat(g, x + 1, y + 1, new[] { "######", ".####.", "..##..", "..##..", ".####.", "######" }, GLASSF);
                            if (!flip) { Rect(g, x + 2, y + 1 + sand, 4, Math.Max(0, 2 - sand / 2), SAND); Rect(g, x + 2, y + 6 - sand / 2, 4, 1 + sand / 2, SAND); Px(g, x + 3, y + 4, SAND); }
                        } };
                }
            }
            return new MP();
        }

        // ---------- poses ----------
        static List<Layer> M(PixGrid g) { return new List<Layer> { new Layer(g, 4, 2) }; }

        static readonly uint[] Rainbow = { 0xFFE5645A, 0xFFF29A3C, 0xFFF2C94C, 0xFF62B37A, 0xFF6C9BE8, 0xFFA87FE0 };

        public static List<Layer> Pose(string name, int k, string skin, string acc) { return Pose(name, k, skin, acc, 0, 0); }

        // lookX/lookY (-1..1): where the open eyes look (used to follow the mouse in idle).
        public static List<Layer> Pose(string name, int k, string skin, string acc, int lookX, int lookY)
        {
            PhonesGreen = name == "spotify";
            if (PhonesGreen) acc = "headphones";
            Func<MP, PixGrid> body = p => { p.Acc = acc; return Body(skin, p); };
            if (name.StartsWith("git_")) return M(body(GitPose(name, k)));
            switch (name)
            {
                case "idle": return M(body(new MP { Dy = k % 16 < 8 ? 0 : 1, Eyes = k % 24 < 2 ? "closed" : "open", EarFlop = k % 48 > 44, Lx = lookX, Ly = lookY }));
                case "walk":       // side-step walk to the right (mirrored by the app for walking left)
                {
                    bool a = (k / 2) % 2 == 1;
                    return M(body(new MP { Dy = a ? -1 : 0, Lx = 1, Mouth = "smile", FootL = a ? -2 : 1, FootR = a ? 1 : 3,
                        ArmsBack = new[] { a ? A(6, 22, 2.5, 3) : A(7, 25, 2.5, 3) }, ArmsFront = new[] { a ? A(26, 24, 2.5, 3) : A(25, 21, 2.5, 3) } }));
                }
                case "sit":        // sits on an edge, legs dangling and swinging
                {
                    int sw = new[] { 0, 1, 2, 1, 0, -1, -2, -1 }[(k / 2) % 8];
                    return M(body(new MP { Sit = true, Swing = sw, Dy = -4, Eyes = k % 40 < 2 ? "closed" : "open", Lx = (k / 16) % 3 - 1, Mouth = "smile",
                        FootL = sw, FootR = -sw, ArmsBack = new double[0][], ArmsFront = new[] { A(9, 22, 2.3, 2), A(23, 22, 2.3, 2) } }));
                }
                case "music":      // head-bobs to the beat, notes float up
                {
                    bool beat = k % 4 < 2;
                    return M(body(new MP { Dy = beat ? 0 : 1, Dx = new[] { -1, 0, 1, 0 }[(k / 4) % 4], Eyes = (k / 8) % 2 == 0 ? "closed" : "happy", Mouth = beat ? "smile" : "o",
                        Glyph = g => { Pat(g, 26, 2 - (k % 12) / 3, new[] { ".###", ".#.#", "##.#", "##.." }, INK); Pat(g, 1, 6 - ((k + 6) % 12) / 3, new[] { ".#", ".#", "##" }, PK); } }));
                }
                case "full":       // context window nearly full: stuffed belly, puffing
                {
                    bool puff = k % 12 < 6;
                    return M(body(new MP { Dy = 1, Eyes = (k / 6) % 4 == 0 ? "closed" : "dot", Mouth = puff ? "o" : "flat", Puff = puff, ArmsBack = new double[0][],
                        Prop = (g, dx, dy) => { Ell(g, 16 + dx, 24 + dy, 8.5, 6.5, B); Ell(g, 14 + dx, 22 + dy, 2.5, 1.6, 0xFFD3CBBE); },
                        ArmsFront = new[] { A(8, 25, 2.3, 2), A(24, 25, 2.3, 2) },
                        Glyph = g => { if (puff) { Rect(g, 22, 19, 3, 1, STEAM); Rect(g, 24, 17, 3, 1, STEAM); } int dr = k % 16; if (dr < 8) Px(g, 26, 9 + dr / 2, WATER); } }));
                }
                case "plan":       // plan mode: a blueprint with lines appearing under a moving pencil
                {
                    int prog = k % 24, px0 = 9 + Math.Min(14, prog * 14 / 20);
                    return M(body(new MP { Eyes = "focus", Ly = 1, Mouth = "flat", ArmsBack = new[] { ArmL },
                        Prop = (g, dx, dy) => {
                            Rect(g, 7 + dx, 21 + dy, 18, 10, 0xFF3D6FB8);
                            for (int x = 9; x < 25; x += 3) Rect(g, x + dx, 21 + dy, 1, 10, 0xFF5584CC);
                            for (int y = 23; y < 31; y += 3) Rect(g, 7 + dx, y + dy, 18, 1, 0xFF5584CC);
                            Rect(g, 9 + dx, 24 + dy, Math.Max(1, px0 - 9), 1, PETAL); if (prog > 12) Rect(g, 9 + dx, 27 + dy, Math.Min(10, (prog - 12) * 2), 1, PETAL);
                            Line(g, px0 + dx, 24 + dy, px0 + 3 + dx, 20 + dy, Y); Px(g, px0 + dx, 24 + dy, DK);
                        },
                        ArmsFront = new[] { A(px0 + 3, 21, 2.2, 2) } }));
                }
                case "bath":       // bubble bath
                {
                    return M(body(new MP { Dy = 3, Eyes = "bliss", Mouth = "smile", ArmsBack = new double[0][],
                        Prop = (g, dx, dy) => {
                            Ell(g, 16 + dx, 28, 14, 5, PETAL); Rect(g, 3 + dx, 24, 26, 4, PETAL); Rect(g, 4 + dx, 24, 24, 2, 0xFFBFE3F5);
                            for (int i = 0; i < 6; i++) Ell(g, 6 + i * 4 + dx, 23 + (i % 2), 2, 1.6, Wh);
                            Rect(g, 6 + dx, 31, 2, 1, 0xFF9A9690); Rect(g, 24 + dx, 31, 2, 1, 0xFF9A9690);
                        },
                        ArmsFront = new[] { A(4, 23, 2.2, 2), A(28, 23, 2.2, 2) },
                        Glyph = g => { for (int i = 0; i < 3; i++) { int ph = (k + i * 5) % 15; Ell(g, 8 + i * 8 + (ph % 3), 20 - ph, 1.3, 1.3, 0xFFD8EEF8); } } }));
                }
                case "slot":       // peeks over a slot machine and pulls the lever; the reels are a separate overlay (SlotReels)
                {
                    int pull = k % 48; double ball = pull >= 1 && pull <= 6 ? new[] { 0, 16, 20, 24, 24, 20, 16 }[pull] : 12;
                    return M(body(new MP { Eyes = pull < 8 ? "focus" : "o", Ly = 1, Mouth = "o", ArmsBack = new double[0][],
                        Prop = (g, dx, dy) => {
                            Rect(g, 3, 18, 22, 14, SLOT); Rect(g, 3, 18, 22, 1, SLOT2); Rect(g, 3, 31, 22, 1, SLOT3);
                            for (int i = 0; i < 6; i++) Px(g, 5 + i * 3, 19, (i + k / 3) % 2 == 0 ? Y : R);   // chasing lights
                            Rect(g, 4, 21, 19, 9, DK);
                            foreach (int x in SlotReelLX) Rect(g, x, 22, 5, 7, REELBG);
                            Rect(g, 6, 30, 15, 1, SLOT3);
                            Line(g, 25, 26, 27, 26, SLOT3); Line(g, 27, 26, 27, ball, SLOT2); Ell(g, 27, ball - 1, 1.8, 1.8, R); Px(g, 26.5, ball - 2, 0xFFFFB0A8);
                        },
                        ArmsFront = new[] { A(7, 18, 2.3, 2), A(27, ball, 2.2, 2) } }));
                }
                case "spotify":    // music is playing: first half DJs at a deck (spins the record, scratches), second half dances
                {
                    Action<PixGrid> notes = g => {
                        int t = k % 12;
                        Pat(g, 0, 7 - t / 2, new[] { ".##", ".#.", "##." }, t < 9 ? Wh : 0xFF9A9AA0);
                        int t2 = (k + 6) % 12;
                        Pat(g, 28, 6 - t2 / 2, new[] { "###", "#.#", "#.#" }, t2 < 9 ? 0xFFB6F2C8 : 0xFF6A9A7A);
                    };
                    if (k % 48 < 24)
                    {
                        int sc = new[] { 0, 1, 2, 1 }[(k / 2) % 4];
                        bool nod = k % 4 < 2;
                        return M(body(new MP { Dy = nod ? 0 : 1, Eyes = k % 12 < 6 ? "focus" : "happy", Mouth = "smile", ArmsBack = new double[0][],
                            Prop = (g, dx, dy) => {
                                Rect(g, 2, 24, 28, 7, DECK); Rect(g, 2, 24, 28, 1, DECK2);
                                Ell(g, 10, 26.5, 5, 1.8, VINYL); Ell(g, 10, 26.5, 3, 1, VINYL2); Ell(g, 10, 26.5, 1, 0.6, SPG);
                                double a = k * Math.PI / 6; Px(g, 10 + Math.Cos(a) * 4, 26.5 + Math.Sin(a) * 1.4, 0xFF8A8A92);
                                Rect(g, 19, 25, 8, 1, DECK2); Rect(g, 20 + (k / 4) % 6, 24, 2, 2, SPG);
                                for (int i = 0; i < 5; i++)   // level meter on the deck
                                {
                                    int h = 1 + (k * 5 + i * 7 + (k / 3) * i) % 4;
                                    for (int y = 0; y < h; y++) Px(g, 19 + i * 2, 29 - y, y >= 3 ? R : y >= 2 ? Y : SPG);
                                }
                            },
                            ArmsFront = new[] { A(9 + sc, 23, 2.3, 2), A(22 + (k / 4) % 6, 22, 2.3, 2) } }));
                    }
                    int ph = (k / 3) % 4; bool up = ph % 2 == 0;
                    return M(body(new MP { Dy = up ? -1 : 1, Dx = new[] { -1, 0, 1, 0 }[ph], Eyes = up ? "closed" : "happy", Mouth = up ? "o" : "smile",
                        FootL = up ? -1 : 0, FootR = up ? 0 : 1, ArmsBack = new double[0][],
                        ArmsFront = ph < 2 ? new[] { A(5, 11, 2.3, 4), A(26, 22, 2.5, 3) } : new[] { A(7, 22, 2.5, 3), A(27, 11, 2.3, 4) },
                        Glyph = notes }));
                }
                case "levelup":    // level up: hops with arms up while stars burst out and an arrow rises
                {
                    int ph = k % 12, hop = new[] { 0, -2, -3, -4, -3, -2, 0, 0, 0, 0, 0, 0 }[ph];
                    return M(body(new MP { Dy = hop, Eyes = "happy", Mouth = "grin", Puff = true, ArmsBack = new double[0][],
                        ArmsFront = new[] { A(5, 11 + hop, 2.3, 4), A(27, 11 + hop, 2.3, 4) },
                        Glyph = g => {
                            uint[] cs = { GOLD, PK, 0xFF7FD1A8, 0xFF6C9BE8 };
                            double r = 7 + (k % 12) * 1.3;
                            for (int i = 0; i < 8; i++)
                            {
                                double ang = i * Math.PI / 4 + 0.3;
                                Px(g, 16 + Math.Cos(ang) * r * 1.2, 14 + Math.Sin(ang) * r, cs[(i + k / 3) % 4]);
                                if (k % 12 < 8) Px(g, 16 + Math.Cos(ang) * (r - 2) * 1.2, 14 + Math.Sin(ang) * (r - 2), Wh);
                            }
                            Pat(g, 13, -7 - (k % 12) / 4, new[] { "...#...", "..###..", ".#####.", "#######", "..###..", "..###.." }, GOLD);
                        } }));
                }
                case "jump":       // mid-air: arms up, feet tucked
                    return M(body(new MP { Dy = -2, Eyes = "open", Mouth = "o", FootL = 1, FootR = -1, ArmsBack = new double[0][],
                                           ArmsFront = new[] { A(5, 12, 2.3, 4), A(27, 12, 2.3, 4) } }));
                case "secret":     // rainbow dance
                {
                    bool up = k % 4 < 2;
                    return M(body(new MP { Dy = up ? -2 : 0, Dx = up ? -1 : 1, Eyes = "happy", Mouth = "grin", ArmsBack = new double[0][],
                        ArmsFront = up ? new[] { A(4, 9, 2.3, 4), A(26, 16, 2.3, 3.5) } : new[] { A(6, 16, 2.3, 3.5), A(28, 9, 2.3, 4) },
                        Glyph = g => {
                            for (int i = 0; i < 6; i++)
                                for (int x = 2; x <= 29; x++)
                                {
                                    double t = (x - 15.5) / 14.0; int y = Rnd(-2 + i - Math.Sqrt(Math.Max(0, 1 - t * t)) * 7);
                                    if ((x + k) % 3 != 0) Px(g, x, y, Rainbow[(i + k / 2) % 6]);
                                }
                        } }));
                }
                case "work":
                {
                    int a = k % 2;
                    var m = body(new MP { Eyes = "focus", Mouth = "flat", Lx = Math.Max(-1, Math.Min(1, Rnd(Math.Cos(k * 2 * Math.PI / 24) * 1.5))),
                                          ArmsBack = new double[0][], Prop = Laptop(k / 2), ArmsFront = new[] { A(10, 25 + a, 2.3, 2), A(22, 26 - a, 2.3, 2) } });
                    return new List<Layer> { new Layer(m, 4, 2) };   // buddies are added in Render (one per session)
                }
                case "wave": return M(body(new MP { Mouth = "open", Eyes = "happy", ArmsBack = new[] { ArmL }, ArmsFront = new[] { k % 4 < 2 ? A(28, 11, 2.3, 4) : A(29, 9, 2.3, 4) } }));
                case "think": return M(body(new MP { Eyes = "dot", Lx = (k / 6) % 2 == 1 ? 1 : -1, Ly = -1, Mouth = "flat", ArmsBack = new[] { ArmL },
                                                     ArmsFront = new[] { A(20, 21 + (k % 4 < 2 ? 0 : 1), 2.4, 2) },
                                                     Glyph = g => Pat(g, 26, 1 + (k % 6 < 3 ? 0 : 1), new[] { ".##.", "#..#", "..#.", ".#..", "....", ".#.." }, INK) }));
                case "run":
                {
                    bool a = k % 2 == 1;
                    return M(body(new MP { Dx = 2, Dy = a ? -1 : 0, Mouth = "o", FootL = a ? -3 : 1, FootR = a ? 1 : 3, EarFlop = true,
                        ArmsBack = new[] { a ? A(5, 22, 2.5, 3) : A(6, 25, 2.5, 3) }, ArmsFront = new[] { a ? A(27, 22, 2.5, 3) : A(26, 19, 2.5, 3) },
                        Glyph = g => { int o = k % 2; Rect(g, 0 + o, 12, 4, 1, SL); Rect(g, 1 - o, 16, 6, 1, SL); Rect(g, 0 + o, 20, 3, 1, SL); } }));
                }
                case "happy": return M(body(new MP { Dx = new[] { -1, 0, 1, 0 }[(k / 2) % 4], Eyes = "happy",
                                                     Glyph = g => Pat(g, 25, 2 + (k % 4 < 2 ? 0 : -1), new[] { ".###", ".#.#", ".#.#", "##.#", "##.." }, INK) }));
                case "sleep":
                    if (skin == "monkey") return M(Sleeping(k));
                    return M(body(new MP { Eyes = "closed", Mouth = "flat", Dy = 2 + (k % 16 < 8 ? 0 : 1), EarFlop = true, Glyph = g => GlyphZ(g, k) }));
                case "milk":
                    if (skin == "monkey") return M(MonkeyMilk(k, acc));
                    return M(body(new MP { Eyes = "bliss", Mouth = "pucker", Prop = MilkProp(k), ArmsBack = new double[0][], ArmsFront = new[] { A(11, 26, 2.4, 2.2), A(21, 26, 2.4, 2.2) } }));
                case "water":
                    return M(body(new MP { Eyes = "bliss", Mouth = k % 12 < 6 ? "pucker" : "smile", Prop = Glass(k), ArmsBack = new[] { ArmL },
                                           ArmsFront = new[] { A(20, (k % 12 < 6 ? 25 : 23), 2.2, 2) } }));
                case "stretch":
                {
                    int ph = (k / 6) % 4; bool up = ph < 3; int j = up ? -1 : 0;
                    return M(body(new MP { Dy = j, Eyes = "closed", Mouth = ph == 1 || ph == 2 ? "open" : "flat", ArmsBack = new double[0][],
                        ArmsFront = up ? new[] { A(5, 6, 2.2, 4.5), A(27, 6, 2.2, 4.5) } : new[] { A(6, 18, 2.3, 4), A(26, 18, 2.3, 4) },
                        Glyph = g => { if (ph == 1 || ph == 2) { Pat(g, 26, 14, new[] { "#.#", ".#." }, INK); } } }));
                }
                case "read": return M(body(new MP { Eyes = "dot", Ly = 1, Lx = (k / 4) % 3 - 1, Mouth = "flat", ArmsBack = new double[0][], Prop = Book(k),
                                                    ArmsFront = new[] { A(8, 25, 2.2, 2), A(24, 25, 2.2, 2) } }));
                case "banana": return M(body(new MP { Eyes = "happy", Mouth = k % 4 < 2 ? "chew" : "grin", Prop = Banana(k), ArmsBack = new[] { ArmL }, ArmsFront = new[] { A(21, 22, 2.4, 2.2) } }));
                case "error": return M(body(new MP { Dx = k % 16 < 6 ? new[] { -1, 1, -1, 1, 0, 0 }[k % 16] : 0, Eyes = "o", Mouth = "open",
                                                     Glyph = g => { if (k % 4 < 3) Pat(g, 27, 1, new[] { "#", "#", "#", "#", ".", "#" }, R); } }));
                case "love": return M(body(new MP { Eyes = "heart", Dy = k % 8 < 4 ? 0 : -1,
                                                    Glyph = g => { Pat(g, 1, 10 - (k % 12) / 2, Heart, PK); Pat(g, 27, 12 - ((k + 6) % 12) / 2, Heart, PK); } }));
                case "type":
                {
                    int a = k % 2;
                    return M(body(new MP { Eyes = "focus", Mouth = "flat", ArmsBack = new double[0][], Prop = LaptopSide(k),
                        ArmsFront = new[] { A(19 + a, 21, 2.2, 1.8), A(23 - a, 21, 2.2, 1.8) },
                        Glyph = g => { for (int i = 0; i < 3; i++) if ((k + i) % 3 != 0) Rect(g, 2 + i * 3, 3, 2, 1, SC); } }));
                }
                case "done":
                {
                    int j = new[] { 0, -2, -3, -2, 0, 0 }[k % 6]; bool up = k % 6 < 4;
                    return M(body(new MP { Dy = j, Eyes = "happy", Mouth = "grin", ArmsBack = new double[0][], EarFlop = !up,
                        ArmsFront = up ? new[] { A(4, 10 + j, 2.3, 4), A(28, 10 + j, 2.3, 4) } : new[] { A(5, 16 + j, 2.3, 3.5), A(27, 16 + j, 2.3, 3.5) },
                        Glyph = g => {
                            int[][] at = { new[] { 2, 3 }, new[] { 27, 2 }, new[] { 29, 13 }, new[] { 1, 14 } };
                            for (int i = 0; i < at.Length; i++) { if ((k + i * 2) % 6 < 3) Pat(g, at[i][0], at[i][1], Plus, Y); else Px(g, at[i][0] + 1, at[i][1] + 1, Y); }
                        } }));
                }
                case "home":
                {
                    bool walk = k >= 16, a = k % 2 == 1;
                    int j = walk ? (a ? -1 : 0) : new[] { 0, -2, -3, -2, 0, 0, 0, 0 }[k % 8];
                    int off = walk ? Rnd((k - 16) * 1.25) : 0;   // walks off the right edge of the stage
                    var m = body(new MP { Dy = j, Eyes = "happy", Mouth = walk ? "smile" : "grin",
                        FootL = walk ? (a ? -2 : 1) : 0, FootR = walk ? (a ? 1 : 3) : 0,
                        ArmsBack = walk ? new[] { a ? A(6, 22, 2.5, 3) : A(7, 25, 2.5, 3) } : new double[0][],
                        ArmsFront = walk ? new[] { A(24, 21, 2.2, 2) } : new[] { A(4, 10 + j, 2.3, 4), A(24, 21, 2.2, 2) },
                        Prop = Bindle });
                    return new List<Layer> { new Layer(m, 4 + off, 2) };
                }
                case "gone": return new List<Layer> { new Layer(House(k), 0, -Top) };
                case "bash":
                {
                    int cyc = k % 24; bool enter = cyc >= 12 && cyc < 14; int a = k % 2;
                    return M(body(new MP { Eyes = "focus", Mouth = "flat", Ly = 1, Dy = enter ? 1 : 0, ArmsBack = new double[0][], Prop = Terminal(k),
                        ArmsFront = enter ? new[] { A(8, 30, 2.3, 1.8), A(25, 29, 2.4, 2) } : new[] { A(8, 30 + a, 2.3, 1.8), A(24, 31 - a, 2.3, 1.8) },
                        Glyph = g => { if (k % 8 < 5) { Pat(g, 25, 2, new[] { "#..", ".#.", "#.." }, 0xFF5FE08A); Rect(g, 28, 4, 3, 1, 0xFF5FE08A); } } }));
                }
                case "lunch":
                {
                    int ph = k % 12; bool lift = ph < 6; double cy = lift ? 24 - ph * 0.6 : 21;
                    return M(body(new MP { Eyes = lift ? "dot" : "happy", Ly = lift ? 1 : 0, Mouth = lift ? "o" : (k % 2 == 0 ? "chew" : "flat"),
                        ArmsBack = new[] { ArmL }, Prop = Lunch(k, cy, lift), ArmsFront = new[] { A(25, cy - 1, 2.2, 2) },
                        Glyph = g => Steam(g, k) }));
                }
                case "wait": return M(body(new MP { Back = true, Dx = k % 16 < 8 ? 0 : 1,
                                                    Glyph = g => { int n = k % 16 < 4 ? 1 : k % 16 < 8 ? 2 : 3; for (int i = 0; i < n; i++) Rect(g, 24 + i * 3, 26, 2, 2, INK); } }));
            }
            return M(body(new MP()));
        }

        // Running buddies (one per session, only in "work") and helper minis, split into the layers that go
        // behind the character and the ones in front of it.
        public static void Extras(string pose, int k, int sessions, int helpers, string miniSkin, List<Layer> back, List<Layer> front)
        {
            if (pose == "work")
            {
                int n = Math.Max(1, Math.Min(SessionColors.Length, sessions));
                for (int i = 0; i < n; i++)
                {
                    double th = k * 2 * Math.PI / 24 + i * 2 * Math.PI / n, s = Math.Sin(th);
                    int dir = -s > 0 ? 1 : -1;
                    var b = new Layer(Buddy(k + i, dir, SessionColors[i]), Rnd(20 + 16 * Math.Cos(th) - 5), Rnd(26 + 5 * s - 5 - ((k + i) % 2)));
                    (s < 0 ? back : front).Add(b);
                }
            }
            if (Rank >= 12 && AuraOn) Aura(k, back, front);
            if (Flies)
                for (int i = 0; i < 3; i++)
                {
                    double a = k * 0.35 + i * 2.1; var fly = new PixGrid(3, 3);
                    Px(fly, 1, 1, 0xFF1C1714); if (k % 2 == 0) { Px(fly, 0, 0, 0xFFD8EEF8); Px(fly, 2, 0, 0xFFD8EEF8); }
                    front.Add(new Layer(fly, Rnd(19 + Math.Cos(a) * (12 + i * 2)), Rnd(10 + Math.Sin(a * 1.3) * 7)));
                }
            for (int i = 0; i < Math.Min(helpers, 4); i++)
            {
                // Helpers trot back and forth along the ground, each on its own phase.
                double t = ((k + i * 13) % 48) / 48.0, x = t < 0.5 ? t * 2 : 2 - t * 2;
                int dir = t < 0.5 ? 1 : -1;
                front.Add(new Layer(Mini(miniSkin, k + i, dir), Rnd(x * 30), 25 - (i % 2) * 3));
            }
        }

        // Only the extras (behind or in front), for drawing over/under any character, including plugins.
        public static byte[] RenderExtras(string pose, int k, int sessions, int helpers, string miniSkin, bool front)
        {
            var b = new List<Layer>(); var f = new List<Layer>();
            Extras(pose, k, sessions, helpers, miniSkin, b, f);
            return Flatten(front ? f : b);
        }

        // Flattens a pose (+ buddies + helper minis) onto the stage, BGRA bytes.
        public static byte[] Render(string pose, int k, string skin, string acc, int sessions, int helpers)
        {
            var back = new List<Layer>(); var front = new List<Layer>();
            Extras(pose, k, sessions, helpers, skin, back, front);
            back.AddRange(Pose(pose, k, skin, acc)); back.AddRange(front);
            return Flatten(back);
        }

        public static byte[] RenderBody(string pose, int k, string skin, string acc) { return Flatten(Pose(pose, k, skin, acc)); }
        public static byte[] RenderBody(string pose, int k, string skin, string acc, int lookX, int lookY) { return Flatten(Pose(pose, k, skin, acc, lookX, lookY)); }

        // ---------- backdrops: a rounded pixel scene behind the character ----------
        public static byte[] Backdrop(string name)
        {
            var g = new PixGrid(StageW, StageH);
            if (name == "none") return ToBgra(g);
            Func<int, int, bool> inside = (x, y) => { int x0 = 3, y0 = 6, x1 = 36, y1 = 42, r = 4; if (x < x0 || x > x1 || y < y0 || y > y1) return false;
                int cx = x < x0 + r ? x0 + r : x > x1 - r ? x1 - r : x, cy = y < y0 + r ? y0 + r : y > y1 - r ? y1 - r : y; return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r; };
            for (int y = 0; y < StageH; y++)
                for (int x = 0; x < StageW; x++)
                {
                    if (!inside(x, y)) continue; uint c;
                    switch (name)
                    {
                        case "beach": c = y < 28 ? (y < 18 ? 0xFF8EC9EE : 0xFFB5DCF2) : y < 31 ? 0xFF4F9ED8 : 0xFFF0D9A0; break;
                        case "space": c = ((x * 7 + y * 13) % 29 == 0) ? 0xFFFFFFFF : y > 36 ? 0xFF6B5E8C : 0xFF1B1830; break;
                        case "forest": c = y < 26 ? 0xFFB9DDB0 : y < 34 ? 0xFF6FAF6A : 0xFF4E8A4B; break;
                        default: c = y < 34 ? 0xFFE9E1D2 : 0xFFB89A74; break;   // office
                    }
                    g.P[y * StageW + x] = c;
                }
            if (name == "beach") { Ell(g, 30, 12, 3, 3, Y); Rect(g, 5, 33, 3, 1, 0xFFE0C48A); }
            if (name == "space") { Ell(g, 9, 13, 3, 3, 0xFFD97757); Ell(g, 9, 13, 5, 1, 0xFFA87FE0); }
            if (name == "forest") foreach (var tx in new[] { 7, 31 }) { Pat(g, tx - 3, 14, new[] { "...#...", "..###..", ".#####.", "#######", "...#..." }, 0xFF3F7A3C); }
            if (name == "office") { Rect(g, 26, 11, 8, 8, 0xFF9CC8E8); Rect(g, 29, 11, 1, 8, PETAL); Rect(g, 26, 14, 8, 1, PETAL); Rect(g, 6, 28, 3, 6, POT); Pat(g, 5, 23, new[] { ".#.#.", "#####", ".###." }, LEAF); }
            return ToBgra(g);
        }

        // ---------- mini-game sprites ----------
        // Obstacles and pickups for the banana runner: "bug", "error", "stack", "banana", "goldbanana", "cloud".
        public static PixGrid GameSprite(string kind, int k)
        {
            PixGrid g;
            switch (kind)
            {
                case "bug":
                    g = new PixGrid(14, 11);
                    Ell(g, 7, 6, 5, 3.6, 0xFF3A2E2A); Ell(g, 7, 6, 3.4, 2.4, 0xFFC8303C); Rect(g, 7, 3, 1, 6, 0xFF3A2E2A);
                    Px(g, 4, 5, Wh); Px(g, 9, 7, Wh); Px(g, 2, 3, 0xFF3A2E2A); Px(g, 12, 3, 0xFF3A2E2A);
                    int l = k % 2; for (int i = 0; i < 3; i++) { Px(g, 2 + i * 4 + l, 10, 0xFF3A2E2A); Px(g, 3 + i * 4 - l, 10, 0xFF3A2E2A); }
                    Outline(g, 0xFF1C1714); return g;
                case "error":
                    g = new PixGrid(12, 14);
                    Rect(g, 1, 1, 10, 12, R); Rect(g, 5, 3, 2, 5, Wh); Rect(g, 5, 9, 2, 2, Wh); Outline(g, 0xFF6A1E1A); return g;
                case "stack":       // tall pile of TODO notes
                    g = new PixGrid(12, 20);
                    for (int i = 0; i < 4; i++) { Rect(g, 1 + (i % 2), 1 + i * 5, 9, 4, i % 2 == 0 ? Y : 0xFFF2E29A); Rect(g, 3 + (i % 2), 2 + i * 5, 5, 1, LINE); }
                    Outline(g, 0xFF6B5A2A); return g;
                case "banana": case "goldbanana":
                    g = new PixGrid(9, 9); uint c = kind == "banana" ? Y : GOLD;
                    Pat(g, 1, 1 + (k % 4 < 2 ? 0 : 1), new[] { "......#", ".....##", "....##.", "..###..", "###....", ".#....." }, c);
                    Px(g, 7, 1 + (k % 4 < 2 ? 0 : 1), WOOD); Outline(g, Y2);
                    if (kind == "goldbanana" && k % 6 < 3) { Px(g, 0, 0, Wh); Px(g, 8, 7, Wh); }
                    return g;
                default:            // cloud
                    g = new PixGrid(20, 8);
                    Ell(g, 6, 5, 5, 3, 0xFF3A3936); Ell(g, 12, 4, 6, 3.6, 0xFF3A3936); Ell(g, 16, 5.5, 3.5, 2.5, 0xFF3A3936); return g;
            }
        }

        // Grid → BGRA bytes (for the game and sticker icons).
        public static byte[] ToBgra(PixGrid g)
        {
            var px = new byte[g.W * g.H * 4];
            for (int i = 0; i < g.P.Length; i++) { uint c = g.P[i]; if (c == 0) continue; px[i * 4] = (byte)c; px[i * 4 + 1] = (byte)(c >> 8); px[i * 4 + 2] = (byte)(c >> 16); px[i * 4 + 3] = (byte)(c >> 24); }
            return px;
        }

        public static byte[] Flatten(List<Layer> layers)
        {
            var px = new byte[StageW * StageH * 4];
            foreach (var l in layers)
                for (int ry = 0; ry < l.G.H; ry++)
                    for (int x = 0; x < l.G.W; x++)
                    {
                        uint c = l.G.P[ry * l.G.W + x]; if (c == 0) continue;
                        int sx = x + l.X, sy = ry - l.G.OffY + l.Y + Top; if (sx < 0 || sy < 0 || sx >= StageW || sy >= StageH) continue;
                        int d = (sy * StageW + sx) * 4;
                        px[d] = (byte)c; px[d + 1] = (byte)(c >> 8); px[d + 2] = (byte)(c >> 16); px[d + 3] = (byte)(c >> 24);
                    }
            return px;
        }

        public static byte[] Render(string pose, int k) { return Render(pose, k, "monkey", "none", 1, 0); }
    }
}
