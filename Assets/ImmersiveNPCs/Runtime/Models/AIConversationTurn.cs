using System;

namespace ImmersiveNPCs
{
    [Serializable]
    public class AIConversationTurn
    {
        public string npcLine;
        public string playerChoice;
        public string mood;
        public DateTime timestampUtc;
    }
}
