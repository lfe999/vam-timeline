using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace VamTimeline
{
    /// <summary>
    /// VaM Timeline
    /// By Acidbubbles
    /// Animation timeline with keyframes
    /// Source: https://github.com/acidbubbles/vam-timeline
    /// </summary>
    public class MocapScreen : ScreenBase
    {
        public const string ScreenName = "Mocap";
        private static readonly TimeSpan _importMocapTimeout = TimeSpan.FromSeconds(5);

        public override string name => ScreenName;

        private UIDynamicButton _importRecordedUI;
        private JSONStorableStringChooser _importRecordedOptionsJSON;
        private UIDynamicButton _reduceKeyframesUI;
        private JSONStorableFloat _reduceMinPosDistanceJSON;
        private JSONStorableFloat _reduceMaxFramesPerSecondJSON;
        private JSONStorableFloat _reduceMinRotationJSON;

        public MocapScreen(IAtomPlugin plugin)
            : base(plugin)
        {

        }
        public override void Init()
        {
            base.Init();

            // Right side

            CreateChangeScreenButton("<b><</b> <i>Back</i>", MoreScreen.ScreenName, true);

            CreateSpacer(true);

            _importRecordedOptionsJSON = new JSONStorableStringChooser(
                "Import Recorded Animation Options",
                 new List<string> { "Keyframe Reduction", "Fixed Frames per Second" },
                 "Keyframe Reduction",
                 "Import Recorded Animation Options")
            {
                isStorable = false
            };
            RegisterStorable(_importRecordedOptionsJSON);
            var importRecordedOptionsUI = plugin.CreateScrollablePopup(_importRecordedOptionsJSON, true);
            RegisterComponent(importRecordedOptionsUI);

            _reduceMinPosDistanceJSON = new JSONStorableFloat("Minimum Distance Between Frames", 0.04f, 0.001f, 0.5f, true);
            RegisterStorable(_reduceMinPosDistanceJSON);
            var reduceMinPosDistanceUI = plugin.CreateSlider(_reduceMinPosDistanceJSON, true);
            RegisterComponent(reduceMinPosDistanceUI);

            _reduceMinRotationJSON = new JSONStorableFloat("Minimum Rotation Between Frames", 10f, 0.1f, 90f, true);
            RegisterStorable(_reduceMinRotationJSON);
            var reduceMinRotationUI = plugin.CreateSlider(_reduceMinRotationJSON, true);
            RegisterComponent(reduceMinRotationUI);

            _reduceMaxFramesPerSecondJSON = new JSONStorableFloat("Max Frames per Second", 5f, (float val) => _reduceMaxFramesPerSecondJSON.valNoCallback = Mathf.Round(val), 1f, 10f, true);
            RegisterStorable(_reduceMaxFramesPerSecondJSON);
            var maxFramesPerSecondUI = plugin.CreateSlider(_reduceMaxFramesPerSecondJSON, true);
            RegisterComponent(maxFramesPerSecondUI);

            CreateSpacer(true);

            _importRecordedUI = plugin.CreateButton("Import Recorded Animation (Mocap)", true);
            _importRecordedUI.button.onClick.AddListener(() => ImportRecorded());
            RegisterComponent(_importRecordedUI);

            CreateSpacer(true);

            _reduceKeyframesUI = plugin.CreateButton("Reduce Float Params Keyframes", true);
            _reduceKeyframesUI.button.onClick.AddListener(() => ReduceKeyframes());
            RegisterComponent(_reduceKeyframesUI);
        }

        private void ImportRecorded()
        {
            if (SuperController.singleton.motionAnimationMaster == null || plugin.containingAtom.motionAnimationControls == null)
            {
                SuperController.LogError("VamTimeline: Missing motion animation controls");
                return;
            }

            current.loop = SuperController.singleton.motionAnimationMaster.loop;
            var length = plugin.containingAtom.motionAnimationControls.Select(m => m.clip.clipLength).Max().Snap(0.01f);
            if (length < 0.001f)
            {
                SuperController.LogError("VamTimeline: No motion animation to import.");
                return;
            }
            current.animationLength = length;

            if (_importRecordedUI == null) return;
            _importRecordedUI.buttonText.text = "Importing, please wait...";
            _importRecordedUI.button.interactable = false;

            plugin.StartCoroutine(ImportRecordedCoroutine());
        }

        private IEnumerator ImportRecordedCoroutine()
        {
            var containingAtom = plugin.containingAtom;
            var totalStopwatch = Stopwatch.StartNew();

            yield return 0;

            var controlCounter = 0;
            foreach (var mot in containingAtom.motionAnimationControls)
            {
                FreeControllerAnimationTarget target = null;
                FreeControllerV3 ctrl;

                _importRecordedUI.buttonText.text = $"Importing, please wait... ({++controlCounter} / {containingAtom.motionAnimationControls.Length})";

                try
                {
                    if (mot == null || mot.clip == null) continue;
                    if (mot.clip.clipLength <= 0.1) continue;
                    ctrl = mot.controller;
                    current.Remove(ctrl);
                    target = current.Add(ctrl);
                    target.StartBulkUpdates();
                    target.SetKeyframeToCurrentTransform(0);
                    target.SetKeyframeToCurrentTransform(current.animationLength);
                }
                catch (Exception exc)
                {
                    SuperController.LogError($"VamTimeline.{nameof(MocapScreen)}.{nameof(ImportRecordedCoroutine)}[Init]: {exc}");
                    target?.EndBulkUpdates();
                    yield break;
                }

                IEnumerator enumerator;
                try
                {
                    if (_importRecordedOptionsJSON.val == "Keyframe Reduction")
                        enumerator = ExtractFramesWithReductionTechnique(mot.clip, target, ctrl).GetEnumerator();
                    else
                        enumerator = ExtractFramesWithFpsTechnique(mot.clip, target, ctrl).GetEnumerator();
                }
                catch
                {
                    target.EndBulkUpdates();
                    throw;
                }

                while (TryMoveNext(enumerator, target))
                    yield return enumerator.Current;

                target.EndBulkUpdates();
            }

            _importRecordedUI.buttonText.text = "Import Recorded Animation (Mocap)";
            _importRecordedUI.button.interactable = true;
        }

        private bool TryMoveNext(IEnumerator enumerator, FreeControllerAnimationTarget target)
        {
            try
            {
                return enumerator.MoveNext();
            }
            catch
            {
                target.EndBulkUpdates();
                throw;
            }
        }

        private struct ControllerKeyframe
        {
            public float time;
            public Vector3 position;
            public Quaternion rotation;

            public static ControllerKeyframe FromStep(float time, MotionAnimationStep s, Atom containingAtom, FreeControllerV3 ctrl)
            {
                var localPosition = s.positionOn ? s.position - containingAtom.transform.position : ctrl.transform.localPosition;
                var locationRotation = s.rotationOn ? Quaternion.Inverse(containingAtom.transform.rotation) * s.rotation : ctrl.transform.localRotation;
                return new ControllerKeyframe
                {
                    time = time,
                    position = localPosition,
                    rotation = locationRotation
                };
            }
        }

        public class ReducerBucket
        {
            public int from;
            public int to;
            public int keyWithLargestPositionDistance = -1;
            public float largestPositionDistance;
            public int keyWithLargestRotationAngle = -1;
            public float largestRotationAngle;
        }

        private IEnumerable ExtractFramesWithReductionTechnique(MotionAnimationClip clip, FreeControllerAnimationTarget target, FreeControllerV3 ctrl)
        {
            var minFrameDistance = 1f / _reduceMaxFramesPerSecondJSON.val;
            var maxIterations = (int)(clip.clipLength * 10);

            var containingAtom = plugin.containingAtom;
            var steps = clip.steps
                .Where(s => s.positionOn || s.rotationOn)
                .GroupBy(s => s.timeStep.Snap(minFrameDistance).ToMilliseconds())
                .Select(g =>
                {
                    var step = g.OrderBy(s => Math.Abs(g.Key - s.timeStep)).First();
                    return ControllerKeyframe.FromStep((g.Key / 1000f).Snap(), step, containingAtom, ctrl);
                })
                .ToList();

            if (steps.Count < 2) yield break;

            target.SetKeyframe(0, steps[0].position, steps[0].rotation);
            target.SetKeyframe(current.animationLength, steps[steps.Count - 1].position, steps[steps.Count - 1].rotation);

            var buckets = new List<ReducerBucket>
            {
                Scan(steps, target, 1, steps.Count - 2)
            };

            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                // Scan for largest difference with curve
                var bucketWithLargestPositionDistance = -1;
                var keyWithLargestPositionDistance = -1;
                var largestPositionDistance = 0f;
                var bucketWithLargestRotationAngle = -1;
                var keyWithLargestRotationAngle = -1;
                var largestRotationAngle = 0f;
                for (var bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
                {
                    var bucket = buckets[bucketIndex];
                    if (bucket.largestPositionDistance > largestPositionDistance)
                    {
                        largestPositionDistance = bucket.largestPositionDistance;
                        keyWithLargestPositionDistance = bucket.keyWithLargestPositionDistance;
                        bucketWithLargestPositionDistance = bucketIndex;
                    }
                    if (bucket.largestRotationAngle > largestRotationAngle)
                    {
                        largestRotationAngle = bucket.largestRotationAngle;
                        keyWithLargestRotationAngle = bucket.keyWithLargestRotationAngle;
                        bucketWithLargestRotationAngle = bucketIndex;
                    }
                }

                // Cannot find large enough diffs, exit
                if (keyWithLargestRotationAngle == -1 || keyWithLargestPositionDistance == -1) break;
                var posInRange = largestPositionDistance >= _reduceMinPosDistanceJSON.val;
                var rotInRange = largestRotationAngle >= _reduceMinRotationJSON.val;
                if (!posInRange && !rotInRange) break;

                // This is an attempt to compare translations and rotations
                var normalizedPositionDistance = largestPositionDistance / 0.4f;
                var normalizedRotationAngle = largestRotationAngle / 180f;
                var selectPosOverRot = (normalizedPositionDistance > normalizedRotationAngle) && posInRange;
                var keyToApply = selectPosOverRot ? keyWithLargestPositionDistance : keyWithLargestRotationAngle;

                var step = steps[keyToApply];
                var key = target.SetKeyframe(step.time, step.position, step.rotation);
                target.SmoothNeighbors(key);

                int bucketToSplitIndex;
                if (selectPosOverRot)
                    bucketToSplitIndex = bucketWithLargestPositionDistance;
                else
                    bucketToSplitIndex = bucketWithLargestRotationAngle;

                if (bucketToSplitIndex > -1)
                {
                    // Split buckets and exclude the scanned keyframe, we never have to scan it again.
                    var bucketToSplit = buckets[bucketToSplitIndex];
                    buckets.RemoveAt(bucketToSplitIndex);
                    if (bucketToSplit.to - keyToApply + 1 > 2)
                        buckets.Insert(bucketToSplitIndex, Scan(steps, target, keyToApply + 1, bucketToSplit.to));
                    if (keyToApply - 1 - bucketToSplit.from > 2)
                        buckets.Insert(bucketToSplitIndex, Scan(steps, target, bucketToSplit.from, keyToApply - 1));
                }

                yield return 0;
            }
        }

        private ReducerBucket Scan(List<ControllerKeyframe> steps, FreeControllerAnimationTarget target, int from, int to)
        {
            var bucket = new ReducerBucket
            {
                from = from,
                to = to
            };
            for (var i = from; i <= to; i++)
            {
                var step = steps[i];
                var positionDiff = Vector3.Distance(
                    new Vector3(
                        target.x.Evaluate(step.time),
                        target.y.Evaluate(step.time),
                        target.z.Evaluate(step.time)
                    ),
                    step.position
                );
                if (positionDiff > bucket.largestPositionDistance)
                {
                    bucket.largestPositionDistance = positionDiff;
                    bucket.keyWithLargestPositionDistance = i;
                }

                var rotationAngle = Vector3.Angle(
                    new Quaternion(
                        target.rotX.Evaluate(step.time),
                        target.rotY.Evaluate(step.time),
                        target.rotZ.Evaluate(step.time),
                        target.rotW.Evaluate(step.time)
                    ).eulerAngles,
                    step.rotation.eulerAngles
                    );
                if (rotationAngle > bucket.largestRotationAngle)
                {
                    bucket.largestRotationAngle = rotationAngle;
                    bucket.keyWithLargestRotationAngle = i;
                }
            }
            return bucket;
        }

        private IEnumerable ExtractFramesWithFpsTechnique(MotionAnimationClip clip, FreeControllerAnimationTarget target, FreeControllerV3 ctrl)
        {
            var minPositionDistanceForFlat = 0.01f;
            var batchStopwatch = Stopwatch.StartNew();
            var containingAtom = plugin.containingAtom;
            var frameLength = 1f / _reduceMaxFramesPerSecondJSON.val;

            var lastRecordedFrame = float.MinValue;
            MotionAnimationStep previousStep = null;
            for (var stepIndex = 0; stepIndex < (clip.steps.Count - (current.loop ? 1 : 0)); stepIndex++)
            {
                try
                {
                    var step = clip.steps[stepIndex];
                    var time = step.timeStep.Snap(0.01f);
                    if (time - lastRecordedFrame < frameLength) continue;
                    var k = ControllerKeyframe.FromStep(time, step, containingAtom, ctrl);
                    target.SetKeyframe(time, k.position, k.rotation);
                    if (previousStep != null && (target.controller.name == "lFootControl" || target.controller.name == "rFootControl") && Vector3.Distance(previousStep.position, step.position) <= minPositionDistanceForFlat)
                    {
                        KeyframeSettings settings;
                        if (target.settings.TryGetValue(previousStep.timeStep.Snap().ToMilliseconds(), out settings))
                            target.ChangeCurve(previousStep.timeStep, CurveTypeValues.Linear);
                    }
                    lastRecordedFrame = time;
                    previousStep = step;
                }
                catch (Exception exc)
                {
                    SuperController.LogError($"VamTimeline.{nameof(MocapScreen)}.{nameof(ImportRecordedCoroutine)}[Step]: {exc}");
                    yield break;
                }

                if (batchStopwatch.ElapsedMilliseconds > 5)
                {
                    batchStopwatch.Reset();
                    yield return 0;
                    batchStopwatch.Start();
                }
            }
        }

        private void ReduceKeyframes()
        {
            _reduceKeyframesUI.buttonText.text = "Optimizing, please wait...";
            _reduceKeyframesUI.button.interactable = false;

            plugin.StartCoroutine(ReduceKeyframesCoroutine());
        }

        private IEnumerator ReduceKeyframesCoroutine()
        {
            foreach (var target in current.GetAllOrSelectedTargets().OfType<FreeControllerAnimationTarget>())
            {
                target.StartBulkUpdates();
                try
                {
                    // ReduceKeyframes(target.X, target.Y, target.Z, target.RotX, target.RotY, target.RotZ, target.RotW);
                }
                catch (Exception exc)
                {
                    _reduceKeyframesUI.button.interactable = true;
                    _reduceKeyframesUI.buttonText.text = "Reduce Float Params Keyframes";
                    SuperController.LogError($"VamTimeline.{nameof(MocapScreen)}.{nameof(ReduceKeyframesCoroutine)}[FloatParam]: {exc}");
                    yield break;
                }
                finally
                {
                    target.dirty = true;
                    target.EndBulkUpdates();
                }
                yield return 0;
            }

            foreach (var target in current.GetAllOrSelectedTargets().OfType<FloatParamAnimationTarget>())
            {
                target.StartBulkUpdates();
                try
                {
                    ReduceKeyframes(target.value);
                }
                catch (Exception exc)
                {
                    _reduceKeyframesUI.button.interactable = true;
                    _reduceKeyframesUI.buttonText.text = "Reduce Float Params Keyframes";
                    SuperController.LogError($"VamTimeline.{nameof(MocapScreen)}.{nameof(ReduceKeyframesCoroutine)}[FloatParam]: {exc}");
                    yield break;
                }
                finally
                {
                    target.dirty = true;
                    target.EndBulkUpdates();
                }
                yield return 0;
            }

            _reduceKeyframesUI.button.interactable = true;
            _reduceKeyframesUI.buttonText.text = "Reduce Float Params Keyframes";
        }

        private void ReduceKeyframes(AnimationCurve source)
        {
            var sw = Stopwatch.StartNew();
            var minFrameDistance = 1f / _reduceMaxFramesPerSecondJSON.val;
            var maxIterations = (int)(source[source.length - 1].time * 10);

            var batchStopwatch = Stopwatch.StartNew();
            var containingAtom = plugin.containingAtom;
            var steps = source.keys
                .GroupBy(s => s.time.Snap(minFrameDistance).ToMilliseconds())
                .Select(g =>
                {
                    var keyframe = g.OrderBy(s => Math.Abs(g.Key - s.time)).First();
                    return new Keyframe((g.Key / 1000f).Snap(), keyframe.value, 0, 0);
                })
                .ToList();

            var target = new AnimationCurve();
            target.FlatFrame(target.AddKey(0, source[0].value));
            target.FlatFrame(target.AddKey(source[source.length - 1].time, source[source.length - 1].value));

            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                // Scan for largest difference with curve
                // TODO: Use the buckets strategy
                var keyWithLargestDiff = -1;
                var largestDiff = 0f;
                for (var i = 1; i < steps.Count - 1; i++)
                {
                    var diff = Mathf.Abs(target.Evaluate(steps[i].time) - steps[i].value);

                    if (diff > largestDiff)
                    {
                        largestDiff = diff;
                        keyWithLargestDiff = i;
                    }
                }

                // Cannot find large enough diffs, exit
                if (keyWithLargestDiff == -1) break;
                var inRange = largestDiff >= _reduceMinPosDistanceJSON.val;
                if (!inRange) break;

                // This is an attempt to compare translations and rotations
                var keyToApply = keyWithLargestDiff;

                var step = steps[keyToApply];
                steps.RemoveAt(keyToApply);
                var key = target.SetKeyframe(step.time, step.value);
                target.FlatFrame(key);
            }

            source.keys = target.keys;
        }
    }
}

