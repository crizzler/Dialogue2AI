#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ImmersiveNPCs.Editor
{
    /// <summary>
    /// Checks and recommends optimal Enter Play Mode Settings for the local LLM backend.
    /// Disabling Domain Reload allows the LLM model to persist between play sessions,
    /// avoiding VRAM leaks and enabling faster iteration.
    /// </summary>
    [InitializeOnLoad]
    public static class DomainReloadChecker
    {
        private const string PrefKey = "ImmersiveNPCs_DomainReloadWarningDismissed";
        private const string MenuPath = "Tools/Immersive NPCs/Check Domain Reload Settings";
        
        static DomainReloadChecker()
        {
            EditorApplication.delayCall += CheckOnStartup;
        }
        
        private static void CheckOnStartup()
        {
            // Only check if using InProcess backend
            var settings = AISettingsLocator.Load();
            if (settings == null || settings.localBackend != LocalBackendMode.InProcess)
            {
                return;
            }
            
            // Don't nag if user dismissed the warning
            if (EditorPrefs.GetBool(PrefKey, false))
            {
                return;
            }
            
            // Check if domain reload is properly disabled
            if (!IsDomainReloadDisabled())
            {
                ShowConfigurationDialog();
            }
        }
        
        [MenuItem(MenuPath)]
        public static void CheckDomainReloadSettings()
        {
            var settings = AISettingsLocator.Load();
            bool usingInProcess = settings != null && settings.localBackend == LocalBackendMode.InProcess;
            bool domainReloadDisabled = IsDomainReloadDisabled();
            
            string status = $"Current Status:\n\n" +
                $"• Local Backend: {(usingInProcess ? "In-Process (llama.cpp)" : "Server/Placeholder")}\n" +
                $"• Enter Play Mode Options: {(EditorSettings.enterPlayModeOptionsEnabled ? "Enabled" : "Disabled")}\n" +
                $"• Domain Reload: {(domainReloadDisabled ? "Disabled ✓" : "Enabled ✗")}\n\n";
            
            if (usingInProcess && !domainReloadDisabled)
            {
                status += "⚠️ RECOMMENDATION:\n" +
                    "Disable Domain Reload to prevent GPU memory leaks and enable model persistence.\n\n" +
                    "Would you like to configure this now?";
                
                if (EditorUtility.DisplayDialog("Domain Reload Settings", status, "Configure Now", "Cancel"))
                {
                    ConfigureOptimalSettings();
                }
            }
            else if (usingInProcess && domainReloadDisabled)
            {
                status += "✓ Optimal configuration for local LLM!\n" +
                    "The model will persist between play sessions.";
                EditorUtility.DisplayDialog("Domain Reload Settings", status, "OK");
            }
            else
            {
                status += "Not using In-Process backend, no special configuration needed.";
                EditorUtility.DisplayDialog("Domain Reload Settings", status, "OK");
            }
        }
        
        [MenuItem("Tools/Immersive NPCs/Configure Optimal Play Mode Settings")]
        public static void ConfigureOptimalSettings()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            
            EditorUtility.DisplayDialog(
                "Settings Configured",
                "Enter Play Mode Options configured:\n\n" +
                "✓ Enter Play Mode Options: Enabled\n" +
                "✓ Reload Domain: Disabled\n\n" +
                "Benefits:\n" +
                "• LLM model persists between play sessions\n" +
                "• No GPU memory leaks\n" +
                "• Faster iteration (no domain reload delay)\n\n" +
                "Note: Static variables now persist. The Immersive NPCs system handles this automatically.",
                "OK");
            
            // Clear the dismissed pref since user explicitly configured
            EditorPrefs.DeleteKey(PrefKey);
            
            Debug.Log("[Immersive NPCs] Configured optimal Enter Play Mode Settings for local LLM backend.");
        }
        
        [MenuItem("Tools/Immersive NPCs/Reset Domain Reload Warning")]
        public static void ResetWarningDismissal()
        {
            EditorPrefs.DeleteKey(PrefKey);
            Debug.Log("[Immersive NPCs] Domain reload warning will show again on next startup if needed.");
        }
        
        [MenuItem("Tools/Immersive NPCs/Dispose LLM Engine")]
        public static void DisposeEngine()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Cannot Dispose",
                    "Cannot dispose engine while in Play mode.\nExit Play mode first.",
                    "OK");
                return;
            }
            
            if (LocalLlamaEngineManager.HasReadyEngine)
            {
                LocalLlamaEngineManager.DisposeEngine();
                EditorUtility.DisplayDialog(
                    "Engine Disposed",
                    "The LLM engine has been disposed and GPU VRAM has been released.\n\n" +
                    "The model will be reloaded when you next enter Play mode.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "No Engine",
                    "No LLM engine is currently loaded.",
                    "OK");
            }
        }
        
        [MenuItem("Tools/Immersive NPCs/Show Engine Status")]
        public static void ShowEngineStatus()
        {
            string status;
            if (LocalLlamaEngineManager.HasReadyEngine)
            {
                status = $"Engine Status: Ready\n\n" +
                         $"Loading State: {LocalLlamaEngineManager.LoadingState}\n" +
                         $"Status: {LocalLlamaEngineManager.EngineStatus}";
            }
            else
            {
                status = $"Engine Status: Not Ready\n\n" +
                         $"Loading State: {LocalLlamaEngineManager.LoadingState}\n" +
                         $"Status: {LocalLlamaEngineManager.EngineStatus}";
            }
            
            EditorUtility.DisplayDialog("LLM Engine Status", status, "OK");
        }
        
        private static void ShowConfigurationDialog()
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Immersive NPCs - Recommended Settings",
                "You're using the In-Process local LLM backend.\n\n" +
                "For optimal performance and to prevent GPU memory issues, " +
                "it's recommended to disable Domain Reload.\n\n" +
                "Benefits:\n" +
                "• LLM model stays loaded between play sessions\n" +
                "• No GPU VRAM leaks\n" +
                "• Faster iteration times\n\n" +
                "Would you like to configure this now?",
                "Configure Now",
                "Don't Ask Again",
                "Remind Me Later");
            
            switch (choice)
            {
                case 0: // Configure Now
                    ConfigureOptimalSettings();
                    break;
                case 1: // Don't Ask Again
                    EditorPrefs.SetBool(PrefKey, true);
                    break;
                case 2: // Remind Me Later
                    // Do nothing, will ask again next startup
                    break;
            }
        }
        
        /// <summary>
        /// Returns true if Enter Play Mode Options are enabled AND Domain Reload is disabled.
        /// </summary>
        public static bool IsDomainReloadDisabled()
        {
            return EditorSettings.enterPlayModeOptionsEnabled &&
                   EditorSettings.enterPlayModeOptions.HasFlag(EnterPlayModeOptions.DisableDomainReload);
        }
        
        /// <summary>
        /// Returns true if the current configuration is optimal for local LLM usage.
        /// </summary>
        public static bool IsOptimalConfiguration()
        {
            var settings = AISettingsLocator.Load();
            if (settings == null || settings.localBackend != LocalBackendMode.InProcess)
            {
                return true; // Not using in-process, any config is fine
            }
            
            return IsDomainReloadDisabled();
        }
    }
}
#endif
