using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Optional interface for providers that support streaming token generation.
    /// Providers that cannot stream should emulate by chunking the final string.
    /// </summary>
    public interface IStreamingInferenceProvider
    {
        /// <summary>
        /// True if the provider supports native streaming.
        /// </summary>
        bool SupportsStreaming { get; }
        
        /// <summary>
        /// Generates a response with streaming token callback.
        /// </summary>
        /// <param name="context">The AI context.</param>
        /// <param name="onToken">Callback invoked for each token/chunk. Return false to cancel.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The complete response after streaming finishes.</returns>
        Task<TurnResult> GenerateStreamAsync(AIContext context, Func<string, bool> onToken, CancellationToken ct);
    }
    
    /// <summary>
    /// Interface for planning-capable providers.
    /// </summary>
    public interface IPlanningProvider
    {
        /// <summary>
        /// True if the provider supports the planning phase.
        /// </summary>
        bool SupportsPlanning { get; }
        
        /// <summary>
        /// Executes a planning call (tiny prompt, strict JSON output).
        /// </summary>
        /// <param name="planContext">Minimal context for planning.</param>
        /// <param name="maxTokens">Maximum tokens for the plan output.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>JSON string containing the plan.</returns>
        Task<string> PlanAsync(AIContext planContext, int maxTokens, CancellationToken ct);
    }
    
    /// <summary>
    /// Combined interface for providers supporting extended features.
    /// </summary>
    public interface IExtendedAIProvider : IAIProvider, IStreamingInferenceProvider, IPlanningProvider
    {
    }
    
    /// <summary>
    /// Extension methods for working with provider capabilities.
    /// </summary>
    public static class ProviderExtensions
    {
        /// <summary>
        /// Checks if a provider supports streaming.
        /// </summary>
        public static bool CanStream(this IAIProvider provider)
        {
            return provider is IStreamingInferenceProvider streaming && streaming.SupportsStreaming;
        }
        
        /// <summary>
        /// Checks if a provider supports planning.
        /// </summary>
        public static bool CanPlan(this IAIProvider provider)
        {
            return provider is IPlanningProvider planning && planning.SupportsPlanning;
        }
        
        /// <summary>
        /// Attempts to generate with streaming, falls back to non-streaming if not supported.
        /// </summary>
        public static async Task<TurnResult> GenerateWithStreamingAsync(
            this IAIProvider provider,
            AIContext context,
            Func<string, bool> onToken,
            CancellationToken ct)
        {
            if (provider is IStreamingInferenceProvider streaming && streaming.SupportsStreaming)
            {
                return await streaming.GenerateStreamAsync(context, onToken, ct).ConfigureAwait(false);
            }
            
            // Fallback: generate non-streaming, then emulate streaming
            TurnResult result = await provider.GenerateTurnAsync(context, ct).ConfigureAwait(false);
            
            if (result != null && !string.IsNullOrEmpty(result.npcLine) && onToken != null)
            {
                // Emulate streaming by chunking the response
                EmulateStreaming(result.npcLine, onToken);
            }
            
            return result;
        }
        
        /// <summary>
        /// Emulates streaming by chunking a complete response.
        /// </summary>
        private static void EmulateStreaming(string text, Func<string, bool> onToken)
        {
            if (string.IsNullOrEmpty(text) || onToken == null)
            {
                return;
            }
            
            // Chunk by words for natural feel
            int chunkSize = 3; // words per chunk
            string[] words = text.Split(' ');
            
            for (int i = 0; i < words.Length; i += chunkSize)
            {
                int count = Math.Min(chunkSize, words.Length - i);
                string chunk = string.Join(" ", words, i, count);
                
                if (i + count < words.Length)
                {
                    chunk += " ";
                }
                
                if (!onToken(chunk))
                {
                    break; // Cancelled
                }
            }
        }
    }
}
