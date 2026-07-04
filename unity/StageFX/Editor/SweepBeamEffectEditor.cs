using UnityEditor;
using UnityEngine;

namespace StageFX.EditorTools
{
    /// <summary>
    /// Edit-mode preview desk for SweepBeamEffect: Play/Stop/Reset without
    /// entering Play mode, driven by EditorApplication.update.
    /// </summary>
    [CustomEditor(typeof(SweepBeamEffect))]
    public class SweepBeamEffectEditor : Editor
    {
        double lastTime;
        bool editorPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var fx = (SweepBeamEffect)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            if (Application.isPlaying)
            {
                if (GUILayout.Button(fx.IsPlaying ? "Playing…" : "Play")) fx.Play();
                if (GUILayout.Button("Stop")) fx.Stop();
                if (GUILayout.Button("Reset")) fx.ResetSweep();
            }
            else
            {
                if (GUILayout.Button(editorPlaying ? "■ Stop Preview" : "► Preview Sweep"))
                {
                    editorPlaying = !editorPlaying;
                    if (editorPlaying)
                    {
                        fx.progress = 0f;
                        lastTime = EditorApplication.timeSinceStartup;
                        EditorApplication.update += Tick;
                    }
                    else
                    {
                        EditorApplication.update -= Tick;
                    }
                }
                if (GUILayout.Button("Reset"))
                {
                    editorPlaying = false;
                    EditorApplication.update -= Tick;
                    fx.ResetSweep();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Scrub Progress to pose the sweep by hand. Lens materials need " +
                "Emission enabled once in the material inspector; the effect " +
                "drives the color per-renderer from there.", MessageType.Info);
        }

        void Tick()
        {
            if (target == null) { EditorApplication.update -= Tick; return; }
            var fx = (SweepBeamEffect)target;
            double now = EditorApplication.timeSinceStartup;
            fx.EditorTick((float)(now - lastTime));
            lastTime = now;

            if (fx.loopMode == SweepBeamEffect.LoopMode.Once && fx.progress >= 1f)
            {
                editorPlaying = false;
                EditorApplication.update -= Tick;
            }
            Repaint();
        }

        void OnDisable() => EditorApplication.update -= Tick;
    }
}
