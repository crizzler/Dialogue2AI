using System.Collections.Generic;

namespace ImmersiveNPCs
{
    public class AIContext
    {
        public string npcId;
        public int slots;
        public string language;
        public string systemPrompt;
        public string userPrompt;
        public string summary;
        public string lastPlayerChoice;
        public string memoryKey;
        public string entityContext;
        public List<AIConversationTurn> recentTurns = new List<AIConversationTurn>();
        public List<AIMemorySnippet> memorySnippets = new List<AIMemorySnippet>();
        public PerceptionSnapshot perception;
    }
}
