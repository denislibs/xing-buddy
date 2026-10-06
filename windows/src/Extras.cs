// Side features: 8-bit sounds, weather, weekly report, installer, settings window and the "ask Xing" panel.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace XingPixel
{
    // Tiny square-wave synth: every sound is generated in memory, nothing to ship.
    static class Sound
    {
        static byte[] Wav(IEnumerable<double[]> notes, bool noise)   // note = {freq, ms, slideToFreq}
        {
            const int rate = 22050; var pcm = new List<byte>(); var rnd = new Random(1);
            foreach (var n in notes)
            {
                int len = (int)(rate * n[1] / 1000);
                double phase = 0;
                for (int i = 0; i < len; i++)
                {
                    double t = (double)i / len, f = n[0] + (n.Length > 2 ? (n[2] - n[0]) * t : 0);
                    phase += f / rate;
                    double env = Math.Min(1, i / 200.0) * Math.Min(1, (len - i) / 400.0);
                    double v = n[0] <= 0 ? 0 : (noise ? (rnd.NextDouble() < 0.5 ? 1 : -1) * 0.5 + ((phase % 1) < 0.5 ? 0.5 : -0.5) : ((phase % 1) < 0.5 ? 1 : -1));
                    pcm.Add((byte)(128 + v * env * 22));
                }
            }
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Count); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate); w.Write((short)1); w.Write((short)8);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Count); w.Write(pcm.ToArray());
            return ms.ToArray();
        }

        static readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();

        public static void Play(string name, string skin)
        {
            string key = name + "|" + skin; byte[] wav;
            if (!cache.TryGetValue(key, out wav))
            {
                switch (name)
                {
                    case "success":
                        wav = skin == "pig" ? Wav(new[] { new double[] { 140, 90, 110 }, new double[] { 0, 40 }, new double[] { 150, 120, 100 } }, true)
                            : skin == "spark" ? Wav(new[] { new double[] { 880, 70 }, new double[] { 1320, 120 } }, false)
                            : Wav(new[] { new double[] { 520, 110, 700 }, new double[] { 0, 30 }, new double[] { 700, 160, 460 } }, false);   // "у-а"
                        break;
                    case "error": wav = Wav(new[] { new double[] { 440, 380, 200 } }, false); break;                                            // sigh
                    case "level": wav = Wav(new[] { new double[] { 523, 80 }, new double[] { 659, 80 }, new double[] { 784, 80 }, new double[] { 1046, 180 } }, false); break;
                    default: wav = skin == "pig" ? Wav(new[] { new double[] { 160, 110, 120 } }, true) : Wav(new[] { new double[] { 900, 40 } }, false); break;   // poke
                }
                cache[key] = wav;
            }
            try { new System.Media.SoundPlayer(new MemoryStream(wav)).Play(); } catch { }
        }
    }

    // Current weather for a city via open-meteo (free, no key). Runs on a background thread.
    class Weather
    {
        public bool Rain, Snow, Cold; public double Temp; public DateTime At = DateTime.MinValue;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static Weather Fetch(string city)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                var geo = Json.Deserialize<Dictionary<string, object>>(wc.DownloadString("https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&name=" + Uri.EscapeDataString(city)));
                var results = geo.ContainsKey("results") ? geo["results"] as ArrayList : null;
                if (results == null || results.Count == 0) return null;
                var r = (Dictionary<string, object>)results[0];
                string lat = Convert.ToString(r["latitude"], System.Globalization.CultureInfo.InvariantCulture), lon = Convert.ToString(r["longitude"], System.Globalization.CultureInfo.InvariantCulture);
                var f = Json.Deserialize<Dictionary<string, object>>(wc.DownloadString("https://api.open-meteo.com/v1/forecast?current=temperature_2m,weather_code&latitude=" + lat + "&longitude=" + lon));
                var cur = (Dictionary<string, object>)f["current"];
                int code = Convert.ToInt32(cur["weather_code"]); double temp = Convert.ToDouble(cur["temperature_2m"]);
                return new Weather {
                    Temp = temp, At = DateTime.Now,
                    Rain = (code >= 51 && code <= 67) || (code >= 80 && code <= 82) || code >= 95,
                    Snow = (code >= 71 && code <= 77) || code == 85 || code == 86,
                    Cold = temp < 0 };
            }
        }
    }

    static class Report
    {
        // Weekly HTML report from the per-day history; the "phrase of the week" comes from Haiku when available.
        public static string Write(string dir, Stats s, string phrase)
        {
            var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(-6 + i)).ToList();
            var rows = days.Select(d => { DayStats h; s.History.TryGetValue(d.ToString("yyyy-MM-dd"), out h); return new { d, h = h ?? new DayStats() }; }).ToList();
            int tasks = rows.Sum(r => r.h.Tasks), commits = rows.Sum(r => r.h.Commits), pushes = rows.Sum(r => r.h.Pushes), edits = rows.Sum(r => r.h.Edits),
                errors = rows.Sum(r => r.h.Errors), green = rows.Sum(r => r.h.TestsGreen), xp = rows.Sum(r => r.h.Xp);
            long longest = rows.Max(r => r.h.LongestMs);
            int maxTasks = Math.Max(1, rows.Max(r => r.h.Tasks));
            string since = days[0].ToString("yyyy-MM-dd");
            var achieved = s.AchievementDays.Where(kv => string.CompareOrdinal(kv.Value, since) >= 0).Select(kv => kv.Key).ToList();
            string[] dn = { "вс", "пн", "вт", "ср", "чт", "пт", "сб" };

            var bars = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                int h = (int)Math.Round(120.0 * rows[i].h.Tasks / maxTasks), x = 20 + i * 64;
                bars.AppendFormat("<rect x='{0}' y='{1}' width='36' height='{2}' rx='4' class='bar'/><text x='{3}' y='160' class='lbl'>{4}</text><text x='{3}' y='{5}' class='val'>{6}</text>",
                    x, 140 - h, Math.Max(h, 2), x + 18, dn[(int)rows[i].d.DayOfWeek], 134 - h, rows[i].h.Tasks);
            }
            Func<string, string> esc = t => WebUtility.HtmlEncode(t ?? "");
            string html = @"<!doctype html><html lang='ru'><head><meta charset='utf-8'><title>Неделя Синсина</title><style>
:root{--bg:#F5F3EE;--card:#fff;--ink:#2B2925;--mute:#76736B;--acc:#D97757;--line:#E4E0D6}
@media (prefers-color-scheme:dark){:root{--bg:#1F1E1C;--card:#2A2927;--ink:#EEEBE3;--mute:#9C988E;--line:#3A3936}}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.6 'Segoe UI',system-ui,sans-serif}
main{max-width:760px;margin:0 auto;padding:40px 20px}h1{font-size:28px;margin:0 0 4px;font-weight:600}.sub{color:var(--mute);margin:0 0 28px}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px;margin-bottom:24px}
.k{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:14px 16px}.k b{display:block;font-size:26px;font-weight:600}.k span{color:var(--mute);font-size:13px}
.card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:18px 20px;margin-bottom:16px}
.quote{font-size:18px;font-style:italic;border-left:3px solid var(--acc);padding-left:14px}
svg{width:100%;height:auto}.bar{fill:var(--acc)}.lbl,.val{fill:var(--mute);font-size:12px;text-anchor:middle}
ul{margin:0;padding-left:20px}</style></head><body><main>
<h1>Неделя Синсина</h1><p class='sub'>" + days[0].ToString("d MMMM", new System.Globalization.CultureInfo("ru-RU")) + " — " + days[6].ToString("d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU")) +
            " · уровень " + Brain.Level(s.Xp) + " · золотых бананов: " + s.GoldenBananas + @"</p>
<div class='grid'>
<div class='k'><b>" + tasks + "</b><span>задач</span></div><div class='k'><b>" + commits + "</b><span>коммитов</span></div>" +
"<div class='k'><b>" + pushes + "</b><span>пушей</span></div><div class='k'><b>" + edits + "</b><span>правок</span></div>" +
"<div class='k'><b>" + green + "</b><span>зелёных прогонов</span></div><div class='k'><b>" + errors + "</b><span>ошибок</span></div>" +
"<div class='k'><b>" + Brain.Clock(longest) + "</b><span>самая долгая задача</span></div><div class='k'><b>+" + xp + "</b><span>опыта</span></div>" + @"
</div>
<div class='card'><svg viewBox='0 0 470 170' role='img' aria-label='Задачи по дням'>" + bars + @"</svg></div>
<div class='card'><b>Ачивки недели</b><ul>" + (achieved.Count == 0 ? "<li>на этой неделе без новых ачивок</li>" : string.Join("", achieved.Select(a => "<li>" + esc(AchievementTitle(a)) + "</li>"))) + @"</ul></div>
<div class='card quote'>" + esc(phrase) + @"<br><span style='font-size:13px;color:var(--mute);font-style:normal'>— Синсин</span></div>
</main></body></html>";
            string reports = Path.Combine(dir, "reports"); Directory.CreateDirectory(reports);
            string path = Path.Combine(reports, "week-" + DateTime.Today.ToString("yyyy-MM-dd") + ".html");
            File.WriteAllText(path, html, new UTF8Encoding(true));
            return path;
        }

        public static string AchievementTitle(string id)
        {
            var t = new Dictionary<string, string> { { "night", "ночная смена" }, { "edits100", "100 правок" }, { "edits1000", "1000 правок" },
                { "green10", "10 зелёных прогонов" }, { "survivor", "выжил после 5 ошибок" }, { "tasks100", "100 задач" }, { "edits50d", "50 правок за день" },
                { "commit", "первый коммит дня" }, { "tasks10", "10 задач за день" }, { "commits100", "100 коммитов" }, { "push1", "первый пуш" },
                { "push50", "50 пушей" }, { "merge1", "первый мерж" }, { "pr1", "первый PR" }, { "marathon", "марафон: задача дольше 30 минут" } };
            string key = id.Contains("-") ? id.Substring(0, id.IndexOf('-')) : id; string v;
            return t.TryGetValue(key, out v) ? v : id;
        }
    }

    // Install / uninstall: hooks in ~/.claude/settings.json (backed up first) and the autostart shortcut.
    static class Installer
    {
        static readonly string[] Events = { "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "PermissionRequest", "Notification", "Stop", "StopFailure" };
        static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"); } }
        static string Shortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Синсин (Claude mascot).lnk"); } }
        static string Exe { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        static Dictionary<string, object> LoadSettings()
        {
            if (!File.Exists(SettingsPath)) return new Dictionary<string, object>();
            string bk = Path.Combine(Program.Dir, "backup"); Directory.CreateDirectory(bk);
            File.Copy(SettingsPath, Path.Combine(bk, "settings.json." + DateTime.Now.ToString("yyyyMMdd-HHmmss")), true);
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(SettingsPath)) ?? new Dictionary<string, object>();
        }
        static void SaveSettings(Dictionary<string, object> d)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
        }
        static bool IsOurs(object entry)
        {
            var e = entry as Dictionary<string, object>; if (e == null || !e.ContainsKey("hooks")) return false;
            foreach (var h in (ArrayList)e["hooks"]) { var hd = h as Dictionary<string, object>; if (hd != null && Convert.ToString(hd["command"]).Contains("xing-pixel")) return true; }
            return false;
        }

        public static bool HooksInstalled()
        {
            try { return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains("xing-pixel"); } catch { return false; }
        }

        public static void InstallHooks()
        {
            var d = LoadSettings();
            var hooks = d.ContainsKey("hooks") ? d["hooks"] as Dictionary<string, object> : null;
            if (hooks == null) { hooks = new Dictionary<string, object>(); d["hooks"] = hooks; }
            string cmd = "\"" + Exe.Replace('\\', '/') + "\" --event";
            foreach (var ev in Events)
            {
                var list = hooks.ContainsKey(ev) ? hooks[ev] as ArrayList : null;
                if (list == null) { list = new ArrayList(); hooks[ev] = list; }
                if (list.Cast<object>().Any(IsOurs)) continue;
                var entry = new Dictionary<string, object>();
                if (ev.StartsWith("Pre") || ev.StartsWith("Post") || ev == "PermissionRequest") entry["matcher"] = "*";
                entry["hooks"] = new ArrayList { new Dictionary<string, object> { { "type", "command" }, { "async", true }, { "timeout", ev == "Stop" ? 90 : 10 }, { "command", cmd } } };
                list.Add(entry);
            }
            SaveSettings(d);
        }

        public static void RemoveHooks()
        {
            if (!File.Exists(SettingsPath)) return;
            var d = LoadSettings();
            var hooks = d.ContainsKey("hooks") ? d["hooks"] as Dictionary<string, object> : null;
            if (hooks == null) return;
            foreach (var ev in hooks.Keys.ToList())
            {
                var list = hooks[ev] as ArrayList; if (list == null) continue;
                var keep = new ArrayList(list.Cast<object>().Where(x => !IsOurs(x)).ToList());
                if (keep.Count == 0) hooks.Remove(ev); else hooks[ev] = keep;
            }
            if (hooks.Count == 0) d.Remove("hooks");
            SaveSettings(d);
        }

        public static bool Autostart { get { return File.Exists(Shortcut); } }

        public static void SetAutostart(bool on)
        {
            if (!on) { if (File.Exists(Shortcut)) File.Delete(Shortcut); return; }
            var t = Type.GetTypeFromProgID("WScript.Shell"); dynamic sh = Activator.CreateInstance(t);
            dynamic lnk = sh.CreateShortcut(Shortcut);
            lnk.TargetPath = Exe; lnk.WorkingDirectory = Program.Dir; lnk.Description = "Синсин — маскот для Claude Desktop";
            lnk.Save();
        }

        public static int Install()
        {
            InstallHooks(); SetAutostart(true);
            Process.Start(Exe);
            MessageBox.Show("Синсин установлен:\n• хуки Claude Code добавлены (копия настроек в папке backup)\n• запуск вместе с Windows включён\n\nОн появится рядом с окном Claude.", "Синсин");
            return 0;
        }

        public static int Uninstall()
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Exe))) if (p.Id != Process.GetCurrentProcess().Id) try { p.Kill(); } catch { }
            RemoveHooks(); SetAutostart(false);
            MessageBox.Show("Синсин удалён: хуки убраны, автозапуск выключен.\nПапку " + Program.Dir + " можно удалить вручную.", "Синсин");
            return 0;
        }
    }

    // "Ask Xing": question box on top, answer underneath (scrolls, so long answers always fit).
    class AskWindow : Window
    {
        public event Action<string> Asked;
        readonly TextBox box; readonly TextBlock answer; readonly ScrollViewer scroll;

        public AskWindow()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight; Topmost = true; ResizeMode = ResizeMode.NoResize;
            var fg = new SolidColorBrush(Color.FromRgb(0xEE, 0xEB, 0xE3));
            box = new TextBox { Width = 300, FontSize = 13, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = fg, CaretBrush = Brushes.White };
            var hint = new TextBlock { Text = "спроси Синсина… (Enter, Esc — закрыть)", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x87, 0x80)), IsHitTestVisible = false };
            var g = new Grid(); g.Children.Add(hint); g.Children.Add(box);
            box.TextChanged += delegate { hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden; };
            answer = new TextBlock { Width = 300, FontSize = 13, Foreground = fg, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
            scroll = new ScrollViewer { Content = answer, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed,
                                        Margin = new Thickness(0, 10, 0, 0) };
            var stack = new StackPanel(); stack.Children.Add(g); stack.Children.Add(scroll);
            Content = new Border { Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x2C, 0x2B, 0x29)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                                   BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 10), Child = stack };
            box.KeyDown += (s, e) => {
                if (e.Key == Key.Escape) Close();
                if (e.Key == Key.Enter && box.Text.Trim().Length > 0) { var q = box.Text.Trim(); box.Text = ""; ShowAnswer("думаю…"); if (Asked != null) Asked(q); }
            };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Deactivated += delegate { try { Close(); } catch { } };
            Loaded += delegate { box.Focus(); };
        }

        public void ShowAnswer(string text)
        {
            answer.Text = text; scroll.Visibility = Visibility.Visible; scroll.ScrollToTop();
        }
    }

    // Settings window: plain WPF form over config.json.
    class SettingsWindow : Window
    {
        readonly Dictionary<string, object> cfg;
        readonly Stats stats;
        public event Action Saved;

        static readonly Dictionary<string, string> AccNames = new Dictionary<string, string> {
            { "auto", "авто (сезон, погода, помодоро)" }, { "none", "без аксессуара" }, { "santa", "новогодний колпак" }, { "glasses", "очки (ур. 2)" },
            { "scarf", "шарф (ур. 3)" }, { "headphones", "наушники (ур. 4)" }, { "crown", "корона (ур. 5 или 3 золотых банана)" }, { "umbrella", "зонтик" },
            { "cap", "кепка (магазин)" }, { "bow", "бантик (магазин)" }, { "flower", "цветок (магазин)" } };

        public static bool Unlocked(string acc, Stats s)
        {
            int lvl = Brain.Level(s.Xp);
            if (s.Owned != null && s.Owned.Contains(acc)) return true;
            switch (acc)
            {
                case "cap": case "bow": case "flower": return false;   // shop only
                case "glasses": return lvl >= 2;
                case "scarf": return lvl >= 3;
                case "headphones": return lvl >= 4;
                case "crown": return lvl >= 5 || s.GoldenBananas >= 3;
                default: return true;
            }
        }

        static string S(Dictionary<string, object> d, string k, string def) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : def; }
        static bool B(Dictionary<string, object> d, string k, bool def) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToBoolean(v) : def; }
        static double D(Dictionary<string, object> d, string k, double def) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToDouble(v) : def; }

        public SettingsWindow(Dictionary<string, object> cfg, Stats stats, List<Character> characters)
        {
            this.cfg = cfg; this.stats = stats;
            Title = "Синсин — настройки"; Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; FontSize = 13;
            var root = new StackPanel { Margin = new Thickness(20) };

            Func<string, TextBlock> head = t => new TextBlock { Text = t, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) };
            Func<string, UIElement, UIElement> row = (label, ctrl) => {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); g.ColumnDefinitions.Add(new ColumnDefinition());
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }; g.Children.Add(l);
                Grid.SetColumn(ctrl, 1); g.Children.Add(ctrl); return g;
            };

            int lvl = Brain.Level(stats.Xp);
            root.Children.Add(new TextBlock { Text = "Уровень " + lvl + " · опыт " + stats.Xp + " (до следующего: " + (Brain.XpForLevel(lvl + 1) - stats.Xp) + ") · золотых бананов: " + stats.GoldenBananas,
                                              Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });

            root.Children.Add(head("Персонаж"));
            var skin = new ComboBox();
            skin.Items.Add(new ComboBoxItem { Content = "Синсин (основной)", Tag = "monkey" });
            foreach (var c in characters) skin.Items.Add(new ComboBoxItem { Content = c.Name + (c.Author.Length > 0 ? " — " + c.Author : ""), Tag = c.Id });
            string curSkin = S(cfg, "skin", "monkey");
            foreach (ComboBoxItem it in skin.Items) if ((string)it.Tag == curSkin) skin.SelectedItem = it;
            if (skin.SelectedItem == null) skin.SelectedIndex = 0;
            root.Children.Add(row("Кто живёт рядом с Claude", skin));
            var plugHint = new TextBlock { Text = "Свои персонажи — папки в characters рядом с exe (character.json + PNG-ленты или GIF). Аксессуары носит только Синсин.",
                                           Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
            var open = new Button { Content = "Открыть папку персонажей", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += delegate { string p = Path.Combine(Program.Dir, "characters"); Directory.CreateDirectory(p); Process.Start("explorer.exe", "\"" + p + "\""); };
            root.Children.Add(plugHint); root.Children.Add(open);
            var acc = new ComboBox();
            foreach (var kv in AccNames) acc.Items.Add(new ComboBoxItem { Content = kv.Value, Tag = kv.Key, IsEnabled = Unlocked(kv.Key, stats) });
            string curAcc = S(cfg, "accessory", "auto");
            foreach (ComboBoxItem it in acc.Items) if ((string)it.Tag == curAcc) acc.SelectedItem = it;
            if (acc.SelectedItem == null) acc.SelectedIndex = 0;
            root.Children.Add(row("Аксессуар", acc));
            var size = new Slider { Minimum = 0.6, Maximum = 1.3, Value = D(cfg, "size", 1), TickFrequency = 0.1, IsSnapToTickEnabled = true };
            root.Children.Add(row("Размер", size));

            root.Children.Add(head("Поведение"));
            var sound = new CheckBox { Content = "8-битные звуки", IsChecked = B(cfg, "sound", false) };
            var health = new CheckBox { Content = "напоминать размяться и попить воды", IsChecked = B(cfg, "health", true) };
            var ai = new CheckBox { Content = "реплики от Haiku в конце задачи", IsChecked = B(cfg, "aiSummary", true) };
            var aiMin = new TextBox { Text = S(cfg, "aiMinSeconds", "20"), Width = 60, HorizontalAlignment = HorizontalAlignment.Left };
            var walks = new CheckBox { Content = "гулять по окну Claude и сидеть на заголовке", IsChecked = B(cfg, "walks", true) };
            var music = new CheckBox { Content = "качать головой под музыку (Spotify)", IsChecked = B(cfg, "music", true) };
            var eyes = new CheckBox { Content = "следить глазами за мышкой", IsChecked = B(cfg, "eyes", true) };
            var clicks = new CheckBox { Content = "пиксельные вспышки от кликов по экрану", IsChecked = B(cfg, "clickFx2", false) };
            var memory = new CheckBox { Content = "помнить проекты и задачи (локально, memory.json)", IsChecked = B(cfg, "memory", true) };
            var docker = new CheckBox { Content = "следить за контейнерами Docker", IsChecked = B(cfg, "docker", true) };
            root.Children.Add(sound); root.Children.Add(health); root.Children.Add(walks); root.Children.Add(music); root.Children.Add(eyes); root.Children.Add(clicks); root.Children.Add(memory); root.Children.Add(docker); root.Children.Add(ai);
            root.Children.Add(row("Звать Haiku, если задача дольше, сек", aiMin));

            root.Children.Add(head("Расписание (в простое)"));
            var weekdays = new CheckBox { Content = "только по будням", IsChecked = B(cfg, "scheduleWeekdaysOnly", true) };
            var home = new TextBox { Text = S(cfg, "homeAfter", "18:30"), Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
            var lunchFrom = new TextBox { Text = S(cfg, "lunchFrom", "13:00"), Width = 70 };
            var lunchTo = new TextBox { Text = S(cfg, "lunchTo", "14:30"), Width = 70, Margin = new Thickness(8, 0, 0, 0) };
            var lunch = new StackPanel { Orientation = Orientation.Horizontal }; lunch.Children.Add(lunchFrom); lunch.Children.Add(new TextBlock { Text = " — ", VerticalAlignment = VerticalAlignment.Center }); lunch.Children.Add(lunchTo);
            root.Children.Add(weekdays); root.Children.Add(row("Уходит домой после", home)); root.Children.Add(row("Обед", lunch));

            root.Children.Add(head("Погода"));
            var city = new TextBox { Text = S(cfg, "city", "") };
            root.Children.Add(row("Город (пусто — выкл.)", city));
            root.Children.Add(new TextBlock { Text = "Погода берётся с open-meteo.com по названию города: в дождь — зонтик, в мороз — шарф.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, FontSize = 12 });

            root.Children.Add(head("Система"));
            var autostart = new CheckBox { Content = "запускать вместе с Windows", IsChecked = Installer.Autostart };
            var keys = new TextBox { Text = S(cfg, "newChatKeys", "ctrl+n"), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
            root.Children.Add(autostart); root.Children.Add(row("Новый чат в Claude", keys));
            var sys = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var reinstall = new Button { Content = Installer.HooksInstalled() ? "Переустановить хуки" : "Установить хуки", Padding = new Thickness(10, 4, 10, 4) };
            reinstall.Click += delegate { try { Installer.RemoveHooks(); Installer.InstallHooks(); MessageBox.Show(this, "Хуки обновлены. Копия настроек — в папке backup.", "Синсин"); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } };
            var uninstall = new Button { Content = "Удалить Синсина…", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
            uninstall.Click += delegate {
                if (MessageBox.Show(this, "Убрать хуки и автозапуск и закрыть Синсина?", "Синсин", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                Installer.RemoveHooks(); Installer.SetAutostart(false); Application.Current.Shutdown();
            };
            sys.Children.Add(reinstall); sys.Children.Add(uninstall); root.Children.Add(sys);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            var save = new Button { Content = "Сохранить", IsDefault = true, Padding = new Thickness(16, 5, 16, 5) };
            var cancel = new Button { Content = "Отмена", IsCancel = true, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(8, 0, 0, 0) };
            save.Click += delegate {
                TimeSpan t;
                foreach (var tb in new[] { home, lunchFrom, lunchTo }) if (!TimeSpan.TryParse(tb.Text, out t)) { MessageBox.Show(this, "Время в формате ЧЧ:ММ, например 18:30"); tb.Focus(); return; }
                int sec; if (!int.TryParse(aiMin.Text, out sec) || sec < 0) { MessageBox.Show(this, "Секунды — целое число"); aiMin.Focus(); return; }
                cfg["skin"] = (string)((ComboBoxItem)skin.SelectedItem).Tag;
                cfg["accessory"] = (string)((ComboBoxItem)acc.SelectedItem).Tag;
                cfg["size"] = Math.Round(size.Value, 1);
                cfg["sound"] = sound.IsChecked == true; cfg["health"] = health.IsChecked == true;
                cfg["walks"] = walks.IsChecked == true; cfg["music"] = music.IsChecked == true; cfg["clickFx2"] = clicks.IsChecked == true; cfg["eyes"] = eyes.IsChecked == true; cfg["memory"] = memory.IsChecked == true; cfg["docker"] = docker.IsChecked == true; cfg["aiSummary"] = ai.IsChecked == true; cfg["aiMinSeconds"] = sec;
                cfg["scheduleWeekdaysOnly"] = weekdays.IsChecked == true; cfg["homeAfter"] = home.Text.Trim(); cfg["lunchFrom"] = lunchFrom.Text.Trim(); cfg["lunchTo"] = lunchTo.Text.Trim();
                cfg["city"] = city.Text.Trim(); cfg["newChatKeys"] = keys.Text.Trim().ToLowerInvariant();
                try { Installer.SetAutostart(autostart.IsChecked == true); } catch (Exception ex) { MessageBox.Show(this, "Автозапуск: " + ex.Message); }
                if (Saved != null) Saved();
                Close();
            };
            buttons.Children.Add(save); buttons.Children.Add(cancel); root.Children.Add(buttons);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = SystemParameters.WorkArea.Height - 80 };
        }
    }
}
