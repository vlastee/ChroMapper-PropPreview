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
    /// For comparisons with the game: PROPPREVIEW_SECONDS=1 reads the shots as song seconds;
    /// PROPPREVIEW_MODE=Playing switches to ChroMapper's Playing mode (notes fly in like gameplay);
    /// PROPPREVIEW_POSES="x,y,z,rx,ry,rz,fov[,near,far];..." (one per shot: world position, euler
    /// rotation, vertical field of view, clip planes) puts the view camera there and saves only its render, at
    /// PROPPREVIEW_SIZE (default 1280x720), without ChroMapper's UI.
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
            var seconds = Environment.GetEnvironmentVariable("PROPPREVIEW_SECONDS") == "1";
            var poses = (Environment.GetEnvironmentVariable("PROPPREVIEW_POSES") ?? "")
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToArray();
            var size = (Environment.GetEnvironmentVariable("PROPPREVIEW_SIZE") ?? "1280x720").Split('x').Select(int.Parse).ToArray();
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
                if (Environment.GetEnvironmentVariable("PROPPREVIEW_MODE") == "Playing")
                {
                    FindAnyObjectByType<UIMode>().SetUIMode(UIModeType.Playing, false);
                    yield return new WaitForSeconds(1);
                }
                var compare = Environment.GetEnvironmentVariable("PROPPREVIEW_NOBLOOM") == "1";
                for (var i = 0; i < shots.Length; i++)
                {
                    var at = shots[i];
                    if (seconds) atsc.MoveToTimeInSeconds(at);
                    else atsc.MoveToJsonTime(at);
                    yield return new WaitForSeconds(1.5f);
                    var name = at.ToString(CultureInfo.InvariantCulture);
                    if (i < poses.Length)
                    {
                        yield return new WaitForEndOfFrame();
                        PoseShot(outDir, name, poses[i], size[0], size[1], atsc.CurrentJsonTime, atsc.CurrentSeconds);
                        continue;
                    }
                    yield return Shot(outDir, name, atsc.CurrentJsonTime);
                    var bloom = BloomfogRenderingController.Instance; // null while it is disabled
                    if (!compare || bloom == null) continue;
                    bloom.enabled = false;
                    yield return new WaitForSeconds(0.5f);
                    yield return Shot(outDir, name + "-nobloom", atsc.CurrentJsonTime);
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
                .Count(t => t.parent == null && t.name.StartsWith("PropPreview assets/") && t.childCount > 0
                    && t.GetChild(0).gameObject.activeSelf);
            Log($"shot {path} at beat {beat}: {active} prefab instances active");
        }

        // The view camera at a given pose, rendered alone (no UI) into a PNG; the pose is put back.
        private static void PoseShot(string outDir, string name, float[] pose, int w, int h, float beat, float secs)
        {
            var cam = FindAnyObjectByType<CameraManager>().SelectedCameraController.Camera;
            var t = cam.transform;
            var (pos, rot, fov, near, far) = (t.position, t.rotation, cam.fieldOfView, cam.nearClipPlane, cam.farClipPlane);
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var previousTarget = cam.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                t.SetPositionAndRotation(new Vector3(pose[0], pose[1], pose[2]), Quaternion.Euler(pose[3], pose[4], pose[5]));
                cam.fieldOfView = pose[6];
                if (pose.Length >= 9)
                {
                    cam.nearClipPlane = pose[7];
                    cam.farClipPlane = pose[8];
                }
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
            }
            finally
            {
                cam.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
                t.SetPositionAndRotation(pos, rot);
                cam.fieldOfView = fov;
                cam.nearClipPlane = near;
                cam.farClipPlane = far;
            }
            Log($"pose shot {name} at beat {beat} ({secs} s), camera {string.Join(",", pose)}");
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.isActiveAndEnabled)
                    Log($"  light {light.name} ({light.type}) intensity {light.intensity} color {light.color} mask {light.cullingMask} at {light.transform.position}"
                        + $" rot {light.transform.eulerAngles} scene {light.gameObject.scene.name}");
            Log($"  ambient {RenderSettings.ambientMode} {RenderSettings.ambientLight} x{RenderSettings.ambientIntensity} sun {(RenderSettings.sun != null ? RenderSettings.sun.name : "-")}");
            Log($"  light probes {(LightmapSettings.lightProbes == null ? "none" : LightmapSettings.lightProbes.count.ToString())}, lightmaps {LightmapSettings.lightmaps.Length}");
            var sh = RenderSettings.ambientProbe;
            Log($"  ambient probe L0 {sh[0, 0]:F3},{sh[1, 0]:F3},{sh[2, 0]:F3} reflection {RenderSettings.defaultReflectionMode} x{RenderSettings.reflectionIntensity}"
                + $" custom {(RenderSettings.customReflectionTexture != null ? RenderSettings.customReflectionTexture.name : "-")}");
            foreach (var probe in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
                if (probe.isActiveAndEnabled)
                    Log($"  reflection probe {probe.name} mode {probe.mode} texture {(probe.texture != null ? probe.texture.name : "-")} intensity {probe.intensity} at {probe.transform.position} size {probe.size} scene {probe.gameObject.scene.name}");
            foreach (var root in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (root.name.StartsWith("PropPreview assets/") && root.childCount > 0 && root.GetChild(0).gameObject.activeSelf
                    && (root.parent != null || root.GetComponent<Beatmap.Animations.ObjectAnimator>() != null || true))
                {
                    var prefab = root.GetChild(0);
                    Log($"  {root.name} at {prefab.position} rot {prefab.eulerAngles} scale {prefab.lossyScale}"
                        + $" holder parent {(root.parent == null ? "-" : root.parent.name + " " + root.parent.position + " " + root.parent.eulerAngles)}"
                        + $" holder local {root.localPosition} {root.localEulerAngles}");
                }
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
