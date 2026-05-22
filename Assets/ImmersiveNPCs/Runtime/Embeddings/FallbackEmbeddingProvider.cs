using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public sealed class FallbackEmbeddingProvider : IEmbeddingProvider
    {
        private readonly IEmbeddingProvider primary;
        private readonly IEmbeddingProvider fallback;

        public FallbackEmbeddingProvider(IEmbeddingProvider primary, IEmbeddingProvider fallback)
        {
            this.primary = primary;
            this.fallback = fallback;
        }

        public bool IsAvailable => (primary != null && primary.IsAvailable) || (fallback != null && fallback.IsAvailable);

        public string Status
        {
            get
            {
                if (primary != null && primary.IsAvailable)
                {
                    return primary.Status;
                }

                if (fallback != null && fallback.IsAvailable)
                {
                    return fallback.Status;
                }

                return "No embedding provider available";
            }
        }

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            if (primary != null && primary.IsAvailable)
            {
                float[] result = await primary.EmbedAsync(text, ct).ConfigureAwait(false);
                if (result != null && result.Length > 0)
                {
                    return result;
                }
            }

            if (fallback != null && fallback.IsAvailable)
            {
                return await fallback.EmbedAsync(text, ct).ConfigureAwait(false);
            }

            return null;
        }
    }
}
