using System.Collections.Generic;
using UnityEngine;

namespace StageFX
{
    /// <summary>
    /// Rotating-beam sweep for stage fixtures (Cyber Lights, Diablo Lights).
    /// The axel rotates on local Y from Start Angle to End Angle while the
    /// lens emission, god-ray, and optional Light fade Off -> Bright -> Off
    /// along the brightness curve (default: cosine peak at the sweep center,
    /// so full brightness at 0 degrees and dark at +/-90).
    ///
    /// Works in edit mode: scrub Progress in the inspector, or use the
    /// Play/Stop buttons in the custom editor. Add one component per fixture
    /// group (floor, left wall, right wall...) and list the fixtures.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Effects/Sweep Beam Effect")]
    public class SweepBeamEffect : MonoBehaviour
    {
        [System.Serializable]
        public class Fixture
        {
            [Tooltip("Transform rotated on local Y.")]
            public Transform axel;
            [Tooltip("Lens renderer. Its material must have Emission enabled once " +
                     "in the material inspector; color is driven per-renderer from here.")]
            public Renderer lens;
            [Tooltip("God-ray renderer (beam mesh). Color property is auto-detected.")]
            public Renderer godRay;
            [Tooltip("Optional real Light on the fixture, faded with the beam.")]
            public Light lightSource;

            // cached at first apply
            [System.NonSerialized] public int godRayColorId = -1;
            [System.NonSerialized] public bool warned;
        }

        [Header("Fixtures in this group")]
        public List<Fixture> fixtures = new List<Fixture>();

        [Header("Sweep")]
        [Tooltip("Axel local Y at Progress 0.")]
        public float startAngle = 90f;
        [Tooltip("Axel local Y at Progress 1.")]
        public float endAngle = -90f;
        [Tooltip("Seconds for one full sweep.")]
        [Min(0.05f)] public float duration = 4f;
        public enum LoopMode { Once, Loop, PingPong }
        public LoopMode loopMode = LoopMode.Once;

        [Header("Look")]
        [Tooltip("Brightness along the sweep. Default rises to 1 at the middle " +
                 "(axel at 0 degrees) and falls back to 0 at the ends.")]
        public AnimationCurve brightness = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2f),
            new Keyframe(0.5f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, -2f, 0f));

        [Tooltip("Lens emission at full brightness (HDR).")]
        [ColorUsage(false, true)]
        public Color lensColor = new Color(4f, 4f, 4f);

        [Tooltip("God-ray tint at full brightness (HDR). Default is a slightly " +
                 "cool white - pure-white beams read warm next to a white-hot lens.")]
        [ColorUsage(true, true)]
        public Color godRayColor = new Color(1.7f, 1.85f, 2f, 1f);

        [Tooltip("Real Light intensity at full brightness.")]
        [Min(0f)] public float lightMaxIntensity = 8f;

        [Tooltip("Leave empty to auto-detect the god-ray color property " +
                 "(_BaseColor, _Color, _TintColor, _UnlitColor). Set to override.")]
        public string godRayColorProperty = "";

        [Header("State")]
        [Range(0f, 1f)] public float progress;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly string[] ColorPropertyCandidates =
            { "_BaseColor", "_Color", "_TintColor", "_UnlitColor" };
        static MaterialPropertyBlock mpb;

        bool playing;
        float lastAppliedProgress = -1f;
        int direction = 1; // ping-pong

        /// <summary>Start the sweep from the beginning.</summary>
        public void Play()
        {
            progress = 0f;
            direction = 1;
            playing = true;
        }

        /// <summary>Freeze the sweep where it is.</summary>
        public void Stop() => playing = false;

        /// <summary>Stop and reset to dark at the start angle.</summary>
        public void ResetSweep()
        {
            playing = false;
            progress = 0f;
            Apply();
        }

        public bool IsPlaying => playing;

        void OnEnable() => Apply();
        void OnValidate() { lastAppliedProgress = -1f; Apply(); }

        void Update()
        {
            if (playing && Application.isPlaying)
            {
                float step = Time.deltaTime / duration;
                switch (loopMode)
                {
                    case LoopMode.Once:
                        progress += step;
                        if (progress >= 1f) { progress = 1f; playing = false; }
                        break;
                    case LoopMode.Loop:
                        progress = Mathf.Repeat(progress + step, 1f);
                        break;
                    case LoopMode.PingPong:
                        progress += step * direction;
                        if (progress >= 1f) { progress = 1f; direction = -1; }
                        else if (progress <= 0f) { progress = 0f; direction = 1; }
                        break;
                }
            }
            Apply();
        }

        /// <summary>Drive the effect in edit mode from the custom editor.</summary>
        public void EditorTick(float deltaTime)
        {
            progress = loopMode == LoopMode.Once
                ? Mathf.Min(1f, progress + deltaTime / duration)
                : Mathf.Repeat(progress + deltaTime / duration, 1f);
            Apply();
        }

        void Apply()
        {
            if (Mathf.Approximately(progress, lastAppliedProgress)) return;
            lastAppliedProgress = progress;

            mpb ??= new MaterialPropertyBlock();

            float angle = Mathf.Lerp(startAngle, endAngle, progress);
            float b = Mathf.Max(0f, brightness.Evaluate(progress));

            foreach (var f in fixtures)
            {
                if (f == null) continue;

                if (f.axel != null)
                {
                    var e = f.axel.localEulerAngles;
                    f.axel.localEulerAngles = new Vector3(e.x, angle, e.z);
                }

                if (f.lens != null)
                {
                    f.lens.GetPropertyBlock(mpb);
                    mpb.SetColor(EmissionColorId, lensColor * b);
                    f.lens.SetPropertyBlock(mpb);
                }

                if (f.godRay != null)
                {
                    if (f.godRayColorId < 0) ResolveGodRayProperty(f);
                    if (f.godRayColorId >= 0)
                    {
                        var c = godRayColor * b;
                        c.a = godRayColor.a * b; // alpha fade for transparent shaders
                        f.godRay.GetPropertyBlock(mpb);
                        mpb.SetColor(f.godRayColorId, c);
                        f.godRay.SetPropertyBlock(mpb);
                        // fully dark rays still cost overdraw; cull them
                        f.godRay.enabled = b > 0.002f;
                    }
                }

                if (f.lightSource != null)
                {
                    f.lightSource.intensity = lightMaxIntensity * b;
                    f.lightSource.enabled = b > 0.002f;
                }
            }
        }

        void ResolveGodRayProperty(Fixture f)
        {
            var mat = f.godRay.sharedMaterial;
            if (mat == null) return;

            if (!string.IsNullOrEmpty(godRayColorProperty))
            {
                if (mat.HasProperty(godRayColorProperty))
                {
                    f.godRayColorId = Shader.PropertyToID(godRayColorProperty);
                    return;
                }
            }
            else
            {
                foreach (var name in ColorPropertyCandidates)
                {
                    if (mat.HasProperty(name))
                    {
                        f.godRayColorId = Shader.PropertyToID(name);
                        return;
                    }
                }
            }

            if (!f.warned)
            {
                f.warned = true;
                Debug.LogWarning(
                    $"[SweepBeam] '{name}': god-ray material '{mat.name}' (shader " +
                    $"'{mat.shader.name}') has no recognized color property. Set " +
                    "'God Ray Color Property' to the shader's color property name.",
                    f.godRay);
            }
        }
    }
}
