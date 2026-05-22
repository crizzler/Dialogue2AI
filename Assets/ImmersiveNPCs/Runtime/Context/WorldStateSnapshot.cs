using System;
using System.Collections.Generic;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Immutable snapshot of the current world state for grounding NPC responses.
    /// Populated by game adapters (Yarn, GC2) before each generation.
    /// </summary>
    public sealed class WorldStateSnapshot
    {
        // === Scene Facts (Tier A) ===
        
        /// <summary>Current scene or location identifier.</summary>
        public string currentLocation = string.Empty;
        
        /// <summary>Time of day or game time context.</summary>
        public string timeOfDay = string.Empty;
        
        /// <summary>Weather or environmental conditions.</summary>
        public string environment = string.Empty;
        
        /// <summary>Active quest ID if any.</summary>
        public string activeQuestId = string.Empty;
        
        /// <summary>Current quest beat/objective identifier.</summary>
        public string currentQuestBeat = string.Empty;
        
        /// <summary>Participant NPCs in the current scene.</summary>
        public List<string> sceneParticipants = new List<string>();
        
        /// <summary>Current emotional tone of the scene (tense, friendly, neutral, etc.).</summary>
        public string emotionalTone = string.Empty;
        
        // === Quest/Story Flags ===
        
        /// <summary>Set of quest flags that are currently true.</summary>
        public HashSet<string> activeFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>Set of completed quest stages.</summary>
        public HashSet<string> completedStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // === Inventory/Items ===
        
        /// <summary>Items the player currently has.</summary>
        public HashSet<string> playerInventory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>Items the NPC can offer for trade.</summary>
        public HashSet<string> npcInventory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // === Entities (valid names the model can reference) ===
        
        /// <summary>Valid location names in the current area.</summary>
        public HashSet<string> validLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>Valid NPC names the player knows about.</summary>
        public HashSet<string> knownNpcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>Valid item names that exist in the game world.</summary>
        public HashSet<string> validItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // === Relationship State ===
        
        /// <summary>Player's relationship level with the NPC (-100 to 100).</summary>
        public int relationshipLevel;
        
        /// <summary>Relationship tier name (hostile, neutral, friendly, trusted).</summary>
        public string relationshipTier = "neutral";
        
        // === Script Authority ===
        
        /// <summary>If true, the current dialogue beat is scripted and must not be overridden.</summary>
        public bool isScriptedBeat;
        
        /// <summary>If true, we're awaiting a specific command or node in Yarn.</summary>
        public bool awaitingScriptedResponse;
        
        /// <summary>The required next beat if script authority is active.</summary>
        public string requiredNextBeat = string.Empty;
        
        /// <summary>Current Yarn node name (for adapters).</summary>
        public string currentYarnNode = string.Empty;
        
        /// <summary>Tags on the current Yarn node.</summary>
        public HashSet<string> yarnNodeTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // === Vendor State ===
        
        /// <summary>True if NPC is a vendor and shop is open.</summary>
        public bool isVendorMode;
        
        /// <summary>Player's current gold/currency amount.</summary>
        public int playerCurrency;
        
        // === Combat State ===
        
        /// <summary>True if combat is active.</summary>
        public bool inCombat;
        
        /// <summary>Player's health percentage (0-100).</summary>
        public int playerHealthPercent = 100;
        
        /// <summary>NPC's health percentage if applicable.</summary>
        public int npcHealthPercent = 100;
        
        /// <summary>Timestamp when snapshot was created.</summary>
        public DateTime createdUtc = DateTime.UtcNow;
        
        /// <summary>
        /// Checks if a quest flag is active.
        /// </summary>
        public bool HasFlag(string flag)
        {
            return !string.IsNullOrEmpty(flag) && activeFlags.Contains(flag);
        }
        
        /// <summary>
        /// Checks if a quest stage is completed.
        /// </summary>
        public bool IsStageComplete(string stage)
        {
            return !string.IsNullOrEmpty(stage) && completedStages.Contains(stage);
        }
        
        /// <summary>
        /// Checks if the player has an item.
        /// </summary>
        public bool PlayerHasItem(string item)
        {
            return !string.IsNullOrEmpty(item) && playerInventory.Contains(item);
        }
        
        /// <summary>
        /// Checks if a location name is valid.
        /// </summary>
        public bool IsValidLocation(string location)
        {
            return !string.IsNullOrEmpty(location) && validLocations.Contains(location);
        }
        
        /// <summary>
        /// Checks if an NPC name is known to the player.
        /// </summary>
        public bool IsKnownNpc(string npcName)
        {
            return !string.IsNullOrEmpty(npcName) && knownNpcs.Contains(npcName);
        }
        
        /// <summary>
        /// Creates a shallow copy of the snapshot.
        /// </summary>
        public WorldStateSnapshot Clone()
        {
            return new WorldStateSnapshot
            {
                currentLocation = currentLocation,
                timeOfDay = timeOfDay,
                environment = environment,
                activeQuestId = activeQuestId,
                currentQuestBeat = currentQuestBeat,
                sceneParticipants = new List<string>(sceneParticipants),
                emotionalTone = emotionalTone,
                activeFlags = new HashSet<string>(activeFlags, StringComparer.OrdinalIgnoreCase),
                completedStages = new HashSet<string>(completedStages, StringComparer.OrdinalIgnoreCase),
                playerInventory = new HashSet<string>(playerInventory, StringComparer.OrdinalIgnoreCase),
                npcInventory = new HashSet<string>(npcInventory, StringComparer.OrdinalIgnoreCase),
                validLocations = new HashSet<string>(validLocations, StringComparer.OrdinalIgnoreCase),
                knownNpcs = new HashSet<string>(knownNpcs, StringComparer.OrdinalIgnoreCase),
                validItems = new HashSet<string>(validItems, StringComparer.OrdinalIgnoreCase),
                relationshipLevel = relationshipLevel,
                relationshipTier = relationshipTier,
                isScriptedBeat = isScriptedBeat,
                awaitingScriptedResponse = awaitingScriptedResponse,
                requiredNextBeat = requiredNextBeat,
                currentYarnNode = currentYarnNode,
                yarnNodeTags = new HashSet<string>(yarnNodeTags, StringComparer.OrdinalIgnoreCase),
                isVendorMode = isVendorMode,
                playerCurrency = playerCurrency,
                inCombat = inCombat,
                playerHealthPercent = playerHealthPercent,
                npcHealthPercent = npcHealthPercent,
                createdUtc = createdUtc
            };
        }
    }
}
