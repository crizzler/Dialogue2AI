using System;

namespace ImmersiveNPCs
{
    [Serializable]
    public struct ProviderMetadata
    {
        public string providerName;
        public long latencyMs;
        public int promptTokens;
        public int completionTokens;
        public string modelName;
        public bool fromCache;
    }
}
