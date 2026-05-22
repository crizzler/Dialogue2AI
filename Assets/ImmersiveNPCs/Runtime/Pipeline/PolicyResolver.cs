using System;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Maps quality presets to internal budgets, timeouts, and validation strictness.
    /// Hides VRAM complexity from developers.
    /// </summary>
    public sealed class PolicyResolver
    {
        private readonly AIConversationSettings settings;
        
        /// <summary>
        /// Resolved policy values.
        /// </summary>
        public sealed class ResolvedPolicy
        {
            /// <summary>Target context window size in tokens.</summary>
            public int targetContextWindow = 4096;
            
            /// <summary>Tier budgets for context assembly.</summary>
            public ContextTierBudgets tierBudgets;
            
            /// <summary>Summarization aggressiveness (0-1).</summary>
            public float summarizerAggressiveness = 0.5f;
            
            /// <summary>Validation strictness level.</summary>
            public ResponseValidator.StrictnessLevel validationStrictness = ResponseValidator.StrictnessLevel.Moderate;
            
            /// <summary>Maximum retry attempts for planning phase.</summary>
            public int planningRetryLimit = 1;
            
            /// <summary>Maximum retry attempts for repair pass.</summary>
            public int repairRetryLimit = 1;
            
            /// <summary>Timeout for planning phase in milliseconds.</summary>
            public int planningTimeoutMs = 5000;
            
            /// <summary>Timeout for generation phase in milliseconds.</summary>
            public int generationTimeoutMs = 20000;
            
            /// <summary>Whether to run the planning phase.</summary>
            public bool enablePlanning = true;
            
            /// <summary>Whether to enable streaming where supported.</summary>
            public bool enableStreaming = false;
            
            /// <summary>Whether to enable memory writes.</summary>
            public bool enableMemoryWrites = true;
            
            /// <summary>Whether to enable script authority checks.</summary>
            public bool enableScriptAuthority = true;
            
            /// <summary>Minimum importance for memory events to be written.</summary>
            public float memoryWriteThreshold = 0.3f;
        }
        
        public PolicyResolver(AIConversationSettings settings)
        {
            this.settings = settings;
        }
        
        /// <summary>
        /// Resolves policy based on preset and actual context size.
        /// </summary>
        public ResolvedPolicy Resolve(QualityPreset preset, int actualContextSize)
        {
            // Use actual context size if available, otherwise use settings or default
            int contextWindow = actualContextSize > 0 
                ? actualContextSize 
                : (settings != null ? settings.localInProcessContextSize : 4096);
            
            var policy = new ResolvedPolicy
            {
                targetContextWindow = contextWindow
            };
            
            switch (preset)
            {
                case QualityPreset.FastSmall:
                    ConfigureFast(policy, contextWindow);
                    break;
                    
                case QualityPreset.Balanced:
                default:
                    ConfigureBalanced(policy, contextWindow);
                    break;
                    
                case QualityPreset.DeepConversation:
                    ConfigureDeep(policy, contextWindow);
                    break;
                    
                case QualityPreset.CinematicQuality:
                    ConfigureCinematic(policy, contextWindow);
                    break;
            }
            
            // Apply settings overrides
            if (settings != null)
            {
                policy.generationTimeoutMs = settings.requestTimeoutMs;
            }
            
            return policy;
        }
        
        /// <summary>
        /// Resolves policy from current settings.
        /// </summary>
        public ResolvedPolicy ResolveFromSettings(int actualContextSize)
        {
            QualityPreset preset = QualityPreset.Balanced;
            if (settings != null)
            {
                preset = settings.qualityPreset;
            }
            
            return Resolve(preset, actualContextSize);
        }
        
        private void ConfigureFast(ResolvedPolicy policy, int contextWindow)
        {
            policy.tierBudgets = ContextTierBudgets.CreateFast(contextWindow);
            policy.summarizerAggressiveness = 0.8f;
            policy.validationStrictness = ResponseValidator.StrictnessLevel.Lenient;
            policy.planningRetryLimit = 0;
            policy.repairRetryLimit = 0;
            policy.planningTimeoutMs = 2000;
            policy.generationTimeoutMs = 10000;
            policy.enablePlanning = false; // Skip planning for speed
            policy.enableStreaming = false;
            policy.enableMemoryWrites = false; // Skip memory for speed
            policy.enableScriptAuthority = true; // Always check script
            policy.memoryWriteThreshold = 0.8f; // Only high importance
        }
        
        private void ConfigureBalanced(ResolvedPolicy policy, int contextWindow)
        {
            policy.tierBudgets = ContextTierBudgets.CreateDefault(contextWindow);
            policy.summarizerAggressiveness = 0.5f;
            policy.validationStrictness = ResponseValidator.StrictnessLevel.Moderate;
            policy.planningRetryLimit = 1;
            policy.repairRetryLimit = 1;
            policy.planningTimeoutMs = 5000;
            policy.generationTimeoutMs = 15000;
            policy.enablePlanning = true;
            policy.enableStreaming = false;
            policy.enableMemoryWrites = true;
            policy.enableScriptAuthority = true;
            policy.memoryWriteThreshold = 0.5f;
        }
        
        private void ConfigureDeep(ResolvedPolicy policy, int contextWindow)
        {
            policy.tierBudgets = ContextTierBudgets.CreateDeep(contextWindow);
            policy.summarizerAggressiveness = 0.3f;
            policy.validationStrictness = ResponseValidator.StrictnessLevel.Moderate;
            policy.planningRetryLimit = 2;
            policy.repairRetryLimit = 2;
            policy.planningTimeoutMs = 8000;
            policy.generationTimeoutMs = 25000;
            policy.enablePlanning = true;
            policy.enableStreaming = true;
            policy.enableMemoryWrites = true;
            policy.enableScriptAuthority = true;
            policy.memoryWriteThreshold = 0.3f;
        }
        
        private void ConfigureCinematic(ResolvedPolicy policy, int contextWindow)
        {
            policy.tierBudgets = ContextTierBudgets.CreateCinematic(contextWindow);
            policy.summarizerAggressiveness = 0.2f;
            policy.validationStrictness = ResponseValidator.StrictnessLevel.Strict;
            policy.planningRetryLimit = 2;
            policy.repairRetryLimit = 2;
            policy.planningTimeoutMs = 10000;
            policy.generationTimeoutMs = 30000;
            policy.enablePlanning = true;
            policy.enableStreaming = true;
            policy.enableMemoryWrites = true;
            policy.enableScriptAuthority = true;
            policy.memoryWriteThreshold = 0.2f;
        }
        
        /// <summary>
        /// Suggests a quality preset based on model context size.
        /// </summary>
        public static QualityPreset SuggestPreset(int contextSize)
        {
            if (contextSize <= 1024)
            {
                return QualityPreset.FastSmall;
            }
            
            if (contextSize <= 2048)
            {
                return QualityPreset.Balanced;
            }
            
            if (contextSize <= 4096)
            {
                return QualityPreset.DeepConversation;
            }
            
            return QualityPreset.CinematicQuality;
        }
    }
}
