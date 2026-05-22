using System;

namespace ImmersiveNPCs
{
    [Serializable]
    public class AICacheEntry
    {
        public long createdUtcTicks;
        public TurnResult result;
    }
}
