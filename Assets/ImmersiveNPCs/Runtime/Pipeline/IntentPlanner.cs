using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Plans the intent before generating a full response.
    /// Stage 1 of the latency pipeline: tiny prompt, strict JSON output.
    /// </summary>
    public sealed class IntentPlanner
    {
        private readonly AIConversationSettings settings;
        private readonly IAIProvider provider;
        
        /// <summary>
        /// Maximum tokens for the planning call.
        /// </summary>
        public int MaxPlanTokens { get; set; } = 128;
        
        /// <summary>
        /// Timeout for planning in milliseconds.
        /// </summary>
        public int TimeoutMs { get; set; } = 5000;
        
        public IntentPlanner(AIConversationSettings settings, IAIProvider provider)
        {
            this.settings = settings;
            this.provider = provider;
        }
        
        /// <summary>
        /// Plans the intent for a given context.
        /// Returns a fallback plan if planning fails or times out.
        /// </summary>
        public async Task<IntentPlan> PlanAsync(AIContext context, WorldStateSnapshot snapshot, CancellationToken ct)
        {
            if (provider == null)
            {
                return IntentPlan.CreateFallback(context?.lastPlayerChoice, snapshot);
            }
            
            // If scripted beat, skip planning
            if (snapshot != null && (snapshot.isScriptedBeat || snapshot.awaitingScriptedResponse))
            {
                var scripted = new IntentPlan
                {
                    intent = IntentType.ScriptedBeat,
                    requiresScriptCheck = true,
                    confidence = 1.0f
                };
                return scripted;
            }
            
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                
                // Build minimal planning context
                AIContext planContext = BuildPlanContext(context, snapshot);
                
                // Generate plan
                TurnResult result = await provider.GenerateTurnAsync(planContext, linkedCts.Token).ConfigureAwait(false);
                
                if (result == null || string.IsNullOrEmpty(result.npcLine))
                {
                    return IntentPlan.CreateFallback(context?.lastPlayerChoice, snapshot);
                }
                
                // Parse JSON response
                IntentPlan plan = ParsePlan(result.npcLine);
                if (plan == null || !plan.IsValid())
                {
                    return IntentPlan.CreateFallback(context?.lastPlayerChoice, snapshot);
                }
                
                plan.rawJson = result.npcLine;
                return plan;
            }
            catch (OperationCanceledException)
            {
                AILogger.Log("[Planner] Planning timed out, using fallback.");
                return IntentPlan.CreateFallback(context?.lastPlayerChoice, snapshot);
            }
            catch (Exception ex)
            {
                AILogger.Warn("[Planner] Planning failed: " + ex.Message);
                return IntentPlan.CreateFallback(context?.lastPlayerChoice, snapshot);
            }
        }
        
        private AIContext BuildPlanContext(AIContext originalContext, WorldStateSnapshot snapshot)
        {
            var planContext = new AIContext
            {
                npcId = originalContext?.npcId ?? "",
                slots = 0, // Not generating options
                language = originalContext?.language ?? "en",
                lastPlayerChoice = originalContext?.lastPlayerChoice ?? ""
            };
            
            // Build minimal system prompt for planning
            var system = new StringBuilder(256);
            system.AppendLine("You are an intent classifier. Analyze the player's message and output JSON only.");
            system.AppendLine("Output format:");
            system.AppendLine("{");
            system.AppendLine("  \"intent\": \"one of: answer_question, ask_clarifying, give_hint, progress_story, smalltalk, vendor_trade, combat_bark, refuse, greeting, farewell, give_directions, share_lore, express_emotion\",");
            system.AppendLine("  \"confidence\": 0.0-1.0,");
            system.AppendLine("  \"required_facts\": [\"list of facts needed from game state\"],");
            system.AppendLine("  \"suggested_tone\": \"neutral/friendly/hostile/sad/excited\",");
            system.AppendLine("  \"requires_strict_validation\": true/false");
            system.AppendLine("}");
            system.AppendLine("Do not include any text outside the JSON.");
            planContext.systemPrompt = system.ToString();
            
            // Build minimal user prompt
            var user = new StringBuilder(128);
            
            if (snapshot != null)
            {
                if (!string.IsNullOrEmpty(snapshot.currentLocation))
                {
                    user.AppendLine("Location: " + snapshot.currentLocation);
                }
                if (snapshot.inCombat)
                {
                    user.AppendLine("Context: In combat");
                }
                if (snapshot.isVendorMode)
                {
                    user.AppendLine("Context: Vendor shop open");
                }
            }
            
            user.AppendLine("Player said: \"" + (planContext.lastPlayerChoice ?? "(nothing)") + "\"");
            planContext.userPrompt = user.ToString();
            
            return planContext;
        }
        
        private IntentPlan ParsePlan(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            
            // Extract JSON if wrapped in other text
            json = AIOutputValidator.ExtractJsonSubstring(json);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            
            try
            {
                var parsed = JsonUtility.FromJson<IntentPlanJson>(json);
                if (parsed == null)
                {
                    return null;
                }
                
                var plan = new IntentPlan
                {
                    intent = ParseIntentType(parsed.intent),
                    confidence = Mathf.Clamp01(parsed.confidence),
                    suggestedTone = parsed.suggested_tone ?? "neutral",
                    requiresScriptCheck = parsed.requires_script_check,
                    requiresStrictValidation = parsed.requires_strict_validation,
                    estimatedResponseTokens = parsed.estimated_response_tokens > 0 ? parsed.estimated_response_tokens : 128
                };
                
                // Copy required facts
                if (parsed.required_facts != null)
                {
                    foreach (var fact in parsed.required_facts)
                    {
                        if (!string.IsNullOrEmpty(fact))
                        {
                            plan.requiredFacts.Add(fact);
                        }
                    }
                }
                
                // Copy memory write candidates
                if (parsed.memory_write_candidates != null)
                {
                    foreach (var candidate in parsed.memory_write_candidates)
                    {
                        if (candidate != null && !string.IsNullOrEmpty(candidate.summary))
                        {
                            plan.memoryWriteCandidates.Add(new MemoryWriteCandidate
                            {
                                eventType = candidate.event_type,
                                summary = candidate.summary,
                                keyEntity = candidate.key_entity,
                                importance = Mathf.Clamp01(candidate.importance)
                            });
                        }
                    }
                }
                
                return plan;
            }
            catch (Exception ex)
            {
                AILogger.Log("[Planner] JSON parse failed: " + ex.Message);
                return null;
            }
        }
        
        private IntentType ParseIntentType(string intentStr)
        {
            if (string.IsNullOrEmpty(intentStr))
            {
                return IntentType.Unknown;
            }
            
            string normalized = intentStr.ToLowerInvariant().Replace("_", "").Replace("-", "");
            
            switch (normalized)
            {
                case "answerquestion":
                case "answer":
                    return IntentType.AnswerQuestion;
                case "askclarifying":
                case "clarify":
                    return IntentType.AskClarifying;
                case "givehint":
                case "hint":
                    return IntentType.GiveHint;
                case "progressstory":
                case "progress":
                    return IntentType.ProgressStory;
                case "smalltalk":
                case "chat":
                    return IntentType.Smalltalk;
                case "vendortrade":
                case "vendor":
                case "trade":
                    return IntentType.VendorTrade;
                case "combatbark":
                case "combat":
                    return IntentType.CombatBark;
                case "refuse":
                case "reject":
                    return IntentType.Refuse;
                case "greeting":
                case "greet":
                case "hello":
                    return IntentType.Greeting;
                case "farewell":
                case "goodbye":
                case "bye":
                    return IntentType.Farewell;
                case "givedirections":
                case "directions":
                    return IntentType.GiveDirections;
                case "sharelore":
                case "lore":
                    return IntentType.ShareLore;
                case "expressemotion":
                case "emotion":
                    return IntentType.ExpressEmotion;
                case "scriptedbeat":
                case "scripted":
                    return IntentType.ScriptedBeat;
                default:
                    return IntentType.Unknown;
            }
        }
    }
}
