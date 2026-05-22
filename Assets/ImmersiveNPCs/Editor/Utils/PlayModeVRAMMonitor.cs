#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ImmersiveNPCs.Editor
{
    /// <summary>
    /// Monitors play mode changes and provides helpful guidance for the local LLM backend.
    /// With proper configuration (Domain Reload disabled), the model persists between
    /// play sessions and no VRAM issues occur.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeVRAMMonitor
    {
        private static int playSessionCount;
        private static bool subscribedToMemoryError;
        
        static PlayModeVRAMMonitor()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            SubscribeToMemoryErrors();
        }
        
        private static void SubscribeToMemoryErrors()
        {
            if (!subscribedToMemoryError)
            {
                LocalLlamaEngine.OnMemoryError += ShowVRAMWarningIfNeeded;
                subscribedToMemoryError = true;
            }
        }
        
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                playSessionCount++;
                SubscribeToMemoryErrors(); // Re-subscribe in case of domain reload
                
                // Log helpful info on first session
                if (playSessionCount == 1)
                {
                    var settings = AISettingsLocator.Load();
                    if (settings != null && settings.localBackend == LocalBackendMode.InProcess)
                    {
                        if (DomainReloadChecker.IsDomainReloadDisabled())
                        {
                            if (LocalLlamaEngineManager.HasReadyEngine)
                            {
                                AILogger.Log("LLM model persisted from previous session - ready immediately!");
                            }
                        }
                    }
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // With domain reload disabled, engine persists - log status
                var settings = AISettingsLocator.Load();
                if (settings != null && settings.localBackend == LocalBackendMode.InProcess)
                {
                    if (DomainReloadChecker.IsDomainReloadDisabled() && LocalLlamaEngineManager.HasReadyEngine)
                    {
                        AILogger.Log("Exited play mode - LLM model remains loaded for next session.");
                    }
                }
            }
        }
        
        /// <summary>
        /// Shows a dialog if VRAM might be exhausted.
        /// Called from LocalLlamaEngine when model loading fails due to VRAM.
        /// </summary>
        public static void ShowVRAMWarningIfNeeded(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                return;
            }
            
            string lowerError = errorMessage.ToLowerInvariant();
            bool isMemoryError = lowerError.Contains("out of memory") ||
                                  lowerError.Contains("cudamalloc") ||
                                  lowerError.Contains("failed to allocate");
            
            if (!isMemoryError)
            {
                return;
            }
            
            // Check configuration and provide appropriate guidance
            if (DomainReloadChecker.IsDomainReloadDisabled())
            {
                // Domain reload is disabled (correct config) but still got memory error
                // This means the model is genuinely too large for available VRAM
                EditorUtility.DisplayDialog(
                    "GPU Memory Exhausted",
                    "The LLM model failed to load because GPU memory is exhausted.\n\n" +
                    "Possible causes:\n" +
                    "• Model is too large for your GPU's VRAM\n" +
                    "• Other applications are using GPU memory\n" +
                    "• Context size is set too high\n\n" +
                    "Solutions:\n" +
                    "• Use a smaller/more quantized model\n" +
                    "• Reduce context size in settings\n" +
                    "• Close other GPU-intensive applications\n" +
                    "• Restart Unity to ensure clean slate",
                    "OK");
            }
            else
            {
                // Domain reload is NOT disabled - this is likely the cause
                int choice = EditorUtility.DisplayDialogComplex(
                    "GPU Memory Exhausted - Configuration Issue",
                    "The LLM model failed to load because GPU memory is exhausted.\n\n" +
                    "This is likely because Domain Reload is enabled, which causes " +
                    "GPU memory to leak between play sessions.\n\n" +
                    "RECOMMENDED: Disable Domain Reload to fix this permanently.\n\n" +
                    "For now, you'll need to restart Unity to free the VRAM.",
                    "Configure Settings",
                    "Restart Unity Later",
                    "Cancel");
                
                if (choice == 0)
                {
                    DomainReloadChecker.ConfigureOptimalSettings();
                }
            }
        }
    }
}
#endif