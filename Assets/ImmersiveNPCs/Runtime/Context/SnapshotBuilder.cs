using System.Collections.Generic;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Builds world state snapshots from various game sources.
    /// Base implementation with hooks for adapter-specific population.
    /// </summary>
    public class SnapshotBuilder
    {
        private WorldStateSnapshot current;
        
        /// <summary>
        /// Starts building a new snapshot.
        /// </summary>
        public SnapshotBuilder Begin()
        {
            current = new WorldStateSnapshot();
            return this;
        }
        
        /// <summary>
        /// Gets the built snapshot.
        /// </summary>
        public WorldStateSnapshot Build()
        {
            return current ?? new WorldStateSnapshot();
        }
        
        // === Scene Facts ===
        
        public SnapshotBuilder SetLocation(string location)
        {
            if (current != null && !string.IsNullOrEmpty(location))
            {
                current.currentLocation = location;
            }
            return this;
        }
        
        public SnapshotBuilder SetTimeOfDay(string time)
        {
            if (current != null && !string.IsNullOrEmpty(time))
            {
                current.timeOfDay = time;
            }
            return this;
        }
        
        public SnapshotBuilder SetEnvironment(string env)
        {
            if (current != null && !string.IsNullOrEmpty(env))
            {
                current.environment = env;
            }
            return this;
        }
        
        public SnapshotBuilder SetEmotionalTone(string tone)
        {
            if (current != null && !string.IsNullOrEmpty(tone))
            {
                current.emotionalTone = tone;
            }
            return this;
        }
        
        public SnapshotBuilder AddParticipant(string npcName)
        {
            if (current != null && !string.IsNullOrEmpty(npcName))
            {
                current.sceneParticipants.Add(npcName);
            }
            return this;
        }
        
        public SnapshotBuilder SetParticipants(IEnumerable<string> participants)
        {
            if (current != null && participants != null)
            {
                current.sceneParticipants.Clear();
                foreach (var p in participants)
                {
                    if (!string.IsNullOrEmpty(p))
                    {
                        current.sceneParticipants.Add(p);
                    }
                }
            }
            return this;
        }
        
        // === Quest State ===
        
        public SnapshotBuilder SetActiveQuest(string questId)
        {
            if (current != null)
            {
                current.activeQuestId = questId ?? string.Empty;
            }
            return this;
        }
        
        public SnapshotBuilder SetQuestBeat(string beatId)
        {
            if (current != null)
            {
                current.currentQuestBeat = beatId ?? string.Empty;
            }
            return this;
        }
        
        public SnapshotBuilder AddFlag(string flag)
        {
            if (current != null && !string.IsNullOrEmpty(flag))
            {
                current.activeFlags.Add(flag);
            }
            return this;
        }
        
        public SnapshotBuilder SetFlags(IEnumerable<string> flags)
        {
            if (current != null && flags != null)
            {
                current.activeFlags.Clear();
                foreach (var f in flags)
                {
                    if (!string.IsNullOrEmpty(f))
                    {
                        current.activeFlags.Add(f);
                    }
                }
            }
            return this;
        }
        
        public SnapshotBuilder AddCompletedStage(string stage)
        {
            if (current != null && !string.IsNullOrEmpty(stage))
            {
                current.completedStages.Add(stage);
            }
            return this;
        }
        
        // === Inventory ===
        
        public SnapshotBuilder AddPlayerItem(string item)
        {
            if (current != null && !string.IsNullOrEmpty(item))
            {
                current.playerInventory.Add(item);
            }
            return this;
        }
        
        public SnapshotBuilder SetPlayerInventory(IEnumerable<string> items)
        {
            if (current != null && items != null)
            {
                current.playerInventory.Clear();
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item))
                    {
                        current.playerInventory.Add(item);
                    }
                }
            }
            return this;
        }
        
        public SnapshotBuilder AddNpcItem(string item)
        {
            if (current != null && !string.IsNullOrEmpty(item))
            {
                current.npcInventory.Add(item);
            }
            return this;
        }
        
        // === Valid Entities ===
        
        public SnapshotBuilder AddValidLocation(string location)
        {
            if (current != null && !string.IsNullOrEmpty(location))
            {
                current.validLocations.Add(location);
            }
            return this;
        }
        
        public SnapshotBuilder SetValidLocations(IEnumerable<string> locations)
        {
            if (current != null && locations != null)
            {
                current.validLocations.Clear();
                foreach (var loc in locations)
                {
                    if (!string.IsNullOrEmpty(loc))
                    {
                        current.validLocations.Add(loc);
                    }
                }
            }
            return this;
        }
        
        public SnapshotBuilder AddKnownNpc(string npcName)
        {
            if (current != null && !string.IsNullOrEmpty(npcName))
            {
                current.knownNpcs.Add(npcName);
            }
            return this;
        }
        
        public SnapshotBuilder SetKnownNpcs(IEnumerable<string> npcs)
        {
            if (current != null && npcs != null)
            {
                current.knownNpcs.Clear();
                foreach (var npc in npcs)
                {
                    if (!string.IsNullOrEmpty(npc))
                    {
                        current.knownNpcs.Add(npc);
                    }
                }
            }
            return this;
        }
        
        public SnapshotBuilder AddValidItem(string item)
        {
            if (current != null && !string.IsNullOrEmpty(item))
            {
                current.validItems.Add(item);
            }
            return this;
        }
        
        // === Relationships ===
        
        public SnapshotBuilder SetRelationship(int level, string tier = null)
        {
            if (current != null)
            {
                current.relationshipLevel = level;
                if (!string.IsNullOrEmpty(tier))
                {
                    current.relationshipTier = tier;
                }
                else
                {
                    current.relationshipTier = ResolveRelationshipTier(level);
                }
            }
            return this;
        }
        
        // === Script Authority ===
        
        public SnapshotBuilder SetScriptedBeat(bool isScripted)
        {
            if (current != null)
            {
                current.isScriptedBeat = isScripted;
            }
            return this;
        }
        
        public SnapshotBuilder SetAwaitingScriptedResponse(bool awaiting)
        {
            if (current != null)
            {
                current.awaitingScriptedResponse = awaiting;
            }
            return this;
        }
        
        public SnapshotBuilder SetRequiredNextBeat(string beatId)
        {
            if (current != null)
            {
                current.requiredNextBeat = beatId ?? string.Empty;
            }
            return this;
        }
        
        // === Yarn Adapter Hooks ===
        
        public SnapshotBuilder SetYarnNode(string nodeName)
        {
            if (current != null)
            {
                current.currentYarnNode = nodeName ?? string.Empty;
            }
            return this;
        }
        
        public SnapshotBuilder AddYarnNodeTag(string tag)
        {
            if (current != null && !string.IsNullOrEmpty(tag))
            {
                current.yarnNodeTags.Add(tag);
            }
            return this;
        }
        
        public SnapshotBuilder SetYarnNodeTags(IEnumerable<string> tags)
        {
            if (current != null && tags != null)
            {
                current.yarnNodeTags.Clear();
                foreach (var tag in tags)
                {
                    if (!string.IsNullOrEmpty(tag))
                    {
                        current.yarnNodeTags.Add(tag);
                    }
                }
            }
            return this;
        }
        
        // === Vendor State ===
        
        public SnapshotBuilder SetVendorMode(bool isVendor)
        {
            if (current != null)
            {
                current.isVendorMode = isVendor;
            }
            return this;
        }
        
        public SnapshotBuilder SetPlayerCurrency(int amount)
        {
            if (current != null)
            {
                current.playerCurrency = amount;
            }
            return this;
        }
        
        // === Combat State ===
        
        public SnapshotBuilder SetCombat(bool inCombat)
        {
            if (current != null)
            {
                current.inCombat = inCombat;
            }
            return this;
        }
        
        public SnapshotBuilder SetPlayerHealth(int percent)
        {
            if (current != null)
            {
                current.playerHealthPercent = percent;
            }
            return this;
        }
        
        public SnapshotBuilder SetNpcHealth(int percent)
        {
            if (current != null)
            {
                current.npcHealthPercent = percent;
            }
            return this;
        }
        
        private static string ResolveRelationshipTier(int level)
        {
            if (level <= -50) return "hostile";
            if (level <= -10) return "unfriendly";
            if (level < 10) return "neutral";
            if (level < 50) return "friendly";
            return "trusted";
        }
    }
}
