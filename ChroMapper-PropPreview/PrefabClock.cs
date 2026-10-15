using System;
using System.Linq;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// Keeps a prefab instance's Animators and particle systems at the song's time. In the game
    /// Vivify starts them when the prefab spawns and ties their speed to the song
    /// (Controllers/Sync/SyncExtensions.cs), so what they show depends on the song seconds since
    /// the spawn. Here they are stepped by hand to exactly that: forward with the song while it
    /// plays, and replayed from the spawn when the editor jumps (backwards, or far ahead).
    /// </summary>
    public class PrefabClock
    {
        private const float MaxForward = 1f; // a bigger jump replays from the spawn
        private const int MaxSteps = 200;    // animator steps when replaying

        private readonly GameObject go;
        private readonly Animator[] animators;
        private readonly ParticleSystem[] particles;
        private readonly float spawnSeconds;
        private float? at; // the song seconds since spawn that the instance shows

        public PrefabClock(GameObject go, float spawnSeconds)
        {
            this.go = go;
            this.spawnSeconds = spawnSeconds;
            animators = go.GetComponentsInChildren<Animator>(true);
            // Root systems only, simulated with their children, so sub-emitters (fireworks) fire.
            particles = go.GetComponentsInChildren<ParticleSystem>(true)
                .Where(p => p.transform.parent == null || p.transform.parent.GetComponentInParent<ParticleSystem>(true) == null)
                .ToArray();
            // Animators are evaluated by hand (Animator.Update) instead of by Unity's clock.
            foreach (var a in animators) a.enabled = false;
        }

        public bool Empty => animators.Length == 0 && particles.Length == 0;

        /// <param name="seconds">The song's current time in seconds.</param>
        /// <param name="reapply">Puts SetAnimatorProperty values back after a rebind reset them.</param>
        public void Sync(float seconds, Action reapply)
        {
            if (!go.activeInHierarchy)
            {
                at = null;
                return;
            }
            var target = Mathf.Max(0, seconds - spawnSeconds);
            if (at is float now && target >= now && target - now <= MaxForward)
            {
                var d = target - now;
                if (d <= 0) return;
                foreach (var a in animators) if (a != null) a.Update(d);
                foreach (var p in particles) if (p != null) p.Simulate(d, true, false, false);
                at = target;
                return;
            }
            foreach (var a in animators)
            {
                if (a == null) continue;
                a.Rebind();
                a.Update(0);
            }
            reapply();
            var steps = Mathf.Clamp(Mathf.CeilToInt(target / 0.1f), 1, MaxSteps);
            for (var i = 0; i < steps; i++)
                foreach (var a in animators) if (a != null) a.Update(target / steps);
            foreach (var p in particles) if (p != null) p.Simulate(target, true, true, false);
            at = target;
        }
    }
}
