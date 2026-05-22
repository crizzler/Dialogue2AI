using System.IO;

namespace ImmersiveNPCs
{
    public static class ProviderFactory
    {
        public static IAIProvider CreateProvider(AIConversationSettings settings, ILocalInferenceEngine localEngine)
        {
            if (settings == null)
            {
                return null;
            }

            IAIProvider localProvider = CreateLocalProvider(settings, localEngine);

            IAIProvider cloudProvider = new CloudLLMProvider(settings);

            switch (settings.providerMode)
            {
                case ProviderMode.Local:
                    return localProvider;
                case ProviderMode.Cloud:
                    return cloudProvider;
                case ProviderMode.Race:
                    return new RaceProvider(localProvider, cloudProvider);
                default:
                    return cloudProvider;
            }
        }

        private static IAIProvider CreateLocalProvider(AIConversationSettings settings, ILocalInferenceEngine localEngine)
        {
            switch (settings.localBackend)
            {
                case LocalBackendMode.Ollama:
                    return new OllamaProvider(settings);
                case LocalBackendMode.OpenAICompatible:
                    return new OpenAICompatibleLocalProvider(settings);
                case LocalBackendMode.InProcess:
                case LocalBackendMode.Sentis:
                case LocalBackendMode.Placeholder:
                default:
                    if (localEngine == null)
                    {
                        return null;
                    }
                    string modelPath = ResolveLocalModelPath(settings);
                    return new LocalLLMProvider(settings, localEngine, modelPath);
            }
        }

        public static string ResolveLocalModelPath(AIConversationSettings settings)
        {
            if (settings == null)
            {
                return string.Empty;
            }

            string folder = PathUtility.ResolveProjectPath(settings.localModelFolder);
            if (string.IsNullOrEmpty(settings.selectedLocalModel))
            {
                return string.Empty;
            }

            string relative = settings.selectedLocalModel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(folder, relative);
        }
    }
}
