using UnityEngine;

namespace FogMachine
{
    /// <summary>
    /// One complete "personality" for the fog machine — everything from a
    /// screaming jet to a floor-hugging turtle creep is just a different
    /// set of these numbers. Create assets via
    /// Assets > Create > Fog Machine > Preset, or use the built-in factory
    /// defaults (FogMachinePreset.CreateBuiltInChain).
    /// </summary>
    [CreateAssetMenu(fileName = "FogPreset", menuName = "Fog Machine/Preset", order = 220)]
    public class FogMachinePreset : ScriptableObject
    {
        [Tooltip("Where this preset sits on the throttle, 0 = slowest creep, 1 = full jet. " +
                 "The controller sorts its preset chain by this value.")]
        [Range(0f, 1f)] public float intensity = 0.5f;

        [Header("Emission")]
        [Tooltip("Particles spawned per second.")]
        [Min(0f)] public float spawnRate = 60f;

        [Tooltip("Initial velocity along the nozzle direction, m/s.")]
        [Min(0f)] public float emitSpeed = 3.5f;

        [Tooltip("Half-angle of the emission cone, degrees. Narrow = focused jet, wide = lazy pour.")]
        [Range(1f, 90f)] public float coneAngle = 30f;

        [Header("Particle life")]
        [Tooltip("Seconds each puff lives.")]
        [Min(0.1f)] public float lifetime = 6f;

        [Tooltip("World-space size of a fully grown puff, meters.")]
        [Min(0.01f)] public float size = 2.5f;

        [Header("Motion")]
        [Tooltip("Curl-noise turbulence strength. Jets churn, creep barely stirs.")]
        [Min(0f)] public float turbulence = 1.2f;

        [Tooltip("Linear drag. Higher = velocity dies off sooner after leaving the nozzle.")]
        [Min(0f)] public float drag = 2.5f;

        [Tooltip("Vertical force. Positive rises like hot smoke, negative sinks and " +
                 "hugs the floor like chilled fog-machine fog.")]
        public float buoyancy = -0.35f;

        [Header("Look")]
        [Tooltip("Overall opacity multiplier, 0..1.")]
        [Range(0f, 1f)] public float density = 0.9f;

        [Tooltip("Fog tint. Alpha is ignored (density drives opacity).")]
        public Color color = Color.white;

        /// <summary>Snapshot this preset into a plain interpolatable struct.</summary>
        public FogParams ToParams()
        {
            return new FogParams
            {
                spawnRate = spawnRate,
                emitSpeed = emitSpeed,
                coneAngle = coneAngle,
                lifetime = lifetime,
                size = size,
                turbulence = turbulence,
                drag = drag,
                buoyancy = buoyancy,
                density = density,
                color = color,
            };
        }

        static FogMachinePreset Make(string presetName, float intensity, float spawnRate,
            float emitSpeed, float coneAngle, float lifetime, float size,
            float turbulence, float drag, float buoyancy, float density)
        {
            var p = CreateInstance<FogMachinePreset>();
            p.name = presetName;
            p.intensity = intensity;
            p.spawnRate = spawnRate;
            p.emitSpeed = emitSpeed;
            p.coneAngle = coneAngle;
            p.lifetime = lifetime;
            p.size = size;
            p.turbulence = turbulence;
            p.drag = drag;
            p.buoyancy = buoyancy;
            p.density = density;
            return p;
        }

        /// <summary>
        /// The default five-mode chain, ordered creep → jet. Used automatically
        /// by FogMachineController when no preset assets are assigned.
        /// Spawn rate × lifetime stays under the recommended graph capacity (640).
        /// </summary>
        public static FogMachinePreset[] CreateBuiltInChain()
        {
            return new[]
            {
                //    name            int.  rate  speed  cone  life  size  turb  drag  buoy   dens
                Make("Turtle Creep",  0.0f,  14f, 0.35f,  60f,  14f, 4.0f, 0.25f, 4.0f, -0.70f, 0.75f),
                Make("Lazy Drift",    0.25f, 30f, 1.2f,   45f,   9f, 3.2f, 0.6f,  3.0f, -0.50f, 0.80f),
                Make("Steady Fill",   0.5f,  60f, 3.5f,   30f,   6f, 2.5f, 1.2f,  2.5f, -0.35f, 0.90f),
                Make("Heavy Pour",    0.75f, 120f, 8f,    18f,   4f, 1.8f, 2.0f,  2.0f, -0.20f, 1.00f),
                Make("Jet Blast",     1.0f,  220f, 22f,    8f, 2.2f, 1.2f, 3.5f,  1.6f,  0.40f, 1.00f),
            };
        }
    }

    /// <summary>
    /// Flat, lerpable snapshot of every runtime-drivable fog parameter.
    /// This is what the controller actually pushes to the VFX Graph.
    /// </summary>
    [System.Serializable]
    public struct FogParams
    {
        public float spawnRate;
        public float emitSpeed;
        public float coneAngle;
        public float lifetime;
        public float size;
        public float turbulence;
        public float drag;
        public float buoyancy;
        public float density;
        public Color color;

        public static FogParams Lerp(in FogParams a, in FogParams b, float t)
        {
            return new FogParams
            {
                spawnRate = Mathf.Lerp(a.spawnRate, b.spawnRate, t),
                emitSpeed = Mathf.Lerp(a.emitSpeed, b.emitSpeed, t),
                coneAngle = Mathf.Lerp(a.coneAngle, b.coneAngle, t),
                lifetime = Mathf.Lerp(a.lifetime, b.lifetime, t),
                size = Mathf.Lerp(a.size, b.size, t),
                turbulence = Mathf.Lerp(a.turbulence, b.turbulence, t),
                drag = Mathf.Lerp(a.drag, b.drag, t),
                buoyancy = Mathf.Lerp(a.buoyancy, b.buoyancy, t),
                density = Mathf.Lerp(a.density, b.density, t),
                color = Color.Lerp(a.color, b.color, t),
            };
        }
    }
}
