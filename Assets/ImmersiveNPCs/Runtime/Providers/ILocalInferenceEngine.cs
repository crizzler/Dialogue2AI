using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Loading state of the local inference engine.
    /// </summary>
    public enum LocalEngineLoadingState
    {
        /// <summary>No model configured or engine not initialized.</summary>
        NotInitialized,
        /// <summary>Model is currently being loaded.</summary>
        Loading,
        /// <summary>Model loaded and ready for inference.</summary>
        Ready,
        /// <summary>Model loading failed.</summary>
        Failed
    }

    public interface ILocalInferenceEngine
    {
        /// <summary>True if model is loaded and ready for inference.</summary>
        bool IsReady { get; }

        /// <summary>Human-readable status message.</summary>
        string Status { get; }

        /// <summary>Current loading state of the engine.</summary>
        LocalEngineLoadingState LoadingState { get; }

        /// <summary>
        /// Explicitly preloads the model asynchronously. 
        /// Returns true when the model is ready, false on failure or timeout.
        /// </summary>
        Task<bool> PreloadModelAsync(string modelPath, CancellationToken ct);

        /// <summary>
        /// Waits until the model is ready or a timeout occurs.
        /// Returns true if ready, false if timeout or failure.
        /// </summary>
        Task<bool> WaitUntilReadyAsync(int timeoutMs, CancellationToken ct);

        Task<string> GenerateAsync(LocalInferenceRequest request, CancellationToken ct);
        Task<float[]> EmbedAsync(LocalEmbeddingRequest request, CancellationToken ct);
    }

    public struct LocalInferenceRequest
    {
        public string prompt;
        public string systemPrompt;
        public string userPrompt;
        public LocalInProcessChatTemplateMode chatTemplateMode;
        public string modelPath;
        public int maxTokens;
        public float temperature;
        public int slots;
        public string npcId;
    }

    public struct LocalEmbeddingRequest
    {
        public string text;
        public string modelPath;
    }
}
