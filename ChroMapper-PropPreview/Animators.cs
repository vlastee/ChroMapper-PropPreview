using System.Collections.Generic;
using System.Linq;
using Beatmap.Base.Customs;
using SimpleJSON;
using UnityEngine;

namespace ChroMapper_PropPreview
{
    /// <summary>
    /// SetAnimatorProperty on prefab instances. Vivify's rules (Events/SetAnimatorProperty.cs):
    /// the event's `id` names an InstantiatePrefab instance, whose Animators (itself and children)
    /// get each property; Bool, Float and point definitions follow the usual timing (a Bool is on
    /// at 1 or more); Integer is set with SetFloat in Vivify, which Unity rejects, so it does
    /// nothing in the game and is skipped here; a Trigger fires once (false resets it), so it only
    /// fires while the song plays through it. PrefabClock keeps the animators at the song's time.
    /// </summary>
    public class Animators
    {
        private class Param
        {
            public Animator[] Animators = null!;
            public string Name = "";
            public string Type = "";
            public readonly Timeline Timeline = new Timeline();
        }

        private readonly List<Param> parameters = new List<Param>();
        private readonly List<(float time, Animator[] animators, string name, bool set)> triggers =
            new List<(float, Animator[], string, bool)>();
        private float lastTime = float.NegativeInfinity;

        public void Clear()
        {
            parameters.Clear();
            triggers.Clear();
        }

        public void Add(BaseCustomEvent ev, GameObject? instance)
        {
            if (instance == null) return;
            var animators = instance.GetComponentsInChildren<Animator>(true);
            if (animators.Length == 0) return;
            var props = ev.Data["properties"];
            if (props == null || !props.IsArray) return;
            foreach (var prop in props.Children)
            {
                string name = prop["id"], type = prop["type"];
                if (string.IsNullOrEmpty(name) || type == "Integer") continue;
                if (type == "Trigger")
                {
                    var v = prop["value"];
                    triggers.Add((ev.JsonTime, animators, name, v == null || !v.IsBoolean || v.AsBool));
                    continue;
                }
                var param = parameters.FirstOrDefault(p => p.Animators == animators && p.Name == name);
                if (param == null) parameters.Add(param = new Param { Animators = animators, Name = name, Type = type });
                var duration = ev.Data["duration"];
                var entry = new Timeline.Entry
                {
                    Time = ev.JsonTime,
                    Duration = duration != null && duration.IsNumber ? duration.AsFloat : 0,
                    Easing = Easings.Get(ev.Data["easing"]),
                };
                if (prop["value"] != null && prop["value"].IsArray) entry.Points = Points.Parse(prop["value"], 1);
                else entry.Static = prop["value"];
                param.Timeline.Add(entry);
            }
        }

        public void Apply(float time, bool playing)
        {
            foreach (var p in parameters)
            {
                var v = p.Timeline.Vector(time, out var s);
                if (v == null && s == null) continue;
                foreach (var a in p.Animators)
                {
                    if (a == null || !a.isActiveAndEnabled) continue;
                    if (p.Type == "Bool") a.SetBool(p.Name, v is Vector4 b ? b.x >= 1 : s!.AsBool);
                    else if (p.Type == "Float") a.SetFloat(p.Name, v?.x ?? s!.AsFloat);
                }
            }
            if (playing)
                foreach (var (t, animators, name, set) in triggers)
                {
                    if (t <= lastTime || t > time) continue;
                    foreach (var a in animators)
                    {
                        if (a == null || !a.isActiveAndEnabled) continue;
                        if (set) a.SetTrigger(name);
                        else a.ResetTrigger(name);
                    }
                }
            lastTime = time;
        }
    }
}
