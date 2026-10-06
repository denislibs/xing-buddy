// Xing Pixel — pixel-art mascot that sits on top of the Claude Desktop window (macOS).
// Built-in Xing plays the sprite sheets exported by the Windows build (sprites/monkey/<accessory>/<pose>.png);
// other characters are plugins in characters/<id>/ (character.json + PNG strips or GIF/WebP), same format as on Windows.
// Usage: XingPixel                 run
//        XingPixel --event         Claude Code hook: reads the hook JSON from stdin
//        XingPixel --state X       write a bare state X and exit
//        XingPixel --install       add hooks + login item, then start   (--uninstall reverses it)
// Build: ./build.sh  (needs Xcode Command Line Tools: xcode-select --install)
import AppKit
import ApplicationServices
import ImageIO
import UserNotifications

let fm = FileManager.default
let home = fm.homeDirectoryForCurrentUser
let dataDir = home.appendingPathComponent(".claude-mascot-pixel")
func dataFile(_ name: String) -> URL { dataDir.appendingPathComponent(name) }
func nowMs() -> Int64 { Int64(Date().timeIntervalSince1970 * 1000) }
func clockText(_ ms: Int64) -> String { let s = Int(max(0, ms) / 1000); return "\(s / 60):" + String(format: "%02d", s % 60) }
func short(_ s: String, _ n: Int) -> String {
    let t = s.replacingOccurrences(of: "\n", with: " ").replacingOccurrences(of: "\r", with: " ").trimmingCharacters(in: .whitespaces)
    return t.count <= n ? t : String(t.prefix(n - 1)) + "…"
}
func todayKey(_ d: Date = Date()) -> String { let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd"; return f.string(from: d) }
func num(_ v: Any?) -> Double { (v as? NSNumber)?.doubleValue ?? 0 }

// MARK: - shared files

func writeJSONAtomically(_ obj: Any, to url: URL) {
    guard let data = try? JSONSerialization.data(withJSONObject: obj) else { return }
    let tmp = url.deletingLastPathComponent().appendingPathComponent(url.lastPathComponent + ".\(getpid()).tmp")
    if (try? data.write(to: tmp)) != nil { _ = rename(tmp.path, url.path) }
}
func readJSON(_ url: URL) -> [String: Any]? {
    guard let d = try? Data(contentsOf: url) else { return nil }
    return (try? JSONSerialization.jsonObject(with: d)) as? [String: Any]
}
func config() -> [String: Any] { readJSON(dataFile("config.json")) ?? [:] }

// MARK: - stats (mood, counters, history, xp, quests, achievements)

struct Quest { let id: String; let title: String; let target: Int }
let quests = [Quest(id: "commits3", title: "сделай 3 коммита", target: 3), Quest(id: "tasks5", title: "закрой 5 задач", target: 5),
              Quest(id: "green3", title: "3 зелёных прогона тестов", target: 3), Quest(id: "edits30", title: "30 правок", target: 30),
              Quest(id: "push2", title: "2 пуша", target: 2), Quest(id: "noerr", title: "4 задачи без единой ошибки", target: 4)]
func level(_ xp: Int) -> Int { 1 + Int(floor(sqrt(Double(xp) / 40))) }
func xpForLevel(_ l: Int) -> Int { (l - 1) * (l - 1) * 40 }

// Progression: a title for every level and what it unlocks (same as Windows; the look comes from sprites/monkey/l<level>).
let maxRank = 20
let rankTitles = ["", "Малыш", "Стажёр", "Джун", "Джун+", "Мидл", "Мидл+", "Сеньор", "Сеньор+", "Тимлид", "Гуру",
                  "Мудрец", "Архитектор", "Шаман кода", "Магистр", "Легенда", "Хранитель репо", "Повелитель мержей", "Мастер дзена", "Почти бог", "Бессмертный"]
func rankTitle(_ l: Int) -> String { l <= maxRank ? rankTitles[max(1, l)] : "Бессмертный +\(l - maxRank)" }
func stageForLevel(_ l: Int) -> String { l <= 2 ? "baby" : l <= 5 ? "teen" : l <= 9 ? "adult" : "guru" }
func auraCount(_ l: Int) -> Int { l >= 17 ? 4 : l >= 13 ? 3 : l >= 12 ? 2 : 0 }
let beltNames = ["белый", "жёлтый", "зелёный", "синий", "коричневый", "чёрный"]
func unlocks(_ l: Int) -> String {
    switch l {
    case 1: return "малыш с кудряшкой"
    case 2: return "очки · трюк «люблю»"
    case 3: return "подрос (подросток) · бейдж стажёра · шарф · трюк «ребейз»"
    case 4: return "белый пояс · наушники · трюк «бег»"
    case 5: return "жёлтый пояс · корона · трюк «готово»"
    case 6: return "взрослый Синсин · зелёный пояс · трюк «музыка»"
    case 7: return "синий пояс · золотой бейдж"
    case 8: return "коричневый пояс · трюк «прыжок»"
    case 9: return "чёрный пояс"
    case 10: return "Гуру: седина, борода, пояс с золотыми полосками · трюк «секретный танец»"
    case 11: return "мудрые цитаты по клику"
    case 12: return "аура: вокруг кружат искры"
    case 13: return "аура ярче (3 искры)"
    case 15: return "Легенда: золото в шерсти"
    case 17: return "аура ещё ярче (4 искры)"
    case 20: return "нимб"
    default: return l > maxRank ? "просто уважение" : "+опыт к следующему званию"
    }
}

struct Stats {
    var d: [String: Any]
    subscript(_ k: String) -> Int { get { Int(num(d[k])) } set { d[k] = newValue } }
    var mood: Double { get { d["Mood"] == nil ? 60 : num(d["Mood"]) } set { d["Mood"] = max(0, min(100, newValue)) } }
    var turnStart: Int64 { get { Int64(num(d["TurnStart"])) } set { d["TurnStart"] = NSNumber(value: newValue) } }
    var achievements: [String] { get { d["Achievements"] as? [String] ?? [] } set { d["Achievements"] = newValue } }
    var achievementDays: [String: String] { get { d["AchievementDays"] as? [String: String] ?? [:] } set { d["AchievementDays"] = newValue } }
    var day: String { d["Day"] as? String ?? "" }
    var sessions: [String: Double] { get { (d["Sessions"] as? [String: NSNumber] ?? [:]).mapValues { $0.doubleValue } } set { d["Sessions"] = newValue } }
    var helpers: [String: Int] { get { (d["Helpers"] as? [String: NSNumber] ?? [:]).mapValues { $0.intValue } } set { d["Helpers"] = newValue } }
    var history: [String: [String: Any]] { get { d["History"] as? [String: [String: Any]] ?? [:] } set { d["History"] = newValue } }
    var quest: Quest { quests.first { $0.id == (d["QuestId"] as? String) } ?? quests[0] }

    mutating func hist(_ k: String, _ delta: Int) {
        var h = history; var today = h[day] ?? [:]
        today[k] = Int(num(today[k])) + delta; h[day] = today
        if h.count > 60 { for key in h.keys.sorted().prefix(h.count - 60) { h.removeValue(forKey: key) } }
        history = h
    }
    mutating func histMax(_ k: String, _ v: Int64) {
        var h = history; var today = h[day] ?? [:]
        if Double(v) > num(today[k]) { today[k] = NSNumber(value: v) }; h[day] = today; history = h
    }

    static func load() -> Stats {
        var s = Stats(d: readJSON(dataFile("stats.json")) ?? [:])
        let today = todayKey()
        if s.day != today {
            s.d["Day"] = today
            for k in ["Tasks", "Edits", "Commands", "Commits", "Pushes", "Tests", "TestsGreen", "Errors", "ErrorStreak", "MaxErrorStreak"] { s[k] = 0 }
        }
        if (s.d["QuestDay"] as? String) != today {
            let idx = Int(Date().timeIntervalSince1970 / 86400) % quests.count
            s.d["QuestDay"] = today; s.d["QuestId"] = quests[idx].id; s["QuestProgress"] = 0; s.d["QuestDone"] = false; s.d["QuestFailed"] = false
        }
        let now = nowMs(), at = Int64(num(s.d["MoodAt"]))
        if at > 0 { s.mood = 60 + (s.mood - 60) * exp(-Double(now - at) / 3_600_000 / 3) }   // drifts back toward 60 (~3h)
        s.d["MoodAt"] = NSNumber(value: now)
        s.sessions = s.sessions.filter { Double(now) - $0.value < 30 * 60 * 1000 }
        let alive = s.sessions
        s.helpers = s.helpers.filter { alive[$0.key] != nil }
        return s
    }
    func save() { writeJSONAtomically(d, to: dataFile("stats.json")) }
    var activeSessions: Int { let now = Double(nowMs()); return max(1, sessions.values.filter { now - $0 < 10 * 60 * 1000 }.count) }
    var runningHelpers: Int { helpers.values.reduce(0, +) }
}

// Read-modify-write under a file lock (hooks fire as separate processes).
@discardableResult
func withStats(_ change: (inout Stats) -> Void) -> Stats {
    let fd = open(dataFile("stats.lock").path, O_CREAT | O_RDWR, 0o644)
    if fd >= 0 { flock(fd, LOCK_EX) }
    defer { if fd >= 0 { flock(fd, LOCK_UN); close(fd) } }
    var s = Stats.load(); change(&s); s.save(); return s
}

// MARK: - Claude CLI (Haiku)

let persona = "Ты — Синсин, пухлая ворчливая, но добрая обезьянка-маскот, которая живёт рядом с окном Claude на компьютере программиста. " +
    "Это игровой персонаж: всегда отвечай от его лица, не называй себя Claude и не говори, что ты ИИ-модель. " +
    "Пиши по-русски, коротко, с юмором и обезьяньими словечками, без markdown, без эмодзи и без кавычек."
let loginHint = "чтобы я отвечал, залогинься через терминал: claude → /login"
func isLoginError(_ e: String?) -> Bool { guard let e = e?.lowercased() else { return false }; return e.contains("authenticat") || e.contains("login") || e.contains("oauth") }

func findCli() -> String? {
    if let p = config()["claudeCli"] as? String, fm.isExecutableFile(atPath: p) { return p }
    for p in [home.appendingPathComponent(".local/bin/claude").path, "/opt/homebrew/bin/claude", "/usr/local/bin/claude"] where fm.isExecutableFile(atPath: p) { return p }
    return nil
}

// `claude -p` with Haiku, fully isolated: Xing's persona as the system prompt, no tools, no user settings/hooks, no MCP, no saved session.
func askClaude(_ prompt: String, timeout: TimeInterval) -> (String?, String?) {
    guard let cli = findCli() else { return (nil, "claude CLI не найден") }
    let p = Process()
    p.executableURL = URL(fileURLWithPath: cli)
    p.arguments = ["-p", "--model", "haiku", "--tools", "", "--restricted", "--strict-mcp-config", "--no-session-persistence",
                   "--settings", "{\"disableAllHooks\":true}", "--system-prompt", persona]
    var env = ProcessInfo.processInfo.environment.filter { !$0.key.hasPrefix("CLAUDE") && $0.key != "ANTHROPIC_BASE_URL" }
    env["XING_NESTED"] = "1"
    p.environment = env
    let inPipe = Pipe(), outPipe = Pipe(), errPipe = Pipe()
    p.standardInput = inPipe; p.standardOutput = outPipe; p.standardError = errPipe
    do { try p.run() } catch { return (nil, error.localizedDescription) }
    inPipe.fileHandleForWriting.write(prompt.data(using: .utf8)!); try? inPipe.fileHandleForWriting.close()
    let deadline = Date().addingTimeInterval(timeout)
    while p.isRunning && Date() < deadline { usleep(100_000) }
    if p.isRunning { p.terminate(); return (nil, "timeout") }
    let out = String(data: outPipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    let err = String(data: errPipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
    if p.terminationStatus != 0 || out.isEmpty { return (nil, (err + " " + out).trimmingCharacters(in: .whitespaces)) }
    return (out.trimmingCharacters(in: CharacterSet(charactersIn: "\"«» ")), nil)
}

// MARK: - hook mode (--event)

func isNight() -> Bool { let h = Calendar.current.component(.hour, from: Date()); return h >= 23 || h < 5 }
func matches(_ s: String, _ pattern: String) -> Bool { s.range(of: pattern, options: [.regularExpression, .caseInsensitive]) != nil }
func capture(_ s: String, _ pattern: String) -> [String]? {
    guard let rx = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
          let m = rx.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)) else { return nil }
    return (1..<m.numberOfRanges).compactMap { Range(m.range(at: $0), in: s).map { String(s[$0]).lowercased() } }
}
let testRx = #"\b(npm|pnpm|yarn|bun)\s+(run\s+)?test|\bpytest\b|\bjest\b|\bvitest\b|\bswift\s+test|\bxcodebuild\s+test|\bgo\s+test|\bcargo\s+test|\bphpunit\b|\bplaywright\s+test"#
let buildRx = #"\b(build|compile|tsc|webpack|gradle|mvn|xcodebuild|make|swiftc)\b"#
let installRx = #"\b(npm|pnpm|yarn|pip|bun|brew|composer)\s+(i|install|add)\b"#

// git/gh command -> (pose, line while running, kind)
func gitInfo(_ cmd: String) -> (String, String, String)? {
    if let g = capture(cmd, #"\bgh\s+(pr|run|issue|release|workflow)\s+([a-z][\w-]*)"#), g.count == 2 {
        let (a, b) = (g[0], g[1])
        if a == "pr" && b == "create" { return ("git_pr", "отправляю PR…", "gh-pr-create") }
        if a == "pr" && b == "merge" { return ("git_merge", "мержу PR…", "gh-pr-merge") }
        if (a == "pr" && b == "checks") || (a == "run" && (b == "watch" || b == "view")) { return ("git_ci", "жду CI…", "gh-ci") }
        if a == "pr" && b == "checkout" { return ("git_branch", "беру ветку PR…", "gh-checkout") }
        return ("git_log", "смотрю \(a)…", "gh-other")
    }
    guard let g = capture(cmd, #"\bgit\s+(?:-[-\w=./]+\s+)*([a-z][\w-]*)"#), let sub = g.first else { return nil }
    switch sub {
    case "commit": return ("git_commit", "коммичу…", "commit")
    case "push": return ("git_push", "пушу в origin…", "push")
    case "pull": return ("git_pull", "тяну изменения…", "pull")
    case "clone": return ("git_pull", "клонирую…", "clone")
    case "fetch": return ("git_fetch", "смотрю, что нового…", "fetch")
    case "merge": return ("git_merge", "сливаю ветки…", "merge")
    case "rebase": return ("git_rebase", "ребейзю…", "rebase")
    case "checkout", "switch": return ("git_branch", "переключаю ветку…", "checkout")
    case "branch": return ("git_branch", "ращу ветку…", "branch")
    case "stash": return ("git_stash", "прячу в заначку…", "stash")
    case "log", "reflog", "show", "blame", "shortlog": return ("git_log", "листаю историю…", "log")
    case "diff": return ("git_diff", "смотрю дифф…", "diff")
    case "status": return ("git_status", "что тут у нас…", "status")
    case "add": return ("git_status", "добавляю в индекс…", "add")
    case "reset", "restore", "clean", "revert": return ("git_reset", "подметаю…", "reset")
    case "tag": return ("git_tag", "вешаю тег…", "tag")
    case "cherry-pick": return ("git_cherry", "срываю вишенку…", "cherry")
    default: return ("bash", "git \(sub)…", "git-other")
    }
}

struct StickerDef { let id: String, title: String, hint: String, pose: String }
let stickerList = [
    StickerDef(id: "night_commit", title: "ночной коммит", hint: "закоммить что-нибудь между полуночью и 5 утра", pose: "git_commit"),
    StickerDef(id: "early_bird", title: "ранняя пташка", hint: "начни работу до 7 утра", pose: "milk"),
    StickerDef(id: "speedrun", title: "спидран", hint: "закрой задачу с правками быстрее 15 секунд", pose: "run"),
    StickerDef(id: "friday13", title: "пятница, 13-е", hint: "поработай в пятницу 13-го", pose: "error"),
    StickerDef(id: "triple_push", title: "тройной пуш", hint: "три пуша за 10 минут", pose: "git_push"),
    StickerDef(id: "conflict5", title: "мастер конфликтов", hint: "переживи 5 конфликтов", pose: "git_conflict"),
    StickerDef(id: "answer42", title: "ответ на всё", hint: "закрой 42-ю задачу", pose: "think"),
    StickerDef(id: "golden10", title: "банановый магнат", hint: "собери 10 золотых бананов", pose: "banana"),
    StickerDef(id: "gamer", title: "геймер", hint: "набери 300 очков в банановом раннере", pose: "jump"),
    StickerDef(id: "champion", title: "чемпион раннера", hint: "набери 1000 очков", pose: "done"),
    StickerDef(id: "meloman", title: "меломан", hint: "послушай музыку с Синсином час", pose: "music"),
    StickerDef(id: "traveler", title: "путешественник", hint: "Синсин прогулялся 10 раз", pose: "walk"),
    StickerDef(id: "clicker", title: "кликер", hint: "тыкни Синсина 100 раз", pose: "love"),
    StickerDef(id: "konami", title: "секретный танец", hint: "кликни по Синсину 7 раз очень быстро", pose: "secret"),
    StickerDef(id: "banana_word", title: "банановое слово", hint: "спроси Синсина про бананы", pose: "banana"),
    StickerDef(id: "noodle", title: "лапшичник", hint: "пообедай с Синсином 5 разных дней", pose: "lunch"),
    StickerDef(id: "jackpot", title: "джекпот", hint: "выбей 777 в автомате", pose: "slot")]

// Unlocks an easter-egg sticker once; the event line announces it.
func sticker(_ s: inout Stats, _ e: inout [String: Any]?, _ id: String, _ cond: Bool) {
    var st = s.d["Stickers"] as? [String: String] ?? [:]
    guard cond, st[id] == nil else { return }
    st[id] = s.day; s.d["Stickers"] = st
    if e != nil, let def = stickerList.first(where: { $0.id == id }) {
        e!["line"] = "пасхалка: \(def.title)!"; e!["pose"] = "secret"; e!["seconds"] = 4.0; e!["sound"] = "level"; e!["sticker"] = id
    }
}

// What Xing remembers per project (keyed by the working directory).
var memoryOn: Bool { config()["memory"] as? Bool ?? true }
func loadMemory() -> [String: [String: Any]] { readJSON(dataFile("memory.json")) as? [String: [String: Any]] ?? [:] }
func remember(_ cwd: String, _ change: (inout [String: Any]) -> Void) {
    guard !cwd.isEmpty, memoryOn else { return }
    var m = loadMemory(); var p = m[cwd] ?? ["Name": URL(fileURLWithPath: cwd).lastPathComponent, "Tasks": 0]
    change(&p); p["LastSeen"] = NSNumber(value: nowMs()); p["LastDay"] = todayKey(); m[cwd] = p
    if m.count > 30 { for k in m.sorted(by: { num($0.value["LastSeen"]) < num($1.value["LastSeen"]) }).prefix(m.count - 30).map({ $0.key }) { m.removeValue(forKey: k) } }
    writeJSONAtomically(m, to: dataFile("memory.json"))
}

func ev(_ state: String?, _ pose: String?, _ line: String?, _ seconds: Double) -> [String: Any] {
    var e: [String: Any] = ["seconds": seconds]
    if let s = state { e["state"] = s }; if let p = pose { e["pose"] = p }; if let l = line { e["line"] = l }
    return e
}

func achieve(_ s: inout Stats, _ e: inout [String: Any]?, _ id: String, _ cond: Bool, _ title: String) {
    guard cond, !s.achievements.contains(id) else { return }
    s.achievements.append(id); var days = s.achievementDays; days[id] = s.day; s.achievementDays = days
    if e != nil { e!["line"] = "ачивка: " + title; e!["pose"] = "done"; e!["seconds"] = 4.0; e!["achievement"] = title; e!["sound"] = "level" }
}

func addXp(_ s: inout Stats, _ e: inout [String: Any]?, _ amount: Int) {
    let before = level(s["Xp"]); s["Xp"] += amount; s.hist("Xp", amount)
    let after = level(s["Xp"])
    if after > before, e != nil {
        let prev = (e!["line"] as? String).map { $0 + "\n" } ?? ""
        e!["line"] = prev + "уровень \(after)! теперь я \(rankTitle(after))\nоткрыл: \(unlocks(after))"
        e!["pose"] = "levelup"; e!["seconds"] = 7.0; e!["sound"] = "level"; e!["levelUp"] = after
        e!["notify"] = "Синсин: уровень \(after) — \(rankTitle(after)). Открыто: \(unlocks(after))"
    }
}

// Advances today's quest; completion pays out a golden banana.
func questStep(_ s: inout Stats, _ e: inout [String: Any]?, _ metric: String) {
    if (s.d["QuestDone"] as? Bool) == true || (s.d["QuestFailed"] as? Bool) == true { return }
    let id = s.quest.id
    if id == "noerr" && metric == "error" { s.d["QuestFailed"] = true; return }
    let want = ["commits3": "commit", "tasks5": "task", "noerr": "task", "green3": "green", "edits30": "edit", "push2": "push"][id] ?? ""
    guard metric == want else { return }
    s["QuestProgress"] += 1
    if s["QuestProgress"] >= s.quest.target {
        s.d["QuestDone"] = true; s["GoldenBananas"] += 1; s.mood += 10
        if e != nil { e!["line"] = "квест выполнен: \(s.quest.title)! золотой банан"; e!["pose"] = "banana"; e!["seconds"] = 4.0; e!["sound"] = "level" }
        addXp(&s, &e, 30)
    }
}

func decide(_ d: [String: Any], _ s: inout Stats) -> [String: Any]? {
    let name = d["hook_event_name"] as? String ?? "", tool = d["tool_name"] as? String ?? "", session = d["session_id"] as? String ?? ""
    let input = d["tool_input"] as? [String: Any] ?? [:]
    let cmd = input["command"] as? String ?? ""
    func base(_ k: String) -> String { ((input[k] as? String) ?? "").split(separator: "/").last.map(String.init) ?? "" }
    if !session.isEmpty { var ss = s.sessions; ss[session] = Double(nowMs()); s.sessions = ss }
    func setHelpers(_ s: inout Stats, _ delta: Int) { var h = s.helpers; h[session] = max(0, min(8, (h[session] ?? 0) + delta)); s.helpers = h }
    var e: [String: Any]?
    switch name {
    case "UserPromptSubmit":
        s.turnStart = nowMs(); s["TurnEdits"] = 0
        let prompt = d["prompt"] as? String ?? ""
        if !prompt.isEmpty && !prompt.hasPrefix("/") { remember(d["cwd"] as? String ?? "") { $0["LastPrompt"] = short(prompt, 200) } }
        let wd = Calendar.current.component(.weekday, from: Date())
        if (wd == 1 || wd == 7) && (s.d["WeekendGrumbleDay"] as? String) != s.day {
            s.d["WeekendGrumbleDay"] = s.day; e = ev("thinking", "think", "ты чего в выходной работаешь? ну ладно…", 0)
        } else { e = ev("thinking", nil, isNight() ? "ночная смена? ну ладно…" : "думаю…", 0) }
        achieve(&s, &e, "night", isNight(), "ночная смена")
        let hr = Calendar.current.component(.hour, from: Date()), dc = Calendar.current.dateComponents([.weekday, .day], from: Date())
        sticker(&s, &e, "early_bird", hr >= 4 && hr < 7)
        sticker(&s, &e, "friday13", dc.weekday == 6 && dc.day == 13)
        return e
    case "PreToolUse":
        if tool == "AskUserQuestion" || tool == "ExitPlanMode" { e = ev("waiting", nil, "твой ход", 0); e!["notify"] = "Синсин ждёт твоего ответа"; return e }
        if ["Edit", "Write", "MultiEdit", "NotebookEdit"].contains(tool) {
            s["Edits"] += 1; s["EditsTotal"] += 1; s.hist("Edits", 1); s["TurnEdits"] += 1
            e = ev("working", "type", "правлю " + short(base("file_path") + base("notebook_path"), 22), 0)
            if s["Edits"] % 5 == 0 { addXp(&s, &e, 1) }
            questStep(&s, &e, "edit")
            achieve(&s, &e, "edits50d-" + s.day, s["Edits"] == 50, "50 правок за день")
            achieve(&s, &e, "edits100", s["EditsTotal"] >= 100, "100 правок")
            achieve(&s, &e, "edits1000", s["EditsTotal"] >= 1000, "1000 правок, легенда")
            return e
        }
        if tool == "Bash" {
            s["Commands"] += 1; s.hist("Commands", 1)
            if matches(cmd, testRx) { s["Tests"] += 1; return ev("working", "work", "гоняю тесты…", 0) }
            if let gi = gitInfo(cmd) { return ev("working", gi.0, gi.1, 0) }
            if matches(cmd, installRx) { return ev("working", "bash", "ставлю пакеты…", 0) }
            if matches(cmd, buildRx) { return ev("working", "bash", "собираю…", 0) }
            return ev("working", "bash", "$ " + short(cmd, 20), 0)
        }
        if tool == "Read" { return ev("working", "read", "читаю " + short(base("file_path"), 22), 0) }
        if tool == "Glob" || tool == "Grep" { return ev("working", "read", "ищу " + short(input["pattern"] as? String ?? "", 18), 0) }
        if tool == "WebSearch" { return ev("working", "read", "гуглю: " + short(input["query"] as? String ?? "", 20), 0) }
        if tool == "WebFetch" { return ev("working", "read", "читаю сайт…", 0) }
        if tool == "Agent" || tool == "Task" { setHelpers(&s, 1); return ev("working", "run", "зову помощников (\(s.runningHelpers))", 0) }
        if tool == "TodoWrite" { return ev("working", "read", "планирую…", 0) }
        if tool.hasPrefix("mcp__") { return ev("working", "work", "дёргаю " + short(tool.components(separatedBy: "__").dropFirst().first ?? tool, 18), 0) }
        return ev("working", "work", "кручу братишку", 0)
    case "PostToolUse":
        s["ErrorStreak"] = 0; s.mood += 0.3
        if tool == "Agent" || tool == "Task" { setHelpers(&s, -1); return ev("thinking", "happy", "помощник вернулся", 2) }
        if tool == "Bash" {
            if matches(cmd, testRx) {
                s["TestsGreen"] += 1; s["TestsGreenTotal"] += 1; s.hist("TestsGreen", 1); s.mood += 3
                e = ev("thinking", "happy", "тесты зелёные!", 2.5); e!["sound"] = "success"
                addXp(&s, &e, 3); questStep(&s, &e, "green")
                achieve(&s, &e, "green10", s["TestsGreenTotal"] >= 10, "10 зелёных прогонов"); return e
            }
            if let gi = gitInfo(cmd) {
                switch gi.2 {
                case "commit":
                    s["Commits"] += 1; s["CommitsTotal"] += 1; s.hist("Commits", 1); s.mood += 4
                    e = ev("thinking", "git_commit", "закоммитил!", 3); e!["sound"] = "success"
                    addXp(&s, &e, 5); questStep(&s, &e, "commit")
                    achieve(&s, &e, "commit-" + s.day, s["Commits"] == 1, "первый коммит дня")
                    achieve(&s, &e, "commits100", s["CommitsTotal"] >= 100, "100 коммитов")
                    sticker(&s, &e, "night_commit", Calendar.current.component(.hour, from: Date()) < 5); return e
                case "push":
                    s["Pushes"] += 1; s["PushesTotal"] += 1; s.hist("Pushes", 1); s.mood += 4
                    e = ev("thinking", "git_push", "запушил! улетело в origin", 3); e!["sound"] = "success"
                    addXp(&s, &e, 5); questStep(&s, &e, "push")
                    achieve(&s, &e, "push1", true, "первый пуш"); achieve(&s, &e, "push50", s["PushesTotal"] >= 50, "50 пушей")
                    var times = (s.d["PushTimes"] as? [NSNumber] ?? []).map { $0.int64Value }.filter { nowMs() - $0 < 600_000 }; times.append(nowMs())
                    s.d["PushTimes"] = times.map { NSNumber(value: $0) }
                    sticker(&s, &e, "triple_push", times.count >= 3); return e
                case "pull", "clone": return ev("thinking", "git_pull", "свежак подтянут", 2.5)
                case "merge", "gh-pr-merge": s.mood += 3; e = ev("thinking", "done", "слил!", 3); addXp(&s, &e, 4); achieve(&s, &e, "merge1", true, "первый мерж"); return e
                case "rebase": return ev("thinking", "happy", "история ровная как банан", 2.5)
                case "gh-pr-create": s.mood += 5; e = ev("thinking", "love", "PR улетел!", 3); addXp(&s, &e, 8); achieve(&s, &e, "pr1", true, "первый PR"); return e
                case "tag": return ev("thinking", "git_tag", "тег повешен", 2)
                case "stash": return ev("thinking", "git_stash", "в заначке", 2)
                case "cherry": return ev("thinking", "git_cherry", "вишенка сорвана", 2)
                default: break
                }
            }
        }
        return ev("thinking", nil, "думаю…", 0)
    case "PostToolUseFailure":
        if tool == "Agent" || tool == "Task" { setHelpers(&s, -1) }
        s["Errors"] += 1; s["ErrorStreak"] += 1; s.hist("Errors", 1); s["MaxErrorStreak"] = max(s["MaxErrorStreak"], s["ErrorStreak"]); s.mood -= 6
        var none: [String: Any]? = nil; questStep(&s, &none, "error")
        let err = d["error"] as? String ?? ""
        let gi = tool == "Bash" ? gitInfo(cmd) : nil
        if err.range(of: "CONFLICT", options: .caseInsensitive) != nil || ["merge", "rebase", "pull", "cherry", "stash"].contains(gi?.2 ?? "") {
            s["Conflicts"] += 1
            e = ev("error", "git_conflict", "конфликт! кто трогал мой банан?!", 0); e!["sound"] = "error"
            sticker(&s, &e, "conflict5", s["Conflicts"] >= 5); return e
        }
        if gi?.2 == "push" { e = ev("error", nil, "push отклонили… надо подтянуть", 0); e!["sound"] = "error"; return e }
        let streak = s["ErrorStreak"]
        e = ev("error", nil, streak >= 10 ? "всё, я в отпуск" : streak >= 3 ? "да что ж такое…" : matches(cmd, testRx) ? "тесты упали…" : "ой…", 0)
        e!["sound"] = "error"; return e
    case "PermissionRequest":
        e = ev("waiting", nil, "можно? жду разрешения", 0); e!["notify"] = "Claude ждёт разрешения"; return e
    case "Notification":
        let msg = d["message"] as? String ?? ""
        e = ev("waiting", nil, "твой ход", 0); e!["notify"] = msg.isEmpty ? "Claude ждёт тебя" : msg; return e
    case "Stop":
        var h = s.helpers; h.removeValue(forKey: session); s.helpers = h
        s["Tasks"] += 1; s["TasksTotal"] += 1; s["Bananas"] += 1; s.hist("Tasks", 1); s.mood += 8
        let dur = s.turnStart > 0 ? nowMs() - s.turnStart : 0
        s.histMax("LongestMs", dur)
        e = ev("success", nil, dur > 60_000 ? "готово за \(clockText(dur))!" : "готово!", 0); e!["sound"] = "success"
        addXp(&s, &e, 10); questStep(&s, &e, "task")
        let survived = s["MaxErrorStreak"] >= 5 && s["ErrorStreak"] == 0
        achieve(&s, &e, "tasks10-" + s.day, s["Tasks"] == 10, "трудяга: 10 задач за день")
        achieve(&s, &e, "survivor", survived, "выжил после 5 ошибок подряд")
        achieve(&s, &e, "tasks100", s["TasksTotal"] >= 100, "100 задач")
        achieve(&s, &e, "marathon", dur > 30 * 60 * 1000, "марафон: задача дольше 30 минут")
        sticker(&s, &e, "speedrun", dur > 0 && dur < 15000 && s["TurnEdits"] > 0)
        sticker(&s, &e, "answer42", s["TasksTotal"] == 42)
        sticker(&s, &e, "golden10", s["GoldenBananas"] >= 10)
        remember(d["cwd"] as? String ?? "") { $0["Tasks"] = Int(num($0["Tasks"])) + 1 }
        return e
    case "StopFailure":
        var h = s.helpers; h.removeValue(forKey: session); s.helpers = h
        s.mood -= 4; e = ev("error", nil, "API прилёг…", 0); e!["sound"] = "error"; return e
    default: return nil
    }
}

func writeEvent(_ e: [String: Any]) { var e = e; e["ts"] = NSNumber(value: nowMs()); writeJSONAtomically(e, to: dataFile("event.json")) }

func lastAssistantText(_ path: String) -> String {
    guard !path.isEmpty, let data = fm.contents(atPath: path), let text = String(data: data, encoding: .utf8) else { return "" }
    for line in text.split(separator: "\n").suffix(400).reversed() where line.contains("\"assistant\"") {
        guard let o = (try? JSONSerialization.jsonObject(with: Data(line.utf8))) as? [String: Any], o["type"] as? String == "assistant",
              let content = (o["message"] as? [String: Any])?["content"] as? [[String: Any]] else { continue }
        let t = content.filter { $0["type"] as? String == "text" }.compactMap { $0["text"] as? String }.joined(separator: " ")
        if !t.isEmpty { return t }
    }
    return ""
}

func summarize(_ d: [String: Any], _ s: Stats) {
    let cfg = config()
    let ai = cfg["aiSummary"] as? Bool ?? true, minSec = cfg["aiMinSeconds"] == nil ? 20 : num(cfg["aiMinSeconds"])
    let dur = s.turnStart > 0 ? nowMs() - s.turnStart : 0
    guard Double(dur) >= minSec * 1000 else { return }
    var text = d["last_assistant_message"] as? String ?? ""
    if text.isEmpty { text = lastAssistantText(d["transcript_path"] as? String ?? "") }
    var line: String?
    if ai && !text.isEmpty { line = askClaude("Одной фразой до 70 символов перескажи, что сделано:\n" + short(text, 1500), timeout: 45).0 }
    if line == nil {
        var bits = ["готово за " + clockText(dur)]
        if s["Edits"] > 0 { bits.append("правок за день: \(s["Edits"])") }
        if s["TestsGreen"] > 0 { bits.append("тесты зелёные") }
        line = bits.joined(separator: " · ")
    }
    writeEvent(ev(nil, nil, short(line!, 90), 7))
    remember(d["cwd"] as? String ?? "") { $0["LastSummary"] = short(line!, 160) }
}

// MARK: - installer (hooks + login item)

let exePath = URL(fileURLWithPath: CommandLine.arguments[0]).standardizedFileURL.path
let hookEvents = ["UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "PermissionRequest", "Notification", "Stop", "StopFailure"]
let settingsURL = home.appendingPathComponent(".claude/settings.json")
let agentURL = home.appendingPathComponent("Library/LaunchAgents/local.xingpixel.plist")

func loadSettingsWithBackup() -> [String: Any] {
    guard fm.fileExists(atPath: settingsURL.path) else { return [:] }
    let bk = dataDir.appendingPathComponent("backup"); try? fm.createDirectory(at: bk, withIntermediateDirectories: true)
    try? fm.copyItem(at: settingsURL, to: bk.appendingPathComponent("settings.json.\(Int(Date().timeIntervalSince1970))"))
    return readJSON(settingsURL) ?? [:]
}
func isOurs(_ entry: Any) -> Bool {
    ((entry as? [String: Any])?["hooks"] as? [[String: Any]] ?? []).contains { ($0["command"] as? String ?? "").contains("XingPixel") }
}
func installHooks() {
    var d = loadSettingsWithBackup(); var hooks = d["hooks"] as? [String: Any] ?? [:]
    for ev in hookEvents {
        var list = hooks[ev] as? [Any] ?? []
        if list.contains(where: isOurs) { continue }
        var entry: [String: Any] = ["hooks": [["type": "command", "async": true, "timeout": ev == "Stop" ? 90 : 10, "command": "\"\(exePath)\" --event"]]]
        if ev.hasPrefix("Pre") || ev.hasPrefix("Post") || ev == "PermissionRequest" { entry["matcher"] = "*" }
        list.append(entry); hooks[ev] = list
    }
    d["hooks"] = hooks
    try? fm.createDirectory(at: settingsURL.deletingLastPathComponent(), withIntermediateDirectories: true)
    if let data = try? JSONSerialization.data(withJSONObject: d, options: [.prettyPrinted]) { try? data.write(to: settingsURL) }
}
func removeHooks() {
    guard fm.fileExists(atPath: settingsURL.path) else { return }
    var d = loadSettingsWithBackup(); guard var hooks = d["hooks"] as? [String: Any] else { return }
    for (k, v) in hooks { let keep = (v as? [Any] ?? []).filter { !isOurs($0) }; if keep.isEmpty { hooks.removeValue(forKey: k) } else { hooks[k] = keep } }
    if hooks.isEmpty { d.removeValue(forKey: "hooks") } else { d["hooks"] = hooks }
    if let data = try? JSONSerialization.data(withJSONObject: d, options: [.prettyPrinted]) { try? data.write(to: settingsURL) }
}
var autostartOn: Bool { fm.fileExists(atPath: agentURL.path) }
func setAutostart(_ on: Bool) {
    if !on { try? fm.removeItem(at: agentURL); return }
    let plist: [String: Any] = ["Label": "local.xingpixel", "ProgramArguments": [exePath], "RunAtLoad": true, "ProcessType": "Interactive"]
    try? fm.createDirectory(at: agentURL.deletingLastPathComponent(), withIntermediateDirectories: true)
    if let data = try? PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0) { try? data.write(to: agentURL) }
}

// MARK: - entry points that exit early

let args = CommandLine.arguments
try? fm.createDirectory(at: dataDir, withIntermediateDirectories: true)
if args.count == 2 && args[1] == "--event" {
    if ProcessInfo.processInfo.environment["XING_NESTED"] == "1" { exit(0) }   // our own Haiku calls
    let input = FileHandle.standardInput.readDataToEndOfFile()
    guard let d = (try? JSONSerialization.jsonObject(with: input)) as? [String: Any] else { exit(0) }
    var e: [String: Any]?
    let s = withStats { st in e = decide(d, &st) }
    if var e = e {
        e["mood"] = NSNumber(value: s.mood.rounded()); e["turnStart"] = NSNumber(value: s.turnStart)
        e["sessions"] = s.activeSessions; e["helpers"] = s.runningHelpers; e["xp"] = s["Xp"]
        writeEvent(e)
    }
    if d["hook_event_name"] as? String == "Stop" { summarize(d, s) }
    exit(0)
}
if args.count == 3 && args[1] == "--state" { writeEvent(["state": args[2]]); exit(0) }
if args.count == 2 && args[1] == "--install" {
    installHooks(); setAutostart(true)
    print("Синсин установлен: хуки добавлены (копия настроек в ~/.claude-mascot-pixel/backup), запуск при входе включён.")
    let bundle = URL(fileURLWithPath: exePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    NSWorkspace.shared.open(bundle); exit(0)
}
if args.count == 2 && args[1] == "--uninstall" {
    removeHooks(); setAutostart(false)
    for a in NSRunningApplication.runningApplications(withBundleIdentifier: "local.xingpixel") where a.processIdentifier != getpid() { a.terminate() }
    print("Синсин удалён: хуки убраны, автозапуск выключен."); exit(0)
}

// MARK: - sounds (tiny square-wave synth, nothing to ship)

enum Synth {
    static var cache: [String: NSSound] = [:]
    static func wav(_ notes: [[Double]], noise: Bool) -> Data {
        let rate = 22050; var pcm = [UInt8](); var seed: UInt32 = 1
        for n in notes {
            let len = Int(Double(rate) * n[1] / 1000); var phase = 0.0
            for i in 0..<len {
                let t = Double(i) / Double(len), f = n[0] + (n.count > 2 ? (n[2] - n[0]) * t : 0)
                phase += f / Double(rate)
                let env = min(1, Double(i) / 200) * min(1, Double(len - i) / 400)
                seed = seed &* 1103515245 &+ 12345
                let sq: Double = phase.truncatingRemainder(dividingBy: 1) < 0.5 ? 1 : -1
                let v = n[0] <= 0 ? 0 : (noise ? ((seed >> 16) & 1 == 0 ? 0.5 : -0.5) + sq * 0.5 : sq)
                pcm.append(UInt8(clamping: Int(128 + v * env * 22)))
            }
        }
        var d = Data()
        func u32(_ v: Int) { var x = UInt32(v).littleEndian; d.append(Data(bytes: &x, count: 4)) }
        func u16(_ v: Int) { var x = UInt16(v).littleEndian; d.append(Data(bytes: &x, count: 2)) }
        d.append("RIFF".data(using: .ascii)!); u32(36 + pcm.count); d.append("WAVEfmt ".data(using: .ascii)!)
        u32(16); u16(1); u16(1); u32(rate); u32(rate); u16(1); u16(8); d.append("data".data(using: .ascii)!); u32(pcm.count); d.append(contentsOf: pcm)
        return d
    }
    static func play(_ name: String, _ voice: String) {
        let key = name + "|" + voice
        if cache[key] == nil {
            let data: Data
            switch name {
            case "success":
                data = voice == "pig" ? wav([[140, 90, 110], [0, 40], [150, 120, 100]], noise: true)
                     : voice == "spark" ? wav([[880, 70], [1320, 120]], noise: false)
                     : wav([[520, 110, 700], [0, 30], [700, 160, 460]], noise: false)   // "у-а"
            case "error": data = wav([[440, 380, 200]], noise: false)
            case "level": data = wav([[523, 80], [659, 80], [784, 80], [1046, 180]], noise: false)
            case "reel": data = wav([[1400, 18]], noise: false)   // a reel stops
            case "jackpot": data = wav([[784, 70], [988, 70], [1175, 70], [1568, 70], [1175, 70], [1568, 260]], noise: false)
            default: data = voice == "pig" ? wav([[160, 110, 120]], noise: true) : wav([[900, 40]], noise: false)
            }
            cache[key] = NSSound(data: data)
        }
        cache[key]?.stop(); cache[key]?.play()
    }
}

// MARK: - characters

struct Manifest: Decodable { let frameWidth: Int; let frameHeight: Int; let frames: Int; let fps: Int; let homeFrames: Int?; let accessories: [String]?; let poses: [String]; let looks: [Int]? }

func resourceDirs(_ name: String) -> [URL] {
    [Bundle.main.resourceURL?.appendingPathComponent(name), URL(fileURLWithPath: args[0]).deletingLastPathComponent().appendingPathComponent(name),
     dataDir.appendingPathComponent(name)].compactMap { $0 }
}

func stripFrames(_ url: URL, _ count: Int) -> [CGImage] {
    guard let img = NSImage(contentsOf: url), let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return [] }
    let w = cg.width / max(1, count)
    return (0..<count).compactMap { cg.cropping(to: CGRect(x: $0 * w, y: 0, width: w, height: cg.height)) }
}
func animatedFrames(_ url: URL) -> [CGImage] {
    guard let src = CGImageSourceCreateWithURL(url as CFURL, nil) else { return [] }
    return (0..<CGImageSourceGetCount(src)).compactMap { CGImageSourceCreateImageAtIndex(src, $0, nil) }
}

// Plugin character: characters/<id>/character.json (+ a PNG strip or GIF/WebP per pose). Missing poses fall back.
final class MascotCharacter {
    let id: String, name: String, author: String, sound: String, dir: URL, pixelArt: Bool, fps: Double
    let lines: [String: String]
    private let poseDefs: [String: Any]; private let defaultFrames: Int
    private var loaded: [String: [CGImage]] = [:]
    static let fallback = ["bash": "work", "type": "work", "read": "work", "run": "work", "work": "idle", "think": "idle", "done": "happy", "banana": "happy",
                           "love": "happy", "stretch": "happy", "happy": "idle", "wave": "happy", "error": "idle", "wait": "idle", "milk": "idle", "water": "milk",
                           "lunch": "milk", "sleep": "idle", "home": "wave", "git_conflict": "error", "spotify": "music", "music": "happy", "levelup": "happy"]

    init?(dir: URL) {
        guard let d = readJSON(dir.appendingPathComponent("character.json")), let poses = d["poses"] as? [String: Any], !poses.isEmpty else { return nil }
        self.dir = dir; id = dir.lastPathComponent; name = d["name"] as? String ?? dir.lastPathComponent; author = d["author"] as? String ?? ""
        sound = d["sound"] as? String ?? "monkey"; pixelArt = d["pixelArt"] as? Bool ?? true; fps = max(1, d["fps"] == nil ? 9 : num(d["fps"]))
        lines = d["lines"] as? [String: String] ?? [:]; poseDefs = poses; defaultFrames = max(1, d["frames"] == nil ? 1 : Int(num(d["frames"])))
        if frames(poses["idle"] != nil ? "idle" : poses.keys.first!).isEmpty { return nil }
    }
    func has(_ p: String) -> Bool { poseDefs[p] != nil }
    func resolve(_ pose: String) -> String {
        var p: String? = pose
        for _ in 0..<6 { guard let q = p else { break }; if has(q) { return q }; p = q.hasPrefix("git_") && q != "git_conflict" ? "work" : MascotCharacter.fallback[q] }
        return has("idle") ? "idle" : poseDefs.keys.first!
    }
    func frames(_ pose: String) -> [CGImage] {
        if let f = loaded[pose] { return f }
        var file = poseDefs[pose] as? String ?? ""; var count = defaultFrames
        if let dd = poseDefs[pose] as? [String: Any] { file = dd["file"] as? String ?? ""; if dd["frames"] != nil { count = max(1, Int(num(dd["frames"]))) } }
        let url = dir.appendingPathComponent(file), ext = url.pathExtension.lowercased()
        let f = ext == "gif" || ext == "webp" ? animatedFrames(url) : stripFrames(url, count)
        loaded[pose] = f; return f
    }
    static func discover() -> [MascotCharacter] {
        var found: [String: MascotCharacter] = [:]
        for root in resourceDirs("characters") {
            for dir in (try? fm.contentsOfDirectory(at: root, includingPropertiesForKeys: nil)) ?? [] {
                if let c = MascotCharacter(dir: dir) { found[c.id] = c }
            }
        }
        return found.values.sorted { $0.name < $1.name }
    }
}

// Built-in Xing: sprites/monkey/l<look>/<accessory>/<pose>.png (one look per level where he changes), loaded lazily;
// the aura (level 12+) is a separate overlay: sprites/aura/<sparkles>_back.png / _front.png.
final class BuiltIn {
    let manifest: Manifest, root: URL
    private var cache: [String: [CGImage]] = [:]
    init?() {
        for dir in resourceDirs("sprites") {
            if let data = try? Data(contentsOf: dir.appendingPathComponent("manifest.json")), let m = try? JSONDecoder().decode(Manifest.self, from: data) {
                manifest = m; root = dir; return
            }
        }
        return nil
    }
    func look(_ lvl: Int) -> Int { (manifest.looks ?? [1]).filter { $0 <= lvl }.max() ?? 1 }
    func frames(_ pose: String, _ acc: String, level lvl: Int = 1) -> [CGImage] {
        let lk = look(lvl), key = "\(lk)/\(acc)/\(pose)"
        if let f = cache[key] { return f }
        let path = manifest.looks == nil ? "monkey/\(acc)/\(pose).png" : "monkey/l\(lk)/\(acc)/\(pose).png"
        let f = stripFrames(root.appendingPathComponent(path), manifest.frames)
        cache[key] = f
        if cache.count > 60, let old = cache.keys.first(where: { $0 != key }) { cache.removeValue(forKey: old) }
        return f
    }
    lazy var slotRows: [CGImage] = {
        guard let s = NSImage(contentsOf: self.root.appendingPathComponent("slot/symbols.png"))?.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return [] }
        return (0..<s.height).compactMap { s.cropping(to: CGRect(x: 0, y: $0, width: s.width, height: 1)) }
    }()
    func aura(_ lvl: Int, front: Bool) -> [CGImage] {
        let n = auraCount(lvl); guard n > 0 else { return [] }
        let name = "\(n)_" + (front ? "front" : "back"), key = "aura/" + name
        if let f = cache[key] { return f }
        let f = stripFrames(root.appendingPathComponent(key + ".png"), manifest.frames)
        cache[key] = f; return f
    }
}

// Stacks pixel layers (bottom first) into one image drawn without smoothing at the given scale.
func composite(_ layers: [CGImage?], scale: CGFloat) -> NSImage {
    NSImage(size: NSSize(width: 40 * scale, height: 44 * scale), flipped: false) { r in
        guard let ctx = NSGraphicsContext.current?.cgContext else { return false }
        ctx.interpolationQuality = .none
        for case let l? in layers { ctx.draw(l, in: r) }
        return true
    }
}
final class FlippedView: NSView { override var isFlipped: Bool { true } }

// MARK: - Spotify via AppleScript (macOS asks once for permission to control Spotify)

struct NowPlaying { var artist = "", title = "", playing = false, position = 0.0, duration = 0.0, artURL = "" }
func spotifyRunning() -> Bool { !NSRunningApplication.runningApplications(withBundleIdentifier: "com.spotify.client").isEmpty }
// Only talks to Spotify when it's already running ("tell application" would launch it).
@discardableResult func spotify(_ command: String) -> String? {
    guard spotifyRunning() else { return nil }
    var err: NSDictionary?
    return NSAppleScript(source: "tell application \"Spotify\" to " + command)?.executeAndReturnError(&err).stringValue
}
func readSpotify() -> NowPlaying? {
    guard spotifyRunning() else { return nil }
    let src = """
    tell application "Spotify"
        set st to player state as string
        if st is "stopped" then return ""
        set t to current track
        return st & "|||" & (artist of t) & "|||" & (name of t) & "|||" & (player position as string) & "|||" & ((duration of t) as string) & "|||" & (artwork url of t)
    end tell
    """
    var err: NSDictionary?
    // No track (stopped, starting up, shutting down, or no permission yet): no player.
    guard let out = NSAppleScript(source: src)?.executeAndReturnError(&err).stringValue else { return nil }
    let p = out.components(separatedBy: "|||")
    guard p.count >= 6, !p[2].isEmpty else { return nil }
    let d = { (s: String) -> Double in Double(s.replacingOccurrences(of: ",", with: ".")) ?? 0 }
    return NowPlaying(artist: p[1], title: p[2], playing: p[0] == "playing", position: d(p[3]), duration: d(p[4]) / 1000, artURL: p[5])
}
// Shrinks an image to n×n pixels (drawn later without smoothing = pixel art).
func pixelated(_ img: CGImage, _ n: Int) -> CGImage? {
    guard let ctx = CGContext(data: nil, width: n, height: n, bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
    ctx.interpolationQuality = .medium; ctx.draw(img, in: CGRect(x: 0, y: 0, width: n, height: n))
    return ctx.makeImage()
}

// MARK: - slot machine: reel symbols come from sprites/slot/symbols.png (5px wide, one symbol per 6px), same as Windows
let slotSymbols = ["seven", "cherry", "banana", "bar", "spark", "bug"]
let slotReelX = [9, 15, 21], slotReelY = 32, slotReelH = 7, slotCell = 6

// MARK: - Claude window lookup & keys

struct ClaudeWindow { let number: Int; let frame: NSRect }

func findClaude() -> ClaudeWindow? {
    guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] else { return nil }
    let screenH = NSScreen.screens.first?.frame.height ?? 0
    var best: ClaudeWindow?; var bestArea: CGFloat = 0
    for w in list {
        guard (w[kCGWindowOwnerName as String] as? String) == "Claude", (w[kCGWindowLayer as String] as? Int) == 0,
              let n = w[kCGWindowNumber as String] as? Int, let b = w[kCGWindowBounds as String] as? NSDictionary,
              let r = CGRect(dictionaryRepresentation: b), r.width > 300, r.height > 200 else { continue }
        if r.width * r.height > bestArea { bestArea = r.width * r.height; best = ClaudeWindow(number: n, frame: NSRect(x: r.minX, y: screenH - r.maxY, width: r.width, height: r.height)) }
    }
    return best
}
func claudeApp() -> NSRunningApplication? { NSWorkspace.shared.runningApplications.first { $0.localizedName == "Claude" } }

func postKeys(_ key: CGKeyCode, flags: CGEventFlags) {
    let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
    guard AXIsProcessTrustedWithOptions(opts) else { return }
    let src = CGEventSource(stateID: .hidSystemState)
    for down in [true, false] { let e = CGEvent(keyboardEventSource: src, virtualKey: key, keyDown: down); e?.flags = flags; e?.post(tap: .cghidEventTap) }
}

// MARK: - weather & report

struct Weather { var rain = false, snow = false, cold = false }

func fetchWeather(_ city: String) -> Weather? {
    func get(_ s: String) -> [String: Any]? {
        guard let u = URL(string: s) else { return nil }
        var out: [String: Any]?; let sem = DispatchSemaphore(value: 0)
        URLSession.shared.dataTask(with: u) { d, _, _ in out = d.flatMap { (try? JSONSerialization.jsonObject(with: $0)) as? [String: Any] }; sem.signal() }.resume()
        _ = sem.wait(timeout: .now() + 15); return out
    }
    guard let q = city.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed),
          let r = (get("https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&name=" + q)?["results"] as? [[String: Any]])?.first,
          let f = get("https://api.open-meteo.com/v1/forecast?current=temperature_2m,weather_code&latitude=\(num(r["latitude"]))&longitude=\(num(r["longitude"]))"),
          let cur = f["current"] as? [String: Any] else { return nil }
    let code = Int(num(cur["weather_code"])), t = num(cur["temperature_2m"])
    return Weather(rain: (51...67).contains(code) || (80...82).contains(code) || code >= 95, snow: (71...77).contains(code) || code == 85 || code == 86, cold: t < 0)
}

let achievementTitles = ["night": "ночная смена", "edits100": "100 правок", "edits1000": "1000 правок", "green10": "10 зелёных прогонов", "survivor": "выжил после 5 ошибок",
                         "tasks100": "100 задач", "edits50d": "50 правок за день", "commit": "первый коммит дня", "tasks10": "10 задач за день", "commits100": "100 коммитов",
                         "push1": "первый пуш", "push50": "50 пушей", "merge1": "первый мерж", "pr1": "первый PR", "marathon": "марафон: задача дольше 30 минут"]
func achievementTitle(_ id: String) -> String { achievementTitles[id.components(separatedBy: "-").first ?? id] ?? id }

func writeReport(_ s: Stats, phrase: String) -> URL {
    let days = (0..<7).map { Calendar.current.date(byAdding: .day, value: -6 + $0, to: Date())! }
    let rows = days.map { ($0, s.history[todayKey($0)] ?? [:]) }
    func sum(_ k: String) -> Int { rows.reduce(0) { $0 + Int(num($1.1[k])) } }
    let maxTasks = max(1, rows.map { Int(num($0.1["Tasks"])) }.max() ?? 1)
    let longest = Int64(rows.map { num($0.1["LongestMs"]) }.max() ?? 0)
    let since = todayKey(days[0])
    let achieved = s.achievementDays.filter { $0.value >= since }.map { achievementTitle($0.key) }
    let dn = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"]
    var bars = ""
    for (i, r) in rows.enumerated() {
        let t = Int(num(r.1["Tasks"])), h = Int((120.0 * Double(t) / Double(maxTasks)).rounded()), x = 20 + i * 64
        let wd = dn[Calendar.current.component(.weekday, from: r.0) - 1]
        bars += "<rect x='\(x)' y='\(140 - h)' width='36' height='\(max(h, 2))' rx='4' class='bar'/><text x='\(x + 18)' y='160' class='lbl'>\(wd)</text><text x='\(x + 18)' y='\(134 - h)' class='val'>\(t)</text>"
    }
    func esc(_ t: String) -> String { t.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;").replacingOccurrences(of: ">", with: "&gt;") }
    func k(_ v: String, _ l: String) -> String { "<div class='k'><b>\(v)</b><span>\(l)</span></div>" }
    let tiles = k("\(sum("Tasks"))", "задач") + k("\(sum("Commits"))", "коммитов") + k("\(sum("Pushes"))", "пушей") + k("\(sum("Edits"))", "правок") +
                k("\(sum("TestsGreen"))", "зелёных прогонов") + k("\(sum("Errors"))", "ошибок") + k(clockText(longest), "самая долгая задача") + k("+\(sum("Xp"))", "опыта")
    let ach = achieved.isEmpty ? "<li>на этой неделе без новых ачивок</li>" : achieved.map { "<li>\(esc($0))</li>" }.joined()
    let html = """
    <!doctype html><html lang='ru'><head><meta charset='utf-8'><title>Неделя Синсина</title><style>
    :root{--bg:#F5F3EE;--card:#fff;--ink:#2B2925;--mute:#76736B;--acc:#D97757;--line:#E4E0D6}
    @media (prefers-color-scheme:dark){:root{--bg:#1F1E1C;--card:#2A2927;--ink:#EEEBE3;--mute:#9C988E;--line:#3A3936}}
    body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.6 -apple-system,system-ui,sans-serif}
    main{max-width:760px;margin:0 auto;padding:40px 20px}h1{font-size:28px;margin:0 0 4px;font-weight:600}.sub{color:var(--mute);margin:0 0 28px}
    .grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px;margin-bottom:24px}
    .k{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:14px 16px}.k b{display:block;font-size:26px;font-weight:600}.k span{color:var(--mute);font-size:13px}
    .card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:18px 20px;margin-bottom:16px}
    .quote{font-size:18px;font-style:italic;border-left:3px solid var(--acc);padding-left:14px}
    svg{width:100%;height:auto}.bar{fill:var(--acc)}.lbl,.val{fill:var(--mute);font-size:12px;text-anchor:middle}ul{margin:0;padding-left:20px}</style></head><body><main>
    <h1>Неделя Синсина</h1><p class='sub'>\(todayKey(days[0])) — \(todayKey(days[6])) · уровень \(level(s["Xp"])) · золотых бананов: \(s["GoldenBananas"])</p>
    <div class='grid'>\(tiles)</div>
    <div class='card'><svg viewBox='0 0 470 170' role='img' aria-label='Задачи по дням'>\(bars)</svg></div>
    <div class='card'><b>Ачивки недели</b><ul>\(ach)</ul></div>
    <div class='card quote'>\(esc(phrase))<br><span style='font-size:13px;color:var(--mute);font-style:normal'>— Синсин</span></div>
    </main></body></html>
    """
    let dir = dataDir.appendingPathComponent("reports"); try? fm.createDirectory(at: dir, withIntermediateDirectories: true)
    let url = dir.appendingPathComponent("week-\(todayKey()).html"); try? html.write(to: url, atomically: true, encoding: .utf8)
    return url
}

// MARK: - views

final class SpriteView: NSView {
    var image: CGImage? { didSet { needsDisplay = true } }
    var back: CGImage?, front: CGImage?   // aura overlays, same stage size as the built-in sprite
    var cover: CGImage?                   // pixel album cover while he DJs
    var reels: [Double]?, reelRows: [CGImage] = []   // slot machine reel positions (in symbols) and the symbol strip rows
    var smooth = false
    var onClick: (() -> Void)?, onDoubleClick: (() -> Void)?, onDragEnd: (() -> Void)?, onDrop: (([URL]) -> Void)?
    var menuProvider: (() -> NSMenu)?
    private var downAt = NSPoint.zero, originAt = NSPoint.zero, moved = false

    override init(frame: NSRect) { super.init(frame: frame); registerForDraggedTypes([.fileURL]) }
    required init?(coder: NSCoder) { fatalError() }
    override func draw(_ dirtyRect: NSRect) {
        guard let img = image, let ctx = NSGraphicsContext.current?.cgContext else { return }
        ctx.interpolationQuality = smooth ? .high : .none
        let fit = min(bounds.width / CGFloat(img.width), bounds.height / CGFloat(img.height))
        let w = CGFloat(img.width) * fit, h = CGFloat(img.height) * fit
        let stageFit = min(bounds.width / 40, bounds.height / 44), stage = CGRect(x: (bounds.width - 40 * stageFit) / 2, y: 0, width: 40 * stageFit, height: 44 * stageFit)
        if let b = back { ctx.interpolationQuality = .none; ctx.draw(b, in: stage); ctx.interpolationQuality = smooth ? .high : .none }
        ctx.draw(img, in: CGRect(x: (bounds.width - w) / 2, y: 0, width: w, height: h))
        if let f = front { ctx.interpolationQuality = .none; ctx.draw(f, in: stage) }
        ctx.interpolationQuality = .none
        if let c = cover { ctx.draw(c, in: CGRect(x: stage.minX, y: stage.minY + 22 * stageFit, width: 11 * stageFit, height: 11 * stageFit)) }
        if let pos = reels, !reelRows.isEmpty {
            let n = reelRows.count
            for r in 0..<3 {
                for row in 0..<slotReelH {
                    var src = Int(floor(pos[r] * Double(slotCell))) + row - 1; src = ((src % n) + n) % n
                    let rect = CGRect(x: stage.minX + CGFloat(slotReelX[r]) * stageFit, y: stage.minY + CGFloat(44 - slotReelY - row - 1) * stageFit,
                                      width: 5 * stageFit, height: stageFit)
                    ctx.setFillColor(CGColor(srgbRed: 0.957, green: 0.945, blue: 0.918, alpha: 1)); ctx.fill(rect)
                    ctx.draw(reelRows[src], in: rect)
                    if row == 0 || row == slotReelH - 1 { ctx.setFillColor(CGColor(gray: 0, alpha: 0.25)); ctx.fill(rect) }
                }
            }
        }
    }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with e: NSEvent) {
        if e.clickCount == 2 { onDoubleClick?(); moved = true; return }
        downAt = NSEvent.mouseLocation; originAt = window?.frame.origin ?? .zero; moved = false
    }
    override func mouseDragged(with e: NSEvent) {
        let p = NSEvent.mouseLocation, dx = p.x - downAt.x, dy = p.y - downAt.y
        if abs(dx) + abs(dy) > 3 { moved = true }
        window?.setFrameOrigin(NSPoint(x: originAt.x + dx, y: originAt.y + dy))
    }
    override func mouseUp(with e: NSEvent) { if e.clickCount >= 2 { return }; if moved { onDragEnd?() } else { onClick?() } }
    override func rightMouseDown(with e: NSEvent) { if let m = menuProvider?() { NSMenu.popUpContextMenu(m, with: e, for: self) } }
    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation { .copy }
    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self]) as? [URL] ?? []
        if !urls.isEmpty { onDrop?(urls) }
        return !urls.isEmpty
    }
}

final class BubbleView: NSView {
    private let label = NSTextField(wrappingLabelWithString: "")
    private let tail = CAShapeLayer()
    private let box = NSView()
    static let paper = NSColor(srgbRed: 0.969, green: 0.957, blue: 0.933, alpha: 1)

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true; box.wantsLayer = true
        box.layer?.backgroundColor = Self.paper.cgColor
        box.layer?.borderColor = NSColor(srgbRed: 0.169, green: 0.141, blue: 0.125, alpha: 1).cgColor
        box.layer?.borderWidth = 2; box.layer?.cornerRadius = 4
        label.font = NSFont.monospacedSystemFont(ofSize: 12.5, weight: .regular)
        label.textColor = NSColor(srgbRed: 0.169, green: 0.161, blue: 0.145, alpha: 1)
        label.alignment = .center; label.preferredMaxLayoutWidth = 206; label.maximumNumberOfLines = 6; label.lineBreakMode = .byTruncatingTail
        box.addSubview(label); addSubview(box)
        tail.fillColor = Self.paper.cgColor
        layer?.addSublayer(tail)
    }
    required init?(coder: NSCoder) { fatalError() }

    var text: String = "" {
        didSet {
            guard text != oldValue else { return }
            label.stringValue = text
            var size = label.sizeThatFits(NSSize(width: 206, height: 110)); size.height = min(size.height, 104)
            let w = size.width + 22, h = size.height + 10
            box.frame = NSRect(x: (bounds.width - w) / 2, y: 8, width: w, height: h)
            label.frame = NSRect(x: 11, y: 5, width: size.width, height: size.height)
            let p = CGMutablePath(), cx = bounds.width / 2
            p.move(to: CGPoint(x: cx - 6, y: 10)); p.addLine(to: CGPoint(x: cx + 6, y: 10)); p.addLine(to: CGPoint(x: cx, y: 3)); p.closeSubpath()
            tail.path = p
        }
    }
}

final class KeyPanel: NSPanel { override var canBecomeKey: Bool { true } }

// Closure-based target/action for buttons built in code.
private var closureKey = 0
final class ClosureTarget: NSObject { let f: () -> Void; init(_ f: @escaping () -> Void) { self.f = f }; @objc func run() { f() } }
extension NSButton {
    func actionClosure(_ f: @escaping () -> Void) {
        let t = ClosureTarget(f); objc_setAssociatedObject(self, &closureKey, t, .OBJC_ASSOCIATION_RETAIN); target = t; action = #selector(ClosureTarget.run)
    }
}

// MARK: - mascot

final class Mascot: NSObject, NSTextFieldDelegate, NSWindowDelegate {
    static let poseFor = ["idle": "idle", "thinking": "think", "working": "work", "success": "done", "error": "error", "waiting": "wait"]
    static let lines = ["idle": "Ну что, погнали?", "thinking": "думаю…", "working": "кручу братишку", "success": "готово!", "error": "ой…", "waiting": "твой ход"]
    static let idleLines: [String: (String, Double)] = ["milk": ("молочко…", 4), "home": ("урааа, домой!", 0), "gone": ("ушёл домой, до завтра", 0),
                                                        "lunch": ("обед, не беспокоить", 0), "night": ("иди спать, ночь на дворе", 5)]
    static let pokeLines = ["не тыкай", "ммм?", "отстань", "щекотно", "я занят, вообще-то"]
    static let wisdom = ["баг, который не воспроизводится, всё ещё баг", "лучший код — ненаписанный", "сначала прочитай ошибку целиком",
                         "коммить маленькими кусками", "тесты — это письма себе в будущее", "если не понятно — назови переменную лучше", "банан в обед, рефакторинг после"]
    static func unlocked(_ acc: String, _ s: Stats) -> Bool {
        switch acc {
        case "glasses": return level(s["Xp"]) >= 2
        case "scarf": return level(s["Xp"]) >= 3
        case "headphones": return level(s["Xp"]) >= 4
        case "crown": return level(s["Xp"]) >= 5 || s["GoldenBananas"] >= 3
        default: return true
        }
    }

    let builtIn: BuiltIn
    var characters: [MascotCharacter] = [], plugin: MascotCharacter?
    let panel: NSPanel
    let sprite = SpriteView(frame: .zero)
    let bubble = BubbleView(frame: NSRect(x: 0, y: 250, width: 240, height: 150))
    let bar = NSVisualEffectView()
    var barFull = NSRect.zero, barShown = false, hoverLostAt: Date?
    var chevron: NSButton?, focusButton: NSButton?
    var askPanel: KeyPanel?, askAnswer: NSTextView?, settingsWindow: NSWindow?, progressWindow: NSWindow?
    var statusItem: NSStatusItem?

    let winW: CGFloat = 240, winH: CGFloat = 400
    var cfg: [String: Any] = [:]
    var cfgRight: CGFloat = 40, cfgBottom: CGFloat = 90, compact = false, sizeMul = 1.0, soundOn = false, healthOn = true
    var accChoice = "auto", city = "", homeAfter = 18 * 60 + 30, lunchFrom = 13 * 60, lunchTo = 14 * 60 + 30, weekdaysOnly = true
    var state = "idle", poseOverride: String?, lastIdlePose = ""
    var stateAt = Date(), bubbleUntil = Date.distantPast, pokeUntil = Date.distantPast, pokePose = "love"
    var homeStartedAt: Date?, lastMoodAction = Date.distantPast, lastUsageWarn = Date.distantPast
    var bubbleBase = "", bubbleTimer = false, lastEventTs: Int64 = 0, turnStart: Int64 = 0, mood = 60.0, xp = 0, goldenBananas = 0
    var pending: [[String: Any]] = []
    var k = 0
    var claude: ClaudeWindow?
    var lastActivity = Date.distantPast, activityStart = Date.distantPast, lastStretch = Date(), lastWater = Date()
    var focus = "off", focusEnd = Date()
    // Spotify
    var np: NowPlaying?, song = "", coverURL = "", coverImage: CGImage?, coverPixel: CGImage?, musicAt = Date.distantPast
    let playerBar = NSVisualEffectView(), playerCover = NSImageView(), playerFill = NSView()
    let playerTitle = NSTextField(labelWithString: ""), playerArtist = NSTextField(labelWithString: ""), playerPos = NSTextField(labelWithString: ""), playerLeft = NSTextField(labelWithString: "")
    var playButton: NSButton?
    // slot machine: reel i eases from slotFrom by slotDist and stops at slotStop(i)
    var slotPos = [0.0, 0.0, 0.0], slotFrom = [0.0, 0.0, 0.0], slotDist = [0.0, 0.0, 0.0], slotT0: Date?, slotResult = [0, 0, 0], slotResolved = true, slotStopped = 0
    var weather: Weather?, weatherAt = Date.distantPast, usageFh = -1, usageSd = -1, usageAt = Date.distantPast
    var dropList = ""

    init(builtIn: BuiltIn) {
        self.builtIn = builtIn
        panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 240, height: 400), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        super.init()
        characters = MascotCharacter.discover()
        loadConfig()
        let s = Stats.load(); mood = s.mood; xp = s["Xp"]; goldenBananas = s["GoldenBananas"]
        lastEventTs = Int64(num(readJSON(dataFile("event.json"))?["ts"]))
        panel.isOpaque = false; panel.backgroundColor = .clear; panel.hasShadow = false
        panel.level = .normal; panel.hidesOnDeactivate = false; panel.becomesKeyOnlyIfNeeded = true
        panel.collectionBehavior = [.fullScreenAuxiliary, .ignoresCycle]
        let root = NSView(frame: NSRect(x: 0, y: 0, width: winW, height: winH)); panel.contentView = root

        sprite.onClick = { [weak self] in self?.poke() }
        sprite.onDoubleClick = { [weak self] in self?.openAsk() }
        sprite.onDragEnd = { [weak self] in self?.saveOffsetFromWindow() }
        sprite.onDrop = { [weak self] urls in self?.dropped(urls) }
        sprite.menuProvider = { [weak self] in self?.menu() ?? NSMenu() }
        root.addSubview(sprite); root.addSubview(bubble)
        buildBar(in: root)
        applySize()
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
        buildStatusItem()

        say(Self.lines["idle"]!, 4)
        Timer.scheduledTimer(withTimeInterval: 1.0 / 9, repeats: true) { [weak self] _ in self?.tick() }
        Timer.scheduledTimer(withTimeInterval: 0.15, repeats: true) { [weak self] _ in self?.readEvents(); self?.updateState(); self?.place() }
        Timer.scheduledTimer(withTimeInterval: 0.04, repeats: true) { [weak self] _ in self?.checkHover() }
        Timer.scheduledTimer(withTimeInterval: 20, repeats: true) { [weak self] _ in self?.slowChecks() }
        slowChecks()
    }

    var lvl: Int { level(xp) }
    var voice: String { plugin?.sound ?? "monkey" }
    var tint: NSColor { NSColor(srgbRed: 0.79, green: 0.776, blue: 0.737, alpha: 1) }

    // MARK: layout

    func applySize() {
        let stage = stageForLevel(lvl)
        let scale = (compact ? 2.5 : 4.0 + Double(min(6, lvl - 1)) * 0.1) * sizeMul * (stage == "baby" ? 0.85 : stage == "guru" ? 1.05 : 1)
        let w = CGFloat(40 * scale), h = CGFloat(44 * scale)
        sprite.frame = NSRect(x: (winW - w) / 2, y: 44, width: w, height: h)
        sprite.smooth = plugin.map { !$0.pixelArt } ?? false
        bubble.frame.origin.y = sprite.frame.maxY
        chevron?.image = NSImage(systemSymbolName: compact ? "chevron.down" : "chevron.up", accessibilityDescription: nil)
    }

    func buildBar(in root: NSView) {
        bar.material = .hudWindow; bar.blendingMode = .behindWindow; bar.state = .active
        bar.appearance = NSAppearance(named: .darkAqua)
        bar.wantsLayer = true
        bar.layer?.borderColor = NSColor(white: 1, alpha: 0.18).cgColor; bar.layer?.borderWidth = 1
        bar.layer?.cornerRadius = 17; bar.layer?.masksToBounds = true
        let items: [(String, String, Selector)] = [("square.and.pencil", "Новый чат", #selector(newChat)), ("waveform", "Диктовка", #selector(voiceInput)),
                                                  ("timer", "Помодоро: 25 минут фокуса", #selector(toggleFocus)), ("7", "Крутануть автомат", #selector(spinSlot)),
                                                  ("chevron.up", "Свернуть", #selector(toggleCompact))]
        var x: CGFloat = 4
        for (i, item) in items.enumerated() {
            let b = item.0 == "7" ? NSButton(title: "7", target: self, action: item.2)
                                  : NSButton(image: NSImage(systemSymbolName: item.0, accessibilityDescription: item.1) ?? NSImage(), target: self, action: item.2)
            if item.0 == "7" {
                b.attributedTitle = NSAttributedString(string: "7", attributes: [.font: NSFont.systemFont(ofSize: 16, weight: .black),
                                                                                  .foregroundColor: NSColor(srgbRed: 0.9, green: 0.39, blue: 0.35, alpha: 1)])
            }
            b.isBordered = false; b.toolTip = item.1; b.contentTintColor = tint
            b.frame = NSRect(x: x, y: 3, width: 40, height: 28); b.autoresizingMask = [.minXMargin, .maxXMargin]
            bar.addSubview(b)
            if i == 2 { focusButton = b }; if item.2 == #selector(toggleCompact) { chevron = b }
            x += 40
            if i < items.count - 1 {
                let sep = NSView(frame: NSRect(x: x, y: 9, width: 1, height: 16))
                sep.wantsLayer = true; sep.layer?.backgroundColor = NSColor(white: 1, alpha: 0.15).cgColor; sep.autoresizingMask = [.minXMargin, .maxXMargin]
                bar.addSubview(sep); x += 1
            }
        }
        barFull = NSRect(x: (winW - (x + 4)) / 2, y: 4, width: x + 4, height: 34)
        bar.frame = collapsedBarFrame(); bar.alphaValue = 0; bar.isHidden = true
        root.addSubview(bar)
        buildPlayer(in: root)
    }

    // Mini player above the hover bar while Spotify runs: cover, track, artist, progress with times, ⏮ ⏯ ⏭.
    func buildPlayer(in root: NSView) {
        let W: CGFloat = 200, H: CGFloat = 104
        playerBar.material = .hudWindow; playerBar.blendingMode = .behindWindow; playerBar.state = .active
        playerBar.appearance = NSAppearance(named: .darkAqua); playerBar.wantsLayer = true
        playerBar.layer?.borderColor = NSColor(white: 1, alpha: 0.18).cgColor; playerBar.layer?.borderWidth = 1
        playerBar.layer?.cornerRadius = 14; playerBar.layer?.masksToBounds = true
        playerBar.frame = NSRect(x: (winW - W) / 2, y: barFull.maxY + 6, width: W, height: H)
        playerCover.frame = NSRect(x: 10, y: H - 50, width: 40, height: 40); playerCover.imageScaling = .scaleProportionallyUpOrDown
        playerCover.wantsLayer = true; playerCover.layer?.cornerRadius = 6; playerCover.layer?.masksToBounds = true
        playerTitle.frame = NSRect(x: 58, y: H - 30, width: W - 66, height: 17); playerTitle.font = .systemFont(ofSize: 13, weight: .semibold); playerTitle.textColor = .white
        playerArtist.frame = NSRect(x: 58, y: H - 47, width: W - 66, height: 15); playerArtist.font = .systemFont(ofSize: 11); playerArtist.textColor = .secondaryLabelColor
        for l in [playerTitle, playerArtist] { l.lineBreakMode = .byTruncatingTail }
        let track = NSView(frame: NSRect(x: 10, y: H - 60, width: W - 20, height: 4)); track.wantsLayer = true
        track.layer?.backgroundColor = NSColor(white: 1, alpha: 0.19).cgColor; track.layer?.cornerRadius = 2
        playerFill.frame = NSRect(x: 0, y: 0, width: 0, height: 4); playerFill.wantsLayer = true
        playerFill.layer?.backgroundColor = NSColor(srgbRed: 0.114, green: 0.725, blue: 0.329, alpha: 1).cgColor; playerFill.layer?.cornerRadius = 2
        track.addSubview(playerFill)
        playerPos.frame = NSRect(x: 10, y: H - 74, width: 60, height: 13); playerLeft.frame = NSRect(x: W - 70, y: H - 74, width: 60, height: 13); playerLeft.alignment = .right
        for l in [playerPos, playerLeft] { l.font = .monospacedDigitSystemFont(ofSize: 10, weight: .regular); l.textColor = .secondaryLabelColor }
        let buttons: [(String, Selector, CGFloat)] = [("backward.fill", #selector(musicPrev), W / 2 - 50), ("pause.fill", #selector(musicToggle), W / 2 - 15),
                                                      ("forward.fill", #selector(musicNext), W / 2 + 20)]
        for (sym, sel, x) in buttons {
            let b = NSButton(image: NSImage(systemSymbolName: sym, accessibilityDescription: nil) ?? NSImage(), target: self, action: sel)
            b.isBordered = false; b.contentTintColor = sel == #selector(musicToggle) ? .white : tint
            b.frame = NSRect(x: x, y: 2, width: 30, height: 26)
            if sel == #selector(musicToggle) { playButton = b }
            playerBar.addSubview(b)
        }
        for v in [playerCover, playerTitle, playerArtist, track, playerPos, playerLeft] as [NSView] { playerBar.addSubview(v) }
        playerBar.alphaValue = 0; playerBar.isHidden = true
        root.addSubview(playerBar)
    }

    func collapsedBarFrame() -> NSRect { NSRect(x: barFull.midX - barFull.width * 0.1, y: barFull.minY + barFull.height * 0.2, width: barFull.width * 0.2, height: barFull.height * 0.6) }

    // Hover is tracked from the cursor position so the empty area stays click-through.
    func checkHover() {
        guard panel.isVisible else { return }
        let p = panel.convertPoint(fromScreen: NSEvent.mouseLocation)
        let zone = NSRect(x: sprite.frame.minX - 10, y: 0, width: sprite.frame.width + 20, height: sprite.frame.minY + sprite.frame.height * 0.85)
        if zone.contains(p) { hoverLostAt = nil; showBar(true) }
        else if barShown { if hoverLostAt == nil { hoverLostAt = Date() } else if Date().timeIntervalSince(hoverLostAt!) > 0.25 { showBar(false) } }
    }

    // Grows from the centre on hover, shrinks back to the centre and fades when the cursor leaves.
    func showBar(_ show: Bool) {
        guard show != barShown else { return }
        barShown = show
        if show { bar.isHidden = false }
        let withPlayer = show && np != nil
        if withPlayer { playerBar.isHidden = false }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = show ? 0.2 : 0.16
            ctx.timingFunction = CAMediaTimingFunction(name: show ? .easeOut : .easeIn)
            bar.animator().frame = show ? barFull : collapsedBarFrame()
            bar.animator().alphaValue = show ? 1 : 0
            playerBar.animator().alphaValue = withPlayer ? 1 : 0
        }, completionHandler: { [weak self] in guard let self = self, !self.barShown else { return }; self.bar.isHidden = true; self.playerBar.isHidden = true })
    }

    func menu() -> NSMenu {
        let m = NSMenu()
        for (title, sel) in [("Спросить Синсина…", #selector(openAsk)), ("Статистика за сегодня", #selector(showStats)), ("Квест дня", #selector(showQuest)),
                             ("Путь Синсина (уровни)", #selector(openProgress)), ("Ачивки", #selector(showAchievements)), ("Отчёт за неделю", #selector(openReport)),
                             ("Крутануть автомат", #selector(spinSlot))] {
            m.addItem(withTitle: title, action: sel, keyEquivalent: "").target = self
        }
        m.addItem(.separator())
        m.addItem(withTitle: "Настройки…", action: #selector(openSettings), keyEquivalent: "").target = self
        m.addItem(withTitle: "Вернуть на место", action: #selector(resetPosition), keyEquivalent: "").target = self
        m.addItem(withTitle: "Закрыть", action: #selector(quit), keyEquivalent: "").target = self
        return m
    }

    func buildStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let f = builtIn.frames("idle", "none").first { item.button?.image = NSImage(cgImage: f, size: NSSize(width: 18, height: 20)) }
        item.menu = menu(); statusItem = item
    }

    // MARK: bubble, appearance, poses

    func say(_ text: String, _ seconds: Double) {
        bubbleBase = text; bubbleTimer = false; bubble.text = text
        bubbleUntil = seconds <= 0 ? .distantFuture : Date().addingTimeInterval(seconds)
    }
    func beep(_ name: String) { if soundOn && focus != "focus" { Synth.play(name, voice) } }

    func currentAcc() -> String {
        if focus == "focus" { return "headphones" }
        if accChoice != "auto" { return Self.unlocked(accChoice, Stats(d: ["Xp": xp, "GoldenBananas": goldenBananas])) ? accChoice : "none" }
        let c = Calendar.current.dateComponents([.month, .day], from: Date())
        if weather?.rain == true { return "umbrella" }
        if (c.month == 12 && (c.day ?? 0) >= 15) || (c.month == 1 && (c.day ?? 99) <= 10) { return "santa" }
        if weather?.snow == true || weather?.cold == true { return "scarf" }
        return "none"
    }

    func scheduleDay() -> Bool { let wd = Calendar.current.component(.weekday, from: Date()); return !weekdaysOnly || (wd != 1 && wd != 7) }

    func currentPose() -> String {
        if Date() < pokeUntil { return pokePose }
        if state != "idle" { return poseOverride ?? Self.poseFor[state] ?? "idle" }
        if focus == "focus" { return "type" }
        let c = Calendar.current.dateComponents([.hour, .minute], from: Date())
        let hour = c.hour ?? 12, minutes = hour * 60 + (c.minute ?? 0), idleFor = Date().timeIntervalSince(stateAt)
        if scheduleDay() && minutes >= homeAfter && idleFor > 10 {
            if homeStartedAt == nil { homeStartedAt = Date(); k = 0 }
            return Date().timeIntervalSince(homeStartedAt!) < Double(builtIn.manifest.homeFrames ?? 48) / 9 ? "home" : "gone"
        }
        if scheduleDay() && minutes >= lunchFrom && minutes < lunchTo && idleFor > 10 { return "lunch" }
        if !song.isEmpty { return "spotify" }
        if (hour >= 23 || hour < 6) && idleFor > 20 { return "sleep" }
        if idleFor > 180 { return "sleep" }
        if idleFor > 60 { return "milk" }
        return "idle"
    }

    func frame(_ pose: String) -> CGImage? {
        if pose == "gone" { return nil }
        if let p = plugin {
            let f = p.frames(p.resolve(pose)); guard !f.isEmpty else { return nil }
            if pose == "home" { return f[min(f.count - 1, Int(Date().timeIntervalSince(homeStartedAt ?? Date()) * p.fps))] }
            return f[Int(Date().timeIntervalSince1970 * p.fps) % f.count]
        }
        let f = builtIn.frames(pose, currentAcc(), level: lvl); guard !f.isEmpty else { return nil }
        return f[min(k, f.count - 1)]
    }

    func tick() {
        let pose = currentPose()
        if state == "idle" && pose != lastIdlePose {
            let h = Calendar.current.component(.hour, from: Date())
            let key = pose == "sleep" && (h >= 23 || h < 6) ? "night" : pose
            if let l = Self.idleLines[key] { say(l.0, l.1) } else if ["gone", "lunch", "home"].contains(lastIdlePose) { bubbleUntil = .distantPast }
            lastIdlePose = pose
        }
        if state != "idle" { lastIdlePose = "" }
        let frames = builtIn.manifest.frames, homeFrames = builtIn.manifest.homeFrames ?? frames
        k = pose == "home" ? min(k + 1, homeFrames - 1) : (k + 1) % frames
        let auraOn = pose != "gone" && auraCount(lvl) > 0
        let ab = auraOn ? builtIn.aura(lvl, front: false) : [], af = auraOn ? builtIn.aura(lvl, front: true) : []
        sprite.back = ab.isEmpty ? nil : ab[k % ab.count]; sprite.front = af.isEmpty ? nil : af[k % af.count]
        if Date().timeIntervalSince(musicAt) > 1.5 { musicAt = Date(); checkMusic() }
        sprite.cover = pose == "spotify" && plugin == nil ? coverPixel : nil
        slotTick(pose)
        sprite.image = frame(pose)
        if bubbleTimer && turnStart > 0 && (state == "thinking" || state == "working") {
            let ms = nowMs() - turnStart
            if ms > 20_000 { bubble.text = bubbleBase + " " + clockText(ms) }
        }
        bubble.isHidden = compact || Date() > bubbleUntil || focus == "focus"
        moodLife()
    }

    func moodLife() {
        guard state == "idle", Date() > pokeUntil, Date().timeIntervalSince(lastMoodAction) > 45, Date().timeIntervalSince(stateAt) > 15, focus != "focus", currentPose() == "idle" else { return }
        lastMoodAction = Date()
        if mood < 30 { say(Bool.random() ? "грустно… дай банан" : "кликни меня, я голодный", 4) }
        else if mood > 80 { pokePose = Bool.random() ? "happy" : "love"; pokeUntil = Date().addingTimeInterval(3); k = 0 }
    }

    // MARK: slow checks: focus timer, health, report, usage, weather

    func slowChecks() {
        let now = Date()
        if focus != "off" && now >= focusEnd {
            if focus == "focus" {
                focus = "break"; focusEnd = now.addingTimeInterval(300)
                pokePose = "stretch"; pokeUntil = now.addingTimeInterval(8); k = 0; say("перерыв 5 минут! разомнись", 8); notify("Помодоро: перерыв 5 минут"); beep("level")
            } else { focus = "off"; say("погнали дальше?", 4) }
            focusButton?.contentTintColor = tint
        }
        let active = now.timeIntervalSince(lastActivity) < 600
        if healthOn && active && focus != "focus" {
            if now.timeIntervalSince(activityStart) >= 5400 && now.timeIntervalSince(lastStretch) >= 5400 {
                lastStretch = now; pokePose = "stretch"; pokeUntil = now.addingTimeInterval(7); k = 0
                say("полтора часа без перерыва. встань, разомнись!", 8); notify("Синсин: встань, разомнись"); beep("poke")
            } else if now.timeIntervalSince(activityStart) >= 3600 && now.timeIntervalSince(lastWater) >= 3600 {
                lastWater = now; pokePose = "water"; pokeUntil = now.addingTimeInterval(6); k = 0; say("попей водички", 6)
            }
        }
        let c = Calendar.current.dateComponents([.weekday, .hour], from: now)
        if c.weekday == 6 && (c.hour ?? 0) >= 17 && state == "idle" && (cfg["lastReport"] as? String) != todayKey() {
            cfg["lastReport"] = todayKey(); saveConfig(); weeklyReport(open: false)
        }
        if now.timeIntervalSince(usageAt) >= 120 { usageAt = now; readUsage() }
        if state == "idle" && now.timeIntervalSince(lastUsageWarn) > 1200 && focus != "focus" {
            if usageFh >= 80 { lastUsageWarn = now; pokePose = "think"; pokeUntil = now.addingTimeInterval(5); say("5-часовой лимит: \(usageFh)%. береги токены", 6) }
            else if usageSd >= 90 { lastUsageWarn = now; pokePose = "think"; pokeUntil = now.addingTimeInterval(5); say("недельный лимит: \(usageSd)%. экономим", 6) }
        }
        if !city.isEmpty && now.timeIntervalSince(weatherAt) >= 1800 {
            weatherAt = now; let c = city
            DispatchQueue.global().async { let w = fetchWeather(c); DispatchQueue.main.async { self.weather = w } }
        }
    }

    func readUsage() {
        let f = home.appendingPathComponent("Library/Application Support/Claude/plan-usage-history.json")
        guard let d = readJSON(f), let last = (d["samples"] as? [[String: Any]])?.last, let u = last["u"] as? [String: Any] else { return }
        let ageH = Double(nowMs() - Int64(num(last["t"]))) / 3_600_000
        usageFh = ageH < 5 && u["fh"] != nil ? Int(num(u["fh"])) : -1
        usageSd = ageH < 48 && u["sd"] != nil ? Int(num(u["sd"])) : -1
    }

    // MARK: state & events

    func setState(_ s: String) {
        guard Self.poseFor[s] != nil else { return }
        if s == state && s != "success" && s != "error" { return }
        state = s; stateAt = Date(); k = 0; poseOverride = nil
        if s != "idle" { homeStartedAt = nil }
        let sticky = s == "working" || s == "thinking" || s == "waiting"
        say(plugin?.lines[s] ?? Self.lines[s]!, sticky ? 0 : (s == "idle" ? 0.01 : 4))
    }

    func apply(_ e: [String: Any]) {
        let now = Date()
        if now.timeIntervalSince(lastActivity) > 600 { activityStart = now }
        lastActivity = now
        if e["mood"] != nil { mood = num(e["mood"]) }
        if e["turnStart"] != nil { turnStart = Int64(num(e["turnStart"])) }
        if e["xp"] != nil { let before = lvl; xp = Int(num(e["xp"])); if lvl != before { applySize() } }
        let seconds = num(e["seconds"])
        if let s = e["state"] as? String { setState(s) }
        if let p = e["pose"] as? String, builtIn.manifest.poses.contains(p) {
            if seconds > 0 { pokePose = p; pokeUntil = now.addingTimeInterval(seconds); k = 0 } else { poseOverride = p }
        }
        if let line = e["line"] as? String {
            let sticky = seconds <= 0 && ["working", "thinking", "waiting"].contains(state)
            say(line, sticky ? 0 : (seconds > 0 ? seconds : 4)); bubbleTimer = sticky
        }
        if let snd = e["sound"] as? String { beep(snd) }
        if e["achievement"] != nil || (e["line"] as? String)?.hasPrefix("квест") == true { goldenBananas = Stats.load()["GoldenBananas"] }
        if let n = e["notify"] as? String { notify(n) }
    }

    func readEvents() {
        guard let e = readJSON(dataFile("event.json")) else { return }
        let ts = Int64(num(e["ts"]))
        guard ts > lastEventTs else { return }
        lastEventTs = ts
        if nowMs() - ts < 15 * 60 * 1000 { pending.append(e) }
    }

    func updateState() {
        var held = Date().timeIntervalSince(stateAt)
        while let e = pending.first {
            if e["state"] != nil && (state == "success" || state == "error") && held < 2.2 { break }
            apply(pending.removeFirst()); held = Date().timeIntervalSince(stateAt)
        }
        if state == "success" && held > 4 && pending.isEmpty && Date() >= bubbleUntil { setState("idle") }
        else if state == "success" && held > 9 { setState("idle") }
        else if state == "error" && held > 6 { setState("idle") }
        else if (state == "thinking" || state == "working") && held > 600 { setState("idle") }
    }

    func notify(_ text: String) {
        if Date() >= pokeUntil { pokePose = "wave"; pokeUntil = Date().addingTimeInterval(3); k = 0 }   // don't cut off a level-up / sticker pose
        if NSWorkspace.shared.frontmostApplication?.localizedName == "Claude" { return }
        let c = UNMutableNotificationContent(); c.title = "Синсин"; c.body = text
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: c, trigger: nil))
    }

    // MARK: interactions

    func poke() {
        k = 0; beep("poke")
        if lastIdlePose == "gone" { say("я ушёл, завтра приходи", 3); return }
        if mood < 50 {
            pokePose = "banana"; pokeUntil = Date().addingTimeInterval(3); say("ням, спасибо!", 3)
            mood = withStats { st in st.mood += 6; st["Bananas"] += 1 }.mood
            return
        }
        var tricks = ["wave", "happy"]
        if lvl >= 2 { tricks.append("love") }; if lvl >= 3 { tricks.append("git_rebase") }; if lvl >= 4 { tricks.append("run") }; if lvl >= 5 { tricks.append("done") }
        if lvl >= 6 { tricks.append("music") }; if lvl >= 8 { tricks.append("jump") }; if lvl >= 10 { tricks.append("secret") }
        pokePose = tricks.randomElement()!; pokeUntil = Date().addingTimeInterval(2.4)
        let wise = lvl >= 11 && Double.random(in: 0..<1) < 0.35
        if state == "idle" && focus != "focus" { say((wise ? Self.wisdom : Self.pokeLines).randomElement()!, wise ? 5 : 2) }
    }

    // MARK: Spotify

    func checkMusic() {
        guard cfg["music"] as? Bool ?? true else { if np != nil { np = nil; setSong(""); updatePlayer() }; return }
        np = readSpotify()
        if let n = np, !n.artURL.isEmpty, n.artURL != coverURL, let u = URL(string: n.artURL) {
            coverURL = n.artURL; coverImage = nil; coverPixel = nil
            URLSession.shared.dataTask(with: u) { [weak self] d, _, _ in
                guard let d = d, let img = NSImage(data: d)?.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return }
                DispatchQueue.main.async { self?.coverImage = img; self?.coverPixel = pixelated(img, 10); self?.updatePlayer() }
            }.resume()
        }
        let now = np.map { $0.playing && !$0.title.isEmpty ? ($0.artist.isEmpty ? "" : $0.artist + " — ") + $0.title : "" } ?? ""
        setSong(now); updatePlayer()
    }
    func setSong(_ now: String) {
        guard now != song else { return }
        song = now
        if !song.isEmpty && state == "idle" && focus != "focus" { say("♪ " + short(song, 44), 5) }
    }
    func updatePlayer() {
        guard let n = np else { playerBar.isHidden = true; playerBar.alphaValue = 0; return }
        if barShown && playerBar.isHidden { playerBar.isHidden = false; playerBar.alphaValue = 1 }
        playerTitle.stringValue = n.title.isEmpty ? "Spotify" : n.title; playerArtist.stringValue = n.artist
        playerCover.image = coverImage.map { NSImage(cgImage: $0, size: NSSize(width: 40, height: 40)) }
        playButton?.image = NSImage(systemSymbolName: n.playing ? "pause.fill" : "play.fill", accessibilityDescription: nil)
        let frac = n.duration > 0 ? max(0, min(1, n.position / n.duration)) : 0
        playerFill.frame.size.width = (playerFill.superview?.bounds.width ?? 180) * CGFloat(frac)
        playerPos.stringValue = n.duration > 0 ? mmss(n.position) : ""; playerLeft.stringValue = n.duration > 0 ? "-" + mmss(n.duration - n.position) : ""
    }
    func mmss(_ s: Double) -> String { let t = Int(max(0, s)); return "\(t / 60):" + String(format: "%02d", t % 60) }
    @objc func musicPrev() { spotify("previous track"); musicReact("prev") }
    @objc func musicNext() { spotify("next track"); musicReact("next") }
    @objc func musicToggle() { spotify("playpause"); musicReact("toggle") }
    func musicReact(_ what: String) {
        if what == "toggle" {
            let was = np?.playing == true
            pokePose = was ? "wave" : "happy"; pokeUntil = Date().addingTimeInterval(1.5); say(was ? "пауза" : "погнали!", 2)
        } else { pokePose = "jump"; pokeUntil = Date().addingTimeInterval(1.2); say(what == "next" ? "некст!" : "давай ещё раз эту", 2) }
        k = 0; musicAt = .distantPast
    }

    // MARK: slot machine (like Telegram's 🎰): pull the lever, three reels spin and stop one by one

    func slotStop(_ i: Int) -> Double { 1.0 + 0.45 * Double(i) }
    @objc func spinSlot() {
        if plugin != nil { say("автомат только у Синсина", 3); return }
        if slotT0 != nil && !slotResolved { return }
        let n = Double(slotSymbols.count)
        for i in 0..<3 {
            slotResult[i] = Int.random(in: 0..<slotSymbols.count)
            let cur = (slotPos[i].truncatingRemainder(dividingBy: n) + n).truncatingRemainder(dividingBy: n)
            let extra = ((Double(slotResult[i]) - cur).truncatingRemainder(dividingBy: n) + n).truncatingRemainder(dividingBy: n)
            slotFrom[i] = cur; slotDist[i] = extra + n * Double(3 + i)
        }
        slotT0 = Date(); slotResolved = false; slotStopped = 0
        pokePose = "slot"; pokeUntil = Date().addingTimeInterval(slotStop(2) + 0.2); k = 0; bubbleUntil = .distantPast
        beep("poke")
    }
    func slotTick(_ pose: String) {
        guard let t0 = slotT0 else { sprite.reels = nil; return }
        let t = Date().timeIntervalSince(t0)
        for i in 0..<3 {
            let x = min(1, t / slotStop(i))
            slotPos[i] = slotFrom[i] + slotDist[i] * (1 - pow(1 - x, 3))
            if x >= 1 && slotStopped == i { slotStopped += 1; slotPos[i] = Double(slotResult[i]); beep("reel") }
        }
        if sprite.reelRows.isEmpty { sprite.reelRows = builtIn.slotRows }
        sprite.reels = pose == "slot" && plugin == nil ? slotPos : nil
        if !slotResolved && t >= slotStop(2) { slotResolved = true; slotPayout() }
    }
    // Prizes: 777 — golden banana + 50 XP, three of a kind — 5 bananas + 10 XP, a pair — 1 banana; prizes stop after 30 spins a day.
    func slotPayout() {
        let a = slotResult[0], b = slotResult[1], c = slotResult[2]
        let triple = a == b && b == c, jackpot = triple && slotSymbols[a] == "seven", pair = !triple && (a == b || b == c || a == c)
        var e: [String: Any]? = [:]; var prizes = true
        let s = withStats { st in
            if (st.d["SlotDay"] as? String) != todayKey() { st.d["SlotDay"] = todayKey(); st["SlotSpins"] = 0 }
            st["SlotSpins"] += 1; prizes = st["SlotSpins"] <= 30
            guard prizes else { return }
            if jackpot { st["GoldenBananas"] += 1; st["Jackpots"] += 1; addXp(&st, &e, 50); var none: [String: Any]? = nil; sticker(&st, &none, "jackpot", true) }
            else if triple { st["Bananas"] += 5; addXp(&st, &e, 10) }
            else if pair { st["Bananas"] += 1 }
        }
        xp = s["Xp"]; goldenBananas = s["GoldenBananas"]
        var line: String, react: String, secs = 3.5
        if jackpot { line = "777! ДЖЕКПОТ! золотой банан и +50 опыта"; react = "secret"; secs = 5; beep("jackpot"); notify("Синсин выбил 777!") }
        else if triple { line = "три в ряд! +5 бананов"; react = "happy"; beep("level") }
        else if pair { line = "пара! +1 банан"; react = "love" }
        else { line = ["эх, мимо", "ну почти…", "автомат подкручен", "ещё разок?"].randomElement()!; react = "error" }
        if !prizes && (jackpot || triple || pair) { line += "\n(призы на сегодня кончились, крутим для души)" }
        pokeUntil = Date().addingTimeInterval(1.0)
        let levelLine = e?["levelUp"] != nil ? e?["line"] as? String : nil
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.9) { [weak self] in
            guard let self = self else { return }
            self.pokePose = react; self.pokeUntil = Date().addingTimeInterval(secs); self.k = 0; self.say(line, secs + 1)
            if let l = levelLine { self.applySize(); self.say(l, 7); self.pokePose = "levelup"; self.pokeUntil = Date().addingTimeInterval(7); self.beep("level") }
        }
    }

    // Dropped files: pick an action, the prompt is pasted into Claude's input (you press Return).
    func dropped(_ urls: [URL]) {
        dropList = urls.prefix(5).map { $0.path }.joined(separator: ", ")
        pokePose = "read"; pokeUntil = Date().addingTimeInterval(3); k = 0
        let m = NSMenu()
        for (i, t) in ["Объясни", "Найди баги", "Сделай ревью", "Напиши тесты"].enumerated() {
            let it = NSMenuItem(title: t, action: #selector(dropAction(_:)), keyEquivalent: ""); it.target = self; it.tag = i; m.addItem(it)
        }
        m.popUp(positioning: nil, at: NSPoint(x: sprite.frame.midX, y: sprite.frame.midY), in: panel.contentView)
    }
    @objc func dropAction(_ item: NSMenuItem) {
        let prefixes = ["Объясни, что делает этот файл: ", "Найди баги и проблемы в файле: ", "Сделай код-ревью файла: ", "Напиши тесты для файла: "]
        typeIntoClaude(prefixes[item.tag] + dropList)
    }
    func typeIntoClaude(_ text: String) {
        guard focusClaude() else { say("не вижу окно Claude", 3); return }
        let pb = NSPasteboard.general, old = pb.string(forType: .string)
        pb.clearContents(); pb.setString(text, forType: .string)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { postKeys(9, flags: .maskCommand) }   // Cmd+V
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.9) { if let o = old { pb.clearContents(); pb.setString(o, forType: .string) } }
        say("вставил в Claude, жми Return", 4)
    }

    @objc func showStats() {
        let s = Stats.load(); mood = s.mood; xp = s["Xp"]
        let usage = usageFh >= 0 || usageSd >= 0 ? "\nлимиты: 5ч \(usageFh >= 0 ? "\(usageFh)%" : "—") · неделя \(usageSd >= 0 ? "\(usageSd)%" : "—")" : ""
        let from = xpForLevel(lvl), to = xpForLevel(lvl + 1)
        say("ур. \(lvl) · \(rankTitle(lvl)) · опыт \(xp - from)/\(to - from)\nзадач: \(s["Tasks"]) · правок: \(s["Edits"])\nкоммитов: \(s["Commits"]) · пушей: \(s["Pushes"]) · ошибок: \(s["Errors"])\nнастроение: \(Int(s.mood.rounded())) · бананов: \(s["Bananas"]) (золотых \(s["GoldenBananas"]))" + usage, 10)
    }
    @objc func showQuest() {
        let s = Stats.load(), q = s.quest
        let st = (s.d["QuestDone"] as? Bool) == true ? "выполнен! золотой банан твой" : (s.d["QuestFailed"] as? Bool) == true ? "провален, завтра новый" : "прогресс: \(min(s["QuestProgress"], q.target))/\(q.target)"
        say("квест дня: \(q.title)\n" + st, 7)
    }
    @objc func showAchievements() {
        let s = Stats.load()
        var shown: [String] = []
        for a in s.achievements where !a.contains("-") || a.hasSuffix(s.day) { let t = achievementTitle(a) + (a.contains("-") ? " (сегодня)" : ""); if !shown.contains(t) { shown.append(t) } }
        say(shown.isEmpty ? "ачивок пока нет, работаем" : "ачивки:\n" + shown.prefix(6).joined(separator: "\n") + (shown.count > 6 ? "\n…и ещё \(shown.count - 6)" : ""), 10)
    }
    @objc func openReport() { weeklyReport(open: true) }

    // "Путь Синсина": current rank, XP bar, how XP is earned, and every level with a preview of how he looks there.
    @objc func openProgress() {
        progressWindow?.close()
        let s = Stats.load(); xp = s["Xp"]
        let l = lvl, from = xpForLevel(l), to = xpForLevel(l + 1)
        func rgb(_ r: Int, _ g: Int, _ b: Int) -> NSColor { NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: 1) }
        func preview(_ lv: Int, _ pose: String, _ kk: Int, _ scale: CGFloat) -> NSImageView {
            let pick: ([CGImage]) -> CGImage? = { $0.isEmpty ? nil : $0[kk % $0.count] }
            let layers = [pick(builtIn.aura(lv, front: false)), pick(builtIn.frames(pose, "none", level: lv)), pick(builtIn.aura(lv, front: true))]
            let iv = NSImageView(image: composite(layers, scale: scale))
            iv.imageScaling = .scaleNone; iv.frame.size = NSSize(width: 40 * scale, height: 44 * scale)
            return iv
        }
        func text(_ t: String, _ size: CGFloat, _ color: NSColor, bold: Bool = false, center: Bool = false, frame: NSRect) -> NSTextField {
            let f = NSTextField(wrappingLabelWithString: t); f.font = bold ? .systemFont(ofSize: size, weight: .semibold) : .systemFont(ofSize: size)
            f.textColor = color; f.alignment = center ? .center : .left; f.frame = frame; return f
        }
        let cardW: CGFloat = 128, cardH: CGFloat = 164, gap: CGFloat = 8, cols = 5
        let rows = (maxRank + cols - 1) / cols, W: CGFloat = 720, H = 190 + CGFloat(rows) * (cardH + gap) + 8
        let doc = FlippedView(frame: NSRect(x: 0, y: 0, width: W, height: H))

        // header: him now, title, XP bar
        let me = preview(l, "idle", 6, 3); me.frame.origin = NSPoint(x: 16, y: 16); doc.addSubview(me)
        doc.addSubview(text("Уровень \(l) · \(rankTitle(l))", 22, rgb(0xEE, 0xEB, 0xE3), bold: true, frame: NSRect(x: 152, y: 20, width: 540, height: 30)))
        let belt = l >= 4 ? beltNames[min(beltNames.count - 1, l - 4)] + " пояс · " : ""
        let stageName = ["baby": "малыш", "teen": "подросток", "adult": "взрослый", "guru": "гуру"][stageForLevel(l)] ?? ""
        doc.addSubview(text(belt + "стадия: " + stageName, 13, rgb(0x9C, 0x98, 0x8E), frame: NSRect(x: 152, y: 54, width: 540, height: 18)))
        let frac = max(0, min(1, Double(xp - from) / Double(max(1, to - from))))
        let track = NSView(frame: NSRect(x: 152, y: 80, width: 460, height: 14)); track.wantsLayer = true
        track.layer?.backgroundColor = rgb(0x2E, 0x2D, 0x2A).cgColor; track.layer?.cornerRadius = 7
        let fill = NSView(frame: NSRect(x: 0, y: 0, width: max(14, 460 * CGFloat(frac)), height: 14)); fill.wantsLayer = true
        fill.layer?.backgroundColor = rgb(0xF2, 0xC1, 0x4C).cgColor; fill.layer?.cornerRadius = 7
        track.addSubview(fill); doc.addSubview(track)
        doc.addSubview(text("опыт \(xp - from) / \(to - from) · дальше: \(rankTitle(l + 1)) — \(unlocks(l + 1))", 12, rgb(0xC9, 0xC6, 0xBC),
                            frame: NSRect(x: 152, y: 100, width: 540, height: 34)))
        doc.addSubview(text("опыт: задача +10 · PR +8 · коммит +5 · пуш +5 · мерж +4 · зелёные тесты +3 · каждые 5 правок +1 · квест дня +30",
                            11, rgb(0x8A, 0x87, 0x80), frame: NSRect(x: 152, y: 138, width: 540, height: 30)))

        // the path: every level as a card
        for lv in 1...maxRank {
            let got = lv <= l, cur = lv == l
            let x = 16 + CGFloat((lv - 1) % cols) * (cardW + gap), y = 190 + CGFloat((lv - 1) / cols) * (cardH + gap)
            let card = FlippedView(frame: NSRect(x: x, y: y, width: cardW, height: cardH)); card.wantsLayer = true
            card.layer?.cornerRadius = 10; card.layer?.borderWidth = cur ? 2 : 1
            card.layer?.backgroundColor = (cur ? rgb(0x3A, 0x33, 0x22) : got ? rgb(0x2E, 0x2D, 0x2A) : rgb(0x26, 0x25, 0x23)).cgColor
            card.layer?.borderColor = (cur ? rgb(0xF2, 0xC1, 0x4C) : got ? rgb(0x5A, 0x50, 0x3A) : rgb(0x3A, 0x39, 0x36)).cgColor
            let img = preview(lv, cur ? "levelup" : "idle", cur ? 3 : 6, 1.6); img.frame.origin = NSPoint(x: (cardW - img.frame.width) / 2, y: 6)
            if !got { img.alphaValue = 0.35 }
            card.addSubview(img)
            card.addSubview(text((got ? "" : "🔒 ") + "\(lv) · \(rankTitle(lv))", 12, got ? rgb(0xEE, 0xEB, 0xE3) : rgb(0x8A, 0x87, 0x80), bold: true, center: true,
                                 frame: NSRect(x: 4, y: 80, width: cardW - 8, height: 32)))
            card.addSubview(text(unlocks(lv), 10, rgb(0x9C, 0x98, 0x8E), center: true, frame: NSRect(x: 4, y: 112, width: cardW - 8, height: 36)))
            card.addSubview(text("\(xpForLevel(lv)) xp", 10, rgb(0x6E, 0x6B, 0x64), center: true, frame: NSRect(x: 4, y: 146, width: cardW - 8, height: 14)))
            doc.addSubview(card)
        }

        let visible = min(H, (NSScreen.main?.visibleFrame.height ?? 900) - 80)
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: W, height: visible), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        w.title = "Путь Синсина · уровень \(l)"; w.isReleasedWhenClosed = false
        w.appearance = NSAppearance(named: .darkAqua); w.backgroundColor = rgb(0x1F, 0x1E, 0x1C)
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: W, height: visible)); scroll.hasVerticalScroller = true; scroll.drawsBackground = false
        scroll.documentView = doc; w.contentView = scroll
        w.center(); progressWindow = w
        NSApp.activate(ignoringOtherApps: true); w.makeKeyAndOrderFront(nil)
    }
    func weeklyReport(open: Bool) {
        say("собираю отчёт за неделю…", 0)
        DispatchQueue.global().async {
            let s = Stats.load()
            let tasks = (0..<7).reduce(0) { acc, i in acc + Int(num(s.history[todayKey(Calendar.current.date(byAdding: .day, value: -i, to: Date())!)]?["Tasks"])) }
            let phrase = askClaude("Скажи одну фразу недели до 90 символов для программиста, который за неделю закрыл \(tasks) задач. Подбодри или пошути.", timeout: 45).0
                ?? "неделя прошла, бананы съедены, код написан"
            let url = writeReport(s, phrase: phrase)
            DispatchQueue.main.async {
                if open { NSWorkspace.shared.open(url); self.say("отчёт открыт в браузере", 4) }
                else { self.say("отчёт за неделю готов: правый клик → Отчёт за неделю", 8); self.notify("Отчёт за неделю готов") }
            }
        }
    }

    // Ask panel: question on top, a scrollable answer underneath, so long answers always fit.
    @objc func openAsk() {
        if let p = askPanel { p.makeKeyAndOrderFront(nil); return }
        let p = KeyPanel(contentRect: NSRect(x: 0, y: 0, width: 330, height: 300), styleMask: [.borderless], backing: .buffered, defer: false)
        p.isOpaque = false; p.backgroundColor = .clear; p.level = .floating; p.delegate = self
        let bg = NSVisualEffectView(frame: NSRect(x: 0, y: 0, width: 330, height: 300))
        bg.material = .hudWindow; bg.state = .active; bg.appearance = NSAppearance(named: .darkAqua)
        bg.wantsLayer = true; bg.layer?.cornerRadius = 14; bg.layer?.masksToBounds = true
        let field = NSTextField(frame: NSRect(x: 14, y: 266, width: 302, height: 22))
        field.placeholderString = "спроси Синсина… (Return, Esc — закрыть)"; field.isBordered = false; field.drawsBackground = false
        field.focusRingType = .none; field.font = .systemFont(ofSize: 13); field.textColor = .white
        field.delegate = self; field.target = self; field.action = #selector(askSubmitted(_:))
        let scroll = NSScrollView(frame: NSRect(x: 14, y: 12, width: 302, height: 244)); scroll.hasVerticalScroller = true; scroll.drawsBackground = false
        let tv = NSTextView(frame: scroll.bounds); tv.isEditable = false; tv.drawsBackground = false; tv.textColor = .white; tv.font = .systemFont(ofSize: 13)
        tv.autoresizingMask = [.width]; scroll.documentView = tv
        bg.addSubview(field); bg.addSubview(scroll); p.contentView = bg
        p.setFrameOrigin(NSPoint(x: panel.frame.minX - 50, y: panel.frame.minY + 30))
        askPanel = p; askAnswer = tv
        NSApp.activate(ignoringOtherApps: true)
        p.makeKeyAndOrderFront(nil); p.makeFirstResponder(field)
    }
    func windowDidResignKey(_ n: Notification) { if let w = n.object as? NSWindow, w === askPanel { askPanel?.close(); askPanel = nil } }
    func control(_ control: NSControl, textView: NSTextView, doCommandBy sel: Selector) -> Bool {
        if sel == #selector(NSResponder.cancelOperation(_:)) { askPanel?.close(); askPanel = nil; return true }
        return false
    }
    @objc func askSubmitted(_ sender: NSTextField) {
        let q = sender.stringValue.trimmingCharacters(in: .whitespaces); guard !q.isEmpty else { return }
        sender.stringValue = ""; askAnswer?.string = "думаю…"
        say("думаю…", 0); pokePose = "think"; pokeUntil = Date().addingTimeInterval(60)
        DispatchQueue.global().async {
            let (a, err) = askClaude("Вопрос: " + q + "\nОтветь коротко, 1–3 предложения.", timeout: 60)
            DispatchQueue.main.async {
                self.pokeUntil = .distantPast
                let text = a ?? (isLoginError(err) ? loginHint : "что-то не думается… (\(short(err ?? "", 60)))")
                if a != nil { self.pokePose = "happy"; self.pokeUntil = Date().addingTimeInterval(2) }
                self.askAnswer?.string = text
                self.say(short(text, 120), 10)
            }
        }
    }

    // Settings: a plain window over config.json.
    @objc func openSettings() {
        if let w = settingsWindow { NSApp.activate(ignoringOtherApps: true); w.makeKeyAndOrderFront(nil); return }
        characters = MascotCharacter.discover()
        let s = Stats.load()
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 580), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        w.title = "Синсин — настройки"; w.isReleasedWhenClosed = false; w.delegate = self
        let v = NSView(frame: NSRect(x: 0, y: 0, width: 460, height: 580)); w.contentView = v
        var y: CGFloat = 545
        func label(_ t: String, bold: Bool = false, gray: Bool = false) {
            let l = NSTextField(labelWithString: t); l.font = bold ? .boldSystemFont(ofSize: 14) : .systemFont(ofSize: gray ? 11 : 13)
            if gray { l.textColor = .secondaryLabelColor }; l.frame = NSRect(x: 20, y: y, width: 420, height: 18); v.addSubview(l)
        }
        func next(_ dy: CGFloat = 28) { y -= dy }
        func check(_ t: String, _ on: Bool) -> NSButton {
            let b = NSButton(checkboxWithTitle: t, target: nil, action: nil); b.state = on ? .on : .off
            b.frame = NSRect(x: 20, y: y - 2, width: 420, height: 20); v.addSubview(b); next(24); return b
        }
        func field(_ t: String, _ value: String, _ width: CGFloat = 80) -> NSTextField {
            label(t); let f = NSTextField(string: value); f.frame = NSRect(x: 280, y: y - 2, width: width, height: 22); v.addSubview(f); next(); return f
        }
        let x = s["Xp"], l = level(x)
        label("Уровень \(l) · \(rankTitle(l)) · опыт \(x) (до следующего \(xpForLevel(l + 1) - x)) · золотых бананов \(s["GoldenBananas"])", gray: true); next(30)
        label("Персонаж", bold: true); next()
        let skin = NSPopUpButton(frame: NSRect(x: 200, y: y - 4, width: 240, height: 26))
        skin.addItem(withTitle: "Синсин (основной)"); skin.lastItem?.representedObject = "monkey"
        for c in characters { skin.addItem(withTitle: c.name); skin.lastItem?.representedObject = c.id }
        let cur = cfg["skin"] as? String ?? "monkey"
        skin.selectItem(at: skin.itemArray.firstIndex { ($0.representedObject as? String) == cur } ?? 0)
        label("Кто живёт рядом с Claude"); v.addSubview(skin); next()
        let acc = NSPopUpButton(frame: NSRect(x: 200, y: y - 4, width: 240, height: 26)); acc.autoenablesItems = false
        let accNames = [("auto", "авто (сезон, погода, помодоро)"), ("none", "без аксессуара"), ("santa", "новогодний колпак"), ("glasses", "очки (ур. 2)"),
                        ("scarf", "шарф (ур. 3)"), ("headphones", "наушники (ур. 4)"), ("crown", "корона (ур. 5 / 3 золотых банана)"), ("umbrella", "зонтик")]
        for (id, t) in accNames { acc.addItem(withTitle: t); acc.lastItem?.representedObject = id; acc.lastItem?.isEnabled = Self.unlocked(id, s) }
        acc.selectItem(at: accNames.firstIndex { $0.0 == accChoice } ?? 0)
        label("Аксессуар"); v.addSubview(acc); next()
        let size = NSSlider(value: sizeMul, minValue: 0.6, maxValue: 1.3, target: nil, action: nil); size.frame = NSRect(x: 200, y: y - 2, width: 240, height: 22)
        label("Размер"); v.addSubview(size); next(22)
        label("Свои персонажи — папки в ~/.claude-mascot-pixel/characters. Аксессуары носит только Синсин.", gray: true); next(30)
        label("Поведение", bold: true); next()
        let sound = check("8-битные звуки", soundOn), health = check("напоминать размяться и попить воды", healthOn)
        let ai = check("реплики от Haiku в конце задачи", cfg["aiSummary"] as? Bool ?? true)
        let aiMin = field("Звать Haiku, если задача дольше, сек", "\(Int(cfg["aiMinSeconds"] == nil ? 20 : num(cfg["aiMinSeconds"])))")
        next(6); label("Расписание (в простое)", bold: true); next()
        let weekdays = check("только по будням", weekdaysOnly)
        let homeF = field("Уходит домой после", Self.hm(homeAfter))
        let lunchF = field("Обед (с-до)", Self.hm(lunchFrom) + "-" + Self.hm(lunchTo), 120)
        next(6); label("Погода и система", bold: true); next()
        let cityF = field("Город для погоды (пусто — выкл.)", city, 160)
        let auto = check("запускать при входе в систему", autostartOn)
        let save = NSButton(title: "Сохранить", target: nil, action: nil); save.frame = NSRect(x: 340, y: 14, width: 100, height: 30); save.keyEquivalent = "\r"
        let hooks = NSButton(title: "Переустановить хуки", target: nil, action: nil); hooks.frame = NSRect(x: 20, y: 14, width: 170, height: 30)
        v.addSubview(save); v.addSubview(hooks)
        hooks.actionClosure { [weak self] in removeHooks(); installHooks(); self?.say("хуки обновлены", 3) }
        save.actionClosure { [weak self, weak w] in
            guard let self = self else { return }
            func parse(_ s: String) -> Int? { let p = s.split(separator: ":").compactMap { Int($0.trimmingCharacters(in: .whitespaces)) }; return p.count == 2 ? p[0] * 60 + p[1] : nil }
            let lp = lunchF.stringValue.split(separator: "-").map(String.init)
            guard let h = parse(homeF.stringValue), lp.count == 2, let lf = parse(lp[0]), let lt = parse(lp[1]), let sec = Int(aiMin.stringValue.trimmingCharacters(in: .whitespaces)) else {
                let a = NSAlert(); a.messageText = "Время в формате ЧЧ:ММ (обед — ЧЧ:ММ-ЧЧ:ММ), секунды — числом"; a.runModal(); return
            }
            self.cfg["skin"] = skin.selectedItem?.representedObject as? String ?? "monkey"
            self.cfg["accessory"] = acc.selectedItem?.representedObject as? String ?? "auto"
            self.cfg["size"] = (size.doubleValue * 10).rounded() / 10
            self.cfg["sound"] = sound.state == .on; self.cfg["health"] = health.state == .on; self.cfg["aiSummary"] = ai.state == .on; self.cfg["aiMinSeconds"] = sec
            self.cfg["scheduleWeekdaysOnly"] = weekdays.state == .on; self.cfg["homeAfter"] = Self.hm(h); self.cfg["lunchFrom"] = Self.hm(lf); self.cfg["lunchTo"] = Self.hm(lt)
            self.cfg["city"] = cityF.stringValue.trimmingCharacters(in: .whitespaces)
            setAutostart(auto.state == .on)
            self.applyConfig(); self.applySize(); self.saveConfig(); self.weatherAt = .distantPast; self.slowChecks()
            self.say("настройки сохранены", 3); w?.close()
        }
        settingsWindow = w; w.center()
        NSApp.activate(ignoringOtherApps: true); w.makeKeyAndOrderFront(nil)
    }
    func windowWillClose(_ n: Notification) { if let w = n.object as? NSWindow, w === settingsWindow { settingsWindow = nil } }

    // MARK: placement & bar actions

    func place() {
        guard let c = findClaude() else { if panel.isVisible { panel.orderOut(nil) }; claude = nil; return }
        claude = c
        let x = max(c.frame.minX, c.frame.maxX - cfgRight - winW), y = max(c.frame.minY + cfgBottom, c.frame.minY)
        if panel.frame.origin != NSPoint(x: x, y: y) { panel.setFrameOrigin(NSPoint(x: x, y: y)) }
        panel.order(.above, relativeTo: c.number)   // just above Claude, not above other apps
    }
    func saveOffsetFromWindow() {
        guard let c = claude else { return }
        cfgRight = max(0, c.frame.maxX - panel.frame.maxX); cfgBottom = max(0, panel.frame.minY - c.frame.minY); saveConfig()
    }
    func focusClaude() -> Bool { guard let app = claudeApp() else { return false }; app.activate(options: []); return true }

    @objc func newChat() {
        guard focusClaude() else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { postKeys(45, flags: .maskCommand) }   // Cmd+N
        pokePose = "wave"; pokeUntil = Date().addingTimeInterval(1.5)
    }
    @objc func voiceInput() {
        guard focusClaude() else { return }
        // macOS Dictation: default shortcut is pressing Fn (Globe) twice; we simulate it.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
            let src = CGEventSource(stateID: .hidSystemState)
            for _ in 0..<2 {
                for flags in [CGEventFlags.maskSecondaryFn, []] {
                    let e = CGEvent(keyboardEventSource: src, virtualKey: 63, keyDown: !flags.isEmpty); e?.flags = flags; e?.type = .flagsChanged; e?.post(tap: .cghidEventTap)
                }
                usleep(80_000)
            }
        }
    }
    @objc func toggleFocus() {
        if focus == "focus" { focus = "off"; say("фокус выключен", 2) }
        else { focus = "focus"; focusEnd = Date().addingTimeInterval(1500); say("25 минут фокуса. я молчу", 2.5) }
        focusButton?.contentTintColor = focus == "focus" ? NSColor(srgbRed: 0.95, green: 0.79, blue: 0.3, alpha: 1) : tint
        focusButton?.toolTip = focus == "focus" ? "Фокус до \(DateFormatter.localizedString(from: focusEnd, dateStyle: .none, timeStyle: .short)) (клик — выключить)" : "Помодоро: 25 минут фокуса"
    }
    @objc func toggleCompact() { compact.toggle(); applySize(); saveConfig() }
    @objc func resetPosition() { cfgRight = 40; cfgBottom = 90; saveConfig() }
    @objc func quit() { NSApp.terminate(nil) }

    // MARK: config

    static func minutes(_ v: Any?, _ def: Int) -> Int {
        guard let s = v as? String else { return def }
        let p = s.split(separator: ":").compactMap { Int($0) }
        return p.count == 2 ? p[0] * 60 + p[1] : def
    }
    static func hm(_ m: Int) -> String { String(format: "%02d:%02d", m / 60, m % 60) }

    func loadConfig() { cfg = config(); applyConfig() }
    func applyConfig() {
        if cfg["right"] != nil { cfgRight = CGFloat(num(cfg["right"])) }
        if cfg["bottom"] != nil { cfgBottom = CGFloat(num(cfg["bottom"])) }
        if let v = cfg["compact"] as? Bool { compact = v }
        if cfg["size"] != nil { sizeMul = max(0.5, min(1.5, num(cfg["size"]))) }
        if let v = cfg["sound"] as? Bool { soundOn = v }
        if let v = cfg["health"] as? Bool { healthOn = v }
        accChoice = cfg["accessory"] as? String ?? "auto"; city = (cfg["city"] as? String ?? "").trimmingCharacters(in: .whitespaces)
        if city.isEmpty { weather = nil }
        homeAfter = Self.minutes(cfg["homeAfter"], homeAfter); lunchFrom = Self.minutes(cfg["lunchFrom"], lunchFrom); lunchTo = Self.minutes(cfg["lunchTo"], lunchTo)
        if let v = cfg["scheduleWeekdaysOnly"] as? Bool { weekdaysOnly = v }
        let want = cfg["skin"] as? String ?? "monkey"
        plugin = want == "monkey" ? nil : characters.first { $0.id == want }
    }
    func saveConfig() {
        cfg["right"] = cfgRight; cfg["bottom"] = cfgBottom; cfg["compact"] = compact; cfg["size"] = sizeMul; cfg["sound"] = soundOn; cfg["health"] = healthOn
        cfg["accessory"] = accChoice; cfg["city"] = city; cfg["scheduleWeekdaysOnly"] = weekdaysOnly
        cfg["homeAfter"] = Self.hm(homeAfter); cfg["lunchFrom"] = Self.hm(lunchFrom); cfg["lunchTo"] = Self.hm(lunchTo)
        if cfg["skin"] == nil { cfg["skin"] = "monkey" }
        if cfg["aiSummary"] == nil { cfg["aiSummary"] = true }
        if cfg["aiMinSeconds"] == nil { cfg["aiMinSeconds"] = 20 }
        writeJSONAtomically(cfg, to: dataFile("config.json"))
    }
}

// MARK: - main

let app = NSApplication.shared
app.setActivationPolicy(.accessory)   // no Dock icon
if let id = Bundle.main.bundleIdentifier, NSRunningApplication.runningApplications(withBundleIdentifier: id).count > 1 { exit(0) }   // single instance
guard let builtIn = BuiltIn() else {
    let a = NSAlert(); a.messageText = "XingPixel: не найдены спрайты (sprites/manifest.json)"; a.runModal(); exit(1)
}
let mascot = Mascot(builtIn: builtIn)
if config()["right"] == nil { mascot.saveConfig() }
app.run()
