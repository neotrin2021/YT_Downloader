using UnityEngine;

namespace FogMachine
{
    /// <summary>
    /// Watchdog that keeps the fog from choking the GPU. It tracks a smoothed
    /// frame time against a target and exposes a QualityScale (0.25..1) that
    /// FogMachineController multiplies into spawn rate (with a small size
    /// compensation so the fog still looks full).
    ///
    /// Deliberately slow and hysteretic: it steps down quickly when frames
    /// blow past budget and creeps back up only when there is clear headroom,
    /// so it never oscillates visibly.
    /// </summary>
    [AddComponentMenu("Effects/Fog Performance Governor")]
    public class FogPerformanceGovernor : MonoBehaviour
    {
        [Tooltip("Target frame rate. 0 = use Application.targetFrameRate, " +
                 "falling back to 60 when that is unset (-1).")]
        [SerializeField, Min(0)] int targetFrameRate = 0;

        [Tooltip("Frame-time smoothing half-life in seconds. Bigger = calmer.")]
        [SerializeField, Range(0.05f, 2f)] float smoothing = 0.5f;

        [Tooltip("Seconds between quality adjustments.")]
        [SerializeField, Range(0.1f, 5f)] float adjustInterval = 0.75f;

        [Tooltip("Never scale emission below this fraction.")]
        [SerializeField, Range(0.05f, 1f)] float minQuality = 0.25f;

        float emaFrameTime = 1f / 60f;
        float nextAdjustAt;
        float quality = 1f;

        /// <summary>Current emission scale, 0.25..1. Read by FogMachineController.</summary>
        public float QualityScale => quality;

        /// <summary>Smoothed frame time in milliseconds, for debugging/HUDs.</summary>
        public float SmoothedFrameMs => emaFrameTime * 1000f;

        float TargetFrameTime
        {
            get
            {
                int fps = targetFrameRate > 0 ? targetFrameRate
                    : (Application.targetFrameRate > 0 ? Application.targetFrameRate : 60);
                return 1f / fps;
            }
        }

        void OnEnable()
        {
            emaFrameTime = TargetFrameTime;
            nextAdjustAt = Time.unscaledTime + adjustInterval;
        }

        void Update()
        {
            // Exponential moving average with a time-based half-life so the
            // smoothing feels the same at any frame rate.
            float dt = Time.unscaledDeltaTime;
            float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, smoothing));
            emaFrameTime = Mathf.Lerp(emaFrameTime, dt, alpha);

            if (Time.unscaledTime < nextAdjustAt) return;
            nextAdjustAt = Time.unscaledTime + adjustInterval;

            float target = TargetFrameTime;
            if (emaFrameTime > target * 1.15f)
            {
                // Over budget by 15%+: step down decisively.
                quality = Mathf.Max(minQuality, quality - 0.08f);
            }
            else if (emaFrameTime < target * 0.90f && quality < 1f)
            {
                // Comfortable headroom: creep back up gently.
                quality = Mathf.Min(1f, quality + 0.02f);
            }
        }
    }
}
