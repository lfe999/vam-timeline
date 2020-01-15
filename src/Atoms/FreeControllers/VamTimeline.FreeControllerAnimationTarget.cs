using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VamTimeline
{
    /// <summary>
    /// VaM Timeline
    /// By Acidbubbles
    /// Animation timeline with keyframes
    /// Source: https://github.com/acidbubbles/vam-timeline
    /// </summary>
    public class FreeControllerAnimationTarget : IAnimationTarget
    {
        public FreeControllerV3 Controller;
        public SortedDictionary<float, KeyframeSettings> Settings = new SortedDictionary<float, KeyframeSettings>();
        public AnimationCurve X = new AnimationCurve();
        public AnimationCurve Y = new AnimationCurve();
        public AnimationCurve Z = new AnimationCurve();
        public AnimationCurve RotX = new AnimationCurve();
        public AnimationCurve RotY = new AnimationCurve();
        public AnimationCurve RotZ = new AnimationCurve();
        public AnimationCurve RotW = new AnimationCurve();
        public List<AnimationCurve> Curves;

        public string Name => Controller.name;

        public FreeControllerAnimationTarget(FreeControllerV3 controller)
        {
            Curves = new List<AnimationCurve> {
                X, Y, Z, RotX, RotY, RotZ, RotW
            };
            Controller = controller;
        }

        #region Control

        public IEnumerable<AnimationCurve> GetCurves()
        {
            return Curves;
        }

        public void ReapplyCurveTypes()
        {
            if (X.keys.Length <= 2) return;
            foreach (var setting in Settings)
            {
                if (setting.Value.CurveType == CurveTypeValues.LeaveAsIs)
                    continue;

                // TODO: We should allow curve control for non-looping animations
                if (setting.Key == 0f || setting.Key == X.keys.Last().time)
                    continue;

                foreach (var curve in Curves)
                {
                    curve.ChangeCurve(setting.Key, setting.Value.CurveType);
                }
            }
        }

        public void ReapplyCurvesToClip(AnimationClip clip)
        {
            var path = GetRelativePath();
            clip.SetCurve(path, typeof(Transform), "localPosition.x", X);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", Y);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", Z);
            clip.SetCurve(path, typeof(Transform), "localRotation.x", RotX);
            clip.SetCurve(path, typeof(Transform), "localRotation.y", RotY);
            clip.SetCurve(path, typeof(Transform), "localRotation.z", RotZ);
            clip.SetCurve(path, typeof(Transform), "localRotation.w", RotW);
        }

        public void SmoothLoop()
        {
            foreach (var curve in Curves)
            {
                curve.SmoothLoop();
            }
        }

        private string GetRelativePath()
        {
            var root = Controller.containingAtom.transform;
            var target = Controller.transform;
            var parts = new List<string>();
            Transform t = target;
            while (t != root && t != t.root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        #endregion

        #region Keyframes control

        public void SetKeyframeToCurrentTransform(float time)
        {
            SetKeyframe(time, Controller.transform.localPosition, Controller.transform.localRotation);
        }

        public void SetKeyframe(float time, Vector3 localPosition, Quaternion locationRotation)
        {
            X.SetKeyframe(time, localPosition.x);
            Y.SetKeyframe(time, localPosition.y);
            Z.SetKeyframe(time, localPosition.z);
            RotX.SetKeyframe(time, locationRotation.x);
            RotY.SetKeyframe(time, locationRotation.y);
            RotZ.SetKeyframe(time, locationRotation.z);
            RotW.SetKeyframe(time, locationRotation.w);
            if (!Settings.ContainsKey(time))
                Settings[time] = new KeyframeSettings { CurveType = CurveTypeValues.Smooth };
        }

        public void DeleteFrame(float time)
        {
            foreach (var curve in GetCurves())
            {
                var key = Array.FindIndex(curve.keys, k => k.time == time);
                if (key != -1) curve.RemoveKey(key);
                var settingIndex = Settings.Remove(time);
            }
        }

        public IEnumerable<float> GetAllKeyframesTime()
        {
            return Curves.SelectMany(c => c.keys).Select(k => k.time).Distinct();
        }

        #endregion

        #region Curves

        public void ChangeCurve(float time, string curveType)
        {
            if (string.IsNullOrEmpty(curveType)) return;

            UpdateSetting(time, curveType);
        }

        public void SmoothAllFrames()
        {
            foreach (var curve in Curves)
            {
                curve.SmoothAllFrames();
            }

            foreach (var time in X.keys.Select(k => k.time))
                UpdateSetting(time, CurveTypeValues.Smooth);
        }

        #endregion

        #region Snapshots

        public FreeControllerV3Snapshot GetCurveSnapshot(float time)
        {
            if (!X.keys.Any(k => k.time == time)) return null;
            KeyframeSettings setting;
            return new FreeControllerV3Snapshot
            {
                X = X.keys.First(k => k.time == time),
                Y = Y.keys.First(k => k.time == time),
                Z = Z.keys.First(k => k.time == time),
                RotX = RotX.keys.First(k => k.time == time),
                RotY = RotY.keys.First(k => k.time == time),
                RotZ = RotZ.keys.First(k => k.time == time),
                RotW = RotW.keys.First(k => k.time == time),
                CurveType = Settings.TryGetValue(time, out setting) ? setting.CurveType : CurveTypeValues.LeaveAsIs
            };
        }

        public void SetCurveSnapshot(float time, FreeControllerV3Snapshot snapshot)
        {
            X.SetKeySnapshot(time, snapshot.X);
            Y.SetKeySnapshot(time, snapshot.Y);
            Z.SetKeySnapshot(time, snapshot.Z);
            RotX.SetKeySnapshot(time, snapshot.RotX);
            RotY.SetKeySnapshot(time, snapshot.RotY);
            RotZ.SetKeySnapshot(time, snapshot.RotZ);
            RotW.SetKeySnapshot(time, snapshot.RotW);
            UpdateSetting(time, snapshot.CurveType);
        }

        private void UpdateSetting(float time, string curveType)
        {
            if (Settings.ContainsKey(time))
                Settings[time].CurveType = curveType;
            else
                Settings.Add(time, new KeyframeSettings { CurveType = curveType });
        }

        #endregion

        #region  Rendering

        public void RenderDebugInfo(StringBuilder display, float time)
        {
            RenderStateController(time, display, "X", X);
            /*
            RenderStateController(time, display, "Y", Y);
            RenderStateController(time, display, "Z", Z);
            RenderStateController(time, display, "RotX", RotX);
            RenderStateController(time, display, "RotY", RotY);
            RenderStateController(time, display, "RotZ", RotZ);
            RenderStateController(time, display, "RotW", RotW);
            */
        }

        private static void RenderStateController(float time, StringBuilder display, string name, AnimationCurve curve)
        {
            display.AppendLine($"{name}");
            foreach (var keyframe in curve.keys)
            {
                display.AppendLine($"  {(keyframe.time == time ? "+" : "-")} {keyframe.time:0.00}s: {keyframe.value:0.00}");
                display.AppendLine($"    Tngt in: {keyframe.inTangent:0.00} out: {keyframe.outTangent:0.00}");
                display.AppendLine($"    Wght in: {keyframe.inWeight:0.00} out: {keyframe.outWeight:0.00} {keyframe.weightedMode}");
            }
        }

        #endregion
    }
}
