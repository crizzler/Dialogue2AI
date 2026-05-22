using System;
using System.Collections.Generic;

namespace ImmersiveNPCs
{
    [Serializable]
    public class TurnResult
    {
        public string npcLine;
        public List<string> options = new List<string>();
        public string mood;
        public string memoryDelta;
        public ProviderMetadata metadata;
        public bool isFallback;

        public TurnResult Clone()
        {
            return new TurnResult
            {
                npcLine = npcLine,
                options = new List<string>(options),
                mood = mood,
                memoryDelta = memoryDelta,
                metadata = metadata,
                isFallback = isFallback
            };
        }
    }
}
