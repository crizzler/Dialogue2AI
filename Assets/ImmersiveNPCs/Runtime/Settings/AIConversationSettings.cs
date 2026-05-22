using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveNPCs
{
    public enum ProviderMode
    {
        Local,
        Cloud,
        Race
    }

    public enum ApiKeyMode
    {
        EnvVarName,
        TextAssetReference,
        InlineText
    }

    public enum CloudProviderMode
    {
        OpenAI,
        Claude,
        DeepSeek
    }

    public enum LocalBackendMode
    {
        Placeholder,
        Ollama,
        OpenAICompatible,
        InProcess,
        Sentis
    }

    public enum LocalInProcessDeviceMode
    {
        CPUOnly,
        GPUPreferred
    }

    public enum LocalInProcessChatTemplateMode
    {
        Auto,
        ChatML,
        Raw
    }

    public enum LocalSentisDeviceMode
    {
        CPU,
        GPUCompute,
        GPUPixel
    }

    [CreateAssetMenu(fileName = "AIConversationSettings", menuName = "Immersive NPCs/AI Conversation Settings")]
    public class AIConversationSettings : ScriptableObject
    {
        [Header("Provider")]
        public ProviderMode providerMode = ProviderMode.Race;

        [Tooltip("Local backend implementation to use when Provider Mode is Local or Race.")]
        public LocalBackendMode localBackend = LocalBackendMode.Placeholder;

        [Tooltip("Project-relative folder containing local models.")]
        public string localModelFolder = "StreamingAssets/ImmersiveNPCs/Models";

        [Tooltip("Selected local model file name.")]
        public string selectedLocalModel = string.Empty;

        [Header("Local: Ollama")]
        [Tooltip("Ollama chat endpoint URL.")]
        public string ollamaEndpoint = "http://localhost:11434/api/chat";

        [Tooltip("Ollama model name.")]
        public string ollamaModelName = "llama3";

        [Header("Local: OpenAI-Compatible")]
        [Tooltip("OpenAI-compatible endpoint URL (e.g., LM Studio).")]
        public string localOpenAIEndpoint = "http://localhost:1234/v1/chat/completions";

        [Tooltip("Local OpenAI-compatible model name.")]
        public string localOpenAIModelName = "local-model";

        [Tooltip("Use API key for local OpenAI-compatible server if required.")]
        public bool localOpenAIUseApiKey = false;

        [Header("Local: In-Process (GGUF)")]
        [Tooltip("Select CPU-only or GPU-preferred execution. GPU requires a CUDA-enabled plugin build.")]
        public LocalInProcessDeviceMode localInProcessDevice = LocalInProcessDeviceMode.CPUOnly;

        [Tooltip("Prompt formatting for in-process models. Auto uses the model's chat template when available.")]
        public LocalInProcessChatTemplateMode localInProcessChatTemplateMode = LocalInProcessChatTemplateMode.Auto;

        [Min(256)]
        [Tooltip("Context size for in-process inference. Larger values allow longer conversations but use more VRAM.")]
        public int localInProcessContextSize = 8192;

        [Min(1)]
        [Tooltip("CPU threads to use for in-process inference.")]
        public int localInProcessThreads = 4;

        [Min(0)]
        [Tooltip("GPU layers to offload. When GPU is preferred, 0 means all layers.")]
        public int localInProcessGpuLayers = 0;

        [Tooltip("Use memory-mapped model loading when available.")]
        public bool localInProcessUseMmap = true;

        [Tooltip("Lock model memory when available.")]
        public bool localInProcessUseMlock = false;

        [Tooltip("Allow host (system RAM) buffers as a fallback for GPU execution.")]
        public bool localInProcessAllowHostMemory = true;

        [Min(0)]
        [Tooltip("Random seed; 0 uses a random seed.")]
        public int localInProcessSeed = 0;

        [Min(1)]
        [Tooltip("Max tokens to use when Max Tokens is -1.")]
        public int localInProcessDefaultMaxTokens = 256;

        [Tooltip("Preload model on startup. If disabled, model loads on first inference request.")]
        public bool localInProcessPreloadModel = true;
        
        [Tooltip("Automatically adjust context size and settings based on model size and available VRAM. Recommended for most users.")]
        public bool localInProcessAutoConfig = true;

        [Min(5)]
        [Tooltip("Timeout in seconds for model loading. 0 disables the timeout.")]
        public int localInProcessLoadTimeoutSeconds = 120;

        [Min(0)]
        [Tooltip("Number of retry attempts for model loading on failure.")]
        public int localInProcessLoadRetryCount = 2;

        [Min(500)]
        [Tooltip("Initial delay in milliseconds between retry attempts (doubles each attempt).")]
        public int localInProcessLoadRetryDelayMs = 1000;

        [Header("Local: Sentis (.sentis + tokenizer.json)")]
        [Tooltip("Sentis backend used for model execution.")]
        public LocalSentisDeviceMode localSentisDevice = LocalSentisDeviceMode.GPUCompute;

        [Tooltip("Tokenizer JSON path. Relative paths are resolved next to the selected .sentis model.")]
        public string localSentisTokenizerFile = "tokenizer.json";

        [Tooltip("Maximum prompt context tokens sent to the Sentis model. Older tokens are truncated first.")]
        [Min(1)]
        public int localSentisMaxContextTokens = 2048;

        [Tooltip("Input tensor name for token IDs.")]
        public string localSentisInputIdsName = "input_ids";

        [Tooltip("Input tensor name for the attention mask.")]
        public string localSentisAttentionMaskName = "attention_mask";

        [Tooltip("Optional output tensor name for logits. Leave empty to use the first model output.")]
        public string localSentisLogitsOutputName = string.Empty;

        [Tooltip("Comma-separated token IDs that stop generation, for example EOS tokens.")]
        public string localSentisStopTokenIds = string.Empty;

        [Tooltip("Preload the Sentis model on startup. If disabled, model loads on first inference request.")]
        public bool localSentisPreloadModel = true;

        [Header("Cloud")]
        [Tooltip("Cloud provider API to use.")]
        public CloudProviderMode cloudProvider = CloudProviderMode.OpenAI;

        [Tooltip("Cloud model name.")]
        public string cloudModelName = "gpt-4.1-mini";

        [Tooltip("Cloud endpoint URL.")]
        public string cloudEndpoint = "https://api.openai.com/v1/responses";

        [Tooltip("Where to read the API key from.")]
        public ApiKeyMode apiKeyMode = ApiKeyMode.EnvVarName;

        [Tooltip("Environment variable name for API key.")]
        public string apiKeyEnvVar = "OPENAI_API_KEY";

        [Tooltip("TextAsset containing the API key (use for local testing only).")]
        public TextAsset apiKeyTextAsset;

        [Tooltip("Inline API key for local testing only. Prefer environment variables for shared projects.")]
        public string apiKeyText = string.Empty;

        [Header("Generation")]
        [Min(1)]
        public int slotsCount = 4;

        [Min(-1)]
        public int maxTokens = 256;

        [Range(0f, 2f)]
        public float temperature = 0.8f;

        [Range(0f, 1f)]
        public float topP = 1f;

        [Range(-2f, 2f)]
        public float presencePenalty = 0f;

        [Range(-2f, 2f)]
        public float frequencyPenalty = 0f;

        [Min(1)]
        public int requestTimeoutMs = 20000;

        [Range(0, 5)]
        public int retryCount = 2;

        [Min(50)]
        public int retryBackoffMs = 500;

        [Header("Caching")]
        [Min(1)]
        public int memoryCacheEntries = 128;

        public bool diskCacheEnabled = true;

        [Tooltip("Project-relative disk cache folder.")]
        public string diskCachePath = "Library/ImmersiveNPCs/Cache";

        [Min(1)]
        public int diskCacheTtlMinutes = 1440;

        [Header("Coherence Validation")]
        [Tooltip("Enable automatic option coherence validation. Ensures options match entities mentioned in NPC dialogue.")]
        public bool enableCoherenceValidation = true;

        [Tooltip("Enable speculative prefetch when idle.")]
        public bool enableSpeculativePrefetch = false;

        [Range(1, 4)]
        public int speculativePrefetchDepth = 2;

        [Tooltip("Maximum number of speculative generations to enqueue per prefetch cycle.")]
        [Min(1)]
        public int speculativePrefetchMaxNodes = 12;

        [Min(1)]
        public int prefetchMaxConcurrent = 2;

        [Header("Memory (RAG)")]
        public bool enableMemory = true;

        public MemoryScopeMode memoryScope = MemoryScopeMode.GlobalAndNpc;

        [Min(1)]
        public int memoryTopK = 6;

        [Min(32)]
        public int memoryMaxChars = 1200;

        [Min(1)]
        public int memoryMaxEntries = 512;

        [Min(1)]
        public int memoryMaxEntriesPerNpc = 128;

        public bool memoryStorePlayerChoices = true;

        public bool memoryStoreNpcReplies = true;

        public bool memoryUseTimeDecay = true;

        [Min(1)]
        public int memoryDecayHalfLifeMinutes = 60;

        public EmbeddingProviderMode embeddingProviderMode = EmbeddingProviderMode.Auto;

        [Tooltip("Cloud embedding model name used when cloud embeddings are enabled.")]
        public string embeddingModelName = "text-embedding-3-small";

        [Tooltip("Cloud embedding endpoint (OpenAI-compatible).")]
        public string embeddingEndpoint = "https://api.openai.com/v1/embeddings";

        public List<MemorySeed> memorySeeds = new List<MemorySeed>();

        [Header("Summarization")]
        public bool summarizationEnabled = true;

        [Min(1)]
        public int maxRecentTurns = 12;

        [Min(32)]
        public int summaryTokenBudget = 256;

        [Header("Safety")]
        [Min(32)]
        public int maxLineLength = 280;

        [Min(16)]
        public int maxOptionLength = 120;

        public List<string> forbiddenTopics = new List<string>();

        public bool stayInCharacter = true;

        [Header("Dialogue Behavior")]
        [Tooltip("Require the NPC reply to directly address the most recent player choice.")]
        public bool strictRespondToChoice = true;

        [Tooltip("Inject the most recent player choice at the end of the user prompt.")]
        public bool injectChoiceAsLastUserMessage = true;

        [Tooltip("Language code used for caching and prompting.")]
        public string language = "en";

        [Header("Perception")]
        [Min(0f)]
        public float perceptionRadius = 8f;

        [Min(0)]
        public int maxPerceptionSignals = 12;

        [Header("Hugging Face (Editor)")]
        public bool huggingFaceUseToken = false;

        [Tooltip("Environment variable name for Hugging Face access token.")]
        public string huggingFaceTokenEnvVar = "HF_TOKEN";

        [Tooltip("TextAsset containing Hugging Face token (Editor only).")]
        public TextAsset huggingFaceTokenAsset;

        [Min(5)]
        public int huggingFaceSearchLimit = 25;

        [Header("Tiered Context (v2 Pipeline)")]
        [Tooltip("Enable the new tiered context memory system. When disabled, uses legacy raw scrollback mode.")]
        public bool enableTieredContext = false;

        [Tooltip("Quality preset that controls context budgets, timeouts, and validation strictness. Ignored in legacy mode.")]
        public QualityPreset qualityPreset = QualityPreset.Balanced;

        [Tooltip("Enable the planning phase before generation. Adds latency but improves response quality.")]
        public bool enablePlanningPhase = true;

        [Tooltip("Enable streaming token generation where supported.")]
        public bool enableStreamingGeneration = false;

        [Tooltip("Enable structured memory writes (only commit-worthy events). When disabled, uses raw chat log storage.")]
        public bool enableStructuredMemory = false;

        [Tooltip("Enable script authority arbitration for Yarn/GC2 integration.")]
        public bool enableScriptAuthority = true;

        [Tooltip("Enable world state validation against snapshot. Catches hallucinated locations, NPCs, items.")]
        public bool enableWorldStateValidation = true;

        [Tooltip("Validation strictness level. Lenient = obvious errors only, Strict = all claims checked.")]
        public ResponseValidator.StrictnessLevel validationStrictness = ResponseValidator.StrictnessLevel.Moderate;

        [Header("Debug")]
        public bool enableRuntimeOverlay = false;

        public bool verboseLogging = true;

        [Tooltip("Enable native in-process logging (requires rebuilt plugin).")]
        public bool enableInProcessLogging = false;

        [Tooltip("Log timing information for pipeline stages.")]
        public bool enableTimingLogs = false;

        [Tooltip("Log validation results and claim checks.")]
        public bool enableValidationLogs = false;
    }

    [System.Serializable]
    public class MemorySeed
    {
        [TextArea(2, 6)]
        public string text = string.Empty;

        public MemoryScopeMode scope = MemoryScopeMode.GlobalAndNpc;

        public string npcId = string.Empty;
    }
}
