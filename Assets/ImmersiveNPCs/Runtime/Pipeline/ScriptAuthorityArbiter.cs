using System.Collections.Generic;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Arbitrates between scripted dialogue and LLM-generated responses.
    /// Enforces script authority hierarchy: main quest beats > LLM > contradictions reconciled to script.
    /// </summary>
    public sealed class ScriptAuthorityArbiter
    {
        /// <summary>
        /// Decision made by the arbiter.
        /// </summary>
        public enum Decision
        {
            /// <summary>Use the LLM response as-is.</summary>
            UseLlmResponse,
            
            /// <summary>Use a scripted response instead.</summary>
            UseScriptedResponse,
            
            /// <summary>Reconcile LLM toward script (modify response).</summary>
            ReconcileToScript,
            
            /// <summary>Hand off to the script system for next beat.</summary>
            HandoffToScript
        }
        
        /// <summary>
        /// Result of arbitration.
        /// </summary>
        public sealed class ArbitrationResult
        {
            public Decision decision;
            public string modifiedResponse;
            public string reason;
            public string scriptedFallback;
            public bool shouldProgressBeat;
        }
        
        /// <summary>
        /// Registry of scripted responses that must be honored.
        /// </summary>
        private readonly Dictionary<string, string> requiredResponses = new Dictionary<string, string>();
        
        /// <summary>
        /// Tags that indicate a main quest beat.
        /// </summary>
        public HashSet<string> MainQuestTags { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "main_quest", "mainquest", "critical", "required", "scripted"
        };
        
        /// <summary>
        /// Registers a required scripted response for a node/beat.
        /// </summary>
        public void RegisterRequiredResponse(string beatId, string response)
        {
            if (string.IsNullOrEmpty(beatId) || string.IsNullOrEmpty(response))
            {
                return;
            }
            
            requiredResponses[beatId] = response;
        }
        
        /// <summary>
        /// Clears all registered responses.
        /// </summary>
        public void ClearRegistrations()
        {
            requiredResponses.Clear();
        }
        
        /// <summary>
        /// Arbitrates between LLM response and script authority.
        /// </summary>
        public ArbitrationResult Arbitrate(
            string llmResponse,
            WorldStateSnapshot snapshot,
            IntentPlan plan,
            ResponseValidator.ValidationResult validation)
        {
            var result = new ArbitrationResult
            {
                decision = Decision.UseLlmResponse,
                modifiedResponse = llmResponse
            };
            
            if (snapshot == null)
            {
                return result;
            }
            
            // Check if this is a scripted beat
            bool isMainQuestBeat = IsMainQuestBeat(snapshot);
            bool hasRequiredResponse = HasRequiredResponse(snapshot);
            
            // Case 1: Scripted beat with required response - script wins
            if (isMainQuestBeat && hasRequiredResponse)
            {
                string scripted = GetRequiredResponse(snapshot);
                result.decision = Decision.UseScriptedResponse;
                result.modifiedResponse = scripted;
                result.scriptedFallback = scripted;
                result.reason = "Main quest beat requires scripted response.";
                return result;
            }
            
            // Case 2: Awaiting specific script command - handoff
            if (snapshot.awaitingScriptedResponse)
            {
                result.decision = Decision.HandoffToScript;
                result.shouldProgressBeat = true;
                result.reason = "Script system awaiting specific command.";
                
                // If we have a fallback, use it
                if (hasRequiredResponse)
                {
                    result.scriptedFallback = GetRequiredResponse(snapshot);
                }
                return result;
            }
            
            // Case 3: Validation failed and scripted beat - reconcile
            if (validation != null && !validation.isValid && isMainQuestBeat)
            {
                if (hasRequiredResponse)
                {
                    result.decision = Decision.UseScriptedResponse;
                    result.modifiedResponse = GetRequiredResponse(snapshot);
                    result.reason = "LLM response invalid during main quest beat.";
                }
                else
                {
                    result.decision = Decision.ReconcileToScript;
                    result.modifiedResponse = ReconcileResponse(llmResponse, snapshot, validation);
                    result.reason = "Reconciled LLM response toward script constraints.";
                }
                return result;
            }
            
            // Case 4: Validation failed, not main quest - try reconcile once
            if (validation != null && !validation.isValid)
            {
                result.decision = Decision.ReconcileToScript;
                result.modifiedResponse = ReconcileResponse(llmResponse, snapshot, validation);
                result.reason = "Reconciled invalid claims in LLM response.";
                return result;
            }
            
            // Case 5: Side chatter - LLM wins
            result.decision = Decision.UseLlmResponse;
            result.reason = "No script authority constraints.";
            return result;
        }
        
        /// <summary>
        /// Determines if current beat is a main quest beat.
        /// </summary>
        public bool IsMainQuestBeat(WorldStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            
            if (snapshot.isScriptedBeat)
            {
                return true;
            }
            
            // Check Yarn node tags
            foreach (var tag in snapshot.yarnNodeTags)
            {
                if (MainQuestTags.Contains(tag))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if there's a registered required response for current beat.
        /// </summary>
        public bool HasRequiredResponse(WorldStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            
            // Check by quest beat
            if (!string.IsNullOrEmpty(snapshot.currentQuestBeat) &&
                requiredResponses.ContainsKey(snapshot.currentQuestBeat))
            {
                return true;
            }
            
            // Check by Yarn node
            if (!string.IsNullOrEmpty(snapshot.currentYarnNode) &&
                requiredResponses.ContainsKey(snapshot.currentYarnNode))
            {
                return true;
            }
            
            // Check by required next beat
            if (!string.IsNullOrEmpty(snapshot.requiredNextBeat) &&
                requiredResponses.ContainsKey(snapshot.requiredNextBeat))
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets the required response for current beat.
        /// </summary>
        public string GetRequiredResponse(WorldStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }
            
            // Try quest beat first
            if (!string.IsNullOrEmpty(snapshot.currentQuestBeat) &&
                requiredResponses.TryGetValue(snapshot.currentQuestBeat, out string response1))
            {
                return response1;
            }
            
            // Try Yarn node
            if (!string.IsNullOrEmpty(snapshot.currentYarnNode) &&
                requiredResponses.TryGetValue(snapshot.currentYarnNode, out string response2))
            {
                return response2;
            }
            
            // Try required next beat
            if (!string.IsNullOrEmpty(snapshot.requiredNextBeat) &&
                requiredResponses.TryGetValue(snapshot.requiredNextBeat, out string response3))
            {
                return response3;
            }
            
            return null;
        }
        
        private string ReconcileResponse(string llmResponse, WorldStateSnapshot snapshot, ResponseValidator.ValidationResult validation)
        {
            if (string.IsNullOrEmpty(llmResponse))
            {
                return GetSafeResponse(snapshot);
            }
            
            // If we have repair hints, apply them
            if (validation != null && !string.IsNullOrEmpty(validation.repairHint))
            {
                // Simple text substitution for obvious fixes
                string repaired = llmResponse;
                foreach (var violation in validation.violations)
                {
                    if (!string.IsNullOrEmpty(violation.correctValue))
                    {
                        string incorrect = ExtractClaimText(violation.claim);
                        if (!string.IsNullOrEmpty(incorrect))
                        {
                            repaired = repaired.Replace(incorrect, violation.correctValue);
                        }
                    }
                }
                
                if (repaired != llmResponse)
                {
                    return repaired;
                }
            }
            
            // If reconciliation didn't help, return a safe response
            return GetSafeResponse(snapshot);
        }
        
        private string GetSafeResponse(WorldStateSnapshot snapshot)
        {
            // Generic safe responses based on context
            if (snapshot != null)
            {
                if (snapshot.inCombat)
                {
                    return "Focus on the fight!";
                }
                
                if (snapshot.isVendorMode)
                {
                    return "What can I help you with today?";
                }
            }
            
            return "I see. What would you like to do?";
        }
        
        private string ExtractClaimText(string claim)
        {
            int colonIndex = claim.IndexOf(':');
            if (colonIndex >= 0 && colonIndex < claim.Length - 1)
            {
                return claim.Substring(colonIndex + 1).Trim();
            }
            return claim;
        }
    }
}
