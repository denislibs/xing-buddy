// Xing Pixel — pixel-art mascot that sits on top of the Claude Desktop window (Windows).
// Usage: xing-pixel.exe                run
//        xing-pixel.exe --event        Claude Code hook: reads hook JSON from stdin (see Brain.cs)
//        xing-pixel.exe --state X      write a bare state X and exit
//        xing-pixel.exe --install      add hooks + autostart, then start      (--uninstall reverses it)
//        xing-pixel.exe --export DIR   write sprite sheets + manifest.json (used by the macOS build)
//        xing-pixel.exe --snapshot F   render a few states into a PNG (dev aid)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace XingPixel
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        delegate bool EnumProc(IntPtr h, IntPtr p);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public KEYBDINPUT k; [FieldOffset(0)] public MOUSEINPUT m; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION u; }

        // Presses all keys in order, releases them in reverse (e.g. Ctrl+N, Win+H).
        public static void Chord(params ushort[] vks)
        {
            var list = new List<INPUT>();
            foreach (var vk in vks) list.Add(Key(vk, false));
            for (int i = vks.Length - 1; i >= 0; i--) list.Add(Key(vks[i], true));
            SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }
        static INPUT Key(ushort vk, bool up)
        {
            var i = new INPUT { type = 1 };
            i.u.k = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u };
            return i;
        }

        // Largest visible, titled, unowned top-level window of claude.exe = the main window.
        public static IntPtr FindClaude()
        {
            var pids = new HashSet<uint>();
            foreach (var p in Process.GetProcessesByName("claude")) pids.Add((uint)p.Id);
            IntPtr best = IntPtr.Zero; long bestArea = 0;
            EnumWindows(delegate(IntPtr h, IntPtr _) {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains(pid) || !IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
                if (GetWindow(h, 4) != IntPtr.Zero) return true;
                RECT r; GetWindowRect(h, out r);
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea) { bestArea = area; best = h; }
                return true;
            }, IntPtr.Zero);
            return best;
        }
    }

    class Mascot : Window
    {
        const double WinW = 240, WinH = 400;      // tall: bubble above, sprite, hover bar below
        static readonly Dictionary<string, string> PoseFor = new Dictionary<string, string> {
            { "idle", "idle" }, { "thinking", "think" }, { "working", "work" }, { "success", "done" }, { "error", "error" }, { "waiting", "wait" } };
        static readonly Dictionary<string, string> Lines = new Dictionary<string, string> {
            { "idle", "Ну что, погнали?" }, { "thinking", "думаю…" }, { "working", "кручу братишку" }, { "success", "готово!" },
            { "error", "ой…" }, { "waiting", "твой ход" } };
        static readonly Dictionary<string, KeyValuePair<string, double>> IdleLines = new Dictionary<string, KeyValuePair<string, double>> {
            { "milk", new KeyValuePair<string, double>("молочко…", 4) },
            { "home", new KeyValuePair<string, double>("урааа, домой!", 0) },
            { "gone", new KeyValuePair<string, double>("ушёл домой, до завтра", 0) },
            { "lunch", new KeyValuePair<string, double>("обед, не беспокоить", 0) },
            { "night", new KeyValuePair<string, double>("иди спать, ночь на дворе", 5) } };
        static readonly string[] PokeLines = { "не тыкай", "ммм?", "отстань", "щекотно", "я занят, вообще-то" };

        readonly string dir, sharedState;
        readonly Dictionary<string, BitmapSource[]> frameCache = new Dictionary<string, BitmapSource[]>();
        readonly Random rnd = new Random();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();

        IntPtr me, claude; DateTime findAt = DateTime.MinValue;
        // config
        Dictionary<string, object> cfg = new Dictionary<string, object>();
        int cfgRight = 40, cfgBottom = 90; bool compact; string newChatKeys = "ctrl+n", skin = "monkey", accChoice = "auto", city = "";
        double sizeMul = 1; bool soundOn, healthOn = true;
        TimeSpan homeAfter = new TimeSpan(18, 30, 0), lunchFrom = new TimeSpan(13, 0, 0), lunchTo = new TimeSpan(14, 30, 0);
        bool scheduleWeekdaysOnly = true;
        // state
        string state = "idle", poseOverride, lastStamp = "", lastIdlePose = "";
        double stateAt, bubbleUntil, pokeUntil, homeStartedAt = -1, lastMoodAction, lastUsageWarn = -9999;
        string pokePose, bubbleBase = ""; bool bubbleTimer;
        long lastEventTs, turnStart; double mood = 60; int sessions = 1, helpers, xp, goldenBananas;
        Queue<Dictionary<string, object>> pendingEvents = new Queue<Dictionary<string, object>>();
        int k;
        // health & focus
        DateTime lastActivity = DateTime.MinValue, activityStart = DateTime.MinValue, lastStretch = DateTime.Now, lastWater = DateTime.Now;
        string focus = "off"; DateTime focusEnd;
        // world
        // drag, eyes, clicks, walks, music, game
        bool dragging, clickFx = false, eyesOn = true, walksOn = true, musicOn = true, mouseWasDown; DateTime lookUntil = DateTime.MinValue; Native.POINT lookAt;
        List<double> clickTimes = new List<double>();
        string exMode = "none"; double exX, exY, exFromX, exFromY, exToX, exToY, exT0, exDur, exUntil; bool facingLeft;
        int homeX, homeY, claudeTop, claudeLeft; double dpiS = 1; bool memoryOn = true;
        // care, looks, context, docker
        double hunger = 80, clean = 90, lastCtxWarn = -9999; int ctxPct = -1; long sessionTokens; string backdrop = "none", phrases = "none";
        bool dockerOn = true; Dictionary<string, string> containers; DateTime dockerAt = DateTime.MinValue; bool dockerBusy;
        Image backdropImg; List<string> owned = new List<string>();
        static readonly Dictionary<string, Dictionary<string, string>> PhrasePacks = new Dictionary<string, Dictionary<string, string>> {
            { "pirate", new Dictionary<string, string> { { "idle", "Йо-хо-хо, на абордаж?" }, { "thinking", "смотрю в подзорную трубу…" }, { "working", "драим палубу" },
                                                         { "success", "добыча наша!" }, { "error", "тысяча чертей…" }, { "waiting", "капитан, твой ход" },
                                                         { "poke", "арр!|не трожь сундук|свистать всех наверх|йо-хо-хо" } } },
            { "polite", new Dictionary<string, string> { { "idle", "Позвольте начать?" }, { "thinking", "размышляю, с вашего позволения…" }, { "working", "имею честь трудиться" },
                                                         { "success", "извольте, готово" }, { "error", "прошу прощения, неувязка" }, { "waiting", "жду ваших распоряжений" },
                                                         { "poke", "благодарю|весьма тронут|к вашим услугам|о, какая честь" } } },
            { "gop", new Dictionary<string, string> { { "idle", "чё как, погнали?" }, { "thinking", "ща, соображаю…" }, { "working", "кручу братишку чётко" },
                                                      { "success", "по красоте!" }, { "error", "чё за дела…" }, { "waiting", "твой ход, братан" },
                                                      { "poke", "э, полегче|семки есть?|чё надо?|ровно всё" } } } };
        string song = ""; DateTime musicMinuteAt = DateTime.Now; bool gameOffered; GameWindow game; string continueText;
        Weather weather; DateTime weatherAt = DateTime.MinValue; int usageFh = -1, usageSd = -1; DateTime usageAt = DateTime.MinValue;

        List<Character> characters = new List<Character>(); Character plugin;
        Image extrasBack, extrasFront;
        Image sprite; Border bubble; Polygon tail; TextBlock bubbleText; Border bar; TextBlock chevron; Border focusBtn;
        ScaleTransform barScale; bool barShown; DateTime hoverLostAt = DateTime.MinValue;
        System.Windows.Forms.NotifyIcon tray;
        AskWindow askWin;

        public Mascot(string dir)
        {
            this.dir = dir;
            sharedState = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-mascot", "state");
            LoadConfig();
            try { var s = Brain.LoadStats(); mood = s.Mood; xp = s.Xp; goldenBananas = s.GoldenBananas; } catch { }

            Title = "XingPixel"; Width = WinW; Height = WinH;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
            Content = BuildScene();
            ApplySize();
            me = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            long ex = Native.GetWindowLongPtr(me, -20).ToInt64();
            Native.SetWindowLongPtr(me, -20, new IntPtr(ex | 0x08000000 | 0x80)); // NOACTIVATE | TOOLWINDOW
            Say(Lines["idle"], 4);
        }

        public void Start()
        {
            BuildTray();
            var anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / Sprites.Fps) };
            anim.Tick += delegate { try { Tick(); } catch (Exception e) { Program.Log("anim: " + e); } };
            anim.Start();
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            poll.Tick += delegate { try { ReadEvents(); UpdateState(); Place(); } catch (Exception e) { Program.Log("poll: " + e.Message); } };
            poll.Start();
            var hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            hover.Tick += delegate { try { CheckHover(); } catch { } };
            hover.Start();
            var music = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            music.Tick += delegate { try { CheckMusic(); } catch { } };
            music.Start();
            var slow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            slow.Tick += delegate { try { SlowChecks(); } catch (Exception e) { Program.Log("slow: " + e.Message); } };
            slow.Start();
            Closed += delegate { if (tray != null) tray.Dispose(); };
            SlowChecks();
        }

        double Now { get { return clock.Elapsed.TotalSeconds; } }
        int Level { get { return Brain.Level(xp); } }

        // ---------- scene ----------
        UIElement BuildScene()
        {
            var root = new StackPanel { Width = WinW, VerticalAlignment = VerticalAlignment.Bottom };

            // Pixel speech bubble with a little tail; capped in height so it never runs off the window.
            bubbleText = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, MaxWidth = 206, TextWrapping = TextWrapping.Wrap,
                                         TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 104,
                                         TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x29, 0x25)) };
            bubble = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF4, 0xEE)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x24, 0x20)),
                                  BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 5),
                                  Child = bubbleText, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
            tail = new Polygon { Points = new PointCollection { new Point(0, 0), new Point(12, 0), new Point(6, 7) }, Fill = bubble.Background,
                                 HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0), IsHitTestVisible = false };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
            stack.Children.Add(bubble); stack.Children.Add(tail);
            root.Children.Add(stack);

            sprite = new Image { Cursor = Cursors.Hand, AllowDrop = true };
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
            sprite.MouseLeftButtonDown += OnSpriteDown;
            sprite.ContextMenu = BuildMenu();
            sprite.Drop += OnDrop;
            sprite.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            extrasBack = new Image { IsHitTestVisible = false }; extrasFront = new Image { IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(extrasBack, BitmapScalingMode.NearestNeighbor); RenderOptions.SetBitmapScalingMode(extrasFront, BitmapScalingMode.NearestNeighbor);
            backdropImg = new Image { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            RenderOptions.SetBitmapScalingMode(backdropImg, BitmapScalingMode.NearestNeighbor);
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            host.Children.Add(backdropImg);
            host.Children.Add(extrasBack); host.Children.Add(sprite); host.Children.Add(extrasFront);
            root.Children.Add(host);

            // Bottom bar: new chat, voice typing, focus timer, collapse. Hidden until hovered; grows from its centre.
            var fg = new SolidColorBrush(Color.FromRgb(0xC9, 0xC6, 0xBC));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(BarButton("", "Новый чат", fg, delegate { NewChat(); }));
            row.Children.Add(Sep());
            row.Children.Add(BarButton("", "Голосовой ввод (Win+H)", fg, delegate { Voice(); }));
            row.Children.Add(Sep());
            focusBtn = (Border)BarButton("", "Помодоро: 25 минут фокуса", fg, delegate { ToggleFocus(); });
            row.Children.Add(focusBtn);
            row.Children.Add(Sep());
            row.Children.Add(BarButton("", "Банановый раннер", fg, delegate { OpenGame(); }));
            row.Children.Add(Sep());
            var chev = BarButton("", "Свернуть", fg, delegate { ToggleCompact(); });
            chevron = (TextBlock)((Border)chev).Child;
            row.Children.Add(chev);
            bar = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x2C, 0x2B, 0x29)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
                               BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(4, 3, 4, 3),
                               HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 4), Child = row,
                               Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5),
                               RenderTransform = barScale = new ScaleTransform(0.2, 0.2),
                               Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35, Direction = 270 } };
            root.Children.Add(bar);
            return root;
        }

        UIElement Sep() { return new Rectangle { Width = 1, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center }; }

        UIElement BarButton(string glyph, string tip, Brush fg, Action click)
        {
            var t = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15, Foreground = fg,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
            var b = new Border { Width = 40, Height = 28, CornerRadius = new CornerRadius(14), Background = Brushes.Transparent, Child = t, ToolTip = tip, Cursor = Cursors.Hand };
            var hover = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            b.MouseEnter += delegate { b.Background = hover; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate { click(); };
            return b;
        }

        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            Action<string, Action> add = (h, a) => { var mi = new MenuItem { Header = h }; mi.Click += delegate { a(); }; menu.Items.Add(mi); };
            add("Спросить Синсина…", OpenAsk);
            add("Статистика за сегодня", ShowStats);
            add("Путь Синсина (уровни)", OpenProgress);
            add("Квест дня", ShowQuest);
            add("Ачивки", ShowAchievements);
            add("Отчёт за неделю", () => WeeklyReport(true));
            add("Коллекция пасхалок", () => new CollectionWindow(Brain.LoadStats(), skin).Show());
            add("Банановый магазин", OpenShop);
            add("Покормить", Feed);
            add("Искупать", Bath);
            add("Банановый раннер", OpenGame);
            add("Что я помню", ShowMemory);
            add("Продолжить вчерашнее", ContinueYesterday);
            menu.Items.Add(new Separator());
            add("Настройки…", OpenSettings);
            add("Вернуть на место", () => { cfgRight = 40; cfgBottom = 90; SaveConfig(); });
            add("Закрыть", Close);
            return menu;
        }

        void BuildTray()
        {
            try
            {
                var px = Sprites.Render("idle", 0, skin, "none", 1, 0);
                var bmp = new System.Drawing.Bitmap(Sprites.StageW, Sprites.StageH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, Sprites.StageW, Sprites.StageH), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
                Marshal.Copy(px, 0, data.Scan0, px.Length); bmp.UnlockBits(data);
                tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.Icon.FromHandle(bmp.GetHicon()), Text = "Синсин", Visible = true };
                var cm = new System.Windows.Forms.ContextMenuStrip();
                cm.Items.Add("Спросить Синсина…", null, delegate { OpenAsk(); });
                cm.Items.Add("Статистика за сегодня", null, delegate { ShowStats(); });
                cm.Items.Add("Путь Синсина (уровни)", null, delegate { OpenProgress(); });
                cm.Items.Add("Отчёт за неделю", null, delegate { WeeklyReport(true); });
                cm.Items.Add("Настройки…", null, delegate { OpenSettings(); });
                cm.Items.Add("Закрыть", null, delegate { Close(); });
                tray.ContextMenuStrip = cm;
                tray.DoubleClick += delegate { OpenSettings(); };
            }
            catch (Exception e) { Program.Log("tray: " + e.Message); }
        }

        // ---------- bubble & sound ----------
        void Say(string text, double seconds)
        {
            bubbleBase = text; bubbleTimer = false;
            bubbleText.Text = text;
            bubbleUntil = seconds <= 0 ? double.MaxValue : Now + seconds;
        }

        string Phrase(string key)
        {
            Dictionary<string, string> pack; string v;
            if (phrases != "none" && PhrasePacks.TryGetValue(phrases, out pack) && pack.TryGetValue(key, out v)) return v;
            return null;
        }

        void Beep(string name) { if (soundOn && focus != "focus") Sound.Play(name, plugin != null ? plugin.Sound : skin); }

        // ---------- appearance ----------
        bool InSeason() { var d = DateTime.Today; return (d.Month == 12 && d.Day >= 15) || (d.Month == 1 && d.Day <= 10); }

        string CurrentAcc()
        {
            if (focus == "focus" || (musicOn && song.Length > 0 && state == "idle")) return "headphones";
            if (accChoice != "auto")
            {
                var s = new Stats { Xp = xp, GoldenBananas = goldenBananas, Owned = owned };
                return SettingsWindow.Unlocked(accChoice, s) ? accChoice : "none";
            }
            if (weather != null && weather.Rain) return "umbrella";
            if (InSeason()) return "santa";
            if (weather != null && (weather.Snow || weather.Cold)) return "scarf";
            return "none";
        }

        BitmapSource Frame(string pose, int i)
        {
            if (plugin != null)
            {
                if (pose == "gone") return null;
                var fr = plugin.Frames(plugin.Resolve(pose));
                int idx = pose == "home" && homeStartedAt >= 0 ? Math.Min(fr.Length - 1, (int)((Now - homeStartedAt) * plugin.Fps)) : (int)(Now * plugin.Fps) % fr.Length;
                return fr[idx];
            }
            string acc = CurrentAcc();
            int lx = 0, ly = 0;
            if (pose == "idle" && eyesOn) Look(out lx, out ly);
            Sprites.SetStage(Sprites.StageForLevel(Level)); Sprites.Rank = Level; Sprites.Dirt = clean < 30;
            string key = pose + "|" + skin + "|" + acc + "|" + lx + ly + "|" + Sprites.Stage + "|r" + Sprites.Rank + (Sprites.Dirt ? "d" : "");
            BitmapSource[] arr;
            if (!frameCache.TryGetValue(key, out arr))
            {
                arr = new BitmapSource[Sprites.Frames];
                for (int j = 0; j < Sprites.Frames; j++)
                    arr[j] = Freeze(BitmapSource.Create(Sprites.StageW, Sprites.StageH, 96, 96, PixelFormats.Bgra32, null, Sprites.RenderBody(pose, j, skin, acc, lx, ly), Sprites.StageW * 4));
                frameCache[key] = arr;
                if (frameCache.Count > 40) frameCache.Remove(frameCache.Keys.First(x => x != key));
            }
            return arr[i];
        }
        static BitmapSource Freeze(BitmapSource b) { b.Freeze(); return b; }

        // Direction from his eyes to the mouse (or to the last click for a moment), -1..1 on each axis.
        void Look(out int lx, out int ly)
        {
            lx = ly = 0;
            Native.POINT p; if (DateTime.Now < lookUntil) p = lookAt; else Native.GetCursorPos(out p);
            Native.RECT w; Native.GetWindowRect(me, out w);
            double s = Native.GetDpiForWindow(me) / 96.0;
            var root = (FrameworkElement)Content;
            if (sprite.ActualWidth <= 0) return;
            var c = sprite.TransformToAncestor(root).Transform(new Point(sprite.ActualWidth / 2, sprite.ActualHeight * 0.42));
            double ex = w.Left + c.X * s, ey = w.Top + (WinH - root.ActualHeight + c.Y) * s;
            double dx = p.X - ex, dy = p.Y - ey;
            if (Math.Abs(dx) > 40 * s) lx = dx > 0 ? 1 : -1;
            if (Math.Abs(dy) > 50 * s) ly = dy > 0 ? 1 : -1;
            if (facingLeft) lx = -lx;
        }

        // He "grows" a little with each level (capped), times the user's size setting.
        void ApplySize()
        {
            double scale = (compact ? 2.5 : 4.0 + Math.Min(6, Level - 1) * 0.1) * sizeMul;
            string stage = Sprites.StageForLevel(Level); Sprites.Rank = Level;
            scale *= stage == "baby" ? 0.85 : stage == "guru" ? 1.05 : 1;
            double bw = Sprites.StageW * scale, bh = Sprites.StageH * scale;
            foreach (var img in new[] { extrasBack, extrasFront, backdropImg }) { img.Width = bw; img.Height = bh; }
            backdropImg.Visibility = backdrop != "none" ? Visibility.Visible : Visibility.Collapsed;
            if (backdrop != "none") backdropImg.Source = Freeze(BitmapSource.Create(Sprites.StageW, Sprites.StageH, 96, 96, PixelFormats.Bgra32, null, Sprites.Backdrop(backdrop), Sprites.StageW * 4));
            if (plugin != null)
            {
                double fit = Math.Min(bw / plugin.FrameW, bh / plugin.FrameH);
                sprite.Width = plugin.FrameW * fit; sprite.Height = plugin.FrameH * fit;
                RenderOptions.SetBitmapScalingMode(sprite, plugin.PixelArt ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
                sprite.VerticalAlignment = VerticalAlignment.Bottom;
            }
            else { sprite.Width = bw; sprite.Height = bh; RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor); }
            if (chevron != null) chevron.RenderTransform = new RotateTransform(compact ? 180 : 0);
        }

        // ---------- poses ----------
        bool ScheduleDay() { var d = DateTime.Now.DayOfWeek; return !scheduleWeekdaysOnly || (d != DayOfWeek.Saturday && d != DayOfWeek.Sunday); }

        string CurrentPose()
        {
            if (Now < pokeUntil) return pokePose;
            if (state != "idle") return poseOverride ?? PoseFor[state];
            if (focus == "focus") return "type";
            if (exMode == "walkOut" || exMode == "walkBack") return "walk";
            if (exMode == "hopUp" || exMode == "hopDown") return "jump";
            if (exMode == "sit" || exMode == "perch") return "sit";

            var t = DateTime.Now.TimeOfDay; double idleFor = Now - stateAt; int h = DateTime.Now.Hour;
            if (ScheduleDay() && t >= homeAfter && idleFor > 10)
            {
                if (homeStartedAt < 0) { homeStartedAt = Now; k = 0; }
                return Now - homeStartedAt < (double)Sprites.HomeFrames / Sprites.Fps ? "home" : "gone";
            }
            if (ScheduleDay() && t >= lunchFrom && t < lunchTo && idleFor > 10) return "lunch";
            if (musicOn && song.Length > 0) return "music";
            if ((h >= 23 || h < 6) && idleFor > 20) return "sleep";
            if (idleFor > 180) return "sleep";
            if (idleFor > 60) return "milk";
            return "idle";
        }

        void Tick()
        {
            string pose = CurrentPose();
            if (state == "idle" && pose != lastIdlePose)
            {
                string key = pose == "sleep" && (DateTime.Now.Hour >= 23 || DateTime.Now.Hour < 6) ? "night" : pose;
                KeyValuePair<string, double> line;
                if (IdleLines.TryGetValue(key, out line)) Say(line.Key, line.Value);
                if (pose == "lunch") { string today = Brain.Today(); Award("noodle", s => s.LunchDays.Count >= 5, s => { if (!s.LunchDays.Contains(today)) s.LunchDays.Add(today); }); }
                else if (lastIdlePose == "gone" || lastIdlePose == "lunch" || lastIdlePose == "home") bubbleUntil = 0;
                lastIdlePose = pose;
            }
            if (state != "idle") lastIdlePose = "";
            if (pose == "home") k = Math.Min(k + 1, Sprites.HomeFrames - 1); else k = (k + 1) % Sprites.Frames;
            Excursion();
            sprite.RenderTransformOrigin = new Point(0.5, 0.5);
            sprite.RenderTransform = new ScaleTransform(facingLeft && exMode != "none" ? -1 : 1, 1);
            sprite.Source = Frame(pose, k);
            Sprites.Flies = clean < 15 && plugin == null;
            bool extras = pose == "work" || helpers > 0 || Sprites.Flies;
            extrasBack.Visibility = extrasFront.Visibility = extras ? Visibility.Visible : Visibility.Collapsed;
            if (extras)
            {
                string mini = plugin != null ? "monkey" : skin;
                extrasBack.Source = Freeze(BitmapSource.Create(Sprites.StageW, Sprites.StageH, 96, 96, PixelFormats.Bgra32, null, Sprites.RenderExtras(pose, k, sessions, helpers, mini, false), Sprites.StageW * 4));
                extrasFront.Source = Freeze(BitmapSource.Create(Sprites.StageW, Sprites.StageH, 96, 96, PixelFormats.Bgra32, null, Sprites.RenderExtras(pose, k, sessions, helpers, mini, true), Sprites.StageW * 4));
            }

            if (bubbleTimer && turnStart > 0 && (state == "thinking" || state == "working"))
            {
                long ms = Brain.Ms() - turnStart;
                if (ms > 20000) bubbleText.Text = bubbleBase + " " + Brain.Clock(ms);
            }
            bool showBubble = !compact && Now < bubbleUntil && focus != "focus" && exMode == "none";
            if (state != "idle" && (state == "thinking" || state == "working") && !gameOffered && Now - stateAt > 45 && game == null)
            { gameOffered = true; Say("долго думает… сыграем в бананы? геймпад в баре", 6); }
            bubble.Visibility = tail.Visibility = showBubble ? Visibility.Visible : Visibility.Hidden;
            MoodLife();
        }

        void MoodLife()
        {
            if (state != "idle" || Now < pokeUntil || Now - lastMoodAction < 45 || Now - stateAt < 15 || focus == "focus") return;
            if (CurrentPose() != "idle") return;
            lastMoodAction = Now;
            if (mood < 30) Say(rnd.Next(2) == 0 ? "грустно… дай банан" : "кликни меня, я голодный", 4);
            else if (mood > 80) { pokePose = rnd.Next(2) == 0 ? "happy" : "love"; pokeUntil = Now + 3; k = 0; }
        }

        // ---------- slow checks: health, focus timer, weekly report, usage, weather ----------
        void SlowChecks()
        {
            var now = DateTime.Now;
            // Pomodoro
            if (focus != "off" && now >= focusEnd)
            {
                if (focus == "focus")
                {
                    focus = "break"; focusEnd = now.AddMinutes(5);
                    pokePose = "stretch"; pokeUntil = Now + 8; k = 0; Say("перерыв 5 минут! разомнись", 8); Notify("Помодоро: перерыв 5 минут"); Beep("level");
                }
                else { focus = "off"; Say("погнали дальше?", 4); }
                UpdateFocusTip();
            }
            if (focus == "focus" && tray != null) tray.Text = "Синсин · фокус: " + (int)(focusEnd - now).TotalMinutes + " мин";

            // Health: continuous activity (no gap > 10 min) → stretch at 90 min, water every 60 min.
            bool active = (now - lastActivity).TotalMinutes < 10;
            if (healthOn && active && focus != "focus")
            {
                if ((now - activityStart).TotalMinutes >= 90 && (now - lastStretch).TotalMinutes >= 90)
                {
                    lastStretch = now; pokePose = "stretch"; pokeUntil = Now + 7; k = 0;
                    Say("полтора часа без перерыва. встань, разомнись!", 8); Notify("Синсин: встань, разомнись"); Beep("poke");
                }
                else if ((now - activityStart).TotalMinutes >= 60 && (now - lastWater).TotalMinutes >= 60)
                {
                    lastWater = now; pokePose = "water"; pokeUntil = Now + 6; k = 0; Say("попей водички", 6);
                }
            }

            try { var cs = Brain.LoadStats(); owned = cs.Owned; bool wasDirty = clean < 30; hunger = cs.Hunger; clean = cs.Clean; if ((clean < 30) != wasDirty) frameCache.Clear(); if (ctxPct < 0) { ctxPct = cs.CtxPct; sessionTokens = cs.SessionTokens; } } catch { }
            CheckDocker();
            if (state == "idle" && exMode == "none" && focus != "focus" && Now > pokeUntil && Now - lastMoodAction > 60)
            {
                if (ctxPct >= 85 && Now - lastCtxWarn > 600) { lastCtxWarn = Now; lastMoodAction = Now; pokePose = "full"; pokeUntil = Now + 5; Say("контекст " + ctxPct + "%: я полный. /compact?", 6); }
                else if (hunger < 25) { lastMoodAction = Now; Say("есть хочу… кликни, покорми", 4); }
                else if (clean < 30) { lastMoodAction = Now; Say("я чумазый… искупай меня (правый клик)", 4); }
            }

            // A stroll along Claude's window now and then while idle.
            if (walksOn && exMode == "none" && state == "idle" && Now - stateAt > 40 && Now > pokeUntil && focus == "off" && claude != IntPtr.Zero
                && CurrentPose() == "idle" && rnd.NextDouble() < 0.25) StartExcursion();

            // Morning: remind what we were doing last time.
            if (memoryOn && now.Hour >= 6 && now.Hour < 13 && state == "idle")
            {
                object g; if (!cfg.TryGetValue("lastGreet", out g) || Convert.ToString(g) != Brain.Today()) { cfg["lastGreet"] = Brain.Today(); SaveConfig(); Greet(); }
            }

            // Weekly report on Friday evenings, once a week.
            if (now.DayOfWeek == DayOfWeek.Friday && now.Hour >= 17 && state == "idle")
            {
                string week = now.ToString("yyyy-MM-dd");
                object v; if (!cfg.TryGetValue("lastReport", out v) || Convert.ToString(v) != week) { cfg["lastReport"] = week; SaveConfig(); WeeklyReport(false); }
            }

            // Claude plan usage (samples written by Claude Desktop); only trust fresh samples.
            if ((now - usageAt).TotalMinutes >= 2) { usageAt = now; ReadUsage(); }
            if (state == "idle" && Now - lastUsageWarn > 20 * 60 && focus != "focus")
            {
                if (usageFh >= 80) { lastUsageWarn = Now; pokePose = "think"; pokeUntil = Now + 5; Say("5-часовой лимит: " + usageFh + "%. береги токены", 6); }
                else if (usageSd >= 90) { lastUsageWarn = Now; pokePose = "think"; pokeUntil = Now + 5; Say("недельный лимит: " + usageSd + "%. экономим", 6); }
            }

            // Weather every 30 min when a city is set.
            if (city.Length > 0 && (now - weatherAt).TotalMinutes >= 30)
            {
                weatherAt = now; string c = city;
                new Thread(() => { try { var w = Weather.Fetch(c); Dispatcher.BeginInvoke(new Action(() => weather = w)); } catch (Exception e) { Program.Log("weather: " + e.Message); } }) { IsBackground = true }.Start();
            }
        }

        void ReadUsage()
        {
            try
            {
                string f = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "plan-usage-history.json");
                if (!File.Exists(f)) return;
                var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                var samples = d["samples"] as System.Collections.ArrayList; if (samples == null || samples.Count == 0) return;
                var last = (Dictionary<string, object>)samples[samples.Count - 1];
                long t = Convert.ToInt64(last["t"]); var u = (Dictionary<string, object>)last["u"];
                double ageH = (Brain.Ms() - t) / 3600000.0;
                usageFh = ageH < 5 && u.ContainsKey("fh") ? Convert.ToInt32(u["fh"]) : -1;
                usageSd = ageH < 48 && u.ContainsKey("sd") ? Convert.ToInt32(u["sd"]) : -1;
            }
            catch { }
        }

        // ---------- state & events ----------
        void SetState(string s)
        {
            if (!PoseFor.ContainsKey(s)) return;
            if (s == state && s != "success" && s != "error") return;
            state = s; stateAt = Now; k = 0; poseOverride = null;
            if (s != "idle") { homeStartedAt = -1; ComeHome(); }
            if (s == "thinking") gameOffered = false;
            string ln; if (plugin == null || !plugin.Lines.TryGetValue(s, out ln)) ln = Phrase(s) ?? Lines[s];
            Say(ln, s == "working" || s == "thinking" || s == "waiting" ? 0 : (s == "idle" ? 0.01 : 4));
        }

        void ApplyEvent(Dictionary<string, object> e)
        {
            object v;
            var now = DateTime.Now;
            if ((now - lastActivity).TotalMinutes > 10) activityStart = now;
            lastActivity = now;
            if (e.TryGetValue("mood", out v)) mood = Convert.ToDouble(v);
            if (e.TryGetValue("turnStart", out v)) turnStart = Convert.ToInt64(v);
            if (e.TryGetValue("sessions", out v)) sessions = Math.Max(1, Convert.ToInt32(v));
            if (e.TryGetValue("helpers", out v)) helpers = Convert.ToInt32(v);
            if (e.TryGetValue("ctxPct", out v)) ctxPct = Convert.ToInt32(v);
            if (e.TryGetValue("sessionTokens", out v)) sessionTokens = Convert.ToInt64(v);
            if (e.TryGetValue("xp", out v)) { int lvl = Level; xp = Convert.ToInt32(v); if (Level != lvl) ApplySize(); }
            double seconds = e.TryGetValue("seconds", out v) ? Convert.ToDouble(v) : 0;
            string pose = e.TryGetValue("pose", out v) ? Convert.ToString(v) : null;
            string line = e.TryGetValue("line", out v) ? Convert.ToString(v) : null;

            if (e.TryGetValue("state", out v)) SetState(Convert.ToString(v));
            if (pose != null && Array.IndexOf(Sprites.Poses, pose) >= 0)
            {
                if (seconds > 0) { pokePose = pose; pokeUntil = Now + seconds; k = 0; }
                else poseOverride = pose;
            }
            if (line != null)
            {
                bool sticky = seconds <= 0 && (state == "working" || state == "thinking" || state == "waiting");
                Say(line, sticky ? 0 : (seconds > 0 ? seconds : 4));
                bubbleTimer = sticky;
            }
            if (e.TryGetValue("sound", out v)) Beep(Convert.ToString(v));
            if (e.ContainsKey("achievement") || (line != null && line.StartsWith("квест"))) try { goldenBananas = Brain.LoadStats().GoldenBananas; } catch { }
            if (e.TryGetValue("notify", out v)) Notify(Convert.ToString(v));
        }

        void ReadEvents()
        {
            string f = System.IO.Path.Combine(dir, "event.json");
            if (File.Exists(f))
            {
                try
                {
                    var e = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    long ts = Convert.ToInt64(e["ts"]);
                    if (ts > lastEventTs)
                    {
                        bool first = lastEventTs == 0; lastEventTs = ts;
                        if (!first && Brain.Ms() - ts < 15 * 60 * 1000) pendingEvents.Enqueue(e);
                    }
                }
                catch { }
            }
            // Fallback to the GIF mascot's shared state file only when our own hooks are silent.
            if (Brain.Ms() - lastEventTs > 30 * 60 * 1000 && File.Exists(sharedState))
            {
                try
                {
                    string raw = File.ReadAllText(sharedState).Trim();
                    if (raw != lastStamp)
                    {
                        bool first = lastStamp == ""; lastStamp = raw;
                        var parts = raw.Split('|'); long ms;
                        if (!first && parts.Length == 2 && long.TryParse(parts[1], out ms) && Brain.Ms() - ms < 15 * 60 * 1000)
                        {
                            var e = new Dictionary<string, object>(); e["state"] = parts[0]; pendingEvents.Enqueue(e);
                        }
                    }
                }
                catch { }
            }
        }

        void UpdateState()
        {
            double held = Now - stateAt;
            while (pendingEvents.Count > 0)
            {
                var e = pendingEvents.Peek();
                if (e.ContainsKey("state") && (state == "success" || state == "error") && held < 2.2) break;
                ApplyEvent(pendingEvents.Dequeue());
                held = Now - stateAt;
            }
            if (state == "success" && held > 4 && pendingEvents.Count == 0 && Now > bubbleUntil - 0.01) SetState("idle");
            else if (state == "success" && held > 9) SetState("idle");
            else if (state == "error" && held > 6) SetState("idle");
            else if ((state == "thinking" || state == "working") && held > 600) SetState("idle");
        }

        void Notify(string text)
        {
            if (Now >= pokeUntil) { pokePose = "wave"; pokeUntil = Now + 3; k = 0; }   // don't cut off a level-up / sticker / alarm pose
            if (claude != IntPtr.Zero && Native.GetForegroundWindow() == claude) return;
            if (tray != null) tray.ShowBalloonTip(5000, "Синсин", text, System.Windows.Forms.ToolTipIcon.Info);
        }

        // ---------- interactions ----------
        void OnSpriteDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { OpenAsk(); return; }
            if (exMode != "none") { ComeHome(); return; }
            Native.RECT before, after; Native.GetWindowRect(me, out before);
            dragging = true;   // keep Place() from snapping him back while the mouse is moving him
            try { DragMove(); } catch { }
            dragging = false;
            Native.GetWindowRect(me, out after);
            if (Math.Abs(after.Left - before.Left) + Math.Abs(after.Top - before.Top) < 4) Poke();
            else if (claude != IntPtr.Zero)
            {
                Native.RECT r; Native.GetWindowRect(claude, out r);
                cfgRight = Math.Max(0, r.Right - after.Right); cfgBottom = Math.Max(0, r.Bottom - after.Bottom); SaveConfig();
            }
        }

        // Click: feeds a banana when he's low; otherwise a trick (more tricks unlock with levels).
        void Poke()
        {
            k = 0; Beep("poke");
            clickTimes.Add(Now); clickTimes.RemoveAll(x => Now - x > 3);
            if (clickTimes.Count >= 7) { clickTimes.Clear(); pokePose = "secret"; pokeUntil = Now + 5; Say("секретный танец!", 4); Award("konami", s => true, null); return; }
            Award("clicker", s => s.Pokes >= 100, s => s.Pokes++);
            if (lastIdlePose == "gone") { Say("я ушёл, завтра приходи", 3); return; }
            if (mood < 50 || hunger < 60) { Feed(); return; }
            string[] pokeLines = Phrase("poke") != null ? Phrase("poke").Split('|') : PokeLines;
            var tricks = new List<string> { "wave", "happy" };
            if (Level >= 2) tricks.Add("love");
            if (Level >= 3) tricks.Add("git_rebase");
            if (Level >= 4) tricks.Add("run");
            if (Level >= 5) tricks.Add("done");
            if (Level >= 6) tricks.Add("music");
            if (Level >= 8) tricks.Add("jump");
            if (Level >= 10) tricks.Add("secret");
            if (Level >= 11 && rnd.NextDouble() < 0.35) pokeLines = Wisdom;
            try { if (Brain.LoadStats().Owned.Contains("trick_secret")) tricks.Add("secret"); } catch { }
            pokePose = tricks[rnd.Next(tricks.Count)]; pokeUntil = Now + 2.4;
            if (state == "idle" && focus != "focus") Say(pokeLines[rnd.Next(pokeLines.Length)], pokeLines == Wisdom ? 5 : 2);
        }

        // Dropping a file asks what to do, then types a ready prompt into Claude's input (you press Enter).
        void OnDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            pokePose = "read"; pokeUntil = Now + 3; k = 0;
            string list = string.Join(", ", files.Take(5));
            var menu = new ContextMenu();
            foreach (var opt in new[] { new[] { "Объясни", "Объясни, что делает этот файл: " }, new[] { "Найди баги", "Найди баги и проблемы в файле: " },
                                        new[] { "Сделай ревью", "Сделай код-ревью файла: " }, new[] { "Напиши тесты", "Напиши тесты для файла: " } })
            {
                var mi = new MenuItem { Header = opt[0] }; string prefix = opt[1];
                mi.Click += delegate { TypeIntoClaude(prefix + list); };
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = sprite; menu.IsOpen = true;
        }

        void TypeIntoClaude(string text)
        {
            if (!FocusClaude()) { Say("не вижу окно Claude", 3); return; }
            string old = null;
            try { if (Clipboard.ContainsText()) old = Clipboard.GetText(); Clipboard.SetText(text); } catch { }
            Native.Chord(0x11, 0x56);   // Ctrl+V — you review and press Enter yourself
            Say("вставил в Claude, жми Enter", 4);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            t.Tick += delegate { t.Stop(); try { if (old != null) Clipboard.SetText(old); } catch { } };
            t.Start();
        }

        void ShowStats()
        {
            var s = Brain.LoadStats(); mood = s.Mood; xp = s.Xp;
            string usage = usageFh >= 0 || usageSd >= 0 ? "\nлимиты: 5ч " + (usageFh >= 0 ? usageFh + "%" : "—") + " · неделя " + (usageSd >= 0 ? usageSd + "%" : "—") : "";
            Say("ур. " + Level + " · " + Brain.RankTitle(Level) + " · опыт " + (xp - Brain.XpForLevel(Level)) + "/" + (Brain.XpForLevel(Level + 1) - Brain.XpForLevel(Level)) +
                "\nзадач: " + s.Tasks + " · правок: " + s.Edits + "\nкоммитов: " + s.Commits + " · пушей: " + s.Pushes + " · ошибок: " + s.Errors +
                "\nнастроение: " + (int)Math.Round(s.Mood) + " · бананов: " + s.Bananas + " (золотых " + s.GoldenBananas + ")" +
                "\nсытость: " + (int)s.Hunger + " · чистота: " + (int)s.Clean +
                (s.CtxPct > 0 ? "\nконтекст: " + s.CtxPct + "% · сессия: " + Brain.Tokens(s.SessionTokens) + " токенов" : "") + usage, 12);
        }

        static readonly string[] Wisdom = { "баг, который не воспроизводится, всё ещё баг", "лучший код — ненаписанный", "сначала прочитай ошибку целиком",
            "коммить маленькими кусками", "тесты — это письма себе в будущее", "если не понятно — назови переменную лучше", "банан в обед, рефакторинг после" };

        void OpenProgress()
        {
            new ProgressWindow(Brain.LoadStats()).Show();
        }

        void ShowQuest()
        {
            var s = Brain.LoadStats(); var q = Brain.TodayQuest(s);
            Say("квест дня: " + q.Title + "\n" + (s.QuestDone ? "выполнен! золотой банан твой" : s.QuestFailed ? "провален, завтра новый" : "прогресс: " + Math.Min(s.QuestProgress, q.Target) + "/" + q.Target), 7);
        }

        void ShowAchievements()
        {
            var s = Brain.LoadStats();
            var shown = s.Achievements.Where(a => !a.Contains("-") || a.EndsWith(s.Day)).Select(a => Report.AchievementTitle(a) + (a.Contains("-") ? " (сегодня)" : "")).Distinct().ToList();
            Say(shown.Count == 0 ? "ачивок пока нет, работаем" : "ачивки:\n" + string.Join("\n", shown.Take(6)) + (shown.Count > 6 ? "\n…и ещё " + (shown.Count - 6) : ""), 10);
        }

        void WeeklyReport(bool open)
        {
            Say("собираю отчёт за неделю…", 0);
            new Thread(() => {
                var s = Brain.LoadStats();
                int tasks = Enumerable.Range(0, 7).Sum(i => { DayStats h; return s.History.TryGetValue(DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd"), out h) ? h.Tasks : 0; });
                string err, phrase = Brain.AskClaude("Скажи одну фразу недели до 90 символов для программиста, который за неделю закрыл " + tasks + " задач. Подбодри или пошути.", 45000, out err)
                                     ?? "неделя прошла, бананы съедены, код написан";
                string path = Report.Write(dir, s, phrase);
                Dispatcher.BeginInvoke(new Action(() => {
                    if (open) { try { Process.Start(path); } catch { } Say("отчёт открыт в браузере", 4); }
                    else { Say("отчёт за неделю готов: правый клик → Отчёт за неделю", 8); Notify("Отчёт за неделю готов"); }
                }));
            }) { IsBackground = true }.Start();
        }

        void OpenAsk()
        {
            if (askWin != null) { askWin.Activate(); return; }
            askWin = new AskWindow { Owner = this };
            Native.RECT w; Native.GetWindowRect(me, out w);
            double s = Native.GetDpiForWindow(me) / 96.0;
            askWin.Left = Math.Max(0, w.Left / s - 110); askWin.Top = w.Bottom / s - 120;
            askWin.Closed += delegate { askWin = null; };
            askWin.Asked += q => {
                if (q.IndexOf("банан", StringComparison.OrdinalIgnoreCase) >= 0) Award("banana_word", st => true, null);
                pokePose = "think"; pokeUntil = Now + 60; Say("думаю…", 0);
                var t = new Thread(() => {
                    string err; string a = Brain.AskClaude("Вопрос: " + q + "\nОтветь коротко, 1–3 предложения.", 60000, out err);
                    Dispatcher.BeginInvoke(new Action(() => {
                        pokeUntil = 0;
                        if (a == null) { Program.Log("ask: " + err); a = Brain.IsLoginError(err) ? Brain.LoginHint : "что-то не думается… (" + Brain.Short(err ?? "", 60) + ")"; }
                        else { pokePose = "happy"; pokeUntil = Now + 2; }
                        if (askWin != null) askWin.ShowAnswer(a);
                        Say(Brain.Short(a, 120), 10);
                    }));
                }) { IsBackground = true };
                t.Start();
            };
            askWin.Show(); askWin.Activate();
        }

        void OpenSettings()
        {
            characters = Character.Discover(System.IO.Path.Combine(dir, "characters"));
            var w = new SettingsWindow(new Dictionary<string, object>(cfg), Brain.LoadStats(), characters);
            w.Saved += delegate {
                foreach (var kv in GetSettingsCfg(w)) cfg[kv.Key] = kv.Value;
                ApplyConfig(); ApplySize(); SaveConfig(); weatherAt = DateTime.MinValue; frameCache.Clear(); SlowChecks();
                Say("настройки сохранены", 3);
            };
            w.Show(); w.Activate();
        }
        static Dictionary<string, object> GetSettingsCfg(SettingsWindow w)
        {
            var f = typeof(SettingsWindow).GetField("cfg", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (Dictionary<string, object>)f.GetValue(w);
        }

        bool FocusClaude()
        {
            if (claude == IntPtr.Zero) return false;
            Native.SetForegroundWindow(claude);
            Thread.Sleep(150);
            return true;
        }

        void NewChat()
        {
            if (!FocusClaude()) return;
            var vks = new List<ushort>();
            foreach (var part in newChatKeys.ToLowerInvariant().Split('+'))
            {
                if (part == "ctrl") vks.Add(0x11); else if (part == "shift") vks.Add(0x10); else if (part == "alt") vks.Add(0x12);
                else if (part.Length == 1) vks.Add((ushort)char.ToUpperInvariant(part[0]));
            }
            Native.Chord(vks.ToArray());
            pokePose = "wave"; pokeUntil = Now + 1.5;
        }

        void Voice()
        {
            if (!FocusClaude()) return;
            Native.Chord(0x5B, 0x48); // Win+H: Windows voice typing into the focused field
        }

        void ToggleFocus()
        {
            if (focus == "focus") { focus = "off"; Say("фокус выключен", 2); }
            else { focus = "focus"; focusEnd = DateTime.Now.AddMinutes(25); Say("25 минут фокуса. я молчу", 2.5); bubbleUntil = Now + 2.5; }
            UpdateFocusTip();
            if (tray != null && focus != "focus") tray.Text = "Синсин";
        }
        void UpdateFocusTip()
        {
            ((TextBlock)focusBtn.Child).Foreground = focus == "focus" ? new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x4C)) : new SolidColorBrush(Color.FromRgb(0xC9, 0xC6, 0xBC));
            focusBtn.ToolTip = focus == "focus" ? "Фокус до " + focusEnd.ToString("HH:mm") + " (клик — выключить)" : "Помодоро: 25 минут фокуса";
        }

        void ToggleCompact() { compact = !compact; ApplySize(); SaveConfig(); }

        // ---------- care ----------
        // Feeding takes a banana from the stash (a free snack if it's empty); bathing resets cleanliness.
        void Feed()
        {
            bool fromStash = false;
            try
            {
                var s = Brain.WithStats(st => {
                    if (st.Bananas > 0) { st.Bananas--; st.Hunger = Math.Min(100, st.Hunger + 35); fromStash = true; } else st.Hunger = Math.Min(100, st.Hunger + 15);
                    st.Mood = Math.Min(100, st.Mood + 6);
                });
                mood = s.Mood; hunger = s.Hunger;
            }
            catch { }
            pokePose = "banana"; pokeUntil = Now + 3; k = 0;
            Say(fromStash ? "ням, спасибо! (бананов осталось меньше)" : "бананов нет, но перекус засчитан", 3);
        }

        void Bath()
        {
            try { var s = Brain.WithStats(st => { st.Clean = 100; st.Mood = Math.Min(100, st.Mood + 4); }); clean = s.Clean; mood = s.Mood; } catch { }
            frameCache.Clear(); pokePose = "bath"; pokeUntil = Now + 6; k = 0; Say("плюх! теперь я чистенький", 5);
        }

        // ---------- docker: a container that was running and stopped makes him panic ----------
        void CheckDocker()
        {
            if (!dockerOn || dockerBusy || (DateTime.Now - dockerAt).TotalSeconds < 30) return;
            dockerAt = DateTime.Now; dockerBusy = true;
            new Thread(() => {
                var now = new Dictionary<string, string>();
                try
                {
                    var psi = new ProcessStartInfo("docker", "ps -a --format \"{{.Names}}|{{.State}}|{{.Status}}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (var p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd();
                        if (!p.WaitForExit(8000) || p.ExitCode != 0) now = null;
                        else foreach (var line in o.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) { var a = line.Trim().Split('|'); if (a.Length >= 3) now[a[0]] = a[1] + "|" + a[2]; }
                    }
                }
                catch { now = null; }   // docker not installed or not running
                Dispatcher.BeginInvoke(new Action(() => { dockerBusy = false; if (now != null) DockerDiff(now); }));
            }) { IsBackground = true }.Start();
        }

        void DockerDiff(Dictionary<string, string> now)
        {
            var before = containers; containers = now;
            if (before == null) return;   // first look: just remember
            foreach (var kv in now)
            {
                string old; if (!before.TryGetValue(kv.Key, out old)) continue;
                bool wasUp = old.StartsWith("running"), isUp = kv.Value.StartsWith("running");
                if (wasUp && !isUp)
                {
                    string status = kv.Value.Substring(kv.Value.IndexOf('|') + 1);
                    pokePose = "error"; pokeUntil = Now + 6; k = 0; Say(kv.Key + " упал! (" + Brain.Short(status, 26) + ")", 8);
                    Notify("Docker: " + kv.Key + " остановился — " + status); Beep("error");
                }
                else if (!wasUp && isUp) { pokePose = "happy"; pokeUntil = Now + 3; Say(kv.Key + " снова жив", 4); }
            }
        }

        // ---------- stickers (UI-side) ----------
        void Award(string id, Func<Stats, bool> cond, Action<Stats> change)
        {
            var e = new Dictionary<string, object>();
            try { Brain.WithStats(s => { if (change != null) change(s); Brain.Sticker(s, e, id, cond(s)); }); } catch { return; }
            if (e.ContainsKey("sticker")) { pokePose = "secret"; pokeUntil = Now + 4; k = 0; Say((string)e["line"], 5); Beep("level"); Notify((string)e["line"]); }
        }

        // ---------- walks: along the bottom edge, or a hop onto the title bar to sit and swing his legs ----------
        void StartExcursion()
        {
            exFromX = exFromY = 0; exT0 = Now;
            if (rnd.NextDouble() < 0.5)
            {
                exMode = "walkOut"; exToX = -(120 + rnd.Next(260)); exToY = 0; facingLeft = true; exDur = Math.Abs(exToX) / 40.0;
            }
            else
            {
                // Feet land ~22 DIP below Claude's top edge, i.e. on the title bar.
                double targetTop = claudeTop + 22 * dpiS - (WinH - 40) * dpiS;
                exMode = "hopUp"; exToX = -rnd.Next(220); exToY = (targetTop - homeY) / dpiS; facingLeft = exToX < 0; exDur = 0.9;
            }
            Say(exMode == "walkOut" ? "пойду разомну лапы" : "залезу повыше", 2);
        }

        void Excursion()
        {
            if (exMode == "none") return;
            double p = exDur <= 0 ? 1 : Math.Min(1, (Now - exT0) / exDur);
            switch (exMode)
            {
                case "walkOut": case "walkBack":
                    exX = exFromX + (exToX - exFromX) * p;
                    if (p >= 1) { if (exMode == "walkOut") { exMode = "sit"; exUntil = Now + 15 + rnd.Next(10); } else Arrived(); }
                    break;
                case "hopUp": case "hopDown":
                    exX = exFromX + (exToX - exFromX) * p; exY = exFromY + (exToY - exFromY) * p - Math.Sin(Math.PI * p) * 40;
                    if (p >= 1) { if (exMode == "hopUp") { exMode = "perch"; exUntil = Now + 20 + rnd.Next(10); } else Arrived(); }
                    break;
                case "sit":
                    if (Now > exUntil) { exMode = "walkBack"; exFromX = exX; exToX = 0; exT0 = Now; exDur = Math.Abs(exX) / 40.0; facingLeft = false; }
                    break;
                case "perch":
                    if (Now > exUntil) { exMode = "hopDown"; exFromX = exX; exFromY = exY; exToX = exToY = 0; exT0 = Now; exDur = 0.9; facingLeft = exFromX > 0; }
                    break;
            }
        }

        void Arrived() { exMode = "none"; exX = exY = 0; facingLeft = false; Award("traveler", s => s.Excursions >= 10, s => s.Excursions++); }

        // Snap back home at once (Claude got busy, or he was clicked mid-walk).
        void ComeHome() { if (exMode == "none") return; exMode = "none"; exX = exY = 0; facingLeft = false; }

        // ---------- music: Spotify shows "Artist - Song" as its window title while playing ----------
        void CheckMusic()
        {
            string title = "";
            if (musicOn) foreach (var p in Process.GetProcessesByName("Spotify")) { try { if (!string.IsNullOrEmpty(p.MainWindowTitle)) title = p.MainWindowTitle; } catch { } }
            bool playing = title.Length > 0 && !title.StartsWith("Spotify", StringComparison.OrdinalIgnoreCase);
            string now = playing ? title : "";
            if (now != song)
            {
                song = now;
                if (song.Length > 0 && state == "idle" && exMode == "none" && focus != "focus") Say("♪ " + Brain.Short(song, 44), 5);
            }
            if (!playing) musicMinuteAt = DateTime.Now;
            else if ((DateTime.Now - musicMinuteAt).TotalMinutes >= 1) { musicMinuteAt = DateTime.Now; Award("meloman", s => s.MusicMinutes >= 60, s => s.MusicMinutes++); }
        }

        // ---------- memory ----------
        ProjectMemory LastProject(bool beforeToday)
        {
            string today = Brain.Today();
            return Brain.LoadMemory().Values.Where(p => (!beforeToday || p.LastDay != today) && (p.LastPrompt.Length > 0 || p.LastSummary.Length > 0))
                                     .OrderByDescending(p => p.LastSeen).FirstOrDefault();
        }

        void Greet()
        {
            var p = LastProject(true);
            if (p == null) { Say("доброе утро! погнали?", 6); return; }
            continueText = p.LastPrompt;
            Say("доброе утро! в прошлый раз в " + p.Name + ": " + Brain.Short(p.LastSummary.Length > 0 ? p.LastSummary : p.LastPrompt, 70) + "\nпродолжим? (правый клик)", 14);
            pokePose = "wave"; pokeUntil = Now + 3;
        }

        void ShowMemory()
        {
            var list = Brain.LoadMemory().Values.OrderByDescending(p => p.LastSeen).Take(3).ToList();
            if (list.Count == 0) { Say(memoryOn ? "пока ничего не помню" : "память выключена в настройках", 4); return; }
            Say("помню:\n" + string.Join("\n", list.Select(p => p.Name + " (" + p.LastDay + "): " + Brain.Short(p.LastSummary.Length > 0 ? p.LastSummary : p.LastPrompt, 40))), 12);
        }

        void ContinueYesterday()
        {
            var p = LastProject(false);
            string text = continueText ?? (p != null ? p.LastPrompt : null);
            if (string.IsNullOrEmpty(text)) { Say("не помню, что мы делали", 3); return; }
            TypeIntoClaude("Продолжим с того места, где остановились: " + text);
        }

        // ---------- shop ----------
        void OpenShop()
        {
            new ShopWindow(kind => kind == "acc" ? accChoice : kind == "backdrop" ? backdrop : kind == "phrases" ? phrases : "",
                item => {
                    string id = item.Id == "none" ? (item.Kind == "acc" ? "auto" : "none") : item.Kind == "acc" ? item.Id : item.Id.Substring(3);
                    if (item.Kind == "acc") accChoice = id; else if (item.Kind == "backdrop") backdrop = id; else if (item.Kind == "phrases") phrases = id;
                    try { owned = Brain.LoadStats().Owned; } catch { }
                    frameCache.Clear(); ApplySize(); SaveConfig();
                    if (item.Id != "none") { pokePose = "love"; pokeUntil = Now + 2.5; Say("обновка! спасибо", 3); }
                }).Show();
        }

        // ---------- game ----------
        void OpenGame()
        {
            if (game != null) { game.Activate(); return; }
            game = new GameWindow(plugin == null ? skin : "monkey", plugin, Brain.LoadStats().GameBest);
            game.Finished += (score, bananas) => {
                Award("gamer", s => s.GameBest >= 300, s => { s.GameBest = Math.Max(s.GameBest, score); s.Bananas += bananas; s.Mood = Math.Min(100, s.Mood + Math.Min(10, bananas)); });
                Award("champion", s => s.GameBest >= 1000, null);
            };
            game.Closed += delegate { game = null; Say("хорошо побегали", 3); };
            game.Show(); game.Activate();
        }

        // ---------- hover bar ----------
        void CheckHover()
        {
            if (!IsVisible) return;
            bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (down && !mouseWasDown)
            {
                Native.POINT cp; Native.GetCursorPos(out cp);
                lookAt = cp; lookUntil = DateTime.Now.AddSeconds(1.5);
                if (clickFx) try { new ClickBurst(cp.X, cp.Y, Native.GetDpiForWindow(me) / 96.0); } catch { }
            }
            mouseWasDown = down;
            if (exMode != "none") { ShowBar(false); return; }
            Native.POINT p; Native.RECT w;
            Native.GetCursorPos(out p); Native.GetWindowRect(me, out w);
            double s = Native.GetDpiForWindow(me) / 96.0;
            double x = (p.X - w.Left) / s, y = (p.Y - w.Top) / s;
            var root = (FrameworkElement)Content;
            var sr = sprite.TransformToAncestor(root).TransformBounds(new Rect(0, 0, sprite.ActualWidth, sprite.ActualHeight));
            double top = WinH - root.ActualHeight + sr.Top + sr.Height * 0.15;
            var zone = new Rect(sr.Left - 10, top, sr.Width + 20, WinH - top);
            if (zone.Contains(x, y)) { hoverLostAt = DateTime.MinValue; ShowBar(true); }
            else if (barShown)
            {
                if (hoverLostAt == DateTime.MinValue) hoverLostAt = DateTime.UtcNow;
                else if ((DateTime.UtcNow - hoverLostAt).TotalMilliseconds > 250) ShowBar(false);
            }
        }

        void ShowBar(bool show)
        {
            if (show == barShown) return;
            barShown = show; bar.IsHitTestVisible = show;
            var dur = new Duration(TimeSpan.FromMilliseconds(show ? 200 : 160));
            IEasingFunction ease = show ? (IEasingFunction)new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseIn };
            barScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(show ? 1 : 0.2, dur) { EasingFunction = ease });
            barScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(show ? 1 : 0.6, dur) { EasingFunction = ease });
            bar.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, dur));
        }

        // ---------- placement ----------
        void Place()
        {
            if (dragging) return;
            if (!Native.IsWindow(claude) || DateTime.UtcNow > findAt)
            {
                var h = Native.FindClaude(); findAt = DateTime.UtcNow.AddSeconds(2);
                if (h != claude) { claude = h; if (h != IntPtr.Zero) Native.SetWindowLongPtr(me, -8, h); }
            }
            if (claude == IntPtr.Zero || Native.IsIconic(claude) || !Native.IsWindowVisible(claude)) { if (IsVisible) Hide(); return; }
            if (!IsVisible) Show();
            Native.RECT r, w; Native.GetWindowRect(claude, out r); Native.GetWindowRect(me, out w);
            double s = Native.GetDpiForWindow(me) / 96.0;
            int pw = (int)Math.Round(WinW * s), ph = (int)Math.Round(WinH * s);
            int x = Math.Max(r.Left, r.Right - cfgRight - pw), y = Math.Max(r.Top, r.Bottom - cfgBottom - ph);
            homeX = x; homeY = y; claudeTop = r.Top; claudeLeft = r.Left; dpiS = s;
            x += (int)Math.Round(exX * s); y += (int)Math.Round(exY * s);
            if (x != w.Left || y != w.Top || w.Right - w.Left != pw || w.Bottom - w.Top != ph)
                Native.SetWindowPos(me, IntPtr.Zero, x, y, pw, ph, 0x0004 | 0x0010);
        }

        // ---------- config ----------
        static TimeSpan Hm(object v, TimeSpan def) { TimeSpan t; return TimeSpan.TryParse(Convert.ToString(v), out t) ? t : def; }

        void LoadConfig()
        {
            characters = Character.Discover(System.IO.Path.Combine(dir, "characters"), System.IO.Path.Combine(Brain.Dir, "..", "characters"));
            try { cfg = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(System.IO.Path.Combine(dir, "config.json"))) ?? new Dictionary<string, object>(); }
            catch { cfg = new Dictionary<string, object>(); }
            ApplyConfig();
        }

        void ApplyConfig()
        {
            object v;
            if (cfg.TryGetValue("right", out v)) cfgRight = Convert.ToInt32(v);
            if (cfg.TryGetValue("bottom", out v)) cfgBottom = Convert.ToInt32(v);
            if (cfg.TryGetValue("newChatKeys", out v)) newChatKeys = Convert.ToString(v);
            if (cfg.TryGetValue("compact", out v)) compact = Convert.ToBoolean(v);
            if (cfg.TryGetValue("homeAfter", out v)) homeAfter = Hm(v, homeAfter);
            if (cfg.TryGetValue("lunchFrom", out v)) lunchFrom = Hm(v, lunchFrom);
            if (cfg.TryGetValue("lunchTo", out v)) lunchTo = Hm(v, lunchTo);
            if (cfg.TryGetValue("scheduleWeekdaysOnly", out v)) scheduleWeekdaysOnly = Convert.ToBoolean(v);
            string want = cfg.TryGetValue("skin", out v) ? Convert.ToString(v) : "monkey";
            plugin = want == "monkey" ? null : characters.FirstOrDefault(c => c.Id == want);
            skin = plugin == null ? "monkey" : want;
            frameCache.Clear();
            if (cfg.TryGetValue("accessory", out v)) accChoice = Convert.ToString(v);
            if (cfg.TryGetValue("size", out v)) sizeMul = Math.Max(0.5, Math.Min(1.5, Convert.ToDouble(v)));
            if (cfg.TryGetValue("sound", out v)) soundOn = Convert.ToBoolean(v);
            if (cfg.TryGetValue("health", out v)) healthOn = Convert.ToBoolean(v);
            if (cfg.TryGetValue("walks", out v)) walksOn = Convert.ToBoolean(v);
            if (cfg.TryGetValue("music", out v)) musicOn = Convert.ToBoolean(v);
            clickFx = cfg.TryGetValue("clickFx2", out v) && Convert.ToBoolean(v);   // off unless re-enabled in settings
            if (cfg.TryGetValue("eyes", out v)) eyesOn = Convert.ToBoolean(v);
            if (cfg.TryGetValue("memory", out v)) memoryOn = Convert.ToBoolean(v);
            if (cfg.TryGetValue("docker", out v)) dockerOn = Convert.ToBoolean(v);
            backdrop = cfg.TryGetValue("backdrop", out v) ? Convert.ToString(v) : "none";
            phrases = cfg.TryGetValue("phrases", out v) ? Convert.ToString(v) : "none";
            city = cfg.TryGetValue("city", out v) ? Convert.ToString(v).Trim() : "";
            if (city.Length == 0) weather = null;
            if (sprite != null) ApplySize();
        }

        void SaveConfig()
        {
            cfg["right"] = cfgRight; cfg["bottom"] = cfgBottom; cfg["compact"] = compact; cfg["newChatKeys"] = newChatKeys;
            cfg["homeAfter"] = homeAfter.ToString(@"hh\:mm"); cfg["lunchFrom"] = lunchFrom.ToString(@"hh\:mm"); cfg["lunchTo"] = lunchTo.ToString(@"hh\:mm");
            cfg["scheduleWeekdaysOnly"] = scheduleWeekdaysOnly; cfg["skin"] = skin; cfg["accessory"] = accChoice; cfg["size"] = sizeMul;
            cfg["sound"] = soundOn; cfg["health"] = healthOn; cfg["city"] = city;
            cfg["walks"] = walksOn; cfg["music"] = musicOn; cfg["clickFx2"] = clickFx; cfg["eyes"] = eyesOn; cfg["memory"] = memoryOn;
            cfg["docker"] = dockerOn; cfg["backdrop"] = backdrop; cfg["phrases"] = phrases;
            if (!cfg.ContainsKey("aiSummary")) cfg["aiSummary"] = true;
            if (!cfg.ContainsKey("aiMinSeconds")) cfg["aiMinSeconds"] = 20;
            try { File.WriteAllText(System.IO.Path.Combine(dir, "config.json"), json.Serialize(cfg)); } catch { }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (!File.Exists(System.IO.Path.Combine(dir, "config.json"))) SaveConfig();
        }

        // Dev aid: renders the window for a list of (skin, acc, pose, line) into one PNG without showing it.
        public void Snapshot(string outPath)
        {
            string[][] shots = {
                new[] { "monkey", "none", "full", "контекст 87%: я полный. /compact?", "200" }, new[] { "monkey", "none", "plan", "черчу план…", "200" },
                new[] { "monkey", "none", "bath", "плюх! теперь я чистенький", "200" }, new[] { "monkey", "cap", "idle", "обновка! спасибо", "20" },
                new[] { "monkey", "bow", "idle", "kz-php-fpm упал! (Exited (1))", "40" }, new[] { "monkey", "flower", "sit", "", "200" },
                new[] { "monkey", "none", "idle", "уровень 10! я расту", "5000" }, new[] { "monkey", "none", "idle", "я чумазый… искупай меня", "200" } };
            var root = (FrameworkElement)Content;
            var sheet = new RenderTargetBitmap((int)(WinW * 2 * shots.Length), (int)(WinH * 2), 192, 192, PixelFormats.Pbgra32);
            for (int i = 0; i < shots.Length; i++)
            {
                plugin = characters.FirstOrDefault(c => c.Id == shots[i][0]); skin = plugin == null ? "monkey" : shots[i][0]; accChoice = shots[i][1]; xp = int.Parse(shots[i][4]);
                backdrop = i == 3 ? "beach" : i == 4 ? "office" : i == 5 ? "space" : "none"; clean = i == 7 ? 10 : 90; frameCache.Clear();
                owned = new List<string> { "cap", "bow", "flower" };
                state = "working"; helpers = 0; sessions = 1;
                sprite.Source = Frame(shots[i][2], 6);
                extrasBack.Visibility = extrasFront.Visibility = Visibility.Visible; Sprites.Flies = clean < 15;
                extrasBack.Source = BitmapSource.Create(Sprites.StageW, Sprites.StageH, 96, 96, PixelFormats.Bgra32, null, Sprites.RenderExtras(shots[i][2], 6, 1, helpers, "monkey", false), Sprites.StageW * 4);
                extrasFront.Source = BitmapSource.Create(Sprites.StageW, Sprites.StageH, 96, 96, PixelFormats.Bgra32, null, Sprites.RenderExtras(shots[i][2], 6, 1, helpers, "monkey", true), Sprites.StageW * 4);
                bubbleText.Text = shots[i][3]; bubble.Visibility = tail.Visibility = Visibility.Visible;
                ApplySize();
                root.Measure(new Size(WinW, WinH)); root.Arrange(new Rect(0, WinH - root.DesiredSize.Height, WinW, root.DesiredSize.Height)); root.UpdateLayout();
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x24)), null, new Rect(i * WinW, 0, WinW, WinH));
                    dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentY = AlignmentY.Bottom }, null, new Rect(i * WinW, 0, WinW, WinH));
                }
                sheet.Render(dv);
            }
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(sheet));
            using (var fs = File.Create(outPath)) enc.Save(fs);
        }
    }

    static class Program
    {
        public static string Dir;
        public static void Log(string msg)
        {
            try { File.AppendAllText(System.IO.Path.Combine(Dir, "xing-pixel.log"), DateTime.Now.ToString("s") + " " + msg + Environment.NewLine); } catch { }
        }

        // Sprite sheets for the macOS build: one PNG per skin/accessory/pose (48 frames in a row) + manifest.json.
        static void Export(string outDir)
        {
            Directory.CreateDirectory(outDir);
            int W = Sprites.StageW, H = Sprites.StageH, n = Sprites.Frames;
            // Bodies per look: monkey/l<level>/<accessory>/<pose>.png (the aura is exported separately below).
            Sprites.AuraOn = false;
            foreach (var look in Sprites.Looks)
                foreach (var acc in Sprites.Accessories)
                {
                    const string skin = "monkey";
                    Sprites.SetStage(Sprites.StageForLevel(look)); Sprites.Rank = look;
                    string sub = System.IO.Path.Combine(outDir, skin, "l" + look, acc); Directory.CreateDirectory(sub);
                    foreach (var p in Sprites.Poses)
                    {
                        var sheet = new byte[W * n * H * 4];
                        for (int i = 0; i < n; i++)
                        {
                            var f = Sprites.Render(p, i, skin, acc, 1, 0);
                            for (int y = 0; y < H; y++) Buffer.BlockCopy(f, y * W * 4, sheet, (y * W * n + i * W) * 4, W * 4);
                        }
                        var bs = BitmapSource.Create(W * n, H, 96, 96, PixelFormats.Bgra32, null, sheet, W * n * 4);
                        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bs));
                        using (var fs = File.Create(System.IO.Path.Combine(sub, p + ".png"))) enc.Save(fs);
                    }
                    // Idle with the eyes looking in 9 directions (mouse following on macOS): idle_<lx>_<ly>.png
                    for (int lx = -1; lx <= 1; lx++)
                        for (int ly = -1; ly <= 1; ly++)
                        {
                            var sheet = new byte[W * n * H * 4];
                            for (int i = 0; i < n; i++)
                            {
                                var f = Sprites.RenderBody("idle", i, skin, acc, lx, ly);
                                for (int y = 0; y < H; y++) Buffer.BlockCopy(f, y * W * 4, sheet, (y * W * n + i * W) * 4, W * 4);
                            }
                            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W * n, H, 96, 96, PixelFormats.Bgra32, null, sheet, W * n * 4)));
                            using (var fs = File.Create(System.IO.Path.Combine(sub, "idle_" + lx + "_" + ly + ".png"))) enc.Save(fs);
                        }
                }
            Sprites.AuraOn = true;
            // Aura overlays: aura/<sparkles>_back.png goes under the body, _front.png over it.
            string adir = System.IO.Path.Combine(outDir, "aura"); Directory.CreateDirectory(adir);
            foreach (int lvl in new[] { 12, 13, 17 })
            {
                Sprites.Rank = lvl;
                foreach (bool front in new[] { false, true })
                {
                    var sheet = new byte[W * n * H * 4];
                    for (int i = 0; i < n; i++)
                    {
                        var b = new List<Layer>(); var f = new List<Layer>(); Sprites.Aura(i, b, f);
                        var px = Sprites.Flatten(front ? f : b);
                        for (int y = 0; y < H; y++) Buffer.BlockCopy(px, y * W * 4, sheet, (y * W * n + i * W) * 4, W * 4);
                    }
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(W * n, H, 96, 96, PixelFormats.Bgra32, null, sheet, W * n * 4)));
                    using (var fs = File.Create(System.IO.Path.Combine(adir, Sprites.AuraCount(lvl) + (front ? "_front" : "_back") + ".png"))) enc.Save(fs);
                }
            }
            Sprites.Rank = 1; Sprites.SetStage("adult");
            // Banana-runner sprites: 2 frames side by side.
            string gdir = System.IO.Path.Combine(outDir, "game"); Directory.CreateDirectory(gdir);
            foreach (var kind in new[] { "bug", "error", "stack", "banana", "goldbanana", "cloud" })
            {
                var a = Sprites.GameSprite(kind, 0); var b = Sprites.GameSprite(kind, 3);
                var px = new byte[a.W * 2 * a.H * 4]; var pa = Sprites.ToBgra(a); var pb = Sprites.ToBgra(b);
                for (int y = 0; y < a.H; y++) { Buffer.BlockCopy(pa, y * a.W * 4, px, y * a.W * 8, a.W * 4); Buffer.BlockCopy(pb, y * a.W * 4, px, y * a.W * 8 + a.W * 4, a.W * 4); }
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(a.W * 2, a.H, 96, 96, PixelFormats.Bgra32, null, px, a.W * 8)));
                using (var fs = File.Create(System.IO.Path.Combine(gdir, kind + ".png"))) enc.Save(fs);
            }
            File.WriteAllText(System.IO.Path.Combine(outDir, "manifest.json"),
                "{ \"frameWidth\": " + W + ", \"frameHeight\": " + H + ", \"frames\": " + n + ", \"fps\": " + Sprites.Fps + ", \"homeFrames\": " + Sprites.HomeFrames +
                ", \"skins\": [\"" + string.Join("\", \"", Sprites.Skins) + "\"], \"accessories\": [\"" + string.Join("\", \"", Sprites.Accessories) +
                "\"], \"poses\": [\"" + string.Join("\", \"", Sprites.Poses) + "\"], \"looks\": [" + string.Join(", ", Sprites.Looks) + "] }");
        }

        [STAThread]
        static int Main(string[] args)
        {
            Dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            Brain.Dir = Dir;
            if (args.Length == 1 && args[0] == "--event")
            {
                try { return Brain.HandleEvent(); } catch (Exception e) { Log("event: " + e.Message); return 0; }
            }
            if (args.Length == 2 && args[0] == "--state")
            {
                try { var e = new Dictionary<string, object>(); e["state"] = args[1]; Brain.WriteEvent(e); } catch { }
                return 0;
            }
            if (args.Length == 1 && args[0] == "--install") return Installer.Install();
            if (args.Length == 1 && args[0] == "--uninstall") return Installer.Uninstall();
            if (args.Length == 2 && args[0] == "--export")
            {
                try { Export(args[1]); return 0; } catch (Exception e) { Log("export: " + e); return 1; }
            }
            if (args.Length == 3 && args[0] == "--export-character")
            {
                try
                {
                    var lines = args[1] == "pig" ? new Dictionary<string, string> { { "idle", "хрю-хрю" }, { "error", "хрю?!" }, { "success", "хрю! готово" } }
                                                 : new Dictionary<string, string> { { "idle", "искрю" }, { "success", "вспыхнуло!" } };
                    Character.ExportBuiltIn(args[1], args[1] == "pig" ? "Мини-пиг" : "Искорка", args[1] == "pig" ? "pig" : "spark", args[2], lines);
                    return 0;
                }
                catch (Exception e) { Log("export-character: " + e); return 1; }
            }
            if (args.Length == 2 && args[0] == "--progress-shot")   // renders the "Путь Синсина" window to a PNG (for checking the art)
            {
                try
                {
                    var sv = (ScrollViewer)new ProgressWindow(Brain.LoadStats()).Content; var c = (FrameworkElement)sv.Content; sv.Content = null;
                    var host = new Border { Child = c, Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1E, 0x1C)), Width = 720 };
                    host.Measure(new Size(720, double.PositiveInfinity)); host.Arrange(new Rect(host.DesiredSize)); host.UpdateLayout();
                    var rtb = new RenderTargetBitmap((int)host.ActualWidth, (int)host.ActualHeight, 96, 96, PixelFormats.Pbgra32); rtb.Render(host);
                    var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = File.Create(args[1])) enc.Save(fs);
                    return 0;
                }
                catch (Exception e) { Log("progress-shot: " + e); return 1; }
            }
            if (args.Length == 2 && args[0] == "--snapshot")
            {
                try { new Mascot(Dir).Snapshot(args[1]); return 0; } catch (Exception e) { Log("snapshot: " + e); return 1; }
            }

            bool created;
            using (var mutex = new Mutex(true, "Local\\ClaudeMascotXingPixel", out created))
            {
                if (!created) return 0;
                try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e) { Log("unhandled: " + e.Exception); e.Handled = true; };
                var win = new Mascot(Dir);
                app.MainWindow = win;
                win.Start();
                Log("started");
                app.Run();
            }
            return 0;
        }
    }
}
