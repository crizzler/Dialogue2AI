using UnityEngine;

namespace ImmersiveNPCs
{
    public static class AISettingsLocator
    {
        private const string ResourcePath = "ImmersiveNPCs/AIConversationSettings";

        public static AIConversationSettings Load()
        {
            return Resources.Load<AIConversationSettings>(ResourcePath);
        }
    }
}
