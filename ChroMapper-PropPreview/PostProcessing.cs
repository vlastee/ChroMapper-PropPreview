using System;
using System.Collections.Generic;
using System.Linq;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// CreateScreenTexture, CreateCamera, Blit and camera culling on ChroMapper's view camera,
    /// following Vivify (Events/ApplyPostProcessing.cs, DeclareRenderTexture.cs,
    /// DeclareCullingTexture.cs, PostProcessing/**):
    /// <list type="bullet">
    /// <item>A screen texture exists from its event on, sized from the view (width or the view's
    /// width, divided by xRatio; height likewise), never cleared, and is a shader global named by
    /// its id.</item>
    /// <item>A CreateCamera camera renders the view from the same pose just before the view camera,
    /// into a colour texture (global named `texture`) and a depth texture (global named
    /// `depthTexture`), with its own SetCameraProperty values (id = its id).</item>
    /// <item>Culling puts the objects on the given tracks (here: prefab instances) on layer 22 while
    /// a camera renders; whitelist renders only them, otherwise they are hidden.</item>
    /// <item>A Blit runs from its event for `duration` beats (a zero duration: one frame, shown here
    /// while the editor sits on its beat or plays through it), higher priority first, older first
    /// at equal priority. BeforeMainEffect and AfterMainEffect run on the final image (both after
    /// ChroMapper's own bloom, which stands in for the game's main effect); the other orders run
    /// inside the frame at the matching camera event. Sources and destinations: "_Main", a screen
    /// texture id, or (as a source) a CreateCamera id.</item>
    /// </list>
    /// Not previewed: a CreateCamera's mainEffect (bloom on its output) and bloomPrePass.
    /// </summary>
    public class PostProcessing
    {
        public const int CullingLayer = 22;

        private class ScreenTexture
        {
            public float Time;
            public string Id = "";
            public float XRatio = 1, YRatio = 1;
            public int? Width, Height;
            public RenderTextureFormat? Format;
            public FilterMode? Filter;
            public RenderTexture? Rt;
        }

        private class SecondaryCamera
        {
            public float Time;
            public string Id = "";
            public string? Texture, DepthTexture;
            public Camera? Camera;
            public RenderTexture? Color, Depth;
        }

        private class Effect
        {
            public float Time, Duration;
            public int Seq;
            public Material? Material;
            public int Pass = -1;
            public int Priority;
            public string Source = CameraPreview.MainCamera;
            public string[] Destinations = { CameraPreview.MainCamera };
            public string Order = "AfterMainEffect";
        }

        private static readonly Dictionary<string, CameraEvent> BufferEvents = new Dictionary<string, CameraEvent>
        {
            ["BeforeSkybox"] = CameraEvent.BeforeSkybox,
            ["AfterSkybox"] = CameraEvent.AfterSkybox,
            ["BeforeOpaque"] = CameraEvent.BeforeForwardOpaque,
            ["AfterOpaque"] = CameraEvent.AfterForwardOpaque,
            ["BeforeAlpha"] = CameraEvent.BeforeForwardAlpha,
            ["AfterAlpha"] = CameraEvent.AfterForwardAlpha,
        };

        private readonly CameraPreview cameras;
        private readonly Func<string, IEnumerable<GameObject>> trackObjects;
        private readonly List<ScreenTexture> textures = new List<ScreenTexture>();
        private readonly List<SecondaryCamera> secondary = new List<SecondaryCamera>();
        private readonly List<Effect> effects = new List<Effect>();

        private float time;
        private readonly List<Effect> active = new List<Effect>();
        private readonly List<(Camera cam, CameraEvent evt, CommandBuffer cb)> buffers =
            new List<(Camera, CameraEvent, CommandBuffer)>();
        private readonly List<(GameObject go, int layer)> culled = new List<(GameObject, int)>();
        private (Camera? cam, int mask) mainMask;

        public PostProcessing(CameraPreview cameras, Func<string, IEnumerable<GameObject>> trackObjects)
        {
            this.cameras = cameras;
            this.trackObjects = trackObjects;
        }

        public bool Empty => textures.Count == 0 && secondary.Count == 0 && effects.Count == 0;

        public void Clear()
        {
            foreach (var t in textures) Release(t.Rt);
            foreach (var c in secondary)
            {
                Release(c.Color);
                Release(c.Depth);
                if (c.Camera != null) UnityEngine.Object.Destroy(c.Camera.gameObject);
            }
            textures.Clear();
            secondary.Clear();
            effects.Clear();
            active.Clear();
            foreach (var hook in UnityEngine.Object.FindObjectsByType<PostProcessHook>(FindObjectsSortMode.None))
                UnityEngine.Object.Destroy(hook);
        }

        // ---- events ----

        public void AddScreenTexture(float t, JSONNode d)
        {
            if (d["id"] == null || !d["id"].IsString) return;
            textures.Add(new ScreenTexture
            {
                Time = t,
                Id = d["id"],
                XRatio = Num(d["xRatio"]) ?? 1,
                YRatio = Num(d["yRatio"]) ?? 1,
                Width = (int?)Num(d["width"]),
                Height = (int?)Num(d["height"]),
                Format = d["colorFormat"] != null && Enum.TryParse(d["colorFormat"].Value, out RenderTextureFormat f) ? f : (RenderTextureFormat?)null,
                Filter = d["filterMode"] != null && Enum.TryParse(d["filterMode"].Value, out FilterMode m) ? m : (FilterMode?)null,
            });
        }

        public void AddCamera(float t, JSONNode d)
        {
            if (d["id"] == null || !d["id"].IsString) return;
            secondary.Add(new SecondaryCamera
            {
                Time = t,
                Id = d["id"],
                Texture = d["texture"]?.IsString == true ? d["texture"].Value : null,
                DepthTexture = d["depthTexture"]?.IsString == true ? d["depthTexture"].Value : null,
            });
            cameras.Add(t, d); // its own properties, by its id
        }

        public void AddBlit(float t, JSONNode d, Material? material)
        {
            if (d["asset"] != null && d["asset"].IsString && material == null) return; // Vivify drops it
            effects.Add(new Effect
            {
                Time = t,
                Duration = Num(d["duration"]) ?? 0,
                Seq = effects.Count,
                Material = material,
                Pass = (int?)Num(d["pass"]) ?? -1,
                Priority = (int?)Num(d["priority"]) ?? 0,
                Source = d["source"]?.IsString == true ? d["source"].Value : CameraPreview.MainCamera,
                Destinations = d["destination"] == null ? new[] { CameraPreview.MainCamera }
                    : CameraPreview.Tracks(d["destination"]).ToArray(),
                Order = d["order"]?.IsString == true ? d["order"].Value : "AfterMainEffect",
            });
        }

        // ---- per frame ----

        public void Evaluate(float now, float previous, bool playing)
        {
            time = now;
            active.Clear();
            foreach (var e in effects)
            {
                var on = e.Duration > 0
                    ? now >= e.Time && now < e.Time + e.Duration
                    : Mathf.Abs(now - e.Time) < 0.001f || (playing && previous < e.Time && e.Time <= now);
                if (on) active.Add(e);
            }
            // Higher priority first; at equal priority the older event first.
            active.Sort((a, b) => a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority)
                : a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Seq.CompareTo(b.Seq));
        }

        public void OnPreCull(Camera cam)
        {
            if (!cameras.IsViewCamera(cam)) return;
            var hook = cam.GetComponent<PostProcessHook>();
            var image = active.Any(e => e.Order == "BeforeMainEffect" || e.Order == "AfterMainEffect");
            if (image && hook == null) hook = cam.gameObject.AddComponent<PostProcessHook>();
            if (hook != null)
            {
                hook.Owner = this;
                hook.enabled = image;
            }
            var state = cameras.Main;
            if (state.CullingTracks != null)
            {
                mainMask = (cam, cam.cullingMask);
                Cull(cam, state);
            }
        }

        public void OnPreRender(Camera cam)
        {
            if (!cameras.IsViewCamera(cam)) return;
            foreach (var t in textures)
            {
                if (t.Time > time) continue;
                var w = Mathf.Max(1, (int)((t.Width ?? cam.scaledPixelWidth) / t.XRatio));
                var h = Mathf.Max(1, (int)((t.Height ?? cam.scaledPixelHeight) / t.YRatio));
                if (t.Rt == null || t.Rt.width != w || t.Rt.height != h)
                {
                    Release(t.Rt);
                    t.Rt = new RenderTexture(w, h, 0, t.Format ?? (cam.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default)) { name = t.Id };
                    if (t.Filter != null) t.Rt.filterMode = t.Filter.Value;
                }
                Shader.SetGlobalTexture(t.Id, t.Rt);
            }
            foreach (var c in secondary)
            {
                if (c.Time > time || (c.Texture == null && c.DepthTexture == null)) continue;
                RenderSecondary(cam, c);
            }
            foreach (var group in active.Where(e => BufferEvents.ContainsKey(e.Order)).GroupBy(e => e.Order))
            {
                var cb = new CommandBuffer { name = "PropPreview " + group.Key };
                var tmp = Shader.PropertyToID("_PropPreviewBlit");
                foreach (var e in group)
                {
                    if (e.Source != CameraPreview.MainCamera) continue;
                    foreach (var dest in e.Destinations)
                    {
                        if (dest == CameraPreview.MainCamera)
                        {
                            if (e.Material == null) continue;
                            cb.GetTemporaryRT(tmp, cam.scaledPixelWidth, cam.scaledPixelHeight, 0);
                            cb.Blit(BuiltinRenderTextureType.CurrentActive, tmp);
                            cb.Blit(tmp, BuiltinRenderTextureType.CameraTarget, e.Material, e.Pass);
                            cb.ReleaseTemporaryRT(tmp);
                        }
                        else if (Texture(dest) is RenderTexture rt)
                        {
                            if (e.Material != null) cb.Blit(BuiltinRenderTextureType.CurrentActive, rt, e.Material, e.Pass);
                            else cb.Blit(BuiltinRenderTextureType.CurrentActive, rt);
                        }
                    }
                }
                cam.AddCommandBuffer(BufferEvents[group.Key], cb);
                buffers.Add((cam, BufferEvents[group.Key], cb));
            }
        }

        public void OnPostRender(Camera cam)
        {
            for (var i = buffers.Count - 1; i >= 0; i--)
            {
                if (buffers[i].cam != cam) continue;
                cam.RemoveCommandBuffer(buffers[i].evt, buffers[i].cb);
                buffers[i].cb.Release();
                buffers.RemoveAt(i);
            }
            if (mainMask.cam == cam)
            {
                Uncull();
                cam.cullingMask = mainMask.mask;
                mainMask = (null, 0);
            }
        }

        private void RenderSecondary(Camera view, SecondaryCamera c)
        {
            if (c.Camera == null)
            {
                var go = new GameObject("PropPreview camera " + c.Id);
                c.Camera = go.AddComponent<Camera>();
                c.Camera.enabled = false;
            }
            var cam = c.Camera;
            cam.transform.SetParent(view.transform, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.CopyFrom(view);
            cam.enabled = false;
            cam.depth = view.depth - 1;
            var w = view.scaledPixelWidth;
            var h = view.scaledPixelHeight;
            if (c.Color == null || c.Color.width != w || c.Color.height != h)
            {
                Release(c.Color);
                Release(c.Depth);
                c.Color = new RenderTexture(w, h, 24, view.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default) { name = c.Id };
                c.Depth = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat) { name = c.Id + " depth" };
            }
            cam.targetTexture = c.Color;
            var state = cameras.StateOf(c.Id, time);
            if (state.ClearFlags != null) cam.clearFlags = state.ClearFlags.Value;
            if (state.BackgroundColor != null) cam.backgroundColor = state.BackgroundColor.Value;
            cam.depthTextureMode |= (state.DepthTextureMode ?? DepthTextureMode.None)
                | (c.DepthTexture != null ? DepthTextureMode.Depth : DepthTextureMode.None);
            CameraPreview.ApplySky(cam, cam.clearFlags == CameraClearFlags.Skybox ? cameras.Skybox : null);
            Cull(cam, state);
            try
            {
                cam.Render();
            }
            finally
            {
                Uncull();
            }
            if (c.DepthTexture != null)
            {
                var depth = Shader.GetGlobalTexture("_CameraDepthTexture");
                if (depth != null) Graphics.Blit(depth, c.Depth);
                Shader.SetGlobalTexture(c.DepthTexture, c.Depth);
            }
            if (c.Texture != null) Shader.SetGlobalTexture(c.Texture, c.Color);
        }

        private void Cull(Camera cam, CameraPreview.State state)
        {
            if (state.CullingTracks == null) return;
            foreach (var track in state.CullingTracks)
            foreach (var root in trackObjects(track))
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                culled.Add((r.gameObject, r.gameObject.layer));
                r.gameObject.layer = CullingLayer;
            }
            cam.cullingMask = state.CullingWhitelist ? 1 << CullingLayer : cam.cullingMask & ~(1 << CullingLayer);
        }

        private void Uncull()
        {
            for (var i = culled.Count - 1; i >= 0; i--)
                if (culled[i].go != null) culled[i].go.layer = culled[i].layer;
            culled.Clear();
        }

        /// <summary>The Blit chain on the final image (BeforeMainEffect, then AfterMainEffect).</summary>
        public void RenderImage(RenderTexture src, RenderTexture dst)
        {
            var main = RenderTexture.GetTemporary(src.descriptor);
            Graphics.Blit(src, main);
            foreach (var order in new[] { "BeforeMainEffect", "AfterMainEffect" })
            foreach (var e in active)
            {
                if (e.Order != order) continue;
                if (e.Source == CameraPreview.MainCamera)
                {
                    foreach (var dest in e.Destinations)
                    {
                        if (dest == CameraPreview.MainCamera)
                        {
                            if (e.Material == null) continue;
                            var next = RenderTexture.GetTemporary(src.descriptor);
                            Graphics.Blit(main, next, e.Material, e.Pass);
                            RenderTexture.ReleaseTemporary(main);
                            main = next;
                        }
                        else if (Texture(dest) is RenderTexture rt)
                        {
                            Draw(main, rt, e);
                        }
                    }
                    continue;
                }
                var source = Texture(e.Source);
                if (source == null) continue;
                foreach (var dest in e.Destinations)
                {
                    if (dest == CameraPreview.MainCamera)
                    {
                        Draw(source, main, e);
                    }
                    else if (dest == e.Source)
                    {
                        if (e.Material == null || !(source is RenderTexture self)) continue;
                        var tmp = RenderTexture.GetTemporary(self.descriptor);
                        Graphics.Blit(self, tmp, e.Material, e.Pass);
                        Graphics.Blit(tmp, self);
                        RenderTexture.ReleaseTemporary(tmp);
                    }
                    else if (Texture(dest) is RenderTexture rt)
                    {
                        Draw(source, rt, e);
                    }
                }
            }
            Graphics.Blit(main, dst);
            RenderTexture.ReleaseTemporary(main);
        }

        private static void Draw(Texture from, RenderTexture to, Effect e)
        {
            if (e.Material != null) Graphics.Blit(from, to, e.Material, e.Pass);
            else Graphics.Blit(from, to);
        }

        /// <summary>A screen texture, or a CreateCamera camera's colour texture, by id.</summary>
        private Texture? Texture(string id)
        {
            var t = textures.FirstOrDefault(x => x.Id == id && x.Time <= time);
            if (t != null) return t.Rt;
            return secondary.FirstOrDefault(x => x.Id == id && x.Time <= time)?.Color;
        }

        private static float? Num(JSONNode? n) => n != null && n.IsNumber ? n.AsFloat : (float?)null;

        private static void Release(RenderTexture? rt)
        {
            if (rt == null) return;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }
    }

    /// <summary>Runs the Blit chain on the view camera's final image.</summary>
    public class PostProcessHook : MonoBehaviour
    {
        public PostProcessing? Owner;

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (Owner == null) Graphics.Blit(src, dst);
            else Owner.RenderImage(src, dst);
        }
    }
}
