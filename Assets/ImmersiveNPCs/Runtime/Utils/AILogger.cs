using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ImmersiveNPCs
{
    public static class AILogger
    {
        public static bool Verbose { get; set; }
        
        /// <summary>
        /// Enable detailed dialogue conversation logging (always logs regardless of Verbose setting).
        /// </summary>
        public static bool LogDialogue { get; set; } = true;
        
        private static int turnNumber = 0;

        public static void Log(string message, Object context = null)
        {
            if (!Verbose) return;
            Debug.Log("[ImmersiveNPCs] " + message, context);
        }

        public static void Warn(string message, Object context = null)
        {
            Debug.LogWarning("[ImmersiveNPCs] " + message, context);
        }

        public static void Error(string message, Object context = null)
        {
            Debug.LogError("[ImmersiveNPCs] " + message, context);
        }
        
        /// <summary>
        /// Logs a complete dialogue turn for easy debugging.
        /// </summary>
        public static void LogDialogueTurn(string npcId, string npcLine, List<string> options, string playerChoice = null, bool isNewTurn = true)
        {
            if (!LogDialogue) return;
            
            if (isNewTurn)
            {
                turnNumber++;
            }
            
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("╔════════════════════════════════════════════════════════════");
            sb.AppendLine($"║ DIALOGUE TURN #{turnNumber} | NPC: {npcId}");
            sb.AppendLine("╠════════════════════════════════════════════════════════════");
            sb.AppendLine($"║ NPC: \"{npcLine}\"");
            sb.AppendLine("║");
            sb.AppendLine("║ OPTIONS:");
            if (options != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    sb.AppendLine($"║   [{i}] {options[i]}");
                }
            }
            if (!string.IsNullOrEmpty(playerChoice))
            {
                sb.AppendLine("║");
                sb.AppendLine($"║ >>> PLAYER CHOSE: \"{playerChoice}\"");
            }
            sb.AppendLine("╚════════════════════════════════════════════════════════════");
            
            Debug.Log("[ImmersiveNPCs-Dialogue]" + sb.ToString());
        }
        
        /// <summary>
        /// Logs when the player makes a choice.
        /// </summary>
        public static void LogPlayerChoice(string npcId, int slotIndex, string choiceText)
        {
            if (!LogDialogue) return;
            
            Debug.Log($"[ImmersiveNPCs-Dialogue] >>> PLAYER SELECTED [{slotIndex}]: \"{choiceText}\" (NPC: {npcId})");
        }
        
        /// <summary>
        /// Resets the turn counter (call when starting a new conversation).
        /// </summary>
        public static void ResetDialogueLog()
        {
            turnNumber = 0;
            if (LogDialogue)
            {
                Debug.Log("[ImmersiveNPCs-Dialogue] ========== NEW CONVERSATION STARTED ==========");
            }
        }
    }
}
