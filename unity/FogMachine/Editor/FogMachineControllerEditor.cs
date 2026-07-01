using UnityEditor;
using UnityEngine;

namespace FogMachine.EditorTools
{
    /// <summary>
    /// Play-mode control desk for the fog machine: preset buttons, the
    /// turtle-to-jet throttle, and a BLAST button. Edit-mode shows the
    /// normal inspector plus a hint.
    /// </summary>
    [CustomEditor(typeof(FogMachineController))]
    public class FogMachineControllerEditor : Editor
    {
        float blastSeconds = 3f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (FogMachineController)target;

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Control Desk", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play mode to drive presets, throttle, and blast live.",
                    MessageType.Info);
                return;
            }

            // Preset buttons, slowest to fastest.
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < controller.Chain.Count; i++)
            {
                bool active = i == controller.ActivePresetIndex;
                GUI.enabled = !active;
                string label = controller.Chain[i] != null ? controller.Chain[i].name : $"#{i}";
                if (GUILayout.Button(label, EditorStyles.miniButton))
                    controller.ApplyPreset(i);
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();

            // Turtle → Jet throttle.
            EditorGUI.BeginChangeCheck();
            float throttle = EditorGUILayout.Slider(
                new GUIContent("Throttle (turtle → jet)"), controller.GetThrottle(), 0f, 1f);
            if (EditorGUI.EndChangeCheck())
                controller.SetThrottle(throttle);

            // Blast + cut.
            EditorGUILayout.BeginHorizontal();
            blastSeconds = EditorGUILayout.FloatField("Blast seconds", blastSeconds);
            GUI.enabled = !controller.IsBlasting;
            if (GUILayout.Button(controller.IsBlasting ? "BLASTING…" : "BLAST",
                    GUILayout.Width(90f)))
                controller.Blast(blastSeconds);
            GUI.enabled = true;
            if (GUILayout.Button("Cut", GUILayout.Width(50f)))
                controller.Cut();
            EditorGUILayout.EndHorizontal();

            Repaint();
        }
    }
}
