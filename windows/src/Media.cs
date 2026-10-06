// Spotify via the Windows media session API (the same one the volume flyout uses): track, artist, play state,
// position, cover art, and play/pause/next/previous. No Spotify login or API key needed.
// WinRT async calls are awaited by polling their status, so no Windows SDK is required to build.
using System;
using System.Threading;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace XingPixel
{
    class NowPlaying
    {
        public string Artist = "", Title = "";
        public bool Playing;
        public double Position, Duration;   // seconds
        public BitmapSource Cover;           // frozen; null when the player gives none
        public string Key { get { return Artist + "\u0001" + Title; } }
    }

    static class Media
    {
        static GlobalSystemMediaTransportControlsSessionManager mgr;
        static GlobalSystemMediaTransportControlsSession session;
        static string coverKey; static BitmapSource cover;

        static R Wait<R>(IAsyncOperation<R> op)
        {
            var until = DateTime.Now.AddSeconds(3);
            while (op.Status == AsyncStatus.Started) { if (DateTime.Now > until) { op.Cancel(); throw new TimeoutException(); } Thread.Sleep(5); }
            return op.GetResults();
        }

        // Spotify's session, or null if Spotify isn't running. Blocking: call off the UI thread.
        public static NowPlaying Read()
        {
            // A closed Spotify can leave a dead session behind for a while (calls fail with "RPC server unavailable"),
            // so no process means no player.
            if (System.Diagnostics.Process.GetProcessesByName("Spotify").Length == 0) { session = null; return null; }
            if (mgr == null) mgr = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            session = null;
            foreach (var s in mgr.GetSessions())
                if (s.SourceAppUserModelId.IndexOf("Spotify", StringComparison.OrdinalIgnoreCase) >= 0) { session = s; break; }
            if (session == null) return null;
            GlobalSystemMediaTransportControlsSessionPlaybackInfo info;
            try { info = session.GetPlaybackInfo(); }
            catch { session = null; mgr = null; return null; }   // the session died between the lookup and the call
            var props = Wait(session.TryGetMediaPropertiesAsync());
            if (string.IsNullOrEmpty(props.Title)) return null;   // no track (Spotify starting up or shutting down): no player
            var tl = session.GetTimelineProperties();
            var np = new NowPlaying { Artist = props.Artist ?? "", Title = props.Title ?? "",
                                      Playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                                      Position = tl.Position.TotalSeconds, Duration = (tl.EndTime - tl.StartTime).TotalSeconds };
            // Spotify reports the position at its last update; extrapolate while playing.
            if (np.Playing && tl.LastUpdatedTime.Year > 2000) np.Position = Math.Min(np.Duration, np.Position + (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds);
            if (np.Key != coverKey)
            {
                coverKey = np.Key; cover = null;
                try { if (props.Thumbnail != null) cover = LoadCover(props.Thumbnail); } catch { }
            }
            np.Cover = cover;
            return np;
        }

        static BitmapSource LoadCover(IRandomAccessStreamReference r)
        {
            var st = Wait(r.OpenReadAsync());
            var dr = new DataReader(st); uint n = Wait(dr.LoadAsync((uint)st.Size)); var buf = new byte[n]; dr.ReadBytes(buf);
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.DecodePixelWidth = 96; bmp.StreamSource = new System.IO.MemoryStream(buf); bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // Fire-and-forget controls (run on the thread pool).
        public static void Toggle() { Run(s => s.TryTogglePlayPauseAsync()); }
        public static void Next() { Run(s => s.TrySkipNextAsync()); }
        public static void Previous() { Run(s => s.TrySkipPreviousAsync()); }
        static void Run(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> f)
        {
            var s = session; if (s == null) return;
            ThreadPool.QueueUserWorkItem(delegate { try { Wait(f(s)); } catch { } });
        }
    }
}
