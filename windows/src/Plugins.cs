// Character plugins. Xing the monkey is built in; any other character lives in characters/<id>/:
//   character.json  — { "name": "Мини-пиг", "author": "...", "pixelArt": true, "fps": 9, "frames": 48, "sound": "pig",
//                       "lines": { "error": "хрю?!" }, "poses": { "idle": "idle.png", "work": { "file": "work.gif" } } }
//   one file per pose: a PNG strip of N equal frames side by side ("frames", per pose or global), or an animated GIF/WebP.
// Missing poses fall back to similar ones (see Fallback), so even a one-GIF character works.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XingPixel
{
    class Character
    {
        public string Id, Name, Author = "", Sound = "monkey", Dir;
        public bool PixelArt = true;
        public double Fps = 9;
        public int FrameW, FrameH;
        public Dictionary<string, string> Lines = new Dictionary<string, string>();
        readonly Dictionary<string, object> poseDefs = new Dictionary<string, object>();
        readonly Dictionary<string, BitmapSource[]> loaded = new Dictionary<string, BitmapSource[]>();
        int defaultFrames = 1;

        static readonly Dictionary<string, string> Fallback = new Dictionary<string, string> {
            { "bash", "work" }, { "type", "work" }, { "read", "work" }, { "run", "work" }, { "work", "idle" }, { "think", "idle" },
            { "done", "happy" }, { "banana", "happy" }, { "love", "happy" }, { "stretch", "happy" }, { "happy", "idle" }, { "wave", "happy" },
            { "error", "idle" }, { "wait", "idle" }, { "milk", "idle" }, { "water", "milk" }, { "lunch", "milk" }, { "sleep", "idle" },
            { "home", "wave" }, { "git_conflict", "error" }, { "spotify", "music" }, { "music", "happy" }, { "levelup", "happy" } };

        public static Character Load(string dir)
        {
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(dir, "character.json")));
            var c = new Character { Dir = dir, Id = Path.GetFileName(dir) };
            object v;
            c.Name = d.TryGetValue("name", out v) ? Convert.ToString(v) : c.Id;
            if (d.TryGetValue("author", out v)) c.Author = Convert.ToString(v);
            if (d.TryGetValue("sound", out v)) c.Sound = Convert.ToString(v);
            if (d.TryGetValue("pixelArt", out v)) c.PixelArt = Convert.ToBoolean(v);
            if (d.TryGetValue("fps", out v)) c.Fps = Math.Max(1, Convert.ToDouble(v));
            if (d.TryGetValue("frames", out v)) c.defaultFrames = Math.Max(1, Convert.ToInt32(v));
            var lines = d.TryGetValue("lines", out v) ? v as Dictionary<string, object> : null;
            if (lines != null) foreach (var kv in lines) c.Lines[kv.Key] = Convert.ToString(kv.Value);
            var poses = d.TryGetValue("poses", out v) ? v as Dictionary<string, object> : null;
            if (poses == null || poses.Count == 0) throw new Exception("no poses");
            foreach (var kv in poses) c.poseDefs[kv.Key] = kv.Value;
            // Frame size comes from the first pose that loads (idle preferred).
            var first = c.Frames(c.poseDefs.ContainsKey("idle") ? "idle" : c.poseDefs.Keys.First());
            c.FrameW = first[0].PixelWidth; c.FrameH = first[0].PixelHeight;
            return c;
        }

        // Every character folder under the given roots (later roots override earlier ones with the same id).
        public static List<Character> Discover(params string[] roots)
        {
            var found = new Dictionary<string, Character>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    if (!File.Exists(Path.Combine(dir, "character.json"))) continue;
                    try { var c = Load(dir); found[c.Id] = c; } catch (Exception e) { Program.Log("character " + dir + ": " + e.Message); }
                }
            }
            return found.Values.OrderBy(c => c.Name).ToList();
        }

        public bool Has(string pose) { return poseDefs.ContainsKey(pose); }

        // The pose to actually play: itself if present, else down the fallback chain, else idle/any.
        public string Resolve(string pose)
        {
            if (pose == "gone") return "gone";
            string p = pose;
            for (int i = 0; i < 6 && p != null; i++)
            {
                if (Has(p)) return p;
                string next; p = p.StartsWith("git_") && p != "git_conflict" ? "work" : Fallback.TryGetValue(p, out next) ? next : null;
            }
            return Has("idle") ? "idle" : poseDefs.Keys.First();
        }

        public BitmapSource[] Frames(string pose)
        {
            BitmapSource[] arr;
            if (loaded.TryGetValue(pose, out arr)) return arr;
            object def = poseDefs[pose];
            string file; int frames = defaultFrames;
            var dd = def as Dictionary<string, object>;
            if (dd != null) { file = Convert.ToString(dd["file"]); object v; if (dd.TryGetValue("frames", out v)) frames = Math.Max(1, Convert.ToInt32(v)); }
            else file = Convert.ToString(def);
            string path = Path.Combine(Dir, file);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            arr = ext == ".gif" || ext == ".webp" ? DecodeAnimated(path) : SliceStrip(path, frames);
            loaded[pose] = arr;
            return arr;
        }

        static BitmapSource[] SliceStrip(string path, int frames)
        {
            var img = new BitmapImage(); img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.UriSource = new Uri(path); img.EndInit(); img.Freeze();
            int w = img.PixelWidth / frames, h = img.PixelHeight;
            var arr = new BitmapSource[frames];
            for (int i = 0; i < frames; i++) { var c = new CroppedBitmap(img, new Int32Rect(i * w, 0, w, h)); c.Freeze(); arr[i] = c; }
            return arr;
        }

        // GIF/WebP: frames may be partial deltas, so composite each one over the previous canvas.
        static BitmapSource[] DecodeAnimated(string path)
        {
            var dec = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int W = dec.Frames[0].PixelWidth, H = dec.Frames[0].PixelHeight;
            var outFrames = new List<BitmapSource>(); BitmapSource prev = null;
            foreach (var f in dec.Frames)
            {
                double left = 0, top = 0;
                try { var md = f.Metadata as BitmapMetadata; if (md != null && dec is GifBitmapDecoder) { left = Convert.ToDouble(md.GetQuery("/imgdesc/Left")); top = Convert.ToDouble(md.GetQuery("/imgdesc/Top")); } } catch { }
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) { if (prev != null) dc.DrawImage(prev, new Rect(0, 0, W, H)); dc.DrawImage(f, new Rect(left, top, f.PixelWidth, f.PixelHeight)); }
                var rt = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32); rt.Render(dv); rt.Freeze();
                prev = rt; outFrames.Add(rt);
            }
            return outFrames.ToArray();
        }

        // Writes one of the built-in drawn bodies (pig, spark) as a plugin folder — also serves as a format example.
        public static void ExportBuiltIn(string skin, string name, string sound, string outDir, Dictionary<string, string> lines)
        {
            Directory.CreateDirectory(outDir);
            int W = Sprites.StageW, H = Sprites.StageH, n = Sprites.Frames;
            var poses = new List<string>();
            foreach (var p in Sprites.Poses)
            {
                if (p == "gone") continue;
                var sheet = new byte[W * n * H * 4];
                for (int i = 0; i < n; i++)
                {
                    var f = Sprites.RenderBody(p, i, skin, "none");
                    for (int y = 0; y < H; y++) Buffer.BlockCopy(f, y * W * 4, sheet, (y * W * n + i * W) * 4, W * 4);
                }
                var bs = BitmapSource.Create(W * n, H, 96, 96, PixelFormats.Bgra32, null, sheet, W * n * 4);
                var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bs));
                using (var fs = File.Create(Path.Combine(outDir, p + ".png"))) enc.Save(fs);
                poses.Add(p);
            }
            var js = new JavaScriptSerializer();
            var doc = new Dictionary<string, object> {
                { "name", name }, { "author", "встроенный пример" }, { "pixelArt", true }, { "fps", Sprites.Fps }, { "frames", n }, { "sound", sound },
                { "lines", lines }, { "poses", poses.ToDictionary(p => p, p => (object)(p + ".png")) } };
            File.WriteAllText(Path.Combine(outDir, "character.json"), js.Serialize(doc), new System.Text.UTF8Encoding(false));
        }
    }
}
