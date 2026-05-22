using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Main static API for interacting with Immersive NPCs system.
    /// Initialize once at game start, access from anywhere.
    /// </summary>
    public static class ImmersiveNpcsRuntime
    {
        // === Core References ===
        private static AIConversationSettings settings;
        private static NpcProfileDatabase profileDatabase;
        private static GlobalWorldState worldState;
        private static NpcStateRegistry npcStates;
        private static AIConversationService conversationService;
        private static MemoryStore globalMemoryStore;
        
        private static bool isInitialized;
        private static readonly object initLock = new object();
        
        // === Public Accessors ===
        
        /// <summary>Current AI settings asset</summary>
        public static AIConversationSettings Settings => settings;
        
        /// <summary>Database of all NPC profiles</summary>
        public static NpcProfileDatabase Profiles => profileDatabase;
        
        /// <summary>Global world state (time, weather, factions, etc.)</summary>
        public static GlobalWorldState World => worldState;
        
        /// <summary>Registry of all NPC runtime states</summary>
        public static NpcStateRegistry NpcStates => npcStates;
        
        /// <summary>Main conversation service for dialogue generation</summary>
        public static AIConversationService Conversation => conversationService;
        
        /// <summary>Global memory store for cross-NPC memories</summary>
        public static MemoryStore Memory => globalMemoryStore;
        
        /// <summary>Whether the system has been initialized</summary>
        public static bool IsInitialized => isInitialized;
        
        // === Events ===
        
        /// <summary>Fired when system is initialized</summary>
        public static event Action OnInitialized;
        
        /// <summary>Fired when system is shut down</summary>
        public static event Action OnShutdown;
        
        /// <summary>Fired when world state changes (time, weather, location)</summary>
        public static event Action<WorldStateChangeArgs> OnWorldStateChanged;
        
        /// <summary>Fired when any NPC state changes (mood, trust, etc.)</summary>
        public static event Action<NpcStateChangeArgs> OnNpcStateChanged;
        
        /// <summary>Fired when a memory is written</summary>
        public static event Action<MemoryWriteArgs> OnMemoryWritten;
        
        /// <summary>Fired when dialogue generation completes</summary>
        public static event Action<DialogueGenerationArgs> OnGenerationCompleted;
        
        /// <summary>Fired on generation error</summary>
        public static event Action<DialogueErrorArgs> OnGenerationError;
        
        // === Initialization ===
        
        /// <summary>
        /// Initialize the runtime system. Call once at game start.
        /// </summary>
        public static void Initialize(
            AIConversationSettings settingsAsset,
            NpcProfileDatabase profileDb = null,
            GlobalWorldState worldStateAsset = null,
            ILocalInferenceEngine localEngine = null)
        {
            lock (initLock)
            {
                if (isInitialized)
                {
                    AILogger.Warn("[ImmersiveNpcsRuntime] Already initialized. Call Shutdown() first to reinitialize.");
                    return;
                }
                
                settings = settingsAsset ?? throw new ArgumentNullException(nameof(settingsAsset));
                profileDatabase = profileDb;
                worldState = worldStateAsset;
                
                // Create state registry
                npcStates = new NpcStateRegistry(profileDatabase);
                
                // Create memory store
                globalMemoryStore = new MemoryStore();
                
                // Create conversation service
                conversationService = new AIConversationService(settings, profileDb, localEngine);
                
                isInitialized = true;
                AILogger.Log("[ImmersiveNpcsRuntime] Initialized successfully.");
                
                OnInitialized?.Invoke();
            }
        }
        
        /// <summary>
        /// Auto-initialize from AIConversationManager
        /// </summary>
        internal static void InitializeFromManager(
            AIConversationSettings managerSettings,
            NpcProfileDatabase profileDb,
            GlobalWorldState world,
            AIConversationService service)
        {
            lock (initLock)
            {
                if (isInitialized) return;
                
                settings = managerSettings;
                profileDatabase = profileDb;
                worldState = world;
                conversationService = service;
                npcStates = new NpcStateRegistry(profileDatabase);
                globalMemoryStore = new MemoryStore();
                
                isInitialized = true;
                AILogger.Log("[ImmersiveNpcsRuntime] Initialized from AIConversationManager.");
                
                OnInitialized?.Invoke();
            }
        }
        
        /// <summary>
        /// Shutdown and cleanup all resources
        /// </summary>
        public static void Shutdown()
        {
            lock (initLock)
            {
                if (!isInitialized) return;
                
                OnShutdown?.Invoke();
                
                conversationService = null;
                npcStates = null;
                globalMemoryStore = null;
                settings = null;
                profileDatabase = null;
                worldState = null;
                
                isInitialized = false;
                AILogger.Log("[ImmersiveNpcsRuntime] Shut down.");
            }
        }
        
        // === Dialogue API ===
        
        /// <summary>
        /// Generate a dialogue response for an NPC
        /// </summary>
        public static async Task<TurnResult> GenerateDialogueAsync(
            string npcId,
            string playerChoice = null,
            int? slotsOverride = null,
            CancellationToken ct = default)
        {
            EnsureInitialized();
            
            try
            {
                var state = npcStates.GetOrCreate(npcId);
                
                // Store player choice
                if (!string.IsNullOrEmpty(playerChoice))
                {
                    state.LastPlayerAction = playerChoice;
                }
                
                int slots = slotsOverride ?? settings.slotsCount;
                
                // Use the standard API - GenerateTieredAsync is internal to the service
                TurnResult result = await conversationService.GetOrGenerateAsync(
                    npcId, slots, settings.language, PerceptionSnapshot.Empty(), ct);
                
                // Update state
                state.LastInteraction = DateTime.UtcNow;
                state.InteractionCount++;
                if (result?.npcLine != null)
                {
                    state.LastNpcResponse = result.npcLine;
                }
                
                OnGenerationCompleted?.Invoke(new DialogueGenerationArgs
                {
                    NpcId = npcId,
                    PlayerChoice = playerChoice,
                    Result = result,
                    LatencyMs = result?.metadata.latencyMs ?? 0
                });
                
                return result;
            }
            catch (Exception ex)
            {
                OnGenerationError?.Invoke(new DialogueErrorArgs 
                { 
                    NpcId = npcId, 
                    Error = ex,
                    Message = ex.Message 
                });
                throw;
            }
        }
        
        // === NPC State API ===
        
        /// <summary>Get runtime state for an NPC</summary>
        public static NpcStateStore GetNpcState(string npcId)
        {
            EnsureInitialized();
            return npcStates.GetOrCreate(npcId);
        }
        
        /// <summary>Modify NPC trust level</summary>
        public static void ModifyTrust(string npcId, float delta)
        {
            var state = GetNpcState(npcId);
            float oldValue = state.TrustLevel;
            state.ModifyTrust(delta);
            
            OnNpcStateChanged?.Invoke(new NpcStateChangeArgs
            {
                NpcId = npcId,
                Property = "TrustLevel",
                OldValue = oldValue,
                NewValue = state.TrustLevel
            });
        }
        
        /// <summary>Set NPC mood</summary>
        public static void SetMood(string npcId, NpcMood mood)
        {
            var state = GetNpcState(npcId);
            var oldMood = state.CurrentMood;
            state.CurrentMood = mood;
            
            OnNpcStateChanged?.Invoke(new NpcStateChangeArgs
            {
                NpcId = npcId,
                Property = "CurrentMood",
                OldValue = oldMood,
                NewValue = mood
            });
        }
        
        /// <summary>Add a topic to NPC's recent memory</summary>
        public static void AddTopic(string npcId, string topic)
        {
            GetNpcState(npcId).AddTopic(topic);
        }
        
        /// <summary>Set custom variable on NPC</summary>
        public static void SetNpcVar<T>(string npcId, string key, T value)
        {
            GetNpcState(npcId).SetVar(key, value);
        }
        
        /// <summary>Get custom variable from NPC</summary>
        public static T GetNpcVar<T>(string npcId, string key, T defaultValue = default)
        {
            return GetNpcState(npcId).GetVar(key, defaultValue);
        }
        
        // === World State API ===
        
        /// <summary>Set time of day</summary>
        public static void SetTimeOfDay(TimeOfDay time)
        {
            EnsureInitialized();
            if (worldState == null) return;
            
            var old = worldState.timeOfDay;
            worldState.timeOfDay = time;
            
            OnWorldStateChanged?.Invoke(new WorldStateChangeArgs
            {
                Property = "timeOfDay",
                OldValue = old,
                NewValue = time
            });
        }
        
        /// <summary>Set weather</summary>
        public static void SetWeather(Weather weather)
        {
            EnsureInitialized();
            if (worldState == null) return;
            
            var old = worldState.weather;
            worldState.weather = weather;
            
            OnWorldStateChanged?.Invoke(new WorldStateChangeArgs
            {
                Property = "weather",
                OldValue = old,
                NewValue = weather
            });
        }
        
        /// <summary>Set current location</summary>
        public static void SetLocation(string location)
        {
            EnsureInitialized();
            if (worldState == null) return;
            
            var old = worldState.currentLocation;
            worldState.currentLocation = location;
            
            OnWorldStateChanged?.Invoke(new WorldStateChangeArgs
            {
                Property = "currentLocation",
                OldValue = old,
                NewValue = location
            });
        }
        
        /// <summary>Set world fact</summary>
        public static void SetWorldFact(string key, string value)
        {
            EnsureInitialized();
            worldState?.SetFact(key, value);
        }
        
        /// <summary>Get world fact</summary>
        public static string GetWorldFact(string key)
        {
            EnsureInitialized();
            return worldState?.GetFact(key);
        }
        
        // === Memory API ===
        
        /// <summary>Write a memory event</summary>
        public static void WriteMemory(string npcId, string content, MemoryEventType type = MemoryEventType.Custom)
        {
            EnsureInitialized();
            
            var evt = new MemoryEvent
            {
                npcId = npcId,
                content = content,
                eventType = type,
                timestamp = DateTime.UtcNow
            };
            
            globalMemoryStore?.Write(evt);
            conversationService?.StructuredMemory?.Write(evt);
            
            OnMemoryWritten?.Invoke(new MemoryWriteArgs
            {
                NpcId = npcId,
                Event = evt
            });
        }
        
        /// <summary>Query memories for context</summary>
        public static MemoryEvent[] QueryMemories(string npcId, string query, int topK = 5)
        {
            EnsureInitialized();
            return globalMemoryStore?.Query(npcId, query, topK) ?? Array.Empty<MemoryEvent>();
        }
        
        // === Save/Load API ===
        
        /// <summary>
        /// Export all runtime state to serializable format
        /// </summary>
        public static RuntimeSaveData ExportSaveData()
        {
            EnsureInitialized();
            
            return new RuntimeSaveData
            {
                version = 1,
                timestamp = DateTime.UtcNow.ToString("o"),
                npcStates = npcStates.ToSaveData(),
                worldFacts = worldState?.customFacts != null 
                    ? new List<WorldFact>(worldState.customFacts)
                    : new List<WorldFact>(),
                memories = globalMemoryStore?.ExportAll() 
                    ?? new List<MemoryEvent>()
            };
        }
        
        /// <summary>
        /// Import runtime state from save data
        /// </summary>
        public static void ImportSaveData(RuntimeSaveData data)
        {
            EnsureInitialized();
            
            if (data == null) return;
            
            // Restore NPC states
            if (data.npcStates != null)
            {
                npcStates.FromSaveData(data.npcStates);
            }
            
            // Restore world facts
            if (data.worldFacts != null && worldState != null)
            {
                worldState.customFacts.Clear();
                worldState.customFacts.AddRange(data.worldFacts);
            }
            
            // Restore memories
            if (data.memories != null)
            {
                globalMemoryStore?.ImportAll(data.memories);
            }
            
            AILogger.Log($"[ImmersiveNpcsRuntime] Imported save data (v{data.version}) from {data.timestamp}");
        }
        
        // === Helpers ===
        
        private static void EnsureInitialized()
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException(
                    "ImmersiveNpcsRuntime not initialized. Call Initialize() first or ensure AIConversationManager is in scene.");
            }
        }
    }
    
    // === Event Args Classes ===
    
    public class WorldStateChangeArgs
    {
        public string Property { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
    }
    
    public class NpcStateChangeArgs
    {
        public string NpcId { get; set; }
        public string Property { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
    }
    
    public class MemoryWriteArgs
    {
        public string NpcId { get; set; }
        public MemoryEvent Event { get; set; }
    }
    
    public class DialogueGenerationArgs
    {
        public string NpcId { get; set; }
        public string PlayerChoice { get; set; }
        public TurnResult Result { get; set; }
        public long LatencyMs { get; set; }
    }
    
    public class DialogueErrorArgs
    {
        public string NpcId { get; set; }
        public Exception Error { get; set; }
        public string Message { get; set; }
    }
    
    /// <summary>
    /// Serializable save data for all runtime state
    /// </summary>
    [Serializable]
    public class RuntimeSaveData
    {
        public int version;
        public string timestamp;
        public Dictionary<string, Dictionary<string, object>> npcStates;
        public List<WorldFact> worldFacts;
        public List<MemoryEvent> memories;
    }
}
