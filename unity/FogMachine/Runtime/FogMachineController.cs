using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace FogMachine
{
    /// <summary>
    /// Drives a VFX Graph fog machine. One continuous throttle from
    /// turtle-creep (0) to jet-blast (1), discrete preset switching with
    /// smooth crossfades, and a momentary Blast() for theatrical hits.
    ///
    /// The target VisualEffect must expose the properties listed in
    /// Docs/VFXGRAPH_BUILD_GUIDE.md (SpawnRate, EmitSpeed, ConeAngle,
    /// ParticleLifetime, ParticleSize, Turbulence, Drag, Buoyancy,
    /// Density, FogColor). Missing properties are reported once at startup
    /// and skipped, so a partially built graph still runs.
    /// </summary>
    [RequireComponent(typeof(VisualEffect))]
    [AddComponentMenu("Effects/Fog Machine Controller")]
    public class FogMachineController : MonoBehaviour
    {
        // Exposed property names in the VFX Graph. Keep in sync with the build guide.
        static readonly int SpawnRateId = Shader.PropertyToID("SpawnRate");
        static readonly int EmitSpeedId = Shader.PropertyToID("EmitSpeed");
        static readonly int ConeAngleId = Shader.PropertyToID("ConeAngle");
        static readonly int LifetimeId = Shader.PropertyToID("ParticleLifetime");
        static readonly int SizeId = Shader.PropertyToID("ParticleSize");
        static readonly int TurbulenceId = Shader.PropertyToID("Turbulence");
        static readonly int DragId = Shader.PropertyToID("Drag");
        static readonly int BuoyancyId = Shader.PropertyToID("Buoyancy");
        static readonly int DensityId = Shader.PropertyToID("Density");
        static readonly int FogColorId = Shader.PropertyToID("FogColor");

        [Tooltip("Preset chain, any order — sorted by intensity at runtime. " +
                 "Leave empty to use the built-in five (Turtle Creep → Jet Blast).")]
        [SerializeField] List<FogMachinePreset> presets = new List<FogMachinePreset>();

        [Tooltip("Preset index applied on enable (into the sorted chain).")]
        [SerializeField, Min(0)] int startPresetIndex = 2;

        [Tooltip("Seconds a preset switch or throttle move takes to crossfade.")]
        [SerializeField, Min(0f)] float transitionSeconds = 1.5f;

        [Tooltip("Optional governor that scales emission down when the GPU struggles.")]
        [SerializeField] FogPerformanceGovernor governor;

        VisualEffect vfx;
        FogMachinePreset[] chain;          // sorted by intensity
        FogParams current;                 // what the graph is showing right now
        FogParams from, to;                // active crossfade endpoints
        float transitionT = 1f;            // 1 = settled on 'to'
        int activePresetIndex;
        float throttle01 = -1f;            // <0 = driven by preset, not throttle

        // Blast state: temporary excursion to full jet, then return.
        float blastUntil = -1f;
        FogParams blastReturn;

        // Property-name existence flags, checked once.
        bool hasSpawnRate, hasEmitSpeed, hasConeAngle, hasLifetime, hasSize,
             hasTurbulence, hasDrag, hasBuoyancy, hasDensity, hasFogColor;

        /// <summary>Sorted preset chain currently in use (built-ins if none assigned).</summary>
        public IReadOnlyList<FogMachinePreset> Chain => chain;

        /// <summary>Index of the active preset in the sorted chain.</summary>
        public int ActivePresetIndex => activePresetIndex;

        /// <summary>True while a Blast() excursion is running.</summary>
        public bool IsBlasting => Time.time < blastUntil;

        void Awake()
        {
            vfx = GetComponent<VisualEffect>();
            BuildChain();
            CachePropertyPresence();
        }

        void OnEnable()
        {
            activePresetIndex = Mathf.Clamp(startPresetIndex, 0, chain.Length - 1);
            current = from = to = chain[activePresetIndex].ToParams();
            transitionT = 1f;
            Push(current);
        }

        void Update()
        {
            // Wind down a finished blast.
            if (blastUntil > 0f && Time.time >= blastUntil)
            {
                blastUntil = -1f;
                BeginTransition(blastReturn);
            }

            if (transitionT < 1f)
            {
                transitionT = transitionSeconds <= 0f
                    ? 1f
                    : Mathf.Min(1f, transitionT + Time.deltaTime / transitionSeconds);
                current = FogParams.Lerp(from, to, Mathf.SmoothStep(0f, 1f, transitionT));
                Push(current);
            }
            else if (governor != null)
            {
                // No crossfade running, but the governor scale can still move.
                Push(current);
            }
        }

        /// <summary>
        /// Switch to a preset by index in the sorted chain (0 = slowest).
        /// </summary>
        public void ApplyPreset(int index)
        {
            index = Mathf.Clamp(index, 0, chain.Length - 1);
            activePresetIndex = index;
            throttle01 = -1f;
            BeginTransition(chain[index].ToParams());
        }

        /// <summary>Switch to a preset asset directly (must be in the chain to update the index).</summary>
        public void ApplyPreset(FogMachinePreset preset)
        {
            if (preset == null) return;
            for (int i = 0; i < chain.Length; i++)
            {
                if (chain[i] == preset) { ApplyPreset(i); return; }
            }
            throttle01 = -1f;
            BeginTransition(preset.ToParams());
        }

        /// <summary>
        /// One slider from turtle to jet: 0 = slowest preset, 1 = fastest,
        /// values in between blend smoothly across the whole chain.
        /// </summary>
        public void SetThrottle(float t)
        {
            throttle01 = Mathf.Clamp01(t);
            BeginTransition(EvaluateChain(throttle01));
        }

        /// <summary>Current throttle if throttle-driven, else the active preset's intensity.</summary>
        public float GetThrottle()
        {
            return throttle01 >= 0f ? throttle01 : chain[activePresetIndex].intensity;
        }

        /// <summary>
        /// Theatrical hit: slam to the top of the chain (full jet) for
        /// <paramref name="seconds"/>, then ease back to whatever was playing.
        /// </summary>
        public void Blast(float seconds = 3f)
        {
            if (!IsBlasting)
                blastReturn = to; // remember where to come home to
            blastUntil = Time.time + Mathf.Max(0.1f, seconds);
            BeginTransition(chain[chain.Length - 1].ToParams());
        }

        /// <summary>Stop emitting (existing fog lives out its lifetime).</summary>
        public void Cut()
        {
            var silent = to;
            silent.spawnRate = 0f;
            BeginTransition(silent);
        }

        void BeginTransition(in FogParams target)
        {
            from = current;
            to = target;
            transitionT = 0f;
            if (transitionSeconds <= 0f)
            {
                current = to;
                transitionT = 1f;
                Push(current);
            }
        }

        /// <summary>Piecewise-lerp across the sorted chain by intensity position.</summary>
        FogParams EvaluateChain(float t)
        {
            if (chain.Length == 1) return chain[0].ToParams();

            float lo = chain[0].intensity;
            float hi = chain[chain.Length - 1].intensity;
            float x = Mathf.Lerp(lo, hi, t);

            for (int i = 0; i < chain.Length - 1; i++)
            {
                float a = chain[i].intensity;
                float b = chain[i + 1].intensity;
                if (x <= b || i == chain.Length - 2)
                {
                    float span = Mathf.Max(1e-4f, b - a);
                    float local = Mathf.Clamp01((x - a) / span);
                    return FogParams.Lerp(chain[i].ToParams(), chain[i + 1].ToParams(), local);
                }
            }
            return chain[chain.Length - 1].ToParams();
        }

        void Push(in FogParams p)
        {
            // The governor trades particle count for a little extra size so the
            // fog stays visually full while overdraw drops.
            float quality = governor != null ? governor.QualityScale : 1f;
            float sizeCompensation = 1f + (1f - quality) * 0.35f;

            if (hasSpawnRate) vfx.SetFloat(SpawnRateId, p.spawnRate * quality);
            if (hasEmitSpeed) vfx.SetFloat(EmitSpeedId, p.emitSpeed);
            if (hasConeAngle) vfx.SetFloat(ConeAngleId, p.coneAngle);
            if (hasLifetime) vfx.SetFloat(LifetimeId, p.lifetime);
            if (hasSize) vfx.SetFloat(SizeId, p.size * sizeCompensation);
            if (hasTurbulence) vfx.SetFloat(TurbulenceId, p.turbulence);
            if (hasDrag) vfx.SetFloat(DragId, p.drag);
            if (hasBuoyancy) vfx.SetFloat(BuoyancyId, p.buoyancy);
            if (hasDensity) vfx.SetFloat(DensityId, p.density);
            if (hasFogColor) vfx.SetVector4(FogColorId, p.color);
        }

        void BuildChain()
        {
            var list = new List<FogMachinePreset>();
            foreach (var p in presets)
                if (p != null) list.Add(p);

            if (list.Count == 0)
                list.AddRange(FogMachinePreset.CreateBuiltInChain());

            list.Sort((a, b) => a.intensity.CompareTo(b.intensity));
            chain = list.ToArray();
        }

        void CachePropertyPresence()
        {
            hasSpawnRate = vfx.HasFloat(SpawnRateId);
            hasEmitSpeed = vfx.HasFloat(EmitSpeedId);
            hasConeAngle = vfx.HasFloat(ConeAngleId);
            hasLifetime = vfx.HasFloat(LifetimeId);
            hasSize = vfx.HasFloat(SizeId);
            hasTurbulence = vfx.HasFloat(TurbulenceId);
            hasDrag = vfx.HasFloat(DragId);
            hasBuoyancy = vfx.HasFloat(BuoyancyId);
            hasDensity = vfx.HasFloat(DensityId);
            hasFogColor = vfx.HasVector4(FogColorId);

            var missing = new List<string>();
            if (!hasSpawnRate) missing.Add("SpawnRate (float)");
            if (!hasEmitSpeed) missing.Add("EmitSpeed (float)");
            if (!hasConeAngle) missing.Add("ConeAngle (float)");
            if (!hasLifetime) missing.Add("ParticleLifetime (float)");
            if (!hasSize) missing.Add("ParticleSize (float)");
            if (!hasTurbulence) missing.Add("Turbulence (float)");
            if (!hasDrag) missing.Add("Drag (float)");
            if (!hasBuoyancy) missing.Add("Buoyancy (float)");
            if (!hasDensity) missing.Add("Density (float)");
            if (!hasFogColor) missing.Add("FogColor (Color)");

            if (missing.Count > 0)
                Debug.LogWarning(
                    $"[FogMachine] '{name}': VFX Graph is missing exposed properties: " +
                    string.Join(", ", missing) +
                    ". See Docs/VFXGRAPH_BUILD_GUIDE.md — those channels will be skipped.",
                    this);
        }
    }
}
