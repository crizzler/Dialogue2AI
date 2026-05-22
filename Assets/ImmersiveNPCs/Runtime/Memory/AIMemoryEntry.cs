using System;

namespace ImmersiveNPCs
{
    public sealed class AIMemoryEntry
    {
        public string id;
        public string text;
        public string npcId;
        public bool isGlobal;
        public MemorySourceType source;
        public float importance;
        public DateTime timestampUtc;
        public float[] embedding;
    }
}
