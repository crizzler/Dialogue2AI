#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ImmersiveNPCs.Editor
{
    public sealed class InProcessLlmConsoleWindow : EditorWindow
    {
        private const int MaxBufferChars = 20000;
        private static readonly GUIContent EnableLabel = new GUIContent("Enable Native Logging", "Requires rebuilt in-process plugin.");

        private AIConversationSettings settings;
        private readonly StringBuilder buffer = new StringBuilder();
        private Vector2 scroll;
        private bool autoScroll = true;
        private bool pause;

        [MenuItem("Tools/Immersive NPCs/LLM Console")]
        public static void Open()
        {
            InProcessLlmConsoleWindow window = GetWindow<InProcessLlmConsoleWindow>("LLM Console");
            window.minSize = new Vector2(520, 360);
        }

        private void OnEnable()
        {
            settings = AISettingsAssetUtility.FindAnySettings();
            EditorApplication.update += UpdateLog;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateLog;
        }

        private void UpdateLog()
        {
            if (pause)
            {
                return;
            }

            string log = InProcessNativeLog.ReadAndClear();
            if (string.IsNullOrEmpty(log))
            {
                return;
            }

            buffer.Append(log);
            if (buffer.Length > MaxBufferChars)
            {
                buffer.Remove(0, buffer.Length - MaxBufferChars);
            }

            Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawControls();
            DrawLogArea();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("In-Process LLM Console", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Displays native llama/ggml logs. Requires rebuilt plugin binaries.", MessageType.Info);
        }

        private void DrawControls()
        {
            EditorGUILayout.BeginHorizontal();
            if (settings == null)
            {
                EditorGUILayout.HelpBox("No settings asset found. Create one in Project Settings -> Immersive NPCs.", MessageType.Warning);
                if (GUILayout.Button("Create Settings Asset", GUILayout.Width(160)))
                {
                    settings = AISettingsAssetUtility.CreateSettingsAsset();
                }
                EditorGUILayout.EndHorizontal();
                return;
            }

            bool enabled = settings.enableInProcessLogging;
            bool newEnabled = EditorGUILayout.ToggleLeft(EnableLabel, enabled, GUILayout.Width(200));
            if (newEnabled != enabled)
            {
                settings.enableInProcessLogging = newEnabled;
                EditorUtility.SetDirty(settings);
                InProcessNativeLog.SetLoggingEnabled(newEnabled);
            }

            pause = GUILayout.Toggle(pause, "Pause", GUILayout.Width(70));
            autoScroll = GUILayout.Toggle(autoScroll, "Auto-Scroll", GUILayout.Width(110));

            if (GUILayout.Button("Clear", GUILayout.Width(70)))
            {
                buffer.Clear();
            }

            if (GUILayout.Button("Copy", GUILayout.Width(70)))
            {
                EditorGUIUtility.systemCopyBuffer = buffer.ToString();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLogArea()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                EditorGUILayout.TextArea(buffer.Length == 0 ? "No native logs yet." : buffer.ToString(), GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            if (autoScroll)
            {
                scroll.y = float.MaxValue;
            }
        }
    }
}
#endif
