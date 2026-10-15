using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// Unattended preview check: with PROPPREVIEW_OPEN=&lt;map folder&gt;, ChroMapper opens that map
    /// (PROPPREVIEW_DIFF, default Expert; PROPPREVIEW_CHAR, default Standard) the way its song menu does,
    /// seeks to each beat of PROPPREVIEW_SHOTS ("8,64,...") and saves &lt;beat&gt;.png in
    /// PROPPREVIEW_OUT (with PROPPREVIEW_NOBLOOM=1 also &lt;beat&gt;-nobloom.png, ChroMapper's
    /// bloomfog switched off), then quits. The song menu's edit button is pressed without saving (its
    /// handler with "no"), so the map's files stay as they are. The last-loaded map in
    /// ChroMapper's settings is put back before quitting. If a step times out, stuck.png is saved.
    /// </summary>
    public class ScriptedOpen : MonoBehaviour
    {
        public static bool Start(out ScriptedOpen? runner)
        {
            runner = null;
            var dir = Environment.GetEnvironmentVariable("PROPPREVIEW_OPEN");
            if (string.IsNullOrEmpty(dir)) return false;
            // Discord's "not running" dialog would cover the screenshots; on again before quitting.
            discordWas = Settings.Instance.DiscordRPCEnabled;
            Settings.Instance.DiscordRPCEnabled = false;
            var go = new GameObject("PropPreview ScriptedOpen");
            DontDestroyOnLoad(go);
            runner = go.AddComponent<ScriptedOpen>();
            return true;
        }

        private static bool discordWas;
        private bool levelLoaded;
        private bool failed;

        private IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("PROPPREVIEW_OPEN")!;
            var diff = Environment.GetEnvironmentVariable("PROPPREVIEW_DIFF") ?? "Expert";
            var characteristic = Environment.GetEnvironmentVariable("PROPPREVIEW_CHAR") ?? "Standard";
            var outDir = Environment.GetEnvironmentVariable("PROPPREVIEW_OUT") ?? dir;
            var shots = (Environment.GetEnvironmentVariable("PROPPREVIEW_SHOTS") ?? "8")
                .Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            Directory.CreateDirectory(outDir);
            LoadInitialMap.OnLevelLoaded += () => levelLoaded = true;

            var settings = Settings.Instance;
            var previous = (settings.LastLoadedMap, settings.LastLoadedChar, settings.LastLoadedDiff);
            failed = false;
            try
            {
                yield return Until(() => SceneManager.GetActiveScene().name == "01_SongSelectMenu", 60, "song list");
                if (failed) yield break;
                yield return new WaitForSeconds(2);
                settings.LastLoadedMap = dir;
                settings.LastLoadedChar = characteristic;
                settings.LastLoadedDiff = diff;
                BeatSaberSongContainer.Instance.SelectSongForEditing(BeatSaberSongUtils.GetInfoFromFolder(dir));
                yield return Until(() => SceneManager.GetActiveScene().name == "02_SongEditMenu", 60, "song menu");
                if (failed) yield break;
                yield return new WaitForSeconds(3);
                // EditMapButtonPressed asks about unsaved changes first; answer "no" (1) directly.
                typeof(SongInfoEditUI).GetMethod("HandleEditMapButtonPressed", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(FindAnyObjectByType<SongInfoEditUI>(), new object[] { 1 });
                yield return Until(() => levelLoaded, 120, "map editor");
                if (failed) yield break;
                yield return new WaitForSeconds(5);

                var atsc = FindAnyObjectByType<BeatmapRuntimeContext>().Atsc;
                var compare = Environment.GetEnvironmentVariable("PROPPREVIEW_NOBLOOM") == "1";
                foreach (var beat in shots)
                {
                    atsc.MoveToJsonTime(beat);
                    yield return new WaitForSeconds(1.5f);
                    yield return Shot(outDir, beat.ToString(CultureInfo.InvariantCulture), atsc.CurrentJsonTime);
                    var bloom = BloomfogRenderingController.Instance; // null while it is disabled
                    if (!compare || bloom == null) continue;
                    bloom.enabled = false;
                    yield return new WaitForSeconds(0.5f);
                    yield return Shot(outDir, beat.ToString(CultureInfo.InvariantCulture) + "-nobloom", atsc.CurrentJsonTime);
                    bloom.enabled = true;
                }
                Log("done");
            }
            finally
            {
                (settings.LastLoadedMap, settings.LastLoadedChar, settings.LastLoadedDiff) = previous;
                settings.DiscordRPCEnabled = discordWas;
                settings.Save();
                Application.Quit();
            }
        }

        private static IEnumerator Shot(string outDir, string name, float beat)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var path = Path.Combine(outDir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            var active = FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Count(t => t.parent == null && t.name.StartsWith("PropPreview assets/"));
            Log($"shot {path} at beat {beat}: {active} prefab instances active");
        }

        // A coroutine that throws never runs its finally, so a timeout is a flag, not an exception.
        private IEnumerator Until(Func<bool> condition, float seconds, string what)
        {
            if (failed) yield break;
            var end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                {
                    failed = true;
                    Log($"FAIL: no {what} after {seconds} s");
                    yield return new WaitForEndOfFrame();
                    var outDir = Environment.GetEnvironmentVariable("PROPPREVIEW_OUT") ?? ".";
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(outDir, "stuck.png"), tex.EncodeToPNG());
                    yield break;
                }
                yield return null;
            }
            Log($"reached {what}");
        }

        private static void Log(string line) => Debug.Log($"PropPreview scripted: {line}");
    }
}
