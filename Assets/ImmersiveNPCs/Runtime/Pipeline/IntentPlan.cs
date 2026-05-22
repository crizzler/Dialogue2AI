using System;
using System.Collections.Generic;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Intent categories for the planning phase.
    /// </summary>
    public enum IntentType
    {
        /// <summary>Direct answer to a question.</summary>
        AnswerQuestion,
        
        /// <summary>Ask the player for clarification.</summary>
        AskClarifying,
        
        /// <summary>Give a hint about the current objective.</summary>
        GiveHint,
        
        /// <summary>Progress the story forward.</summary>
        ProgressStory,
        
        /// <summary>Casual conversation / small talk.</summary>
        Smalltalk,
        
        /// <summary>Vendor transaction mode.</summary>
        VendorTrade,
        
        /// <summary>Combat-related bark.</summary>
        CombatBark,
        
        /// <summary>Refuse a request.</summary>
        Refuse,
        
        /// <summary>Greet the player.</summary>
        Greeting,
        
        /// <summary>Say goodbye.</summary>
        Farewell,
        
        /// <summary>Provide directions or guidance.</summary>
        GiveDirections,
        
        /// <summary>Share lore or background information.</summary>
        ShareLore,
        
        /// <summary>Express emotion (sympathy, anger, joy).</summary>
        ExpressEmotion,
        
        /// <summary>Scripted response required by dialogue system.</summary>
        ScriptedBeat,
        
        /// <summary>Fallback when intent cannot be determined.</summary>
        Unknown
    }
    
    /// <summary>
    /// Result of the planning phase. Strict JSON schema for reliable parsing.
    /// </summary>
    [Serializable]
    public sealed class IntentPlan
    {
        /// <summary>Primary intent category.</summary>
        public IntentType intent = IntentType.Unknown;
        
        /// <summary>Confidence in the intent classification (0-1).</summary>
        public float confidence = 0.5f;
        
        /// <summary>Facts from the snapshot that should be included in the response.</summary>
        public List<string> requiredFacts = new List<string>();
        
        /// <summary>Candidate memory events to write after this turn.</summary>
        public List<MemoryWriteCandidate> memoryWriteCandidates = new List<MemoryWriteCandidate>();
        
        /// <summary>Suggested emotional tone for the response.</summary>
        public string suggestedTone = "neutral";
        
        /// <summary>If true, script authority should be checked before generation.</summary>
        public bool requiresScriptCheck;
        
        /// <summary>If true, world state validation should be strict.</summary>
        public bool requiresStrictValidation;
        
        /// <summary>Estimated tokens needed for the response.</summary>
        public int estimatedResponseTokens = 128;
        
        /// <summary>Raw JSON from the planner (for debugging).</summary>
        public string rawJson;
        
        /// <summary>True if this plan was created from a fallback heuristic.</summary>
        public bool isFallback;
        
        /// <summary>
        /// Creates a fallback plan when planning fails.
        /// </summary>
        public static IntentPlan CreateFallback(string playerChoice, WorldStateSnapshot snapshot)
        {
            var plan = new IntentPlan
            {
                isFallback = true,
                confidence = 0.3f
            };
            
            // Simple heuristics for fallback
            if (snapshot != null)
            {
                if (snapshot.isScriptedBeat || snapshot.awaitingScriptedResponse)
                {
                    plan.intent = IntentType.ScriptedBeat;
                    plan.requiresScriptCheck = true;
                    plan.confidence = 0.9f;
                    return plan;
                }
                
                if (snapshot.inCombat)
                {
                    plan.intent = IntentType.CombatBark;
                    plan.confidence = 0.8f;
                    return plan;
                }
                
                if (snapshot.isVendorMode)
                {
                    plan.intent = IntentType.VendorTrade;
                    plan.confidence = 0.8f;
                    return plan;
                }
            }
            
            if (string.IsNullOrEmpty(playerChoice))
            {
                plan.intent = IntentType.Greeting;
                return plan;
            }
            
            string lower = playerChoice.ToLowerInvariant();
            
            if (lower.Contains("?") || lower.StartsWith("what") || lower.StartsWith("where") || 
                lower.StartsWith("who") || lower.StartsWith("how") || lower.StartsWith("why"))
            {
                plan.intent = IntentType.AnswerQuestion;
            }
            else if (lower.Contains("bye") || lower.Contains("farewell") || lower.Contains("leave"))
            {
                plan.intent = IntentType.Farewell;
            }
            else if (lower.Contains("hello") || lower.Contains("hi ") || lower.Contains("greetings"))
            {
                plan.intent = IntentType.Greeting;
            }
            else if (lower.Contains("buy") || lower.Contains("sell") || lower.Contains("trade"))
            {
                plan.intent = IntentType.VendorTrade;
            }
            else if (lower.Contains("tell me about") || lower.Contains("what do you know"))
            {
                plan.intent = IntentType.ShareLore;
            }
            else if (lower.Contains("where") || lower.Contains("how do i get"))
            {
                plan.intent = IntentType.GiveDirections;
            }
            else
            {
                plan.intent = IntentType.Smalltalk;
            }
            
            return plan;
        }
        
        /// <summary>
        /// Validates the plan has required fields.
        /// </summary>
        public bool IsValid()
        {
            return intent != IntentType.Unknown || isFallback;
        }
    }
    
    /// <summary>
    /// Candidate memory event to write, pending validation.
    /// </summary>
    [Serializable]
    public sealed class MemoryWriteCandidate
    {
        /// <summary>Type of memory event.</summary>
        public string eventType;
        
        /// <summary>Summary of what to remember.</summary>
        public string summary;
        
        /// <summary>Key entity involved.</summary>
        public string keyEntity;
        
        /// <summary>Importance weight (0-1).</summary>
        public float importance;
    }
    
    /// <summary>
    /// JSON schema for planner output. Used for strict parsing.
    /// </summary>
    [Serializable]
    internal sealed class IntentPlanJson
    {
        public string intent;
        public float confidence;
        public string[] required_facts;
        public MemoryWriteCandidateJson[] memory_write_candidates;
        public string suggested_tone;
        public bool requires_script_check;
        public bool requires_strict_validation;
        public int estimated_response_tokens;
    }
    
    [Serializable]
    internal sealed class MemoryWriteCandidateJson
    {
        public string event_type;
        public string summary;
        public string key_entity;
        public float importance;
    }
}
