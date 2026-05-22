using System;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Token budgets for each context tier. Used by ContextAssembler.
    /// </summary>
    public sealed class ContextTierBudgets
    {
        /// <summary>
        /// Tier A: Current scene facts (quest state, location, participants, emotional tone).
        /// Always included. Relatively small.
        /// </summary>
        public int tierASceneFacts = 256;
        
        /// <summary>
        /// Tier B: NPC identity card (personality, role, taboo list, speaking style).
        /// Always included. Medium size.
        /// </summary>
        public int tierBIdentity = 512;
        
        /// <summary>
        /// Tier C: Compressed episodic memory (summaries of past interactions).
        /// Optional. Can be large for deep conversation preset.
        /// </summary>
        public int tierCMemory = 384;
        
        /// <summary>
        /// Tier D: Retrieval snippets (RAG results, only when relevant).
        /// Optional. Variable size.
        /// </summary>
        public int tierDRetrieval = 256;
        
        /// <summary>
        /// Reserved for player's last message and recent turns.
        /// </summary>
        public int recentConversation = 512;
        
        /// <summary>
        /// Reserved for model's response.
        /// </summary>
        public int responseReserve = 256;
        
        /// <summary>
        /// Safety margin for tokenization variance.
        /// </summary>
        public int safetyMargin = 64;
        
        /// <summary>
        /// Total context window target. Set by PolicyResolver based on actual model context.
        /// </summary>
        public int targetContextWindow = 4096;
        
        /// <summary>
        /// Returns the total budget for all tiers (excluding response reserve and safety margin).
        /// </summary>
        public int TotalPromptBudget =>
            Math.Max(256, targetContextWindow - responseReserve - safetyMargin);
        
        /// <summary>
        /// Returns available budget after allocating mandatory tiers A and B.
        /// </summary>
        public int AvailableForOptionalTiers =>
            Math.Max(0, TotalPromptBudget - tierASceneFacts - tierBIdentity - recentConversation);
        
        /// <summary>
        /// Creates default budgets for a given context window size.
        /// </summary>
        public static ContextTierBudgets CreateDefault(int contextWindow)
        {
            // Scale budgets proportionally to context window
            float scale = contextWindow / 4096f;
            
            return new ContextTierBudgets
            {
                targetContextWindow = contextWindow,
                tierASceneFacts = (int)(256 * scale),
                tierBIdentity = (int)(512 * scale),
                tierCMemory = (int)(384 * scale),
                tierDRetrieval = (int)(256 * scale),
                recentConversation = (int)(512 * scale),
                responseReserve = Math.Max(128, (int)(256 * scale)),
                safetyMargin = 64
            };
        }
        
        /// <summary>
        /// Creates minimal budgets for FastSmall preset.
        /// </summary>
        public static ContextTierBudgets CreateFast(int contextWindow)
        {
            return new ContextTierBudgets
            {
                targetContextWindow = contextWindow,
                tierASceneFacts = 128,
                tierBIdentity = 256,
                tierCMemory = 0,       // Skip memory for speed
                tierDRetrieval = 0,    // Skip retrieval for speed
                recentConversation = 256,
                responseReserve = 128,
                safetyMargin = 32
            };
        }
        
        /// <summary>
        /// Creates generous budgets for DeepConversation preset.
        /// </summary>
        public static ContextTierBudgets CreateDeep(int contextWindow)
        {
            float scale = contextWindow / 4096f;
            
            return new ContextTierBudgets
            {
                targetContextWindow = contextWindow,
                tierASceneFacts = (int)(384 * scale),
                tierBIdentity = (int)(640 * scale),
                tierCMemory = (int)(768 * scale),
                tierDRetrieval = (int)(512 * scale),
                recentConversation = (int)(768 * scale),
                responseReserve = (int)(384 * scale),
                safetyMargin = 64
            };
        }
        
        /// <summary>
        /// Creates maximum budgets for CinematicQuality preset.
        /// </summary>
        public static ContextTierBudgets CreateCinematic(int contextWindow)
        {
            float scale = contextWindow / 4096f;
            
            return new ContextTierBudgets
            {
                targetContextWindow = contextWindow,
                tierASceneFacts = (int)(512 * scale),
                tierBIdentity = (int)(768 * scale),
                tierCMemory = (int)(1024 * scale),
                tierDRetrieval = (int)(768 * scale),
                recentConversation = (int)(1024 * scale),
                responseReserve = (int)(512 * scale),
                safetyMargin = 64
            };
        }
    }
}
