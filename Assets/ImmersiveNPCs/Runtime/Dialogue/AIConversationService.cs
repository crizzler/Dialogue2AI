using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public sealed class AIConversationService
    {
        private readonly AIConversationSettings settings;
        private readonly NpcProfileDatabase profileDatabase;
        private readonly IAIProvider provider;
        private readonly AICache cache;
        private readonly AIMemoryService memoryService;
        private readonly AISessionMemory sessionMemory;
        private readonly Dictionary<string, AIConversationState> states = new Dictionary<string, AIConversationState>();
        private readonly Dictionary<string, Task<TurnResult>> inflightByKey = new Dictionary<string, Task<TurnResult>>();
        private readonly object sync = new object();
        private readonly SemaphoreSlim concurrency;
        private readonly AIDebugMetrics metrics = new AIDebugMetrics();
        
        // === Tiered Context Pipeline Components ===
        private readonly MemoryStore structuredMemory;
        private readonly MemoryWritePolicy memoryWritePolicy;
        private readonly MemorySummarizer memorySummarizer;
        private readonly PolicyResolver policyResolver;
        private readonly IntentPlanner intentPlanner;
        private readonly ResponseValidator responseValidator;
        private readonly ScriptAuthorityArbiter scriptArbiter;
        private readonly ContextAssembler contextAssembler;
        
        /// <summary>
        /// Optional: Set this to provide world state snapshots for validation.
        /// Can be populated by DialogueCommandBridge or Yarn adapter.
        /// </summary>
        public SnapshotBuilder CurrentSnapshotBuilder { get; set; }

        public AIConversationService(AIConversationSettings settings, NpcProfileDatabase profileDatabase, ILocalInferenceEngine localEngine)
        {
            this.settings = settings;
            this.profileDatabase = profileDatabase;
            cache = new AICache(settings != null ? settings.memoryCacheEntries : 128);
            if (settings != null && settings.diskCacheEnabled)
            {
                string diskPath = PathUtility.ResolveProjectPath(settings.diskCachePath);
                cache.ConfigureDisk(diskPath, TimeSpan.FromMinutes(settings.diskCacheTtlMinutes));
            }
            provider = ProviderFactory.CreateProvider(settings, localEngine);
            IEmbeddingProvider embeddingProvider = EmbeddingProviderFactory.Create(settings, localEngine);
            if (embeddingProvider != null)
            {
                memoryService = new AIMemoryService(settings, embeddingProvider);
            }
            sessionMemory = new AISessionMemory();
            concurrency = new SemaphoreSlim(settings != null ? settings.prefetchMaxConcurrent : 2);
            
            // Initialize tiered context pipeline components
            if (settings != null && settings.enableTieredContext)
            {
                structuredMemory = new MemoryStore();
                memoryWritePolicy = new MemoryWritePolicy();
                memorySummarizer = new MemorySummarizer();
                policyResolver = new PolicyResolver(settings);
                intentPlanner = new IntentPlanner(settings, provider);
                responseValidator = new ResponseValidator
                {
                    Strictness = settings.validationStrictness
                };
                scriptArbiter = new ScriptAuthorityArbiter();
                contextAssembler = new ContextAssembler(ContextTierBudgets.CreateDefault(4096));
                
                AILogger.Log("[TieredContext] Pipeline initialized with preset: " + settings.qualityPreset);
            }
        }

        public AIDebugMetrics Metrics => metrics;
        public AISessionMemory SessionMemory => sessionMemory;
        
        // === Tiered Context Pipeline Accessors ===
        public MemoryStore StructuredMemory => structuredMemory;
        public ScriptAuthorityArbiter ScriptArbiter => scriptArbiter;

        public AIConversationState GetState(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                npcId = "default";
            }

            lock (sync)
            {
                if (!states.TryGetValue(npcId, out AIConversationState state))
                {
                    state = new AIConversationState();
                    states[npcId] = state;
                }
                return state;
            }
        }

        public async Task<TurnResult> GetOrGenerateAsync(string npcId, int slots, string language, PerceptionSnapshot perception, CancellationToken ct)
        {
            AIConversationState state = GetState(npcId);
            state.SummarizeIfNeeded(settings);

            NpcProfile profile = profileDatabase != null ? profileDatabase.FindProfile(npcId) : null;
            AIContext context = AIContextBuilder.BuildContext(settings, npcId, slots, language, state, profile, perception);
            string memoryKey = GetMemoryKey(npcId);
            context.memoryKey = memoryKey;
            context.entityContext = GetEntityContext(npcId);

            string cacheKey = AICacheKey.BuildKey(npcId, context.summary, context.lastPlayerChoice, perception, slots, language, memoryKey);
            metrics.lastCacheKey = cacheKey;

            TurnResult cached = await cache.TryGetAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached != null)
            {
                cached.metadata.fromCache = true;
                metrics.IncrementHits();
                metrics.lastFromCache = true;
                return cached;
            }

            metrics.IncrementMisses();

            if (memoryService != null)
            {
                await ApplyMemoryAsync(context, ct).ConfigureAwait(false);
            }

            Task<TurnResult> inFlight;
            bool isOwner = false;
            lock (sync)
            {
                if (!inflightByKey.TryGetValue(cacheKey, out inFlight))
                {
                    inFlight = GenerateInternalAsync(context, cacheKey, ct);
                    inflightByKey[cacheKey] = inFlight;
                    isOwner = true;
                }
            }

            if (!isOwner)
            {
                return await inFlight.ConfigureAwait(false);
            }

            try
            {
                return await inFlight.ConfigureAwait(false);
            }
            finally
            {
                lock (sync)
                {
                    inflightByKey.Remove(cacheKey);
                }
            }
        }

        public async Task<TurnResult> PrefetchWithContextAsync(AIContext context, string cacheKey, CancellationToken ct)
        {
            TurnResult cached = await cache.TryGetAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached != null)
            {
                return cached;
            }

            Task<TurnResult> inFlight;
            bool isOwner = false;
            lock (sync)
            {
                if (!inflightByKey.TryGetValue(cacheKey, out inFlight))
                {
                    inFlight = GenerateInternalAsync(context, cacheKey, ct);
                    inflightByKey[cacheKey] = inFlight;
                    isOwner = true;
                }
            }

            if (!isOwner)
            {
                return await inFlight.ConfigureAwait(false);
            }

            try
            {
                return await inFlight.ConfigureAwait(false);
            }
            finally
            {
                lock (sync)
                {
                    inflightByKey.Remove(cacheKey);
                }
            }
        }

        public Task RecordChoiceMemoryAsync(string npcId, string playerChoice, string npcLine, CancellationToken ct)
        {
            if (memoryService == null)
            {
                return Task.CompletedTask;
            }

            return memoryService.AddChoiceAsync(npcId, playerChoice, npcLine, ct);
        }

        /// <summary>
        /// Record a completed turn for session tracking.
        /// Call this after the player makes a choice.
        /// </summary>
        public void RecordTurnForSession(string npcId, string npcLine, string playerChoice, List<string> offeredOptions)
        {
            sessionMemory?.RecordTurn(npcId, npcLine, playerChoice, offeredOptions);
        }

        /// <summary>
        /// Get entity context string for prompt injection.
        /// </summary>
        public string GetEntityContext(string npcId)
        {
            return sessionMemory?.BuildEntityContext(npcId) ?? string.Empty;
        }

        public string GetMemoryKey(string npcId)
        {
            if (memoryService == null)
            {
                return string.Empty;
            }

            return memoryService.GetMemoryKey(npcId);
        }

        public async Task<AIContext> BuildContextAsync(string npcId, int slots, string language, AIConversationState state, NpcProfile profile, PerceptionSnapshot perception, CancellationToken ct)
        {
            AIContext context = AIContextBuilder.BuildContext(settings, npcId, slots, language, state, profile, perception);
            context.memoryKey = GetMemoryKey(npcId);
            context.entityContext = GetEntityContext(npcId);

            if (memoryService != null)
            {
                await ApplyMemoryAsync(context, ct).ConfigureAwait(false);
            }

            return context;
        }

        private async Task ApplyMemoryAsync(AIContext context, CancellationToken ct)
        {
            List<AIMemorySnippet> snippets = await memoryService.QueryAsync(context, ct).ConfigureAwait(false);
            if (snippets != null && snippets.Count > 0)
            {
                context.memorySnippets.AddRange(snippets);
                context.userPrompt = AIContextBuilder.BuildUserPrompt(context, settings);
            }
        }

        private async Task<TurnResult> GenerateInternalAsync(AIContext context, string cacheKey, CancellationToken ct)
        {
            // === Route to tiered context pipeline if enabled ===
            if (settings != null && settings.enableTieredContext && structuredMemory != null)
            {
                return await GenerateTieredAsync(context, cacheKey, ct).ConfigureAwait(false);
            }
            
            metrics.IncrementInflight();
            await concurrency.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                TurnResult result;
                if (provider == null)
                {
                    result = AIOutputValidator.CreateFallback(context.slots, settings);
                }
                else
                {
                    try
                    {
                        result = await provider.GenerateTurnAsync(context, ct).ConfigureAwait(false);
                        result = AIOutputValidator.Sanitize(result, context.slots, settings);
                        
                        AILogger.Log($"[Validation] Checking response: \"{result.npcLine}\" (isFallback={result.isFallback})");
                        
                        // Retry once if we got a confused response
                        if (AIOutputValidator.IsConfusedResponse(result.npcLine))
                        {
                            AILogger.Warn("Detected confused response: \"" + result.npcLine + "\", retrying...");
                            result.isFallback = true;
                            result = await provider.GenerateTurnAsync(context, ct).ConfigureAwait(false);
                            result = AIOutputValidator.Sanitize(result, context.slots, settings);
                        }
                        
                        // Detect incoherent response (hallucination unrelated to player choice)
                        if (!result.isFallback && AIOutputValidator.IsIncoherentResponse(result.npcLine, context.lastPlayerChoice))
                        {
                            AILogger.Warn("Detected incoherent response (hallucination), retrying...");
                            context = AddCoherenceHint(context);
                            result = await provider.GenerateTurnAsync(context, ct).ConfigureAwait(false);
                            result = AIOutputValidator.Sanitize(result, context.slots, settings);
                        }
                        
                        // Detect repetition loop and retry with progression hint
                        if (!result.isFallback && AIOutputValidator.IsRepeatedResponse(result.npcLine, context.recentTurns))
                        {
                            AILogger.Warn("Detected repeated response, retrying with progression hint...");
                            context = AddProgressionHint(context);
                            result = await provider.GenerateTurnAsync(context, ct).ConfigureAwait(false);
                            result = AIOutputValidator.Sanitize(result, context.slots, settings);
                            
                            // If STILL repeating after retry, force progression
                            if (AIOutputValidator.IsRepeatedResponse(result.npcLine, context.recentTurns))
                            {
                                AILogger.Warn("Model still repeating - forcing story progression!");
                                result = ForceStoryProgression(result, context);
                            }
                        }
                        
                        // Apply coherence validation
                        if (settings != null && settings.enableCoherenceValidation && !result.isFallback)
                        {
                            result = ApplyCoherenceValidation(result, context);
                        }
                    }
                    catch (Exception ex)
                    {
                        AILogger.Warn("Provider failed, using fallback: " + ex.Message);
                        result = AIOutputValidator.CreateFallback(context.slots, settings);
                    }
                }

                metrics.lastProvider = result.metadata.providerName;
                metrics.lastLatencyMs = result.metadata.latencyMs;
                metrics.lastFromCache = false;
                metrics.lastUpdatedUtc = DateTime.UtcNow;

                if (!result.isFallback)
                {
                    await cache.StoreAsync(cacheKey, result, ct).ConfigureAwait(false);
                }
                return result;
            }
            finally
            {
                concurrency.Release();
                metrics.DecrementInflight();
            }
        }

        private TurnResult ApplyCoherenceValidation(TurnResult result, AIContext context)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.npcLine) || result.options == null)
            {
                return result;
            }

            CoherenceValidationResult validation = AIOptionCoherenceValidator.ValidateWithContext(
                result.npcLine,
                result.options,
                context.slots,
                sessionMemory,
                context.npcId
            );

            if (!validation.isCoherent && validation.correctedOptions != null)
            {
                // Log the correction for debugging
                if (validation.issues.Count > 0)
                {
                    AILogger.Log("Coherence validation corrected options: " + string.Join("; ", validation.issues));
                }

                result.options = validation.correctedOptions;
            }

            return result;
        }

        // ==========================================
        // === TIERED CONTEXT PIPELINE (New) ===
        // ==========================================
        
        /// <summary>
        /// Generates a response using the tiered context pipeline.
        /// This is the new architecture with:
        /// - Tier A: Scene facts (current location, time, weather)
        /// - Tier B: NPC identity (persona, relationships)
        /// - Tier C: Episodic memory (commit-worthy events)
        /// - Tier D: Retrieval (RAG snippets)
        /// </summary>
        private async Task<TurnResult> GenerateTieredAsync(AIContext context, string cacheKey, CancellationToken ct)
        {
            var latencyTracker = new PipelineLatencyTracker(settings.enableTimingLogs);
            latencyTracker.Begin(PipelineStages.Total);
            
            metrics.IncrementInflight();
            await concurrency.WaitAsync(ct).ConfigureAwait(false);
            
            try
            {
                // Resolve policy from quality preset
                int actualContextSize = settings?.localInProcessContextSize ?? 4096;
                var policy = policyResolver.ResolveFromSettings(actualContextSize);
                
                // Build world state snapshot
                latencyTracker.Begin(PipelineStages.SnapshotBuild);
                WorldStateSnapshot snapshot = BuildCurrentSnapshot(context);
                latencyTracker.End(PipelineStages.SnapshotBuild);
                
                // Check for script authority override first
                if (settings.enableScriptAuthority && scriptArbiter != null)
                {
                    latencyTracker.Begin(PipelineStages.ScriptArbitration);
                    if (snapshot.isScriptedBeat || snapshot.awaitingScriptedResponse)
                    {
                        var arbiterResult = scriptArbiter.Arbitrate(null, snapshot, null, null);
                        if (arbiterResult.decision == ScriptAuthorityArbiter.Decision.UseScriptedResponse)
                        {
                            latencyTracker.End(PipelineStages.ScriptArbitration);
                            
                            var scriptedResult = CreateScriptedResult(arbiterResult.modifiedResponse, context);
                            LogPipelineTiming(latencyTracker, "Scripted");
                            return scriptedResult;
                        }
                    }
                    latencyTracker.End(PipelineStages.ScriptArbitration);
                }
                
                // Assemble tiered context
                latencyTracker.Begin(PipelineStages.ContextAssemble);
                var tierBudgets = policy.tierBudgets ?? ContextTierBudgets.CreateDefault(policy.targetContextWindow);
                
                NpcProfile profile = profileDatabase?.FindProfile(context.npcId);
                var memoryEvents = structuredMemory?.GetRecentEvents(context.npcId, 20) ?? new List<MemoryEvent>();
                
                AIContext tieredContext = contextAssembler.Assemble(
                    context.npcId,
                    context.slots,
                    context.language,
                    snapshot,
                    profile,
                    memoryEvents,
                    context.memorySnippets,
                    context.recentTurns,
                    context.lastPlayerChoice,
                    settings
                );
                latencyTracker.End(PipelineStages.ContextAssemble);
                
                // Optional: Intent planning phase (Stage 1)
                IntentPlan plan = null;
                if (policy.enablePlanning && intentPlanner != null)
                {
                    latencyTracker.Begin(PipelineStages.IntentPlanning);
                    try
                    {
                        plan = await intentPlanner.PlanAsync(tieredContext, snapshot, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        AILogger.Warn("[TieredContext] Planning failed, using fallback: " + ex.Message);
                        plan = IntentPlan.CreateFallback(context.lastPlayerChoice, snapshot);
                    }
                    latencyTracker.End(PipelineStages.IntentPlanning);
                    
                    // Inject plan hints into context
                    if (plan != null && !plan.isFallback)
                    {
                        tieredContext = InjectPlanHints(tieredContext, plan);
                    }
                }
                
                // Generate response (Stage 2)
                latencyTracker.Begin(PipelineStages.ResponseGeneration);
                TurnResult result;
                
                if (provider == null)
                {
                    result = AIOutputValidator.CreateFallback(context.slots, settings);
                }
                else
                {
                    try
                    {
                        // Try streaming if available and enabled
                        if (policy.enableStreaming && provider is IStreamingInferenceProvider streamingProvider)
                        {
                            result = await GenerateWithStreamingAsync(streamingProvider, tieredContext, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            result = await provider.GenerateTurnAsync(tieredContext, ct).ConfigureAwait(false);
                        }
                        
                        result = AIOutputValidator.Sanitize(result, context.slots, settings);
                    }
                    catch (Exception ex)
                    {
                        AILogger.Warn("[TieredContext] Generation failed: " + ex.Message);
                        result = AIOutputValidator.CreateFallback(context.slots, settings);
                    }
                }
                latencyTracker.End(PipelineStages.ResponseGeneration);
                
                // Validate response against world state
                if (settings.enableWorldStateValidation && !result.isFallback && responseValidator != null)
                {
                    latencyTracker.Begin(PipelineStages.Validation);
                    var validationResult = responseValidator.Validate(result.npcLine, snapshot, plan);
                    
                    if (!validationResult.isValid)
                    {
                        if (settings.enableValidationLogs)
                        {
                            AILogger.Warn("[Validation] Response violated world state: " + 
                                string.Join(", ", validationResult.violations));
                        }
                        
                        // Attempt repair
                        string repaired = responseValidator.AttemptRepair(result.npcLine, validationResult, snapshot);
                        if (!string.IsNullOrEmpty(repaired))
                        {
                            result.npcLine = repaired;
                        }
                    }
                    latencyTracker.End(PipelineStages.Validation);
                }
                
                // Script authority arbitration on final response
                if (settings.enableScriptAuthority && scriptArbiter != null)
                {
                    var arbiterResult = scriptArbiter.Arbitrate(result.npcLine, snapshot, plan, null);
                    if (arbiterResult.decision == ScriptAuthorityArbiter.Decision.ReconcileToScript)
                    {
                        result.npcLine = arbiterResult.modifiedResponse;
                    }
                }
                
                // Apply standard validations (repetition, coherence)
                if (!result.isFallback)
                {
                    result = ApplyStandardValidations(result, context, tieredContext);
                }
                
                // Memory write phase
                if (policy.enableMemoryWrites && structuredMemory != null && memoryWritePolicy != null && !result.isFallback)
                {
                    latencyTracker.Begin(PipelineStages.MemoryWrite);
                    WriteStructuredMemory(context.npcId, context.lastPlayerChoice, result.npcLine, result.memoryDelta);
                    latencyTracker.End(PipelineStages.MemoryWrite);
                }
                
                // Finalize metrics
                latencyTracker.End(PipelineStages.Total);
                
                metrics.lastProvider = result.metadata.providerName ?? "Unknown";
                metrics.lastLatencyMs = result.metadata.latencyMs;
                metrics.lastFromCache = false;
                metrics.lastUpdatedUtc = DateTime.UtcNow;
                
                LogPipelineTiming(latencyTracker, result.metadata.providerName);
                
                // Cache if valid
                if (!result.isFallback)
                {
                    await cache.StoreAsync(cacheKey, result, ct).ConfigureAwait(false);
                }
                
                return result;
            }
            finally
            {
                concurrency.Release();
                metrics.DecrementInflight();
            }
        }
        
        private WorldStateSnapshot BuildCurrentSnapshot(AIContext context)
        {
            if (CurrentSnapshotBuilder != null)
            {
                return CurrentSnapshotBuilder.Build();
            }
            
            // Build minimal snapshot from context
            var builder = new SnapshotBuilder().Begin();
            
            // Add any valid NPCs from profile database (use displayName for prompts)
            if (profileDatabase != null && profileDatabase.profiles != null)
            {
                foreach (var profile in profileDatabase.profiles)
                {
                    if (profile != null)
                    {
                        string name = !string.IsNullOrEmpty(profile.displayName) ? profile.displayName : profile.npcId;
                        builder.AddKnownNpc(name);
                    }
                }
            }
            
            return builder.Build();
        }
        
        private TurnResult CreateScriptedResult(string scriptedLine, AIContext context)
        {
            return new TurnResult
            {
                npcLine = scriptedLine,
                options = GenerateDefaultOptions(context.slots),
                mood = "neutral",
                isFallback = false,
                metadata = new ProviderMetadata
                {
                    providerName = "Scripted",
                    latencyMs = 0
                }
            };
        }
        
        private List<string> GenerateDefaultOptions(int slots)
        {
            var options = new List<string>
            {
                "Continue",
                "Ask for more details",
                "Change the subject",
                "End conversation"
            };
            
            while (options.Count > slots) options.RemoveAt(options.Count - 1);
            while (options.Count < slots) options.Add("Continue");
            
            return options;
        }
        
        private AIContext InjectPlanHints(AIContext context, IntentPlan plan)
        {
            if (plan == null) return context;
            
            string hint = "\n\n[Intent: " + plan.intent.ToString();
            
            if (!string.IsNullOrEmpty(plan.suggestedTone))
            {
                hint += ", Tone: " + plan.suggestedTone;
            }
            
            if (plan.requiredFacts != null && plan.requiredFacts.Count > 0)
            {
                hint += ", Must include: " + string.Join(", ", plan.requiredFacts);
            }
            
            hint += "]\n";
            
            context.systemPrompt = context.systemPrompt + hint;
            return context;
        }
        
        private async Task<TurnResult> GenerateWithStreamingAsync(IStreamingInferenceProvider streamingProvider, AIContext context, CancellationToken ct)
        {
            var fullResponse = new System.Text.StringBuilder();
            long latencyMs = 0;
            
            try
            {
                var result = await streamingProvider.GenerateStreamAsync(context, (chunk) =>
                {
                    fullResponse.Append(chunk);
                    return true; // Continue streaming
                }, ct).ConfigureAwait(false);
                
                // If the streaming provider returned a valid result, use it
                if (result != null && !result.isFallback)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                AILogger.Warn("[Streaming] Error during streaming: " + ex.Message);
                // Fall back to non-streaming
                return await provider.GenerateTurnAsync(context, ct).ConfigureAwait(false);
            }
            
            // Parse the accumulated response if we didn't get a valid result
            if (fullResponse.Length > 0 && AIOutputValidator.TryParse(fullResponse.ToString(), out TurnResult parsedResult))
            {
                parsedResult.metadata = new ProviderMetadata
                {
                    providerName = "Streaming",
                    latencyMs = (int)latencyMs
                };
                return parsedResult;
            }
            
            return AIOutputValidator.CreateFallback(context.slots, settings);
        }
        
        private TurnResult ApplyStandardValidations(TurnResult result, AIContext originalContext, AIContext tieredContext)
        {
            // Repetition check
            if (AIOutputValidator.IsRepeatedResponse(result.npcLine, originalContext.recentTurns))
            {
                AILogger.Warn("[TieredContext] Detected repetition in tiered response");
                // Don't retry in tiered mode - let the plan handle diversity
            }
            
            // Coherence validation
            if (settings.enableCoherenceValidation)
            {
                result = ApplyCoherenceValidation(result, originalContext);
            }
            
            return result;
        }
        
        private void WriteStructuredMemory(string npcId, string playerChoice, string npcLine, string memoryDelta)
        {
            var events = memoryWritePolicy.AnalyzeTurn(npcId, playerChoice, npcLine, null);
            
            // Also extract from memory delta if provided
            if (!string.IsNullOrEmpty(memoryDelta))
            {
                var deltaEvents = memoryWritePolicy.ExtractFromMemoryDelta(npcId, memoryDelta, null);
                events.AddRange(deltaEvents);
            }
            
            foreach (var evt in events)
            {
                if (memoryWritePolicy.ShouldCommit(evt))
                {
                    structuredMemory.Add(evt);
                    
                    if (settings.enableValidationLogs)
                    {
                        AILogger.Log("[MemoryWrite] Committed: " + evt.eventType + " - " + evt.summary);
                    }
                }
            }
        }
        
        private void LogPipelineTiming(PipelineLatencyTracker tracker, string providerName)
        {
            if (!settings.enableTimingLogs) return;
            
            var timings = tracker.GetTimings();
            string summary = "[TieredPipeline] " + providerName + " | ";
            
            foreach (var kvp in timings)
            {
                if (kvp.Key != PipelineStages.Total)
                {
                    summary += kvp.Key + "=" + kvp.Value + "ms ";
                }
            }
            
            summary += "| Total=" + tracker.TotalMs + "ms";
            
            AILogger.Log(summary);
        }

        /// <summary>
        /// Adds a progression hint to the context when the model is stuck in a loop.
        /// </summary>
        private AIContext AddCoherenceHint(AIContext context)
        {
            string hint = "\n\n=== CRITICAL: COHERENCE REQUIRED ===\n";
            hint += "Your previous response was INCOHERENT and did not address the player's choice.\n";
            hint += "The player chose: \"" + context.lastPlayerChoice + "\"\n";
            hint += "You MUST respond DIRECTLY to this choice. Do NOT invent unrelated scenarios.\n";
            hint += "Do NOT mention: gates, locked paths, old maps, caves, dungeons, or anything not directly related.\n";
            hint += "Keep your response grounded in the current conversation context.\n";
            hint += "==========================================\n";
            
            context.userPrompt = context.userPrompt + hint;
            return context;
        }
        
        private AIContext AddProgressionHint(AIContext context)
        {
            // Check if player is insisting on something
            AIOutputValidator.IsPlayerInsisting(context.lastPlayerChoice, context.recentTurns, out int insistCount);
            
            string hint = "\n\n=== IMPORTANT: STORY PROGRESSION REQUIRED ===\n";
            hint += "The player has been asking for the same action multiple times.\n";
            hint += "You MUST progress the story now - do NOT repeat your previous response.\n";
            
            if (insistCount >= 2)
            {
                hint += "Since the player is insisting on \"" + context.lastPlayerChoice + "\", you must either:\n";
                hint += "1. LET THEM DO IT - Describe them actually going there or doing it\n";
                hint += "2. FIRMLY BLOCK IT - Give a definitive reason why it's impossible (not just dangerous)\n";
                hint += "Do NOT give another warning. Either let them proceed or give a hard no.\n";
            }
            else
            {
                hint += "Give a DIFFERENT response than before. Move the story forward.\n";
            }
            
            hint += "==========================================\n";
            
            context.userPrompt = context.userPrompt + hint;
            return context;
        }
        
        /// <summary>
        /// When the model fails to progress the story after retries, we force it by rewriting the response.
        /// This is a fallback for smaller models that struggle with complex instructions.
        /// </summary>
        private TurnResult ForceStoryProgression(TurnResult result, AIContext context)
        {
            string playerChoice = context.lastPlayerChoice?.ToLowerInvariant() ?? "";
            
            // Extract the key action from player choice
            string action = ExtractActionFromChoice(playerChoice);
            string location = ExtractLocationFromChoice(playerChoice);
            
            // Generate a progression response based on what the player wants
            string newNpcLine;
            List<string> newOptions;
            
            if (!string.IsNullOrEmpty(location))
            {
                // Player wants to go somewhere - let them arrive
                newNpcLine = $"You push forward through the fog. After a moment, you find yourself at the {location}. The air is thick with salt and moisture.";
                newOptions = new List<string>
                {
                    $"Look around the {location}",
                    "Ask someone nearby for help",
                    "Search for something useful",
                    "Head back the way you came"
                };
            }
            else if (!string.IsNullOrEmpty(action))
            {
                // Player wants to do something - let them do it
                newNpcLine = $"Very well. You proceed to {action}. Let's see what you find.";
                newOptions = new List<string>
                {
                    "Examine what you found",
                    "Ask about what this means",
                    "Continue exploring",
                    "Do something else"
                };
            }
            else
            {
                // Generic progression
                newNpcLine = "I understand. Let me help you with that. Follow me.";
                newOptions = new List<string>
                {
                    "Follow the guide",
                    "Ask a question first",
                    "Look around instead",
                    "Change your mind"
                };
            }
            
            // Truncate options to slot count
            while (newOptions.Count > context.slots)
            {
                newOptions.RemoveAt(newOptions.Count - 1);
            }
            while (newOptions.Count < context.slots)
            {
                newOptions.Add("Continue");
            }
            
            AILogger.Log($"[ForceProgression] Rewrote response to: \"{newNpcLine}\"");
            
            return new TurnResult
            {
                npcLine = newNpcLine,
                options = newOptions,
                mood = "helpful",
                memoryDelta = $"Player insisted on: {playerChoice}. Story progressed.",
                isFallback = false,
                metadata = result.metadata
            };
        }
        
        private string ExtractLocationFromChoice(string choice)
        {
            // Common location patterns
            string[] locationPatterns = { "market", "tavern", "lighthouse", "harbor", "dock", "shop", "inn", "square", "plaza", "beach", "pier" };
            
            foreach (var loc in locationPatterns)
            {
                if (choice.Contains(loc))
                {
                    return loc;
                }
            }
            return null;
        }
        
        private string ExtractActionFromChoice(string choice)
        {
            // Extract verb phrases
            if (choice.StartsWith("check ") || choice.StartsWith("look at ") || choice.StartsWith("examine "))
            {
                return choice;
            }
            if (choice.Contains("map"))
            {
                return "check the map";
            }
            if (choice.Contains("ask") || choice.Contains("talk"))
            {
                return "speak with them";
            }
            return null;
        }
    }
}
