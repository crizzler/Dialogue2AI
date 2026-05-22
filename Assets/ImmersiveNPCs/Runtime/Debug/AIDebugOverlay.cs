using UnityEngine;

namespace ImmersiveNPCs
{
    [AddComponentMenu("Immersive NPCs/Debug Overlay")]
    public class AIDebugOverlay : MonoBehaviour
    {
        [SerializeField] private AIConversationManager manager;
        [SerializeField] private bool showOverlay = true;

        private void Awake()
        {
            if (manager == null)
            {
                manager = FindFirstObjectByType<AIConversationManager>();
            }
        }

        private void OnGUI()
        {
            if (!showOverlay || manager == null || manager.Settings == null)
            {
                return;
            }

            if (!manager.Settings.enableRuntimeOverlay)
            {
                return;
            }

            AIDebugMetrics metrics = manager.Metrics;
            if (metrics == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10, 10, 360, 200), GUI.skin.box);
            GUILayout.Label("Immersive NPCs Debug");
            GUILayout.Label("Cache hits: " + metrics.CacheHits + " | misses: " + metrics.CacheMisses);
            GUILayout.Label("Inflight: " + metrics.InflightRequests);
            GUILayout.Label("Last provider: " + (metrics.lastProvider ?? "-") + " (" + metrics.lastLatencyMs + " ms)");
            GUILayout.Label("From cache: " + metrics.lastFromCache);
            GUILayout.Label("Last key: " + (metrics.lastCacheKey ?? "-"));
            GUILayout.EndArea();
        }
    }
}
