using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public sealed class LocalLLMProvider : IAIProvider, IAIProviderHealth, IPlanningProvider
    {
        private readonly AIConversationSettings settings;
        private readonly ILocalInferenceEngine engine;
        private readonly string modelPath;

        public LocalLLMProvider(AIConversationSettings settings, ILocalInferenceEngine engine, string modelPath)
        {
            this.settings = settings;
            this.engine = engine;
            this.modelPath = modelPath;
        }

        public bool IsAvailable
        {
            get
            {
                if (engine == null || string.IsNullOrEmpty(modelPath))
                {
                    return false;
                }

                if (settings != null && settings.localBackend == LocalBackendMode.InProcess)
                {
                    return true;
                }

                if (settings != null && settings.localBackend == LocalBackendMode.Sentis)
                {
                    return true;
                }

                return engine.IsReady;
            }
        }
        public string Status => engine != null ? engine.Status : "No engine";

        public async Task<TurnResult> GenerateTurnAsync(AIContext context, CancellationToken ct)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            LocalInProcessChatTemplateMode templateMode = ResolveTemplateMode();
            string prompt = BuildPrompt(context, templateMode, out string systemPrompt, out string userPrompt);

            // Always log prompts for debugging
            UnityEngine.Debug.Log("[ImmersiveNPCs] === SYSTEM PROMPT ===\n" + systemPrompt + "\n=== END SYSTEM ===");
            UnityEngine.Debug.Log("[ImmersiveNPCs] === USER PROMPT ===\n" + userPrompt + "\n=== END USER ===");

            LocalInferenceRequest request = new LocalInferenceRequest
            {
                prompt = prompt,
                systemPrompt = systemPrompt,
                userPrompt = userPrompt,
                chatTemplateMode = templateMode,
                modelPath = modelPath,
                maxTokens = settings.maxTokens,
                temperature = settings.temperature,
                slots = context.slots,
                npcId = context.npcId
            };

            string output = await engine.GenerateAsync(request, ct).ConfigureAwait(false);

            // Always log raw output for debugging
            UnityEngine.Debug.Log("[ImmersiveNPCs] === RAW OUTPUT ===\n" + output + "\n=== END OUTPUT ===");

            TurnResult result = ParseOutput(output, context.slots);
            string providerName = "Local";
            if (settings != null && settings.localBackend == LocalBackendMode.InProcess)
            {
                providerName = "Local In-Process";
            }
            else if (settings != null && settings.localBackend == LocalBackendMode.Sentis)
            {
                providerName = "Local Sentis";
            }
            result.metadata = new ProviderMetadata
            {
                providerName = providerName,
                latencyMs = stopwatch.ElapsedMilliseconds,
                modelName = settings.selectedLocalModel
            };

            return result;
        }

        private string BuildPrompt(AIContext context, LocalInProcessChatTemplateMode templateMode, out string system, out string user)
        {
            system = context?.systemPrompt ?? string.Empty;
            user = context?.userPrompt ?? string.Empty;

            if (settings != null && settings.localBackend == LocalBackendMode.InProcess)
            {
                int contextSize = settings.localInProcessContextSize > 0 ? settings.localInProcessContextSize : 8192;
                int maxTokens = settings.maxTokens > 0 ? settings.maxTokens : settings.localInProcessDefaultMaxTokens;
                if (maxTokens <= 0) maxTokens = 256;
                
                // Reserve space for the model's response. Use a conservative 3 chars per token ratio
                // to avoid context overflow, since some tokens can be 1-2 characters.
                int reservedForResponse = maxTokens;
                int availableTokensForPrompt = Math.Max(256, contextSize - reservedForResponse - 64); // 64 token safety margin
                int maxChars = availableTokensForPrompt * 3; // Conservative 3 chars per token
                
                int separator = string.IsNullOrEmpty(user) ? 0 : 2;
                int total = system.Length + separator + user.Length;

                // Log context budget for debugging
                if (total > maxChars)
                {
                    AILogger.Warn($"Context budget: contextSize={contextSize} tokens, maxChars={maxChars}, system={system.Length}, user={user.Length}, total={total}");
                }

                if (total > maxChars)
                {
                    // Priority: Keep user prompt (conversation context) over system prompt
                    // Reserve at least 30% of context for user prompt when possible
                    int minUserChars = Math.Min(user.Length, maxChars * 30 / 100);
                    int maxSystemChars = maxChars - minUserChars - separator;
                    
                    if (system.Length > maxSystemChars && maxSystemChars > 256)
                    {
                        // Trim system prompt from the beginning (keep the end with JSON schema)
                        system = system.Substring(system.Length - maxSystemChars);
                        AILogger.Warn($"Prompt too large for context. Trimmed system prompt to {maxSystemChars} chars.");
                    }
                    
                    int allowedUser = maxChars - system.Length - separator;
                    if (allowedUser < 0)
                    {
                        // System prompt still too large, trim more aggressively
                        int absoluteMinSystem = Math.Min(512, maxChars / 2);
                        if (system.Length > absoluteMinSystem)
                        {
                            system = system.Substring(system.Length - absoluteMinSystem);
                        }
                        allowedUser = maxChars - system.Length - separator;
                        AILogger.Warn("Prompt severely trimmed. Consider increasing context size.");
                    }
                    
                    if (user.Length > allowedUser && allowedUser > 0)
                    {
                        // Trim user history from the beginning (keep recent context)
                        user = user.Substring(user.Length - allowedUser);
                        AILogger.Warn($"Prompt too large for context. Trimmed user history to {allowedUser} chars.");
                    }
                    else if (allowedUser <= 0)
                    {
                        user = string.Empty;
                        AILogger.Warn("No space for user prompt. Increase context size.");
                    }
                }
            }

            if (settings != null && settings.localBackend == LocalBackendMode.InProcess)
            {
                if (templateMode == LocalInProcessChatTemplateMode.ChatML)
                {
                    return BuildChatMlPrompt(system, user);
                }
                if (templateMode == LocalInProcessChatTemplateMode.Raw)
                {
                    return BuildRawPrompt(system, user);
                }
                // Auto falls back to raw when the native template helper is unavailable.
                return BuildRawPrompt(system, user);
            }

            if (string.IsNullOrEmpty(user))
            {
                return system ?? string.Empty;
            }

            return (system ?? string.Empty) + "\n\n" + user;
        }

        private static string BuildChatMlPrompt(string system, string user)
        {
            system = (system ?? string.Empty).Trim();
            user = (user ?? string.Empty).Trim();

            return "<|im_start|>system\n" + system + "\n<|im_end|>\n"
                + "<|im_start|>user\n" + user + "\n<|im_end|>\n"
                + "<|im_start|>assistant\n";
        }

        private static string BuildRawPrompt(string system, string user)
        {
            if (string.IsNullOrEmpty(user))
            {
                return system ?? string.Empty;
            }

            return (system ?? string.Empty) + "\n\n" + user;
        }

        private LocalInProcessChatTemplateMode ResolveTemplateMode()
        {
            if (settings == null || settings.localBackend != LocalBackendMode.InProcess)
            {
                return LocalInProcessChatTemplateMode.Raw;
            }

            return settings.localInProcessChatTemplateMode;
        }

        private TurnResult ParseOutput(string output, int slots)
        {
            // First, strip any <think>...</think> reasoning blocks (Qwen3, etc.)
            string cleaned = AIOutputValidator.StripThinkTags(output);
            
            if (AIOutputValidator.TryParse(cleaned, out TurnResult parsed))
            {
                return AIOutputValidator.Sanitize(parsed, slots, settings);
            }

            string json = AIOutputValidator.ExtractJsonSubstring(cleaned);
            if (AIOutputValidator.TryParse(json, out parsed))
            {
                return AIOutputValidator.Sanitize(parsed, slots, settings);
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                string preview = cleaned.Trim();
                if (preview.Length > 220)
                {
                    preview = preview.Substring(0, 220) + "...";
                }
                AILogger.Warn("Local provider returned non-JSON output. Using fallback. Preview: " + preview);
            }
            else
            {
                AILogger.Warn("Local provider returned empty output. Using fallback.");
            }

            return AIOutputValidator.CreateFallback(slots, settings);
        }
        
        // === IPlanningProvider Implementation ===
        
        public bool SupportsPlanning => engine != null && !string.IsNullOrEmpty(modelPath);
        
        public async Task<string> PlanAsync(AIContext planContext, int maxTokens, CancellationToken ct)
        {
            if (engine == null || string.IsNullOrEmpty(modelPath))
            {
                return null;
            }
            
            // Use tiny token budget for planning
            int planMaxTokens = Math.Min(maxTokens, 128);
            
            LocalInProcessChatTemplateMode templateMode = ResolveTemplateMode();
            
            LocalInferenceRequest request = new LocalInferenceRequest
            {
                prompt = planContext?.systemPrompt ?? string.Empty,
                systemPrompt = planContext?.systemPrompt ?? string.Empty,
                userPrompt = planContext?.userPrompt ?? string.Empty,
                chatTemplateMode = templateMode,
                modelPath = modelPath,
                maxTokens = planMaxTokens,
                temperature = 0.3f, // Lower temperature for planning
                slots = 4,
                npcId = planContext?.npcId ?? "planner"
            };
            
            try
            {
                string output = await engine.GenerateAsync(request, ct).ConfigureAwait(false);
                return AIOutputValidator.StripThinkTags(output?.Trim());
            }
            catch (Exception ex)
            {
                AILogger.Warn("[Planning] Failed: " + ex.Message);
                return null;
            }
        }
    }
}
