namespace ImmersiveNPCs
{
    public static class EmbeddingProviderFactory
    {
        public static IEmbeddingProvider Create(AIConversationSettings settings, ILocalInferenceEngine localEngine)
        {
            if (settings == null || !settings.enableMemory)
            {
                return null;
            }

            switch (settings.embeddingProviderMode)
            {
                case EmbeddingProviderMode.Local:
                    return new LocalEmbeddingProvider(settings, localEngine);
                case EmbeddingProviderMode.Cloud:
                    return new CloudEmbeddingProvider(settings);
                case EmbeddingProviderMode.Auto:
                default:
                    IEmbeddingProvider local = new LocalEmbeddingProvider(settings, localEngine);
                    IEmbeddingProvider cloud = new CloudEmbeddingProvider(settings);
                    return new FallbackEmbeddingProvider(local, cloud);
            }
        }
    }
}
