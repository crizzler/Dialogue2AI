using System;
using UnityEngine;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Manages the lifecycle of the LocalLlamaEngine to support domain reload persistence.
    /// 
    /// When "Reload Domain" is disabled in Enter Play Mode Settings, static fields persist
    /// between play sessions. This class ensures the engine is properly reused or cleaned up.
    /// 
    /// Benefits of disabling domain reload:
    /// - Model stays loaded between play sessions (faster iteration)
    /// - CUDA context remains stable (no memory leaks)
    /// - Proper cleanup is possible without crashes
    /// </summary>
    public static class LocalLlamaEngineManager
    {
        private static LocalLlamaEngine sharedEngine;
        private static AIConversationSettings cachedSettings;
        private static string cachedModelPath;
        private static string cachedModelFolder;
        private static int cachedGpuLayers;
        private static int cachedContextSize;
        private static bool engineInitializedThisSession;
        
        /// <summary>
        /// Gets or creates a shared LocalLlamaEngine instance.
        /// If an engine exists from a previous play session (domain reload disabled),
        /// it will be reused if the settings match.
        /// </summary>
        public static LocalLlamaEngine GetOrCreateEngine(AIConversationSettings settings)
        {
            if (settings == null)
            {
                AILogger.Warn("LocalLlamaEngineManager: Cannot create engine without settings");
                return null;
            }
            
            // Check if we have a valid existing engine with matching settings
            if (sharedEngine != null && !sharedEngine.IsDisposed)
            {
                // Reuse existing engine if settings haven't changed significantly
                if (SettingsMatch(settings))
                {
                    if (!engineInitializedThisSession)
                    {
                        AILogger.Log("LocalLlamaEngineManager: Reusing engine from previous session (domain reload disabled)");
                        engineInitializedThisSession = true;
                    }
                    return sharedEngine;
                }
                
                // Settings changed, need to dispose and recreate
                AILogger.Log("LocalLlamaEngineManager: Settings changed, recreating engine");
                DisposeEngine();
            }
            
            // Create new engine and cache settings
            sharedEngine = new LocalLlamaEngine(settings);
            cachedSettings = settings;
            cachedModelPath = settings.selectedLocalModel;
            cachedModelFolder = settings.localModelFolder;
            cachedGpuLayers = settings.localInProcessGpuLayers;
            cachedContextSize = settings.localInProcessContextSize;
            engineInitializedThisSession = true;
            
            AILogger.Log("LocalLlamaEngineManager: Created new engine instance");
            return sharedEngine;
        }
        
        /// <summary>
        /// Disposes the shared engine if one exists.
        /// Safe to call multiple times.
        /// </summary>
        public static void DisposeEngine()
        {
            if (sharedEngine != null)
            {
                AILogger.Log("LocalLlamaEngineManager: Disposing engine");
                sharedEngine.Dispose();
                sharedEngine = null;
            }
            cachedSettings = null;
            cachedModelPath = null;
            cachedModelFolder = null;
        }
        
        /// <summary>
        /// Returns true if an engine exists and is ready.
        /// </summary>
        public static bool HasReadyEngine => sharedEngine != null && !sharedEngine.IsDisposed && sharedEngine.IsReady;
        
        /// <summary>
        /// Returns the shared engine's status, or a default message if no engine exists.
        /// </summary>
        public static string EngineStatus => sharedEngine?.Status ?? "No engine";
        
        /// <summary>
        /// Returns the shared engine's loading state.
        /// </summary>
        public static LocalEngineLoadingState LoadingState => 
            sharedEngine?.LoadingState ?? LocalEngineLoadingState.NotInitialized;
        
        /// <summary>
        /// Called automatically when domain reloads (if enabled) or when entering play mode.
        /// Resets the session-specific flag so we know this is a fresh session.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            // This is called on domain reload or when entering play mode
            // If domain reload is disabled, the static fields persist but we want to know
            // that we've entered a new play session
            engineInitializedThisSession = false;
            
            // If domain reload IS happening, our engine reference will become invalid
            // but the native resources may still be held - this is the crash scenario
            // With domain reload disabled, the engine stays valid
            
#if UNITY_EDITOR
            // Check if domain reload is enabled - if so, we need to cleanup aggressively
            if (UnityEditor.EditorSettings.enterPlayModeOptionsEnabled &&
                !UnityEditor.EditorSettings.enterPlayModeOptions.HasFlag(UnityEditor.EnterPlayModeOptions.DisableDomainReload))
            {
                // Domain reload is NOT disabled, so this method is being called during reload
                // The engine reference may become invalid after reload completes
                // Don't dispose here - it's too late and causes crashes
            }
#endif
        }
        
        /// <summary>
        /// Called when exiting play mode. With domain reload disabled, this is our
        /// opportunity to properly clean up if needed.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            // Nothing special needed here, but hook is available for future use
        }
        
        private static bool SettingsMatch(AIConversationSettings newSettings)
        {
            if (cachedSettings == null)
            {
                return false;
            }
            
            // Check critical settings that would require engine recreation
            return newSettings.selectedLocalModel == cachedModelPath &&
                   newSettings.localModelFolder == cachedModelFolder &&
                   newSettings.localInProcessGpuLayers == cachedGpuLayers &&
                   newSettings.localInProcessContextSize == cachedContextSize;
        }
    }
}
