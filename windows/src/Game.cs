// Fun bits: the banana runner (a dino-style game), the easter-egg album and the pixel click burst.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace XingPixel
{
    // Endless runner: Xing jumps over bugs, errors and TODO piles and grabs bananas. Space / ↑ / click to jump, Esc to close.
    class GameWindow : Window
    {
        const int W = 320, H = 96, Ground = 84, Scale = 2;
        class Thing { public string Kind; public double X; public int Y, Wd, Ht; public bool Taken; }

        readonly WriteableBitmap screen = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null);
        readonly uint[] buf = new uint[W * H];
        readonly string skin; readonly Character plugin;
        readonly Random rnd = new Random();
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        readonly TextBlock scoreText, hint;
        readonly Dictionary<string, byte[]> spriteCache = new Dictionary<string, byte[]>();
        readonly List<Thing> things = new List<Thing>();
        double y, vy, speed, dist, nextSpawn, cloudX;
        int frame, score, best, bananas; bool running, over;
        public event Action<int, int> Finished;   // score, bananas

        public GameWindow(string skin, Character plugin, int best)
        {
            this.skin = skin; this.plugin = plugin; this.best = best;
            Title = "Банановый раннер"; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1E, 0x1C));
            var img = new Image { Source = screen, Width = W * Scale, Height = H * Scale };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            scoreText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEB, 0xE3)), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 14,
                                        HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 12, 0) };
            hint = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC6, 0xBC)), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 14,
                                   HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center,
                                   Text = "пробел, ↑ или клик — прыжок\nбаги перепрыгивай, бананы лови" };
            var g = new Grid(); g.Children.Add(img); g.Children.Add(scoreText); g.Children.Add(hint);
            Content = g;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Space || e.Key == Key.Up || e.Key == Key.W) Jump(); };
            MouseLeftButtonDown += delegate { Jump(); };
            timer.Tick += delegate { Step(); };
            Closed += delegate { timer.Stop(); if (running || over) Report(); };
            Reset(); Draw();
            timer.Start();
        }

        void Reset()
        {
            y = 0; vy = 0; speed = 2.2; dist = 0; nextSpawn = 60; score = 0; bananas = 0; things.Clear(); over = false;
        }

        void Jump()
        {
            if (over) { Reset(); running = true; hint.Visibility = Visibility.Collapsed; return; }
            if (!running) { running = true; hint.Visibility = Visibility.Collapsed; }
            if (y <= 0.01) vy = 6.3;
        }

        bool reported;
        void Report() { if (reported) return; reported = true; if (Finished != null) Finished(Math.Max(score, best), bananas); }

        void Step()
        {
            frame++;
            if (running && !over)
            {
                dist += speed; speed = Math.Min(6.5, 2.2 + dist / 2500.0);
                score = (int)(dist / 6) + bananas * 25;
                vy -= 0.42; y = Math.Max(0, y + vy); if (y == 0) vy = 0;
                cloudX -= speed * 0.25; if (cloudX < -30) cloudX = W;
                nextSpawn -= speed;
                if (nextSpawn <= 0)
                {
                    double r = rnd.NextDouble();
                    if (r < 0.28) things.Add(new Thing { Kind = rnd.NextDouble() < 0.12 ? "goldbanana" : "banana", X = W, Y = Ground - 30 - rnd.Next(14), Wd = 9, Ht = 9 });
                    else { string k = r < 0.6 ? "bug" : r < 0.85 ? "error" : "stack"; var s = Sprites.GameSprite(k, 0); things.Add(new Thing { Kind = k, X = W, Y = Ground - s.H, Wd = s.W, Ht = s.H }); }
                    nextSpawn = 70 + rnd.Next(110) - speed * 4;
                }
                foreach (var t in things) t.X -= speed;
                things.RemoveAll(t => t.X < -24 || t.Taken);

                // Hitbox of the runner: body of the 40x44 sprite placed at x=20, feet on the ground.
                int px = 20 + 12, pw = 16, py = Ground - (int)y - 34, ph = 30;
                foreach (var t in things)
                {
                    bool hit = t.X + 2 < px + pw && t.X + t.Wd - 2 > px && t.Y + 2 < py + ph && t.Y + t.Ht - 1 > py;
                    if (!hit) continue;
                    if (t.Kind.EndsWith("banana")) { t.Taken = true; bananas += t.Kind == "goldbanana" ? 4 : 1; }
                    else { over = true; running = false; best = Math.Max(best, score); hint.Text = "ой! " + score + " очков\nпробел — ещё раз, Esc — выйти"; hint.Visibility = Visibility.Visible; Report(); reported = false; }
                }
            }
            scoreText.Text = "🍌 " + bananas + "   " + score.ToString("00000") + "   рекорд " + Math.Max(best, score).ToString("00000");
            Draw();
        }

        byte[] Sprite(string key, Func<byte[]> make) { byte[] b; if (!spriteCache.TryGetValue(key, out b)) { b = make(); spriteCache[key] = b; } return b; }

        void Blit(byte[] bgra, int sw, int sh, int dx, int dy)
        {
            for (int yy = 0; yy < sh; yy++)
            {
                int ty = dy + yy; if (ty < 0 || ty >= H) continue;
                for (int xx = 0; xx < sw; xx++)
                {
                    int tx = dx + xx; if (tx < 0 || tx >= W) continue;
                    int s = (yy * sw + xx) * 4; if (bgra[s + 3] == 0) continue;
                    buf[ty * W + tx] = (uint)(bgra[s] | bgra[s + 1] << 8 | bgra[s + 2] << 16 | 0xFF << 24);
                }
            }
        }

        void Draw()
        {
            for (int i = 0; i < buf.Length; i++) buf[i] = 0xFF1F1E1C;
            var cloud = Sprites.GameSprite("cloud", 0);
            Blit(Sprite("cloud", () => Sprites.ToBgra(cloud)), cloud.W, cloud.H, (int)cloudX, 14);
            Blit(Sprite("cloud", () => Sprites.ToBgra(cloud)), cloud.W, cloud.H, (int)cloudX + 170, 26);
            for (int x = 0; x < W; x++) { buf[Ground * W + x] = 0xFF5A5650; if (((x + (int)dist) / 7) % 5 == 0) buf[(Ground + 3) * W + x] = 0xFF3A3936; if (((x + (int)dist) / 11) % 7 == 0) buf[(Ground + 7) * W + x] = 0xFF3A3936; }
            foreach (var t in things)
            {
                var g = Sprites.GameSprite(t.Kind, frame / 6);
                Blit(Sprites.ToBgra(g), g.W, g.H, (int)t.X, t.Y);
            }
            // The runner: built-in Xing (walk / jump poses) or a plugin's frames.
            string pose = y > 0 ? "jump" : "walk"; int k = (frame / 3) % Sprites.Frames;
            byte[] me; int mw = Sprites.StageW, mh = Sprites.StageH;
            if (plugin == null) me = Sprite(pose + k, () => Sprites.RenderBody(pose, k, skin, "none"));
            else
            {
                var fr = plugin.Frames(plugin.Resolve(y > 0 ? "jump" : "run")); var f = fr[(frame / 4) % fr.Length];
                var scaled = new TransformedBitmap(f, new ScaleTransform(40.0 / f.PixelWidth, 40.0 / f.PixelWidth));
                var conv = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
                mw = conv.PixelWidth; mh = conv.PixelHeight; me = new byte[mw * mh * 4]; conv.CopyPixels(me, mw * 4, 0);
            }
            Blit(me, mw, mh, 20, Ground - (int)y - mh + 2);
            screen.WritePixels(new Int32Rect(0, 0, W, H), buf, W * 4, 0);
        }
    }

    // Easter-egg album: unlocked stickers in colour with the day they were found, locked ones greyed with a hint.
    class CollectionWindow : Window
    {
        public CollectionWindow(Stats s, string skin)
        {
            Title = "Коллекция пасхалок · " + s.Stickers.Count + " из " + Brain.StickerList.Length; Width = 640; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1E, 0x1C));
            var wrap = new WrapPanel { Margin = new Thickness(12) };
            foreach (var st in Brain.StickerList)
            {
                string day; bool got = s.Stickers.TryGetValue(st.Id, out day);
                var px = Sprites.RenderBody(st.Pose, 6, skin == "monkey" ? "monkey" : "monkey", "none");
                if (!got) for (int i = 0; i < px.Length; i += 4) { byte gray = (byte)((px[i] + px[i + 1] + px[i + 2]) / 3 / 3 + 30); px[i] = px[i + 1] = px[i + 2] = gray; }
                var bmp = BitmapSource.Create(Sprites.StageW, Sprites.StageH, 96, 96, PixelFormats.Bgra32, null, px, Sprites.StageW * 4);
                var img = new Image { Source = bmp, Width = 80, Height = 88 }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                var title = new TextBlock { Text = got ? st.Title : "???", FontWeight = FontWeights.SemiBold, FontSize = 13, TextAlignment = TextAlignment.Center,
                                            Foreground = new SolidColorBrush(got ? Color.FromRgb(0xEE, 0xEB, 0xE3) : Color.FromRgb(0x8A, 0x87, 0x80)) };
                var sub = new TextBlock { Text = got ? day : st.Hint, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                                          Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0x98, 0x8E)) };
                var stack = new StackPanel { Width = 130 }; stack.Children.Add(img); stack.Children.Add(title); stack.Children.Add(sub);
                wrap.Children.Add(new Border { Child = stack, Margin = new Thickness(6), Padding = new Thickness(8), CornerRadius = new CornerRadius(10),
                                               Background = new SolidColorBrush(got ? Color.FromRgb(0x2E, 0x2D, 0x2A) : Color.FromRgb(0x26, 0x25, 0x23)),
                                               BorderBrush = new SolidColorBrush(got ? Color.FromRgb(0xC9, 0x96, 0x2A) : Color.FromRgb(0x3A, 0x39, 0x36)), BorderThickness = new Thickness(1) });
            }
            Content = new ScrollViewer { Content = wrap, MaxHeight = SystemParameters.WorkArea.Height - 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }

    // "Путь Синсина": current rank, XP bar, how XP is earned, and every level with a preview of how he looks there.
    class ProgressWindow : Window
    {
        static SolidColorBrush Br(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }

        // Renders the monkey as he looks at a given level, then restores the shared sprite switches.
        static Image Preview(int lvl, string pose, int k, double w)
        {
            string stage = Sprites.Stage; int rank = Sprites.Rank;
            Sprites.SetStage(Sprites.StageForLevel(lvl)); Sprites.Rank = lvl;
            var back = new List<Layer>(); var front = new List<Layer>();
            Sprites.Extras("idle", k, 1, 0, "monkey", back, front);
            back.AddRange(Sprites.Pose(pose, k, "monkey", "none")); back.AddRange(front);
            var px = Sprites.Flatten(back);
            Sprites.SetStage(stage); Sprites.Rank = rank;
            var img = new Image { Source = BitmapSource.Create(Sprites.StageW, Sprites.StageH, 96, 96, PixelFormats.Bgra32, null, px, Sprites.StageW * 4),
                                  Width = w, Height = w * Sprites.StageH / Sprites.StageW };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            return img;
        }

        public ProgressWindow(Stats s)
        {
            int lvl = Brain.Level(s.Xp), from = Brain.XpForLevel(lvl), to = Brain.XpForLevel(lvl + 1);
            Title = "Путь Синсина · уровень " + lvl; Width = 720; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Br(0x1F, 0x1E, 0x1C);
            var root = new StackPanel { Margin = new Thickness(16) };

            // header: him now + title + XP bar
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var me = Preview(lvl, "idle", 6, 120); DockPanel.SetDock(me, Dock.Left); head.Children.Add(me);
            var info = new StackPanel { Margin = new Thickness(16, 8, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = "Уровень " + lvl + " · " + Brain.RankTitle(lvl), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Br(0xEE, 0xEB, 0xE3) });
            int belt = Sprites.BeltIndex(lvl);
            info.Children.Add(new TextBlock { Text = (belt >= 0 ? Sprites.BeltNames[belt] + " пояс · " : "") + "стадия: " +
                                                     new Dictionary<string, string> { { "baby", "малыш" }, { "teen", "подросток" }, { "adult", "взрослый" }, { "guru", "гуру" } }[Sprites.StageForLevel(lvl)],
                                              FontSize = 13, Foreground = Br(0x9C, 0x98, 0x8E), Margin = new Thickness(0, 2, 0, 10) });
            double frac = Math.Max(0, Math.Min(1, (s.Xp - from) / (double)Math.Max(1, to - from)));
            var bar = new Grid { Height = 14, Width = 460, HorizontalAlignment = HorizontalAlignment.Left };
            bar.Children.Add(new Border { Background = Br(0x2E, 0x2D, 0x2A), CornerRadius = new CornerRadius(7) });
            bar.Children.Add(new Border { Background = Br(0xF2, 0xC1, 0x4C), CornerRadius = new CornerRadius(7), Width = Math.Max(14, 460 * frac), HorizontalAlignment = HorizontalAlignment.Left });
            info.Children.Add(bar);
            info.Children.Add(new TextBlock { Text = "опыт " + (s.Xp - from) + " / " + (to - from) + " · дальше: " + Brain.RankTitle(lvl + 1) + " — " + Brain.Unlocks(lvl + 1),
                                              FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Br(0xC9, 0xC6, 0xBC), Margin = new Thickness(0, 6, 0, 0), MaxWidth = 520 });
            info.Children.Add(new TextBlock { Text = "опыт: задача +10 · PR +8 · коммит +5 · пуш +5 · мерж +4 · зелёные тесты +3 · каждые 5 правок +1 · квест дня +30",
                                              FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Br(0x8A, 0x87, 0x80), Margin = new Thickness(0, 6, 0, 0), MaxWidth = 520 });
            head.Children.Add(info);
            root.Children.Add(head);

            // the path: every level as a card
            var wrap = new WrapPanel();
            for (int l = 1; l <= Brain.MaxRank; l++)
            {
                bool got = l <= lvl, cur = l == lvl;
                var img = Preview(l, cur ? "levelup" : "idle", cur ? 3 : 6, 64);
                if (!got) img.Opacity = 0.35;
                var stack = new StackPanel { Width = 112 };
                stack.Children.Add(img);
                stack.Children.Add(new TextBlock { Text = (got ? "" : "🔒 ") + l + " · " + Brain.RankTitle(l), FontWeight = FontWeights.SemiBold, FontSize = 12, TextAlignment = TextAlignment.Center,
                                                   TextWrapping = TextWrapping.Wrap, Foreground = got ? Br(0xEE, 0xEB, 0xE3) : Br(0x8A, 0x87, 0x80) });
                stack.Children.Add(new TextBlock { Text = Brain.Unlocks(l), FontSize = 10, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                                                   Foreground = Br(0x9C, 0x98, 0x8E), Margin = new Thickness(0, 2, 0, 0) });
                stack.Children.Add(new TextBlock { Text = Brain.XpForLevel(l) + " xp", FontSize = 10, TextAlignment = TextAlignment.Center, Foreground = Br(0x6E, 0x6B, 0x64) });
                wrap.Children.Add(new Border { Child = stack, Margin = new Thickness(4), Padding = new Thickness(6), CornerRadius = new CornerRadius(10),
                                               Background = cur ? Br(0x3A, 0x33, 0x22) : got ? Br(0x2E, 0x2D, 0x2A) : Br(0x26, 0x25, 0x23),
                                               BorderBrush = cur ? Br(0xF2, 0xC1, 0x4C) : got ? Br(0x5A, 0x50, 0x3A) : Br(0x3A, 0x39, 0x36), BorderThickness = new Thickness(cur ? 2 : 1) });
            }
            root.Children.Add(wrap);
            Content = new ScrollViewer { Content = root, MaxHeight = SystemParameters.WorkArea.Height - 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }

    // A tiny click-through window that plays a pixel burst where the user clicked.
    class ClickBurst : Window
    {
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLong(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        const int N = 24;   // logical pixels; each drawn 3x3
        static readonly uint[] Colors = { 0xFFF2C94C, 0xFFE86A8E, 0xFF6C9BE8, 0xFF7FD1A8, 0xFFD97757 };
        readonly WriteableBitmap bmp = new WriteableBitmap(N, N, 96, 96, PixelFormats.Bgra32, null);
        readonly double[][] parts; int t;
        static readonly Random rnd = new Random();

        public ClickBurst(int sx, int sy, double dpi)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            Width = Height = N * 3; ResizeMode = ResizeMode.NoResize; IsHitTestVisible = false;
            var img = new Image { Source = bmp, Width = N * 3, Height = N * 3 }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Content = img;
            parts = Enumerable.Range(0, 9).Select(i => { double a = i * Math.PI * 2 / 9 + rnd.NextDouble() * 0.5, v = 1.6 + rnd.NextDouble() * 1.4; return new[] { N / 2.0, N / 2.0, Math.Cos(a) * v, Math.Sin(a) * v, Colors[rnd.Next(Colors.Length)] }; }).ToArray();
            var h = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            SetLong(h, -20, new IntPtr(GetLong(h, -20).ToInt64() | 0x20 | 0x80000 | 0x08000000 | 0x80));   // TRANSPARENT | LAYERED | NOACTIVATE | TOOLWINDOW
            int px = (int)Math.Round(N * 3 * dpi);
            SetWindowPos(h, new IntPtr(-1), sx - px / 2, sy - px / 2, px, px, 0x0010);   // topmost, no activate
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += delegate { if (++t > 14) { timer.Stop(); Close(); return; } Render(); };
            Render(); Show(); timer.Start();
        }

        void Render()
        {
            var buf = new uint[N * N];
            int alpha = Math.Max(0, 255 - t * 17);
            foreach (var p in parts)
            {
                p[0] += p[2]; p[1] += p[3]; p[3] += 0.12;
                int x = (int)p[0], y = (int)p[1];
                if (x < 0 || y < 0 || x >= N || y >= N) continue;
                uint c = ((uint)p[4] & 0x00FFFFFF) | ((uint)alpha << 24);
                buf[y * N + x] = Premul(c);
                if (t < 6 && x + 1 < N) buf[y * N + x + 1] = Premul(c);
            }
            if (t < 3) { uint c = Premul(0xFFFFFFFF); buf[(N / 2) * N + N / 2] = c; }
            bmp.WritePixels(new Int32Rect(0, 0, N, N), buf, N * 4, 0);
        }
        static uint Premul(uint c) { uint a = c >> 24; return (a << 24) | (((c >> 16) & 255) * a / 255 << 16) | (((c >> 8) & 255) * a / 255 << 8) | ((c & 255) * a / 255); }
    }

    // Banana shop: spend bananas / golden bananas on accessories, backdrops, phrase packs and tricks.
    class ShopWindow : Window
    {
        readonly StackPanel list = new StackPanel();
        readonly TextBlock wallet = new TextBlock { FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x4C)), Margin = new Thickness(0, 0, 0, 10) };
        readonly Func<string, string> equipped;   // kind -> currently equipped id
        readonly Action<ShopItem> equip;

        public ShopWindow(Func<string, string> equipped, Action<ShopItem> equip)
        {
            this.equipped = equipped; this.equip = equip;
            Title = "Банановый магазин"; Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1E, 0x1C));
            var root = new StackPanel { Margin = new Thickness(16) }; root.Children.Add(wallet); root.Children.Add(list);
            Content = new ScrollViewer { Content = root, MaxHeight = SystemParameters.WorkArea.Height - 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Refresh();
        }

        void Refresh()
        {
            var s = Brain.LoadStats();
            wallet.Text = "бананов: " + s.Bananas + "   ·   золотых: " + s.GoldenBananas;
            list.Children.Clear();
            string lastKind = null;
            foreach (var it in Brain.ShopItems)
            {
                if (it.Kind != lastKind)
                {
                    lastKind = it.Kind;
                    list.Children.Add(new TextBlock { Text = it.Kind == "acc" ? "Аксессуары" : it.Kind == "backdrop" ? "Фоны" : it.Kind == "phrases" ? "Наборы фраз" : "Трюки",
                                                      FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 10, 0, 4) });
                }
                bool owned = s.Owned.Contains(it.Id);
                bool afford = it.Golden ? s.GoldenBananas >= it.Price : s.Bananas >= it.Price;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
                row.Children.Add(new TextBlock { Text = it.Title, Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEB, 0xE3)), VerticalAlignment = VerticalAlignment.Center });
                var price = new TextBlock { Text = owned ? "куплено" : it.Price + (it.Golden ? " золотых" : " бананов"), VerticalAlignment = VerticalAlignment.Center,
                                            Foreground = new SolidColorBrush(owned ? Color.FromRgb(0x7F, 0xC4, 0x8A) : Color.FromRgb(0xC9, 0xC6, 0xBC)) };
                Grid.SetColumn(price, 1); row.Children.Add(price);
                var btn = new Button { Padding = new Thickness(8, 2, 8, 2) };
                string id = it.Kind == "backdrop" ? it.Id.Substring(3) : it.Kind == "phrases" ? it.Id.Substring(3) : it.Id;
                if (!owned) { btn.Content = "Купить"; btn.IsEnabled = afford; var item = it; btn.Click += delegate { Buy(item); }; }
                else if (it.Kind == "trick") { btn.Content = "есть"; btn.IsEnabled = false; }
                else if (equipped(it.Kind) == id) { btn.Content = "Снять"; var item = it; btn.Click += delegate { equip(new ShopItem("none", item.Kind, "", 0, false)); Refresh(); }; }
                else { btn.Content = "Надеть"; var item = it; btn.Click += delegate { equip(item); Refresh(); }; }
                Grid.SetColumn(btn, 2); row.Children.Add(btn);
                list.Children.Add(row);
            }
        }

        void Buy(ShopItem it)
        {
            bool ok = false;
            Brain.WithStats(s => {
                if (s.Owned.Contains(it.Id)) return;
                if (it.Golden) { if (s.GoldenBananas < it.Price) return; s.GoldenBananas -= it.Price; }
                else { if (s.Bananas < it.Price) return; s.Bananas -= it.Price; }
                s.Owned.Add(it.Id); ok = true;
            });
            if (ok && it.Kind != "trick") equip(it);
            Refresh();
        }
    }
}