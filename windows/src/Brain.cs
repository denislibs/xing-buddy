// Xing's "brain": turns Claude Code hook payloads into mascot events, keeps daily stats, history, mood, XP,
// quests and achievements, and (optionally) asks Claude Haiku for in-character one-liners and answers.
// Hook mode: xing-pixel.exe --event   (reads the hook JSON from stdin, writes event.json + stats.json)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace XingPixel
{
    class DayStats
    {
        public int Tasks, Edits, Commands, Commits, Pushes, Errors, TestsGreen, Xp;
        public long LongestMs;
    }

    class Stats
    {
        public string Day = "";
        public int Tasks, Edits, Commands, Commits, Pushes, Tests, TestsGreen, Errors, ErrorStreak, MaxErrorStreak;
        public int TasksTotal, EditsTotal, CommitsTotal, PushesTotal, TestsGreenTotal, Bananas, GoldenBananas, Xp;
        public double Mood = 60;
        public long MoodAt, TurnStart;
        public List<string> Achievements = new List<string>();
        public Dictionary<string, string> AchievementDays = new Dictionary<string, string>();
        public Dictionary<string, DayStats> History = new Dictionary<string, DayStats>();
        public Dictionary<string, long> Sessions = new Dictionary<string, long>();     // session id -> last seen (ms)
        public Dictionary<string, int> Helpers = new Dictionary<string, int>();        // session id -> running subagents
        public string QuestDay = "", QuestId = "", WeekendGrumbleDay = "";
        public int QuestProgress; public bool QuestDone, QuestFailed;
        // easter-egg collection and the counters behind it
        public Dictionary<string, string> Stickers = new Dictionary<string, string>();   // id -> day unlocked
        public int TurnEdits, Conflicts, Pokes, Excursions, MusicMinutes, GameBest;
        public List<long> PushTimes = new List<long>();
        public List<string> LunchDays = new List<string>();
        public string SlotDay = ""; public int SlotSpins, Jackpots;
        // tamagotchi layer
        public double Hunger = 80, Clean = 90;      // 0..100, drift down over time
        public long CareAt;
        public List<string> Owned = new List<string>();   // shop purchases
        // Claude Code context of the last active session
        public int CtxPct; public long SessionTokens;
    }

    class ShopItem
    {
        public string Id, Kind, Title; public int Price; public bool Golden;
        public ShopItem(string id, string kind, string title, int price, bool golden) { Id = id; Kind = kind; Title = title; Price = price; Golden = golden; }
    }

    class Sticker { public string Id, Title, Hint, Pose; public Sticker(string id, string t, string h, string p) { Id = id; Title = t; Hint = h; Pose = p; } }

    // What Xing remembers per project (keyed by the working directory).
    class ProjectMemory { public string Name = "", LastPrompt = "", LastSummary = "", LastDay = ""; public long LastSeen; public int Tasks; }

    class Quest { public string Id, Title; public int Target; public Quest(string id, string t, int n) { Id = id; Title = t; Target = n; } }

    static class Brain
    {
        public static string Dir;
        public const string Persona =
            "Ты — Синсин, пухлая ворчливая, но добрая обезьянка-маскот, которая живёт рядом с окном Claude на компьютере программиста. " +
            "Это игровой персонаж: всегда отвечай от его лица, не называй себя Claude и не говори, что ты ИИ-модель. " +
            "Пиши по-русски, коротко, с юмором и обезьяньими словечками, без markdown, без эмодзи и без кавычек.";
        public const string LoginHint = "чтобы я отвечал, залогинься через терминал: claude → /login";
        public static readonly Quest[] Quests = {
            new Quest("commits3", "сделай 3 коммита", 3), new Quest("tasks5", "закрой 5 задач", 5), new Quest("green3", "3 зелёных прогона тестов", 3),
            new Quest("edits30", "30 правок", 30), new Quest("push2", "2 пуша", 2), new Quest("noerr", "4 задачи без единой ошибки", 4) };

        public static readonly Sticker[] StickerList = {
            new Sticker("night_commit", "ночной коммит", "закоммить что-нибудь между полуночью и 5 утра", "git_commit"),
            new Sticker("early_bird", "ранняя пташка", "начни работу до 7 утра", "milk"),
            new Sticker("speedrun", "спидран", "закрой задачу с правками быстрее 15 секунд", "run"),
            new Sticker("friday13", "пятница, 13-е", "поработай в пятницу 13-го", "error"),
            new Sticker("triple_push", "тройной пуш", "три пуша за 10 минут", "git_push"),
            new Sticker("conflict5", "мастер конфликтов", "переживи 5 конфликтов", "git_conflict"),
            new Sticker("answer42", "ответ на всё", "закрой 42-ю задачу", "think"),
            new Sticker("golden10", "банановый магнат", "собери 10 золотых бананов", "banana"),
            new Sticker("gamer", "геймер", "набери 300 очков в банановом раннере", "jump"),
            new Sticker("champion", "чемпион раннера", "набери 1000 очков", "done"),
            new Sticker("meloman", "меломан", "послушай музыку с Синсином час", "music"),
            new Sticker("traveler", "путешественник", "Синсин прогулялся 10 раз", "walk"),
            new Sticker("clicker", "кликер", "тыкни Синсина 100 раз", "love"),
            new Sticker("konami", "секретный танец", "кликни по Синсину 7 раз очень быстро", "secret"),
            new Sticker("banana_word", "банановое слово", "спроси Синсина про бананы", "banana"),
            new Sticker("noodle", "лапшичник", "пообедай с Синсином 5 разных дней", "lunch"),
            new Sticker("jackpot", "джекпот", "выбей 777 в автомате", "slot") };

        public static readonly ShopItem[] ShopItems = {
            new ShopItem("cap", "acc", "кепка", 30, false), new ShopItem("bow", "acc", "бантик", 30, false), new ShopItem("flower", "acc", "цветок за ухом", 40, false),
            new ShopItem("glasses", "acc", "очки (или ур. 2)", 60, false), new ShopItem("scarf", "acc", "шарф (или ур. 3)", 60, false),
            new ShopItem("headphones", "acc", "наушники (или ур. 4)", 80, false), new ShopItem("crown", "acc", "корона (или ур. 5)", 3, true),
            new ShopItem("bg_office", "backdrop", "фон: офис", 50, false), new ShopItem("bg_forest", "backdrop", "фон: лес", 60, false),
            new ShopItem("bg_beach", "backdrop", "фон: пляж", 80, false), new ShopItem("bg_space", "backdrop", "фон: космос", 2, true),
            new ShopItem("ph_pirate", "phrases", "фразы: пират", 40, false), new ShopItem("ph_polite", "phrases", "фразы: интеллигент", 40, false),
            new ShopItem("ph_gop", "phrases", "фразы: пацанские", 40, false), new ShopItem("trick_secret", "trick", "трюк: секретный танец по клику", 2, true) };

        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        static string P(string f) { return Path.Combine(Dir, f); }
        public static long Ms() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
        public static string Today() { return DateTime.Now.ToString("yyyy-MM-dd"); }
        public static int Level(int xp) { return 1 + (int)Math.Floor(Math.Sqrt(xp / 40.0)); }
        public static int XpForLevel(int lvl) { return (lvl - 1) * (lvl - 1) * 40; }

        // ---------- progression: a title for every level and what it unlocks ----------
        static readonly string[] RankTitles = { "", "Малыш", "Стажёр", "Джун", "Джун+", "Мидл", "Мидл+", "Сеньор", "Сеньор+", "Тимлид", "Гуру",
            "Мудрец", "Архитектор", "Шаман кода", "Магистр", "Легенда", "Хранитель репо", "Повелитель мержей", "Мастер дзена", "Почти бог", "Бессмертный" };
        public const int MaxRank = 20;
        public static string RankTitle(int lvl) { return lvl <= MaxRank ? RankTitles[Math.Max(1, lvl)] : "Бессмертный +" + (lvl - MaxRank); }
        public static string Unlocks(int lvl)
        {
            switch (lvl)
            {
                case 1: return "малыш с кудряшкой";
                case 2: return "очки · трюк «люблю»";
                case 3: return "подрос (подросток) · бейдж стажёра · шарф · трюк «ребейз»";
                case 4: return "белый пояс · наушники · трюк «бег»";
                case 5: return "жёлтый пояс · корона · трюк «готово»";
                case 6: return "взрослый Синсин · зелёный пояс · трюк «музыка»";
                case 7: return "синий пояс · золотой бейдж";
                case 8: return "коричневый пояс · трюк «прыжок»";
                case 9: return "чёрный пояс";
                case 10: return "Гуру: седина, борода, пояс с золотыми полосками · трюк «секретный танец»";
                case 11: return "мудрые цитаты по клику";
                case 12: return "аура: вокруг кружат искры";
                case 13: return "аура ярче (3 искры)";
                case 15: return "Легенда: золото в шерсти";
                case 17: return "аура ещё ярче (4 искры)";
                case 20: return "нимб";
                default: return lvl > MaxRank ? "просто уважение" : "+опыт к следующему званию";
            }
        }

        // ---------- files ----------
        static void Swap(string tmp, string dst)
        {
            for (int i = 0; i < 5; i++)
            {
                try { if (File.Exists(dst)) File.Replace(tmp, dst, null); else File.Move(tmp, dst); return; }
                catch { Thread.Sleep(20); }
            }
        }

        public static Stats LoadStats()
        {
            Stats s = null;
            try { s = Json.Deserialize<Stats>(File.ReadAllText(P("stats.json"))); } catch { }
            if (s == null) s = new Stats();
            if (s.Achievements == null) s.Achievements = new List<string>();
            if (s.AchievementDays == null) s.AchievementDays = new Dictionary<string, string>();
            if (s.History == null) s.History = new Dictionary<string, DayStats>();
            if (s.Sessions == null) s.Sessions = new Dictionary<string, long>();
            if (s.Helpers == null) s.Helpers = new Dictionary<string, int>();
            if (s.Stickers == null) s.Stickers = new Dictionary<string, string>();
            if (s.PushTimes == null) s.PushTimes = new List<long>();
            if (s.LunchDays == null) s.LunchDays = new List<string>();
            if (s.Owned == null) s.Owned = new List<string>();
            string today = Today();
            if (s.Day != today)
            {
                s.Day = today; s.Tasks = s.Edits = s.Commands = s.Commits = s.Pushes = s.Tests = s.TestsGreen = s.Errors = s.ErrorStreak = s.MaxErrorStreak = 0;
            }
            if (s.QuestDay != today)
            {
                // Same quest for everyone on a given day; rotates daily.
                var q = Quests[(int)(DateTime.Today.Ticks / TimeSpan.TicksPerDay % Quests.Length)];
                s.QuestDay = today; s.QuestId = q.Id; s.QuestProgress = 0; s.QuestDone = s.QuestFailed = false;
            }
            long now = Ms();
            if (s.MoodAt > 0) s.Mood = 60 + (s.Mood - 60) * Math.Exp(-(now - s.MoodAt) / 3600000.0 / 3);   // drifts back to 60 (~3h)
            s.MoodAt = now;
            // Hunger and cleanliness drift down slowly (gentle: no penalties beyond looks and mood).
            if (s.CareAt > 0) { double hrs = (now - s.CareAt) / 3600000.0; s.Hunger = Math.Max(0, s.Hunger - 4 * hrs); s.Clean = Math.Max(0, s.Clean - 1.5 * hrs); }
            s.CareAt = now;
            foreach (var k in s.Sessions.Keys.ToList()) if (now - s.Sessions[k] > 30 * 60 * 1000) { s.Sessions.Remove(k); s.Helpers.Remove(k); }
            return s;
        }

        public static void SaveStats(Stats s)
        {
            string tmp = P("stats-" + Process.GetCurrentProcess().Id + ".tmp");
            File.WriteAllText(tmp, Json.Serialize(s));
            Swap(tmp, P("stats.json"));
        }

        // Read-modify-write under a machine-wide lock (hooks fire as separate processes).
        public static Stats WithStats(Action<Stats> change)
        {
            using (var m = new Mutex(false, "Local\\XingPixelStats"))
            {
                bool got = false;
                try { got = m.WaitOne(3000); } catch (AbandonedMutexException) { got = true; }
                try { var s = LoadStats(); change(s); SaveStats(s); return s; }
                finally { if (got) m.ReleaseMutex(); }
            }
        }

        public static void WriteEvent(Dictionary<string, object> e)
        {
            e["ts"] = Ms();
            string tmp = P("event-" + Process.GetCurrentProcess().Id + ".tmp");
            File.WriteAllText(tmp, Json.Serialize(e));
            Swap(tmp, P("event.json"));
        }

        public static Dictionary<string, object> Config()
        {
            try { return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(P("config.json"))) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        public static int ActiveSessions(Stats s) { long now = Ms(); return s.Sessions.Values.Count(t => now - t < 10 * 60 * 1000); }
        public static int RunningHelpers(Stats s) { return s.Helpers.Values.Sum(); }
        public static Quest TodayQuest(Stats s) { return Quests.FirstOrDefault(q => q.Id == s.QuestId) ?? Quests[0]; }

        // ---------- hook mode ----------
        static string Str(Dictionary<string, object> d, string k)
        {
            object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }
        public static string Short(string s, int n) { s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }
        static string Base(string path) { try { return Path.GetFileName(path.TrimEnd('/', '\\')); } catch { return path; } }
        static bool IsNight() { int h = DateTime.Now.Hour; return h >= 23 || h < 5; }

        static readonly Regex TestRx = new Regex(@"\b(npm|pnpm|yarn|bun)\s+(run\s+)?test|\bpytest\b|\bjest\b|\bvitest\b|\bdotnet\s+test|\bgo\s+test|\bcargo\s+test|\bphpunit\b|\bmocha\b|\bplaywright\s+test", RegexOptions.IgnoreCase);
        static readonly Regex GitRx = new Regex(@"\bgit\s+(?:-[-\w=./]+\s+)*([a-z][\w-]*)", RegexOptions.IgnoreCase);
        static readonly Regex GhRx = new Regex(@"\bgh\s+(pr|run|issue|release|workflow)\s+([a-z][\w-]*)", RegexOptions.IgnoreCase);
        static readonly Regex BuildRx = new Regex(@"\b(build|compile|csc|tsc|webpack|gradle|mvn|msbuild|make)\b", RegexOptions.IgnoreCase);
        static readonly Regex InstallRx = new Regex(@"\b(npm|pnpm|yarn|pip|bun|composer)\s+(i|install|add)\b", RegexOptions.IgnoreCase);

        // git/gh command -> (pose, line while running)
        static string[] GitInfo(string cmd)
        {
            var gh = GhRx.Match(cmd);
            if (gh.Success)
            {
                string a = gh.Groups[1].Value.ToLower(), b = gh.Groups[2].Value.ToLower();
                if (a == "pr" && b == "create") return new[] { "git_pr", "отправляю PR…", "gh-pr-create" };
                if (a == "pr" && b == "merge") return new[] { "git_merge", "мержу PR…", "gh-pr-merge" };
                if (a == "pr" && b == "checks" || a == "run" && (b == "watch" || b == "view")) return new[] { "git_ci", "жду CI…", "gh-ci" };
                if (a == "pr" && b == "checkout") return new[] { "git_branch", "беру ветку PR…", "gh-checkout" };
                return new[] { "git_log", "смотрю " + a + "…", "gh-other" };
            }
            var m = GitRx.Match(cmd);
            if (!m.Success) return null;
            switch (m.Groups[1].Value.ToLower())
            {
                case "commit": return new[] { "git_commit", "коммичу…", "commit" };
                case "push": return new[] { "git_push", "пушу в origin…", "push" };
                case "pull": return new[] { "git_pull", "тяну изменения…", "pull" };
                case "clone": return new[] { "git_pull", "клонирую…", "clone" };
                case "fetch": return new[] { "git_fetch", "смотрю, что нового…", "fetch" };
                case "merge": return new[] { "git_merge", "сливаю ветки…", "merge" };
                case "rebase": return new[] { "git_rebase", "ребейзю…", "rebase" };
                case "checkout": case "switch": return new[] { "git_branch", "переключаю ветку…", "checkout" };
                case "branch": return new[] { "git_branch", "ращу ветку…", "branch" };
                case "stash": return new[] { "git_stash", "прячу в заначку…", "stash" };
                case "log": case "reflog": case "show": case "blame": case "shortlog": return new[] { "git_log", "листаю историю…", "log" };
                case "diff": return new[] { "git_diff", "смотрю дифф…", "diff" };
                case "status": return new[] { "git_status", "что тут у нас…", "status" };
                case "add": return new[] { "git_status", "добавляю в индекс…", "add" };
                case "reset": case "restore": case "clean": case "revert": return new[] { "git_reset", "подметаю…", "reset" };
                case "tag": return new[] { "git_tag", "вешаю тег…", "tag" };
                case "cherry-pick": return new[] { "git_cherry", "срываю вишенку…", "cherry" };
                default: return new[] { "bash", "git " + m.Groups[1].Value + "…", "git-other" };
            }
        }

        public static int HandleEvent()
        {
            if (Environment.GetEnvironmentVariable("XING_NESTED") == "1") return 0;   // our own Haiku calls
            string raw;
            using (var r = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false))) raw = r.ReadToEnd();
            Dictionary<string, object> d;
            try { d = Json.Deserialize<Dictionary<string, object>>(raw); } catch { return 0; }
            if (d == null) return 0;

            string hook = Str(d, "hook_event_name");
            int ctxPct = -1; long sessionTokens = 0;
            if (hook == "Stop" || hook == "UserPromptSubmit") TranscriptUsage(Str(d, "transcript_path"), out ctxPct, out sessionTokens);
            Dictionary<string, object> ev = null;
            var snapshot = WithStats(s => {
                ev = Decide(d, s);
                if (ctxPct >= 0) { s.CtxPct = ctxPct; s.SessionTokens = sessionTokens; }
            });
            if (ev != null && ctxPct >= 0)
            {
                ev["ctxPct"] = ctxPct; ev["sessionTokens"] = sessionTokens;
                if (hook == "Stop" && ctxPct >= 80) { ev["line"] = Convert.ToString(ev.ContainsKey("line") ? ev["line"] : "готово") + "\nконтекст " + ctxPct + "%: я полный, сделай /compact"; ev["pose"] = "full"; ev["seconds"] = 5.0; }
                if (hook == "UserPromptSubmit" && ctxPct >= 90) { ev["line"] = "контекст " + ctxPct + "%! сделай /compact, а то забуду начало"; ev["pose"] = "full"; ev["seconds"] = 4.0; }
            }
            // Plan mode: he sits over a blueprint instead of the usual work poses.
            if (ev != null && Str(d, "permission_mode") == "plan" && ev.ContainsKey("state"))
            {
                string st = Convert.ToString(ev["state"]); object sec; ev.TryGetValue("seconds", out sec);
                if ((st == "thinking" || st == "working") && (sec == null || Convert.ToDouble(sec) <= 0))
                {
                    ev["pose"] = "plan";
                    string ln = ev.ContainsKey("line") ? Convert.ToString(ev["line"]) : "";
                    if (ln == "думаю…" || ln == "кручу братишку" || ln.Length == 0) ev["line"] = "черчу план…";
                }
            }
            if (ev != null)
            {
                ev["mood"] = Math.Round(snapshot.Mood);
                ev["turnStart"] = snapshot.TurnStart;
                ev["sessions"] = ActiveSessions(snapshot);
                ev["helpers"] = RunningHelpers(snapshot);
                ev["xp"] = snapshot.Xp;
                WriteEvent(ev);
            }
            if (Str(d, "hook_event_name") == "Stop") Summarize(d, snapshot);
            return 0;
        }

        static Dictionary<string, object> E(string state, string pose, string line, double seconds)
        {
            var e = new Dictionary<string, object>();
            if (state != null) e["state"] = state;
            if (pose != null) e["pose"] = pose;
            if (line != null) e["line"] = line;
            e["seconds"] = seconds;
            return e;
        }

        static void Mood(Stats s, double delta) { s.Mood = Math.Max(0, Math.Min(100, s.Mood + delta)); }

        static DayStats Hist(Stats s)
        {
            DayStats h; if (!s.History.TryGetValue(s.Day, out h)) { h = new DayStats(); s.History[s.Day] = h; }
            // keep ~8 weeks
            if (s.History.Count > 60) foreach (var k in s.History.Keys.OrderBy(x => x).Take(s.History.Count - 60).ToList()) s.History.Remove(k);
            return h;
        }

        public static void GiveXp(Stats s, Dictionary<string, object> e, int amount) { AddXp(s, e, amount); }
        static void AddXp(Stats s, Dictionary<string, object> e, int amount)
        {
            int before = Level(s.Xp); s.Xp += amount; Hist(s).Xp += amount;
            int after = Level(s.Xp);
            if (after > before && e != null)
            {
                string prev = e.ContainsKey("line") ? (string)e["line"] + "\n" : "";
                e["line"] = prev + "уровень " + after + "! теперь я " + RankTitle(after) + "\nоткрыл: " + Unlocks(after);
                e["pose"] = "levelup"; e["seconds"] = 7.0; e["sound"] = "level"; e["levelUp"] = after;
                e["notify"] = "Синсин: уровень " + after + " — " + RankTitle(after) + ". Открыто: " + Unlocks(after);
            }
        }

        // Unlocks an easter-egg sticker (once); the event line announces it.
        public static bool Sticker(Stats s, Dictionary<string, object> e, string id, bool cond)
        {
            if (!cond || s.Stickers.ContainsKey(id)) return false;
            s.Stickers[id] = s.Day;
            var st = StickerList.FirstOrDefault(x => x.Id == id);
            if (e != null && st != null) { e["line"] = "пасхалка: " + st.Title + "!"; e["pose"] = "secret"; e["seconds"] = 4.0; e["sound"] = "level"; e["sticker"] = id; }
            return true;
        }

        // ---------- memory ----------
        public static Dictionary<string, ProjectMemory> LoadMemory()
        {
            try { return Json.Deserialize<Dictionary<string, ProjectMemory>>(File.ReadAllText(P("memory.json"))) ?? new Dictionary<string, ProjectMemory>(); }
            catch { return new Dictionary<string, ProjectMemory>(); }
        }
        static void SaveMemory(Dictionary<string, ProjectMemory> m)
        {
            string tmp = P("memory-" + Process.GetCurrentProcess().Id + ".tmp");
            File.WriteAllText(tmp, Json.Serialize(m)); Swap(tmp, P("memory.json"));
        }
        static bool MemoryOn() { object v; return !Config().TryGetValue("memory", out v) || Convert.ToBoolean(v); }
        public static void Remember(string cwd, Action<ProjectMemory> change)
        {
            if (string.IsNullOrEmpty(cwd) || !MemoryOn()) return;
            var m = LoadMemory(); ProjectMemory p;
            if (!m.TryGetValue(cwd, out p)) { p = new ProjectMemory { Name = Base(cwd) }; m[cwd] = p; }
            change(p); p.LastSeen = Ms(); p.LastDay = Today();
            if (m.Count > 30) foreach (var k in m.OrderBy(x => x.Value.LastSeen).Take(m.Count - 30).Select(x => x.Key).ToList()) m.Remove(k);
            SaveMemory(m);
        }

        static bool Achieve(Stats s, Dictionary<string, object> e, string id, bool cond, string title)
        {
            if (!cond || s.Achievements.Contains(id)) return false;
            s.Achievements.Add(id); s.AchievementDays[id] = s.Day;
            e["line"] = "ачивка: " + title; e["pose"] = "done"; e["seconds"] = 4.0; e["achievement"] = title; e["sound"] = "level";
            return true;
        }

        // Advances today's quest; completion pays out a golden banana.
        static void QuestStep(Stats s, Dictionary<string, object> e, string metric, int delta)
        {
            if (s.QuestDone || s.QuestFailed) return;
            if (s.QuestId == "noerr" && metric == "error") { s.QuestFailed = true; return; }
            string want = s.QuestId == "commits3" ? "commit" : s.QuestId == "tasks5" || s.QuestId == "noerr" ? "task" : s.QuestId == "green3" ? "green" : s.QuestId == "edits30" ? "edit" : s.QuestId == "push2" ? "push" : "";
            if (metric != want) return;
            s.QuestProgress += delta;
            var q = TodayQuest(s);
            if (s.QuestProgress >= q.Target)
            {
                s.QuestDone = true; s.GoldenBananas++; Mood(s, 10);
                if (e != null) { e["line"] = "квест выполнен: " + q.Title + "! золотой банан"; e["pose"] = "banana"; e["seconds"] = 4.0; e["sound"] = "level"; }
                AddXp(s, e, 30);
            }
        }

        static Dictionary<string, object> Decide(Dictionary<string, object> d, Stats s)
        {
            string name = Str(d, "hook_event_name"), tool = Str(d, "tool_name"), session = Str(d, "session_id");
            var input = d.ContainsKey("tool_input") ? d["tool_input"] as Dictionary<string, object> : null;
            string cmd = Str(input, "command");
            if (session.Length > 0) s.Sessions[session] = Ms();
            var h = Hist(s);
            Dictionary<string, object> e;

            switch (name)
            {
                case "UserPromptSubmit":
                {
                    s.TurnStart = Ms(); s.TurnEdits = 0;
                    string prompt = Str(d, "prompt");
                    if (prompt.Length > 0 && !prompt.StartsWith("/")) Remember(Str(d, "cwd"), p => p.LastPrompt = Short(prompt, 200));
                    var dow = DateTime.Now.DayOfWeek;
                    if ((dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) && s.WeekendGrumbleDay != s.Day)
                    {
                        s.WeekendGrumbleDay = s.Day;
                        e = E("thinking", "think", "ты чего в выходной работаешь? ну ладно…", 0);
                    }
                    else e = E("thinking", null, IsNight() ? "ночная смена? ну ладно…" : "думаю…", 0);
                    Achieve(s, e, "night", IsNight(), "ночная смена");
                    Sticker(s, e, "early_bird", DateTime.Now.Hour >= 4 && DateTime.Now.Hour < 7);
                    Sticker(s, e, "friday13", DateTime.Now.DayOfWeek == DayOfWeek.Friday && DateTime.Now.Day == 13);
                    return e;
                }

                case "PreToolUse":
                    if (tool == "AskUserQuestion" || tool == "ExitPlanMode")
                    {
                        e = E("waiting", null, "твой ход", 0); e["notify"] = "Синсин ждёт твоего ответа"; return e;
                    }
                    if (tool == "Edit" || tool == "Write" || tool == "MultiEdit" || tool == "NotebookEdit")
                    {
                        s.Edits++; s.EditsTotal++; h.Edits++; s.TurnEdits++;
                        e = E("working", "type", "правлю " + Short(Base(Str(input, "file_path") + Str(input, "notebook_path")), 22), 0);
                        if (s.Edits % 5 == 0) AddXp(s, e, 1);
                        QuestStep(s, e, "edit", 1);
                        Achieve(s, e, "edits50d-" + s.Day, s.Edits == 50, "50 правок за день");
                        Achieve(s, e, "edits100", s.EditsTotal >= 100, "100 правок");
                        Achieve(s, e, "edits1000", s.EditsTotal >= 1000, "1000 правок, легенда");
                        return e;
                    }
                    if (tool == "Bash" || tool == "PowerShell")
                    {
                        s.Commands++; h.Commands++;
                        if (TestRx.IsMatch(cmd)) { s.Tests++; return E("working", "work", "гоняю тесты…", 0); }
                        var gi = GitInfo(cmd);
                        if (gi != null) return E("working", gi[0], gi[1], 0);
                        if (InstallRx.IsMatch(cmd)) return E("working", "bash", "ставлю пакеты…", 0);
                        if (BuildRx.IsMatch(cmd)) return E("working", "bash", "собираю…", 0);
                        return E("working", "bash", "$ " + Short(cmd, 20), 0);
                    }
                    if (tool == "Read") return E("working", "read", "читаю " + Short(Base(Str(input, "file_path")), 22), 0);
                    if (tool == "Glob" || tool == "Grep") return E("working", "read", "ищу " + Short(Str(input, "pattern"), 18), 0);
                    if (tool == "WebSearch") return E("working", "read", "гуглю: " + Short(Str(input, "query"), 20), 0);
                    if (tool == "WebFetch") return E("working", "read", "читаю сайт…", 0);
                    if (tool == "Agent" || tool == "Task")
                    {
                        int n; s.Helpers.TryGetValue(session, out n); s.Helpers[session] = Math.Min(8, n + 1);
                        return E("working", "run", "зову помощников (" + RunningHelpers(s) + ")", 0);
                    }
                    if (tool == "TodoWrite") return E("working", "read", "планирую…", 0);
                    if (tool.StartsWith("mcp__"))
                    {
                        var parts = tool.Split(new[] { "__" }, StringSplitOptions.None);
                        return E("working", "work", "дёргаю " + Short(parts.Length > 1 ? parts[1] : tool, 18), 0);
                    }
                    return E("working", "work", "кручу братишку", 0);

                case "PostToolUse":
                    s.ErrorStreak = 0; Mood(s, 0.3);
                    if (tool == "Agent" || tool == "Task") { int n; s.Helpers.TryGetValue(session, out n); s.Helpers[session] = Math.Max(0, n - 1); return E("thinking", "happy", "помощник вернулся", 2); }
                    if (tool == "Bash" || tool == "PowerShell")
                    {
                        if (TestRx.IsMatch(cmd))
                        {
                            s.TestsGreen++; s.TestsGreenTotal++; h.TestsGreen++; Mood(s, 3);
                            e = E("thinking", "happy", "тесты зелёные!", 2.5); e["sound"] = "success";
                            AddXp(s, e, 3); QuestStep(s, e, "green", 1);
                            Achieve(s, e, "green10", s.TestsGreenTotal >= 10, "10 зелёных прогонов");
                            return e;
                        }
                        var gi = GitInfo(cmd);
                        if (gi != null)
                        {
                            switch (gi[2])
                            {
                                case "commit":
                                    s.Commits++; s.CommitsTotal++; h.Commits++; Mood(s, 4);
                                    e = E("thinking", "git_commit", "закоммитил!", 3); e["sound"] = "success";
                                    AddXp(s, e, 5); QuestStep(s, e, "commit", 1);
                                    Achieve(s, e, "commit-" + s.Day, s.Commits == 1, "первый коммит дня");
                                    Achieve(s, e, "commits100", s.CommitsTotal >= 100, "100 коммитов");
                                    Sticker(s, e, "night_commit", DateTime.Now.Hour < 5);
                                    return e;
                                case "push":
                                    s.Pushes++; s.PushesTotal++; h.Pushes++; Mood(s, 4);
                                    e = E("thinking", "git_push", "запушил! улетело в origin", 3); e["sound"] = "success";
                                    AddXp(s, e, 5); QuestStep(s, e, "push", 1);
                                    Achieve(s, e, "push1", s.PushesTotal >= 1, "первый пуш");
                                    Achieve(s, e, "push50", s.PushesTotal >= 50, "50 пушей");
                                    s.PushTimes.Add(Ms()); s.PushTimes.RemoveAll(t => Ms() - t > 10 * 60 * 1000);
                                    Sticker(s, e, "triple_push", s.PushTimes.Count >= 3);
                                    return e;
                                case "pull": case "clone": return E("thinking", "git_pull", "свежак подтянут", 2.5);
                                case "merge": case "gh-pr-merge": Mood(s, 3); e = E("thinking", "done", "слил!", 3); AddXp(s, e, 4); Achieve(s, e, "merge1", true, "первый мерж"); return e;
                                case "rebase": return E("thinking", "happy", "история ровная как банан", 2.5);
                                case "gh-pr-create": Mood(s, 5); e = E("thinking", "love", "PR улетел!", 3); AddXp(s, e, 8); Achieve(s, e, "pr1", true, "первый PR"); return e;
                                case "tag": return E("thinking", "git_tag", "тег повешен", 2);
                                case "stash": return E("thinking", "git_stash", "в заначке", 2);
                                case "cherry": return E("thinking", "git_cherry", "вишенка сорвана", 2);
                            }
                        }
                    }
                    return E("thinking", null, "думаю…", 0);

                case "PostToolUseFailure":
                {
                    if (tool == "Agent" || tool == "Task") { int n; s.Helpers.TryGetValue(session, out n); s.Helpers[session] = Math.Max(0, n - 1); }
                    s.Errors++; s.ErrorStreak++; h.Errors++; s.MaxErrorStreak = Math.Max(s.MaxErrorStreak, s.ErrorStreak); Mood(s, -6);
                    QuestStep(s, null, "error", 1);
                    string err = Str(d, "error");
                    var gi = (tool == "Bash" || tool == "PowerShell") ? GitInfo(cmd) : null;
                    bool conflict = err.IndexOf("CONFLICT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    (gi != null && (gi[2] == "merge" || gi[2] == "rebase" || gi[2] == "pull" || gi[2] == "cherry" || gi[2] == "stash"));
                    if (conflict)
                    {
                        s.Conflicts++;
                        e = E("error", "git_conflict", "конфликт! кто трогал мой банан?!", 0); e["sound"] = "error";
                        Sticker(s, e, "conflict5", s.Conflicts >= 5);
                        return e;
                    }
                    if (gi != null && gi[2] == "push") { e = E("error", null, "push отклонили… надо подтянуть", 0); e["sound"] = "error"; return e; }
                    string line = s.ErrorStreak >= 10 ? "всё, я в отпуск" : s.ErrorStreak >= 3 ? "да что ж такое…" :
                                  TestRx.IsMatch(cmd) ? "тесты упали…" : "ой…";
                    e = E("error", null, line, 0); e["sound"] = "error";
                    return e;
                }

                case "PermissionRequest":
                    e = E("waiting", null, "можно? жду разрешения", 0); e["notify"] = "Claude ждёт разрешения"; return e;

                case "Notification":
                {
                    string msg = Str(d, "message");
                    e = E("waiting", null, "твой ход", 0); e["notify"] = msg.Length > 0 ? msg : "Claude ждёт тебя"; return e;
                }

                case "Stop":
                {
                    s.Helpers.Remove(session);
                    s.Tasks++; s.TasksTotal++; s.Bananas++; h.Tasks++; Mood(s, 8);
                    long dur = s.TurnStart > 0 ? Ms() - s.TurnStart : 0;
                    if (dur > h.LongestMs) h.LongestMs = dur;
                    e = E("success", null, dur > 60000 ? "готово за " + Clock(dur) + "!" : "готово!", 0); e["sound"] = "success";
                    AddXp(s, e, 10);
                    QuestStep(s, e, "task", 1);
                    bool survived = s.MaxErrorStreak >= 5 && s.ErrorStreak == 0;
                    Achieve(s, e, "tasks10-" + s.Day, s.Tasks == 10, "трудяга: 10 задач за день");
                    Achieve(s, e, "survivor", survived, "выжил после 5 ошибок подряд");
                    Achieve(s, e, "tasks100", s.TasksTotal >= 100, "100 задач");
                    Achieve(s, e, "marathon", dur > 30 * 60 * 1000, "марафон: задача дольше 30 минут");
                    Sticker(s, e, "speedrun", dur > 0 && dur < 15000 && s.TurnEdits > 0);
                    Sticker(s, e, "answer42", s.TasksTotal == 42);
                    Sticker(s, e, "golden10", s.GoldenBananas >= 10);
                    Remember(Str(d, "cwd"), p => p.Tasks++);
                    return e;
                }

                case "StopFailure":
                    s.Helpers.Remove(session);
                    Mood(s, -4);
                    e = E("error", null, "API прилёг…", 0); e["sound"] = "error"; return e;
            }
            return null;
        }

        // Context fill of the session = prompt size of its latest assistant message; spend = all tokens across messages.
        // A message is written as several lines (one per content block) sharing an id and usage, so count each id once.
        static readonly Regex MsgIdRx = new Regex("\"id\":\"(msg_[A-Za-z0-9_-]+)\""), UsageRx = new Regex("\"(input_tokens|cache_read_input_tokens|cache_creation_input_tokens|output_tokens)\":(\\d+)");
        public static void TranscriptUsage(string path, out int pct, out long total)
        {
            pct = -1; total = 0;
            try
            {
                if (path.Length == 0 || !File.Exists(path)) return;
                var seen = new Dictionary<string, long>(); long lastCtx = 0;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        // Only the top-level message.usage counts: it also nests an "iterations" copy of the same numbers.
                        if (line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0 || line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) continue;
                        Dictionary<string, object> o; try { o = Json.Deserialize<Dictionary<string, object>>(line); } catch { continue; }
                        object sc; if (o.TryGetValue("isSidechain", out sc) && Convert.ToBoolean(sc)) continue;
                        var msg = o.ContainsKey("message") ? o["message"] as Dictionary<string, object> : null;
                        var usage = msg != null && msg.ContainsKey("usage") ? msg["usage"] as Dictionary<string, object> : null;
                        if (usage == null) continue;
                        Func<string, long> n = key => { object v; return usage.TryGetValue(key, out v) && v != null ? Convert.ToInt64(v) : 0; };
                        long sum = n("input_tokens") + n("cache_read_input_tokens") + n("cache_creation_input_tokens") + n("output_tokens");
                        seen[Str(msg, "id").Length > 0 ? Str(msg, "id") : Guid.NewGuid().ToString()] = sum; lastCtx = sum;
                    }
                }
                total = seen.Values.Sum();
                if (lastCtx == 0) return;
                long window = ContextWindow();
                if (lastCtx > window) window = 1000000;
                pct = (int)Math.Min(100, Math.Round(100.0 * lastCtx / window));
            }
            catch { }
        }

        // Config override, else 1M when the Claude Code model is a [1m] one, else 200k.
        static long ContextWindow()
        {
            object v; var cfg = Config();
            if (cfg.TryGetValue("contextWindow", out v)) { long w; if (long.TryParse(Convert.ToString(v), out w) && w > 0) return w; }
            try
            {
                string s = File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"));
                var m = Regex.Match(s, "\"model\"\\s*:\\s*\"([^\"]*)\"");
                if (m.Success && m.Groups[1].Value.IndexOf("1m", StringComparison.OrdinalIgnoreCase) >= 0) return 1000000;
            }
            catch { }
            return 200000;
        }

        public static string Tokens(long n) { return n >= 1000000 ? (n / 1000000.0).ToString("0.0") + "M" : n >= 1000 ? (n / 1000) + "k" : n.ToString(); }

        public static string Clock(long ms) { var t = TimeSpan.FromMilliseconds(ms); return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00"); }

        // ---------- Haiku ----------
        static void Summarize(Dictionary<string, object> d, Stats s)
        {
            var cfg = Config();
            object v;
            bool ai = !cfg.TryGetValue("aiSummary", out v) || Convert.ToBoolean(v);
            double minSec = cfg.TryGetValue("aiMinSeconds", out v) ? Convert.ToDouble(v) : 20;
            long dur = s.TurnStart > 0 ? Ms() - s.TurnStart : 0;
            if (dur < minSec * 1000) return;

            string text = Str(d, "last_assistant_message");
            if (text.Length == 0) text = LastAssistantText(Str(d, "transcript_path"));
            string line = null;
            if (ai && text.Length > 0)
            {
                string err;
                line = AskClaude("Одной фразой до 70 символов перескажи, что сделано:\n" + Short(text, 1500), 45000, out err);
                if (line == null) Program.Log("summary: " + err);
            }
            if (line == null)
            {
                var bits = new List<string> { "готово за " + Clock(dur) };
                if (s.Edits > 0) bits.Add("правок за день: " + s.Edits);
                if (s.TestsGreen > 0) bits.Add("тесты зелёные");
                line = string.Join(" · ", bits);
            }
            WriteEvent(E(null, null, Short(line, 90), 7));
            Remember(Str(d, "cwd"), p => p.LastSummary = Short(line, 160));
        }

        static string LastAssistantText(string transcript)
        {
            try
            {
                if (transcript.Length == 0 || !File.Exists(transcript)) return "";
                string[] lines;
                using (var fs = new FileStream(transcript, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(fs, Encoding.UTF8)) lines = r.ReadToEnd().Split('\n');
                for (int i = lines.Length - 1; i >= 0 && i >= lines.Length - 400; i--)
                {
                    if (!lines[i].Contains("\"assistant\"")) continue;
                    var o = Json.Deserialize<Dictionary<string, object>>(lines[i]);
                    if (Str(o, "type") != "assistant") continue;
                    var msg = o["message"] as Dictionary<string, object>;
                    var content = msg != null && msg.ContainsKey("content") ? msg["content"] as System.Collections.ArrayList : null;
                    if (content == null) continue;
                    var sb = new StringBuilder();
                    foreach (var c in content) { var cd = c as Dictionary<string, object>; if (Str(cd, "type") == "text") sb.Append(Str(cd, "text")).Append(' '); }
                    if (sb.Length > 0) return sb.ToString().Trim();
                }
            }
            catch { }
            return "";
        }

        static string FindCli()
        {
            object v; var cfg = Config();
            if (cfg.TryGetValue("claudeCli", out v) && File.Exists(Convert.ToString(v))) return Convert.ToString(v);
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
            if (File.Exists(local)) return local;
            foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { string f = Path.Combine(p.Trim(), "claude.exe"); if (File.Exists(f)) return f; } catch { }
            }
            return null;
        }

        static string Quote(string s) { return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        // True when the CLI failed because it isn't logged in.
        public static bool IsLoginError(string err) { return err != null && (err.IndexOf("authenticat", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("OAuth", StringComparison.OrdinalIgnoreCase) >= 0); }

        // Runs `claude -p` with Haiku, fully isolated: Xing's persona as the whole system prompt, no tools,
        // no user settings/hooks, no MCP, no saved session.
        public static string AskClaude(string prompt, int timeoutMs, out string error)
        {
            error = null;
            string cli = FindCli();
            if (cli == null) { error = "claude CLI не найден"; return null; }
            string args = "-p --model haiku --tools \"\" --restricted --strict-mcp-config --no-session-persistence --settings \"{\\\"disableAllHooks\\\":true}\" --system-prompt " + Quote(Persona);
            var psi = new ProcessStartInfo(cli, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Dir
            };
            // Don't inherit the desktop session's plumbing (its proxy URL, session ids) — this is a separate CLI call.
            var drop = new List<string>();
            foreach (System.Collections.DictionaryEntry kv in psi.EnvironmentVariables)
            {
                string k = (string)kv.Key;
                if (k.StartsWith("CLAUDE", StringComparison.OrdinalIgnoreCase) || k.StartsWith("ANTHROPIC_BASE_URL", StringComparison.OrdinalIgnoreCase)) drop.Add(k);
            }
            foreach (var k in drop) psi.EnvironmentVariables.Remove(k);
            psi.EnvironmentVariables["XING_NESTED"] = "1";
            try
            {
                using (var p = Process.Start(psi))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(prompt);
                    p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length); p.StandardInput.Close();
                    var outTask = p.StandardOutput.ReadToEndAsync(); var errTask = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } error = "timeout"; return null; }
                    string o = outTask.Result.Trim(), er = errTask.Result.Trim();
                    if (p.ExitCode != 0 || o.Length == 0) { error = (er + " " + o).Trim(); return null; }
                    return o.Trim('"', '«', '»', ' ');
                }
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }
    }
}
