using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    [AddComponentMenu("Immersive NPCs/Conversation Manager")]
    public class AIConversationManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private AIConversationSettings settings;
        
        [Header("NPC System")]
        [Tooltip("Database of NPC profiles")]
        [SerializeField] private NpcProfileDatabase npcProfileDatabase;
        
        [Tooltip("Global world state asset")]
        [SerializeField] private GlobalWorldState globalWorldState;
        
        [Header("Perception")]
        [SerializeField] private UnityScenePerceptionProvider perceptionProvider;
        [SerializeField] private Transform perceptionOrigin;
        [SerializeField] private LayerMask perceptionLayerMask = ~0;

        private AIConversationService service;
        private ILocalInferenceEngine localEngine;
        private CancellationTokenSource lifecycleCts;
        private Task modelPreloadTask;
        private bool modelPreloadStarted;

        public AIConversationSettings Settings => settings;
        public AIDebugMetrics Metrics => service != null ? service.Metrics : null;
        
        /// <summary>NPC Profile Database</summary>
        public NpcProfileDatabase ProfileDatabase => npcProfileDatabase;
        
        /// <summary>Global World State</summary>
        public GlobalWorldState WorldState => globalWorldState;

        /// <summary>
        /// Returns the current loading state of the local inference engine.
        /// </summary>
        public LocalEngineLoadingState LocalEngineState => localEngine?.LoadingState ?? LocalEngineLoadingState.NotInitialized;

        /// <summary>
        /// Returns true if the local model is ready for inference.
        /// </summary>
        public bool IsLocalModelReady => localEngine?.IsReady ?? false;

        /// <summary>
        /// Returns the local engine status message.
        /// </summary>
        public string LocalEngineStatus => localEngine?.Status ?? "No engine";

        private void Awake()
        {
            if (settings == null)
            {
                settings = AISettingsLocator.Load();
            }

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<AIConversationSettings>();
            }

            AILogger.Verbose = settings.verboseLogging;

            if (UsesLocalBackend(settings.providerMode))
            {
                switch (settings.localBackend)
                {
                    case LocalBackendMode.Placeholder:
                        localEngine = new LocalInferenceEnginePlaceholder();
                        break;
                    case LocalBackendMode.InProcess:
                        // Use shared engine manager for proper domain reload handling
                        localEngine = LocalLlamaEngineManager.GetOrCreateEngine(settings);
                        break;
                    case LocalBackendMode.Sentis:
                        localEngine = new SentisLocalInferenceEngine(settings);
                        break;
                }
            }

            service = new AIConversationService(settings, npcProfileDatabase, localEngine);

            if (perceptionProvider == null)
            {
                perceptionProvider = GetComponent<UnityScenePerceptionProvider>();
            }

            if (perceptionOrigin == null)
            {
                perceptionOrigin = transform;
            }

            lifecycleCts = new CancellationTokenSource();

            // Initialize the Runtime API with our settings and databases
            ImmersiveNpcsRuntime.InitializeFromManager(
                settings,
                npcProfileDatabase,
                globalWorldState,
                service
            );

            // Start model preloading if enabled
            if (ShouldPreloadLocalModel())
            {
                StartModelPreload();
            }
        }

        private void StartModelPreload()
        {
            if (modelPreloadStarted)
            {
                return;
            }

            modelPreloadStarted = true;
            string modelPath = ResolveModelPath();
            if (string.IsNullOrEmpty(modelPath))
            {
                AILogger.Warn("Cannot preload model: no model path configured.");
                return;
            }

            AILogger.Log("Starting model preload: " + modelPath);
            modelPreloadTask = PreloadModelInternalAsync(modelPath, lifecycleCts.Token);
        }

        private async Task PreloadModelInternalAsync(string modelPath, CancellationToken ct)
        {
            try
            {
                bool success = await localEngine.PreloadModelAsync(modelPath, ct).ConfigureAwait(false);
                if (success)
                {
                    AILogger.Log("Model preloaded successfully.");
                }
                else
                {
                    AILogger.Warn("Model preload failed: " + localEngine.Status);
                }
            }
            catch (OperationCanceledException)
            {
                AILogger.Log("Model preload cancelled.");
            }
            catch (Exception ex)
            {
                AILogger.Warn("Model preload error: " + ex.Message);
            }
        }

        private string ResolveModelPath()
        {
            if (settings == null || string.IsNullOrEmpty(settings.selectedLocalModel))
            {
                return null;
            }

            string folder = PathUtility.ResolveProjectPath(settings.localModelFolder);
            return System.IO.Path.Combine(folder, settings.selectedLocalModel);
        }

        /// <summary>
        /// Waits until the local model is ready for inference.
        /// Returns true if the model is ready, false on timeout or failure.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds. 0 uses the configured timeout.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<bool> WaitForModelReadyAsync(int timeoutMs = 0, CancellationToken ct = default)
        {
            if (localEngine == null)
            {
                return false;
            }

            if (localEngine.IsReady)
            {
                return true;
            }

            if (timeoutMs <= 0 && settings != null)
            {
                timeoutMs = settings.localInProcessLoadTimeoutSeconds * 1000;
            }

            if (timeoutMs <= 0)
            {
                timeoutMs = 120000; // Default 2 minutes
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifecycleCts.Token);
            return await localEngine.WaitUntilReadyAsync(timeoutMs, linked.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Explicitly triggers model preloading if not already started.
        /// </summary>
        public void EnsureModelPreloading()
        {
            if (settings != null && UsesLocalBackend(settings.providerMode) && IsEngineBackedLocalModel(settings.localBackend))
            {
                StartModelPreload();
            }
        }

        private void OnDestroy()
        {
            // Shutdown the runtime API
            ImmersiveNpcsRuntime.Shutdown();
            
            // Only dispose non-shared engines (like Placeholder)
            // The shared LocalLlamaEngine is managed by LocalLlamaEngineManager
            // and persists across play sessions when domain reload is disabled
            if (localEngine is LocalInferenceEnginePlaceholder)
            {
                // Placeholder doesn't hold native resources, safe to dispose
            }
            else if (localEngine is SentisLocalInferenceEngine sentisEngine)
            {
                sentisEngine.Dispose();
            }
            // Don't dispose LocalLlamaEngine - it's managed by LocalLlamaEngineManager

            if (lifecycleCts != null)
            {
                lifecycleCts.Cancel();
                lifecycleCts.Dispose();
                lifecycleCts = null;
            }
        }

        public async Task<TurnResult> PrefetchAsync(string npcId, int slots, CancellationToken ct)
        {
            if (service == null)
            {
                return AIOutputValidator.CreateFallback(slots, settings);
            }

            // Capture perception on main thread BEFORE any ConfigureAwait(false) calls
            // Unity physics APIs can only be called from the main thread
            Vector3 perceptionOriginPos = perceptionOrigin != null ? perceptionOrigin.position : transform.position;
            PerceptionSnapshot perception = await CapturePerceptionAsync(perceptionOriginPos, ct);

            // If using an engine-backed local backend, ensure model loading has started and wait if needed
            if (UsesLocalBackend(settings.providerMode) && IsEngineBackedLocalModel(settings.localBackend) && localEngine != null)
            {
                var loadState = localEngine.LoadingState;

                // If not initialized, start preloading
                if (loadState == LocalEngineLoadingState.NotInitialized)
                {
                    EnsureModelPreloading();
                }

                // If loading, wait for it to complete with timeout
                if (loadState == LocalEngineLoadingState.Loading || loadState == LocalEngineLoadingState.NotInitialized)
                {
                    int timeoutMs = settings.localInProcessLoadTimeoutSeconds * 1000;
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifecycleCts.Token);
                    bool ready = await localEngine.WaitUntilReadyAsync(timeoutMs, linked.Token).ConfigureAwait(false);
                    if (!ready)
                    {
                        AILogger.Warn("Model not ready for prefetch. State: " + localEngine.LoadingState + " Status: " + localEngine.Status);
                        return AIOutputValidator.CreateFallback(slots > 0 ? slots : settings.slotsCount, settings);
                    }
                }

                // If failed, return fallback
                if (localEngine.LoadingState == LocalEngineLoadingState.Failed)
                {
                    AILogger.Warn("Cannot prefetch: model loading failed. Status: " + localEngine.Status);
                    return AIOutputValidator.CreateFallback(slots > 0 ? slots : settings.slotsCount, settings);
                }
            }

            int resolvedSlots = slots > 0 ? slots : settings.slotsCount;
            string language = string.IsNullOrEmpty(settings.language) ? "en" : settings.language;

            using var linkedMain = CancellationTokenSource.CreateLinkedTokenSource(ct, lifecycleCts.Token);
            TurnResult result = await service.GetOrGenerateAsync(npcId, resolvedSlots, language, perception, linkedMain.Token).ConfigureAwait(false);

            AIConversationState state = service.GetState(npcId);
            state.SetGeneratedTurn(result);

            if (settings.enableSpeculativePrefetch && result != null)
            {
                PrefetchSpeculativeAsync(npcId, result, perception, resolvedSlots, language, lifecycleCts.Token).Forget();
            }

            return result;
        }

        private bool ShouldPreloadLocalModel()
        {
            if (settings == null)
            {
                return false;
            }

            if (!UsesLocalBackend(settings.providerMode))
            {
                return false;
            }

            if (settings.localBackend == LocalBackendMode.InProcess)
            {
                return settings.localInProcessPreloadModel;
            }

            if (settings.localBackend == LocalBackendMode.Sentis)
            {
                return settings.localSentisPreloadModel;
            }

            return false;
        }

        private static bool IsEngineBackedLocalModel(LocalBackendMode backend)
        {
            return backend == LocalBackendMode.InProcess || backend == LocalBackendMode.Sentis;
        }

        private static bool UsesLocalBackend(ProviderMode mode)
        {
            return mode == ProviderMode.Local || mode == ProviderMode.Race;
        }

        public void RecordChoice(string npcId, int slotIndex)
        {
            if (service == null)
            {
                return;
            }

            AIConversationState state = service.GetState(npcId);
            TurnResult last = state.LastGenerated;
            if (last == null || last.options == null)
            {
                return;
            }

            if (slotIndex < 0 || slotIndex >= last.options.Count)
            {
                AILogger.Warn("Choice index out of range: " + slotIndex, this);
                return;
            }

            string choice = last.options[slotIndex];
            state.RecordChoice(choice, last.mood);
            state.AppendMemoryDelta(last.memoryDelta);
            state.SummarizeIfNeeded(settings);

            // Record turn to session memory for entity tracking
            service.RecordTurnForSession(npcId, last.npcLine, choice, last.options);

            service.RecordChoiceMemoryAsync(npcId, choice, last.npcLine, lifecycleCts.Token).Forget();

            PrefetchAsync(npcId, settings.slotsCount, lifecycleCts.Token).Forget();
        }

        public TurnResult GetLastGenerated(string npcId)
        {
            if (service == null)
            {
                return null;
            }

            return service.GetState(npcId).LastGenerated;
        }

        private async Task PrefetchSpeculativeAsync(string npcId, TurnResult lastResult, PerceptionSnapshot perception, int slots, string language, CancellationToken ct)
        {
            if (lastResult == null || lastResult.options == null || lastResult.options.Count == 0)
            {
                return;
            }

            int maxDepth = Mathf.Clamp(settings.speculativePrefetchDepth, 1, 4);
            if (maxDepth < 2)
            {
                return;
            }

            int maxNodes = Mathf.Max(1, settings.speculativePrefetchMaxNodes);
            AIConversationState baseState = service.GetState(npcId);
            NpcProfile profile = npcProfileDatabase != null ? npcProfileDatabase.FindProfile(npcId) : null;

            Queue<PrefetchNode> queue = new Queue<PrefetchNode>();
            HashSet<string> seenKeys = new HashSet<string>();
            AIConversationState rootState = baseState.Clone();
            rootState.SetGeneratedTurn(lastResult);
            queue.Enqueue(new PrefetchNode(rootState, lastResult, 1));

            int scheduled = 0;

            while (queue.Count > 0 && scheduled < maxNodes && !ct.IsCancellationRequested)
            {
                PrefetchNode node = queue.Dequeue();
                if (node.depth >= maxDepth)
                {
                    continue;
                }

                if (node.turn == null || node.turn.options == null || node.turn.options.Count == 0)
                {
                    continue;
                }

                int optionsCount = node.turn.options.Count;
                List<Task<PrefetchResult>> tasks = new List<Task<PrefetchResult>>(optionsCount);

                for (int i = 0; i < optionsCount && scheduled < maxNodes; i++)
                {
                    string option = node.turn.options[i];
                    if (string.IsNullOrWhiteSpace(option))
                    {
                        continue;
                    }

                    AIConversationState simulated = node.state.Clone();
                    simulated.SetGeneratedTurn(node.turn);
                    simulated.RecordChoice(option, node.turn.mood);
                    simulated.AppendMemoryDelta(node.turn.memoryDelta);
                    simulated.SummarizeIfNeeded(settings);

                    AIContext context = await service.BuildContextAsync(npcId, slots, language, simulated, profile, perception, ct).ConfigureAwait(false);
                    string cacheKey = AICacheKey.BuildKey(npcId, context.summary, context.lastPlayerChoice, perception, slots, language, context.memoryKey);
                    if (!seenKeys.Add(cacheKey))
                    {
                        continue;
                    }

                    scheduled++;
                    tasks.Add(PrefetchNodeAsync(simulated, context, cacheKey, node.depth + 1, ct));
                }

                if (tasks.Count == 0)
                {
                    continue;
                }

                PrefetchResult[] results;
                try
                {
                    results = await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    continue;
                }

                for (int i = 0; i < results.Length; i++)
                {
                    PrefetchResult result = results[i];
                    if (result == null || result.turn == null || result.turn.options == null || result.turn.options.Count == 0)
                    {
                        continue;
                    }

                    if (result.depth < maxDepth)
                    {
                        queue.Enqueue(new PrefetchNode(result.state, result.turn, result.depth));
                    }
                }
            }
        }

        private async Task<PrefetchResult> PrefetchNodeAsync(AIConversationState state, AIContext context, string cacheKey, int depth, CancellationToken ct)
        {
            try
            {
                TurnResult result = await service.PrefetchWithContextAsync(context, cacheKey, ct).ConfigureAwait(false);
                if (result == null || result.isFallback)
                {
                    return null;
                }

                AIConversationState nextState = state.Clone();
                nextState.SetGeneratedTurn(result);
                return new PrefetchResult(nextState, result, depth);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private sealed class PrefetchNode
        {
            public AIConversationState state;
            public TurnResult turn;
            public int depth;

            public PrefetchNode(AIConversationState state, TurnResult turn, int depth)
            {
                this.state = state;
                this.turn = turn;
                this.depth = depth;
            }
        }

        private sealed class PrefetchResult
        {
            public AIConversationState state;
            public TurnResult turn;
            public int depth;

            public PrefetchResult(AIConversationState state, TurnResult turn, int depth)
            {
                this.state = state;
                this.turn = turn;
                this.depth = depth;
            }
        }

        private Task<PerceptionSnapshot> CapturePerceptionAsync(Vector3 origin, CancellationToken ct)
        {
            if (perceptionProvider == null)
            {
                return Task.FromResult(PerceptionSnapshot.Empty());
            }

            PerceptionRequest request = new PerceptionRequest
            {
                origin = origin,
                radius = settings.perceptionRadius,
                maxSignals = settings.maxPerceptionSignals,
                layerMask = perceptionLayerMask
            };

            return perceptionProvider.CaptureAsync(request, ct);
        }
    }
}
