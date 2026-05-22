using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public sealed class LocalEmbeddingProvider : IEmbeddingProvider
    {
        private readonly AIConversationSettings settings;
        private readonly ILocalInferenceEngine localEngine;

        public LocalEmbeddingProvider(AIConversationSettings settings, ILocalInferenceEngine localEngine)
        {
            this.settings = settings;
            this.localEngine = localEngine;
        }

        public bool IsAvailable => localEngine != null && settings != null && settings.localBackend == LocalBackendMode.InProcess;

        public string Status
        {
            get
            {
                if (localEngine == null)
                {
                    return "Missing local engine";
                }

                if (settings == null || settings.localBackend != LocalBackendMode.InProcess)
                {
                    return "Local backend disabled";
                }

                return localEngine.Status;
            }
        }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            if (!IsAvailable)
            {
                return Task.FromResult<float[]>(null);
            }

            string modelPath = ProviderFactory.ResolveLocalModelPath(settings);
            if (string.IsNullOrEmpty(modelPath))
            {
                return Task.FromResult<float[]>(null);
            }

            LocalEmbeddingRequest request = new LocalEmbeddingRequest
            {
                text = text,
                modelPath = modelPath
            };
            return localEngine.EmbedAsync(request, ct);
        }
    }
}
