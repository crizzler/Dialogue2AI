using UnityEngine;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Attach to NPC GameObjects in scene.
    /// Links to NpcProfile and provides easy access to runtime state.
    /// </summary>
    public class NpcAgent : MonoBehaviour
    {
        [Header("Profile")]
        [SerializeField] private NpcProfile profile;
        [SerializeField] private string npcIdOverride; // Use if profile is null
        
        [Header("Perception")]
        [SerializeField] private float perceptionRadius = 10f;
        [SerializeField] private LayerMask perceptionLayers = -1;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        
        private NpcStateStore runtimeState;
        
        /// <summary>
        /// Get the NPC ID (from profile or override)
        /// </summary>
        public string NpcId => !string.IsNullOrEmpty(npcIdOverride) ? npcIdOverride : profile?.npcId;
        
        /// <summary>
        /// Get the NPC profile
        /// </summary>
        public NpcProfile Profile => profile;
        
        /// <summary>
        /// Get runtime state (creates if needed)
        /// </summary>
        public NpcStateStore State
        {
            get
            {
                if (runtimeState == null && ImmersiveNpcsRuntime.IsInitialized)
                {
                    runtimeState = ImmersiveNpcsRuntime.NpcStates?.GetOrCreate(NpcId);
                }
                return runtimeState;
            }
        }
        
        /// <summary>
        /// Perception radius for this NPC
        /// </summary>
        public float PerceptionRadius => perceptionRadius;
        
        /// <summary>
        /// Perception layer mask for this NPC
        /// </summary>
        public LayerMask PerceptionLayers => perceptionLayers;
        
        private void OnValidate()
        {
            // Auto-name GameObject in editor
            if (profile != null && string.IsNullOrEmpty(npcIdOverride))
            {
                if (!gameObject.name.StartsWith("NPC_"))
                {
                    gameObject.name = $"NPC_{profile.displayName}";
                }
            }
        }
        
        private void Start()
        {
            // Ensure state is created
            if (ImmersiveNpcsRuntime.IsInitialized)
            {
                runtimeState = ImmersiveNpcsRuntime.NpcStates?.GetOrCreate(NpcId);
            }
        }
        
        /// <summary>
        /// Get effective quality preset (profile override or global)
        /// </summary>
        public QualityPreset GetEffectiveQualityPreset()
        {
            var settings = ImmersiveNpcsRuntime.Settings;
            if (settings == null) return QualityPreset.Balanced;
            
            return profile?.GetEffectivePreset(settings.qualityPreset) ?? settings.qualityPreset;
        }
        
        /// <summary>
        /// Get token budget multiplier for this NPC
        /// </summary>
        public float GetTokenBudgetMultiplier()
        {
            return profile?.tokenBudgetMultiplier ?? 1.0f;
        }
        
        /// <summary>
        /// Convenience: Modify trust
        /// </summary>
        public void ModifyTrust(float delta)
        {
            State?.ModifyTrust(delta);
        }
        
        /// <summary>
        /// Convenience: Set mood
        /// </summary>
        public void SetMood(NpcMood mood)
        {
            if (State != null)
            {
                State.CurrentMood = mood;
            }
        }
        
        /// <summary>
        /// Convenience: Add topic
        /// </summary>
        public void AddTopic(string topic)
        {
            State?.AddTopic(topic);
        }
        
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw perception radius
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, perceptionRadius);
        }
        
        private void OnGUI()
        {
            if (!showDebugInfo || !Application.isPlaying) return;
            
            var state = State;
            if (state == null) return;
            
            // Convert world position to screen position
            Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2f);
            if (screenPos.z < 0) return;
            
            screenPos.y = Screen.height - screenPos.y;
            
            GUILayout.BeginArea(new Rect(screenPos.x - 75, screenPos.y, 150, 100));
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>{NpcId}</b>");
            GUILayout.Label($"Mood: {state.CurrentMood}");
            GUILayout.Label($"Trust: {state.TrustLevel:F0}");
            GUILayout.Label($"Interactions: {state.InteractionCount}");
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
#endif
    }
}
