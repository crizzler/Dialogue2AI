#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using System.IO;
using UnityEngine;
using ImmersiveNPCs;

namespace ImmersiveNPCs.Editor
{
    internal static class AIConversationSettingsProvider
    {
        private static AIConversationSettings settings;
        private static SerializedObject serializedSettings;
        private static List<string> modelOptions = new List<string>();
        private static string scannedModelFolder;
        private static LocalBackendMode scannedModelBackend;
        private static Vector2 scrollPosition;
        
        // Foldout states (persisted via SessionState)
        private static bool showAdvanced;
        private static bool foldoutProvider;
        private static bool foldoutGeneration;
        private static bool foldoutCaching;
        private static bool foldoutMemory;
        private static bool foldoutContext;
        private static bool foldoutPipeline;
        private static bool foldoutSafety;
        private static bool foldoutDebug;
        
        // Styles
        private static GUIStyle quickSetupBoxStyle;
        private static GUIStyle sectionHeaderStyle;
        private static GUIStyle foldoutHeaderStyle;
        private static bool stylesInitialized;

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/Immersive NPCs", SettingsScope.Project)
            {
                guiHandler = DrawGUI,
                label = "Immersive NPCs"
            };
        }

        private static void InitStyles()
        {
            if (stylesInitialized) return;
            
            quickSetupBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 5, 10)
            };
            
            sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                margin = new RectOffset(0, 0, 8, 4)
            };
            
            foldoutHeaderStyle = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold
            };
            
            stylesInitialized = true;
        }

        private static void DrawGUI(string searchContext)
        {
            InitStyles();
            
            if (settings == null)
            {
                settings = AISettingsAssetUtility.FindAnySettings();
                if (settings != null)
                {
                    serializedSettings = new SerializedObject(settings);
                }
            }

            if (settings == null)
            {
                EditorGUILayout.HelpBox("No settings asset found. Create one to configure the system.", MessageType.Info);
                if (GUILayout.Button("Create Settings Asset"))
                {
                    settings = AISettingsAssetUtility.CreateSettingsAsset();
                    serializedSettings = new SerializedObject(settings);
                }
                return;
            }

            serializedSettings.Update();
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            // ═══════════════════════════════════════════════════════════════
            // QUICK SETUP SECTION (Always Visible)
            // ═══════════════════════════════════════════════════════════════
            DrawQuickSetup();
            
            EditorGUILayout.Space(10);
            
            // ═══════════════════════════════════════════════════════════════
            // ADVANCED SETTINGS (Collapsible)
            // ═══════════════════════════════════════════════════════════════
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced Settings", true, foldoutHeaderStyle);
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                
                DrawProviderFoldout();
                DrawGenerationFoldout();
                DrawCachingFoldout();
                DrawMemoryFoldout();
                DrawContextFoldout();
                DrawPipelineFoldout();
                DrawSafetyFoldout();
                DrawDebugFoldout();
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndScrollView();
            
            serializedSettings.ApplyModifiedProperties();

            if (settings != null)
            {
                InProcessNativeLog.SetLoggingEnabled(settings.enableInProcessLogging);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // QUICK SETUP
        // ═══════════════════════════════════════════════════════════════════
        private static void DrawQuickSetup()
        {
            EditorGUILayout.LabelField("Quick Setup", sectionHeaderStyle);
            
            EditorGUILayout.BeginVertical(quickSetupBoxStyle);
            
            // Row 1: Provider & Backend
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("providerMode"), new GUIContent("Provider"), GUILayout.Width(250));
            GUILayout.Space(20);
            
            SerializedProperty providerMode = serializedSettings.FindProperty("providerMode");
            if (UsesLocalBackend((ProviderMode)providerMode.enumValueIndex))
            {
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("localBackend"), new GUIContent("Backend"), GUILayout.Width(250));
            }
            EditorGUILayout.EndHorizontal();
            
            // Conditional: Connection settings based on backend
            SerializedProperty localBackend = serializedSettings.FindProperty("localBackend");
            if (UsesLocalBackend((ProviderMode)providerMode.enumValueIndex))
            {
                switch ((LocalBackendMode)localBackend.enumValueIndex)
                {
                    case LocalBackendMode.Ollama:
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("ollamaEndpoint"), new GUIContent("Endpoint"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("ollamaModelName"), new GUIContent("Model"));
                        break;
                    case LocalBackendMode.OpenAICompatible:
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localOpenAIEndpoint"), new GUIContent("Endpoint"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localOpenAIModelName"), new GUIContent("Model"));
                        break;
                    case LocalBackendMode.InProcess:
                    case LocalBackendMode.Placeholder:
                    case LocalBackendMode.Sentis:
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localModelFolder"), new GUIContent("Model Folder"));
                        DrawModelPopup();
                        if ((LocalBackendMode)localBackend.enumValueIndex == LocalBackendMode.Sentis)
                        {
                            DrawSentisBackendHelp();
                        }
                        break;
                }
            }

            if (UsesCloudProvider((ProviderMode)providerMode.enumValueIndex))
            {
                if (UsesLocalBackend((ProviderMode)providerMode.enumValueIndex))
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Cloud Provider", EditorStyles.boldLabel);
                }

                DrawCloudProviderFields();
            }
            
            DrawHorizontalLine();
            
            // Row 2: Quality & Slots
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("qualityPreset"), new GUIContent("Quality Preset"), GUILayout.Width(250));
            GUILayout.Space(20);
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("slotsCount"), new GUIContent("Response Slots"), GUILayout.Width(200));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("language"), new GUIContent("Language"));
            
            DrawHorizontalLine();
            
            // Row 3: Feature Toggles
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableMemory"), new GUIContent("Enable Memory"), GUILayout.Width(180));
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableTieredContext"), new GUIContent("Enable Tiered Context Pipeline"), GUILayout.Width(250));
            EditorGUILayout.EndHorizontal();
            
            // Show preset description
            if (serializedSettings.FindProperty("enableTieredContext").boolValue)
            {
                QualityPreset preset = (QualityPreset)serializedSettings.FindProperty("qualityPreset").enumValueIndex;
                string desc = GetPresetDescription(preset);
                EditorGUILayout.HelpBox(desc, MessageType.None);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private static string GetPresetDescription(QualityPreset preset)
        {
            return preset switch
            {
                QualityPreset.FastSmall => "FastSmall: 2K tokens, no planning. Best for mobile or many concurrent NPCs.",
                QualityPreset.Balanced => "Balanced: 4K tokens with planning. Good quality/speed balance for most NPCs.",
                QualityPreset.DeepConversation => "DeepConversation: 8K tokens, rich memory. For important story NPCs.",
                QualityPreset.CinematicQuality => "CinematicQuality: 16K tokens, full pipeline. For cutscenes and key moments.",
                _ => ""
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // ADVANCED FOLDOUTS
        // ═══════════════════════════════════════════════════════════════════
        
        private static void DrawProviderFoldout()
        {
            foldoutProvider = EditorGUILayout.Foldout(foldoutProvider, "Provider Configuration", true);
            if (foldoutProvider)
            {
                EditorGUI.indentLevel++;

                SerializedProperty providerModeProperty = serializedSettings.FindProperty("providerMode");
                ProviderMode providerMode = (ProviderMode)providerModeProperty.enumValueIndex;

                if (UsesLocalBackend(providerMode))
                {
                    SerializedProperty localBackend = serializedSettings.FindProperty("localBackend");
                    LocalBackendMode backendMode = (LocalBackendMode)localBackend.enumValueIndex;

                    if (backendMode == LocalBackendMode.InProcess || backendMode == LocalBackendMode.Placeholder)
                    {
                        EditorGUILayout.HelpBox("Requires native in-process backend plugin. If missing, the provider will fall back to safe defaults.", MessageType.Info);

                        if (!settings.localModelFolder.Contains("StreamingAssets"))
                        {
                            EditorGUILayout.HelpBox("For builds, place models under StreamingAssets so they are included in the player.", MessageType.Warning);
                        }

                        if (!InProcessBackendInstaller.IsPluginInstalled())
                        {
                            if (GUILayout.Button("Install In-Process Backend (Download)"))
                            {
                                InProcessBackendInstaller.InstallFromMenu();
                            }
                        }

                        if (GUILayout.Button("Open Hugging Face Model Browser"))
                        {
                            HuggingFaceModelBrowserWindow.Open();
                        }

                        EditorGUILayout.Space(5);
                        EditorGUILayout.LabelField("In-Process Settings", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessDevice"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessChatTemplateMode"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessContextSize"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessThreads"));

                        SerializedProperty deviceMode = serializedSettings.FindProperty("localInProcessDevice");
                        if ((LocalInProcessDeviceMode)deviceMode.enumValueIndex == LocalInProcessDeviceMode.GPUPreferred)
                        {
                            EditorGUILayout.HelpBox("GPU requires a CUDA-enabled plugin build. If CUDA is unavailable, inference falls back to CPU.", MessageType.Info);
                            EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessGpuLayers"));
                            EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessAllowHostMemory"));
                        }

                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessUseMmap"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessUseMlock"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessSeed"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localInProcessDefaultMaxTokens"));
                    }
                    else if (backendMode == LocalBackendMode.Sentis)
                    {
                        DrawSentisBackendHelp();

                        if (!settings.localModelFolder.Contains("StreamingAssets"))
                        {
                            EditorGUILayout.HelpBox("For builds, place Sentis models and tokenizer files under StreamingAssets so they are included in the player.", MessageType.Warning);
                        }

                        if (GUILayout.Button("Open Hugging Face Model Browser"))
                        {
                            HuggingFaceModelBrowserWindow.Open();
                        }

                        EditorGUILayout.Space(5);
                        EditorGUILayout.LabelField("Sentis Settings", EditorStyles.boldLabel);
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localSentisDevice"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localSentisTokenizerFile"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localSentisMaxContextTokens"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localSentisInputIdsName"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localSentisAttentionMaskName"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localSentisLogitsOutputName"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localSentisStopTokenIds"));
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localSentisPreloadModel"));
                    }
                    else if (backendMode == LocalBackendMode.OpenAICompatible)
                    {
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("localOpenAIUseApiKey"));
                        if (serializedSettings.FindProperty("localOpenAIUseApiKey").boolValue)
                        {
                            EditorGUILayout.HelpBox("Uses the API key settings configured below.", MessageType.Info);
                        }
                    }
                }

                if (UsesCloudProvider(providerMode))
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Cloud Settings", EditorStyles.boldLabel);
                    DrawCloudProviderFields();
                }
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Cloud / API Key", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("apiKeyMode"));
                DrawApiKeyFields();
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Hugging Face (Editor)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("huggingFaceUseToken"));
                if (serializedSettings.FindProperty("huggingFaceUseToken").boolValue)
                {
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("huggingFaceTokenEnvVar"));
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("huggingFaceTokenAsset"));
                }
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("huggingFaceSearchLimit"));
                
                EditorGUI.indentLevel--;
            }
        }
        
        private static void DrawGenerationFoldout()
        {
            foldoutGeneration = EditorGUILayout.Foldout(foldoutGeneration, "Generation Parameters", true);
            if (foldoutGeneration)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("maxTokens"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("temperature"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("topP"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("presencePenalty"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("frequencyPenalty"));
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Network", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("requestTimeoutMs"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("retryCount"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("retryBackoffMs"));
                EditorGUI.indentLevel--;
            }
        }
        
        private static void DrawCachingFoldout()
        {
            foldoutCaching = EditorGUILayout.Foldout(foldoutCaching, "Caching & Prefetch", true);
            if (foldoutCaching)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryCacheEntries"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("diskCacheEnabled"));
                
                if (serializedSettings.FindProperty("diskCacheEnabled").boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("diskCachePath"));
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("diskCacheTtlMinutes"));
                    EditorGUI.indentLevel--;
                }
                
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableSpeculativePrefetch"));
                if (serializedSettings.FindProperty("enableSpeculativePrefetch").boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("speculativePrefetchDepth"));
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("speculativePrefetchMaxNodes"));
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("prefetchMaxConcurrent"));
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
        }
        
        private static void DrawMemoryFoldout()
        {
            foldoutMemory = EditorGUILayout.Foldout(foldoutMemory, "Memory & RAG", true);
            if (foldoutMemory)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryScope"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryTopK"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryMaxChars"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryMaxEntries"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryMaxEntriesPerNpc"));
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Storage", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryStorePlayerChoices"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryStoreNpcReplies"));
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Time Decay", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryUseTimeDecay"));
                if (serializedSettings.FindProperty("memoryUseTimeDecay").boolValue)
                {
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("memoryDecayHalfLifeMinutes"));
                }
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Embeddings", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("embeddingProviderMode"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("embeddingModelName"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("embeddingEndpoint"));
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Summarization", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("summarizationEnabled"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("maxRecentTurns"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("summaryTokenBudget"));
                
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("memorySeeds"), true);
                
                EditorGUI.indentLevel--;
            }
        }
        
        private static void DrawContextFoldout()
        {
            foldoutContext = EditorGUILayout.Foldout(foldoutContext, "Context & Behavior", true);
            if (foldoutContext)
            {
                EditorGUI.indentLevel++;
                
                // Asset creation buttons
                EditorGUILayout.LabelField("World & NPC Assets", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Context and persona are now defined in GlobalWorldState and NpcProfile assets.", MessageType.Info);
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Create GlobalWorldState Asset"))
                {
                    CreateGlobalWorldStateAsset();
                }
                if (GUILayout.Button("Create NpcProfile Asset"))
                {
                    CreateNpcProfileAsset();
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("strictRespondToChoice"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("injectChoiceAsLastUserMessage"));
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Perception", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("perceptionRadius"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("maxPerceptionSignals"));
                
                EditorGUI.indentLevel--;
            }
        }
        
        private static void DrawPipelineFoldout()
        {
            foldoutPipeline = EditorGUILayout.Foldout(foldoutPipeline, "Tiered Context Pipeline", true);
            if (foldoutPipeline)
            {
                EditorGUI.indentLevel++;
                
                if (!serializedSettings.FindProperty("enableTieredContext").boolValue)
                {
                    EditorGUILayout.HelpBox("Enable Tiered Context in Quick Setup to configure these options.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.LabelField("Pipeline Features", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("enablePlanningPhase"));
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableStreamingGeneration"));
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableStructuredMemory"));
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableScriptAuthority"));
                    
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("World State Validation", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableWorldStateValidation"));
                    if (serializedSettings.FindProperty("enableWorldStateValidation").boolValue)
                    {
                        EditorGUILayout.PropertyField(serializedSettings.FindProperty("validationStrictness"));
                    }
                }
                
                EditorGUI.indentLevel--;
            }
        }
        
        private static void DrawSafetyFoldout()
        {
            foldoutSafety = EditorGUILayout.Foldout(foldoutSafety, "Safety & Limits", true);
            if (foldoutSafety)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("maxLineLength"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("maxOptionLength"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("forbiddenTopics"), true);
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("stayInCharacter"));
                EditorGUI.indentLevel--;
            }
        }
        
        private static void DrawDebugFoldout()
        {
            foldoutDebug = EditorGUILayout.Foldout(foldoutDebug, "Debug & Logging", true);
            if (foldoutDebug)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableRuntimeOverlay"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("verboseLogging"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableInProcessLogging"));
                
                if (serializedSettings.FindProperty("enableTieredContext").boolValue)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Pipeline Logging", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableTimingLogs"));
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("enableValidationLogs"));
                }
                EditorGUI.indentLevel--;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════════
        
        private static void DrawModelPopup()
        {
            LocalBackendMode backendMode = (LocalBackendMode)serializedSettings.FindProperty("localBackend").enumValueIndex;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan Local Models", GUILayout.Width(150)))
            {
                RescanModelOptions(backendMode);
            }
            EditorGUILayout.EndHorizontal();

            if (modelOptions == null || modelOptions.Count == 0 || scannedModelBackend != backendMode || scannedModelFolder != settings.localModelFolder)
            {
                RescanModelOptions(backendMode);
            }

            SerializedProperty selectedModel = serializedSettings.FindProperty("selectedLocalModel");
            int currentIndex = modelOptions.IndexOf(selectedModel.stringValue);
            bool foundSelectedModel = currentIndex >= 0;
            if (currentIndex < 0 && !string.IsNullOrEmpty(selectedModel.stringValue))
            {
                string fileName = Path.GetFileName(selectedModel.stringValue);
                for (int i = 0; i < modelOptions.Count; i++)
                {
                    if (Path.GetFileName(modelOptions[i]) == fileName)
                    {
                        currentIndex = i;
                        foundSelectedModel = true;
                        break;
                    }
                }
            }
            currentIndex = Mathf.Max(0, currentIndex);
            if (modelOptions.Count > 0)
            {
                if (!foundSelectedModel)
                {
                    selectedModel.stringValue = modelOptions[currentIndex];
                }

                int newIndex = EditorGUILayout.Popup("Selected Model", currentIndex, modelOptions.ToArray());
                if (newIndex >= 0 && newIndex < modelOptions.Count && newIndex != currentIndex)
                {
                    selectedModel.stringValue = modelOptions[newIndex];
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No local models found in the folder.", MessageType.Info);
            }
        }

        private static void RescanModelOptions(LocalBackendMode backendMode)
        {
            modelOptions = ModelScanner.ScanModels(settings.localModelFolder, backendMode);
            scannedModelBackend = backendMode;
            scannedModelFolder = settings.localModelFolder;
        }

        private static bool UsesLocalBackend(ProviderMode mode)
        {
            return mode == ProviderMode.Local || mode == ProviderMode.Race;
        }

        private static bool UsesCloudProvider(ProviderMode mode)
        {
            return mode == ProviderMode.Cloud || mode == ProviderMode.Race;
        }

        private static void DrawCloudProviderFields()
        {
            SerializedProperty cloudProvider = serializedSettings.FindProperty("cloudProvider");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(cloudProvider, new GUIContent("Cloud Provider"));
            bool providerChanged = EditorGUI.EndChangeCheck();

            CloudProviderMode provider = (CloudProviderMode)cloudProvider.enumValueIndex;
            if (providerChanged)
            {
                ApplyCloudProviderDefaults(provider);
            }

            EditorGUILayout.PropertyField(serializedSettings.FindProperty("cloudEndpoint"), new GUIContent("Endpoint"));
            EditorGUILayout.PropertyField(serializedSettings.FindProperty("cloudModelName"), new GUIContent("Model"));

            DrawCloudProviderHelp(provider);
            DrawCloudApiKeyHelp(provider);
        }

        private static void DrawApiKeyFields()
        {
            SerializedProperty apiKeyMode = serializedSettings.FindProperty("apiKeyMode");

            switch ((ApiKeyMode)apiKeyMode.enumValueIndex)
            {
                case ApiKeyMode.EnvVarName:
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("apiKeyEnvVar"), new GUIContent("Environment Variable Name"));
                    break;
                case ApiKeyMode.TextAssetReference:
                    EditorGUILayout.PropertyField(serializedSettings.FindProperty("apiKeyTextAsset"), new GUIContent("API Key Text Asset"));
                    break;
                case ApiKeyMode.InlineText:
                    DrawInlineApiKeyField();
                    break;
            }

            if (apiKeyMode.enumValueIndex == (int)ApiKeyMode.EnvVarName)
            {
                DrawPastedKeyMigration();
            }
        }

        private static void DrawInlineApiKeyField()
        {
            SerializedProperty apiKeyText = serializedSettings.FindProperty("apiKeyText");
            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.PasswordField("API Key", apiKeyText.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                apiKeyText.stringValue = value;
            }

            EditorGUILayout.HelpBox("Inline API keys are stored in the settings asset. Use this for local testing only; environment variables are safer for shared projects.", MessageType.Warning);
        }

        private static void DrawPastedKeyMigration()
        {
            SerializedProperty apiKeyEnvVar = serializedSettings.FindProperty("apiKeyEnvVar");
            if (!ApiKeyResolver.LooksLikeApiKey(apiKeyEnvVar.stringValue))
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "This field contains a pasted API key, not an environment variable name. Runtime will accept it for local testing, but the key is stored in the settings asset.",
                MessageType.Warning);

            if (GUILayout.Button("Move Pasted Key To Masked API Key Field"))
            {
                SerializedProperty apiKeyText = serializedSettings.FindProperty("apiKeyText");
                SerializedProperty apiKeyMode = serializedSettings.FindProperty("apiKeyMode");
                SerializedProperty cloudProvider = serializedSettings.FindProperty("cloudProvider");

                apiKeyText.stringValue = apiKeyEnvVar.stringValue;
                apiKeyEnvVar.stringValue = ApiKeyResolver.GetDefaultCloudApiKeyEnvVar((CloudProviderMode)cloudProvider.enumValueIndex);
                apiKeyMode.enumValueIndex = (int)ApiKeyMode.InlineText;
            }
        }

        private static void ApplyCloudProviderDefaults(CloudProviderMode provider)
        {
            SerializedProperty endpoint = serializedSettings.FindProperty("cloudEndpoint");
            SerializedProperty model = serializedSettings.FindProperty("cloudModelName");
            SerializedProperty apiKeyMode = serializedSettings.FindProperty("apiKeyMode");
            SerializedProperty apiKeyEnvVar = serializedSettings.FindProperty("apiKeyEnvVar");

            switch (provider)
            {
                case CloudProviderMode.Claude:
                    endpoint.stringValue = "https://api.anthropic.com/v1/messages";
                    model.stringValue = "claude-sonnet-4-6";
                    if (apiKeyMode.enumValueIndex == (int)ApiKeyMode.EnvVarName)
                    {
                        apiKeyEnvVar.stringValue = "ANTHROPIC_API_KEY";
                    }
                    break;
                case CloudProviderMode.DeepSeek:
                    endpoint.stringValue = "https://api.deepseek.com/chat/completions";
                    model.stringValue = "deepseek-v4-flash";
                    if (apiKeyMode.enumValueIndex == (int)ApiKeyMode.EnvVarName)
                    {
                        apiKeyEnvVar.stringValue = "DEEPSEEK_API_KEY";
                    }
                    break;
                case CloudProviderMode.OpenAI:
                default:
                    endpoint.stringValue = "https://api.openai.com/v1/responses";
                    model.stringValue = "gpt-4.1-mini";
                    if (apiKeyMode.enumValueIndex == (int)ApiKeyMode.EnvVarName)
                    {
                        apiKeyEnvVar.stringValue = "OPENAI_API_KEY";
                    }
                    break;
            }
        }

        private static void DrawCloudProviderHelp(CloudProviderMode provider)
        {
            switch (provider)
            {
                case CloudProviderMode.Claude:
                    EditorGUILayout.HelpBox(
                        "Claude uses Anthropic's Messages API. Use endpoint https://api.anthropic.com/v1/messages, an Anthropic model name, and an ANTHROPIC_API_KEY value.",
                        MessageType.Info);
                    break;
                case CloudProviderMode.DeepSeek:
                    EditorGUILayout.HelpBox(
                        "DeepSeek uses an OpenAI-compatible chat completions API. Use endpoint https://api.deepseek.com/chat/completions, a DeepSeek model name, and a DEEPSEEK_API_KEY value.",
                        MessageType.Info);
                    break;
                case CloudProviderMode.OpenAI:
                default:
                    EditorGUILayout.HelpBox(
                        "OpenAI uses the Responses API by default. Chat Completions endpoints are also supported when the endpoint URL contains chat/completions.",
                        MessageType.Info);
                    break;
            }
        }

        private static void DrawCloudApiKeyHelp(CloudProviderMode provider)
        {
            SerializedProperty apiKeyMode = serializedSettings.FindProperty("apiKeyMode");
            if (apiKeyMode.enumValueIndex != (int)ApiKeyMode.EnvVarName)
            {
                return;
            }

            string expected = ApiKeyResolver.GetDefaultCloudApiKeyEnvVar(provider);
            SerializedProperty apiKeyEnvVar = serializedSettings.FindProperty("apiKeyEnvVar");
            if (ApiKeyResolver.LooksLikeApiKey(apiKeyEnvVar.stringValue))
            {
                EditorGUILayout.HelpBox(
                    "The API key env var field currently contains a pasted API key. Move it to the masked API key field below, or replace it with " + expected + ".",
                    MessageType.Warning);
                return;
            }

            if (!string.Equals(apiKeyEnvVar.stringValue, expected, System.StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox(
                    "Current API key env var is " + apiKeyEnvVar.stringValue + ". " + provider + " defaults to " + expected + ". The runtime will try both, but applying defaults makes the configuration explicit.",
                    MessageType.Warning);
            }
        }

        private static void DrawSentisBackendHelp()
        {
            EditorGUILayout.HelpBox(
                "Sentis does not load .gguf files. Use In-Process for GGUF models. The Sentis backend expects a .sentis model plus a Hugging Face tokenizer.json.",
                MessageType.Warning);

            EditorGUILayout.HelpBox(
                "Built-in Sentis glue loads the model/tokenizer and runs a simple autoregressive loop for common causal-LM inputs (input_ids, attention_mask, position_ids, token_type_ids). KV-cache or custom-input exports may need a model-specific runner.",
                MessageType.Info);
        }

        private static void DrawHorizontalLine()
        {
            EditorGUILayout.Space(5);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            EditorGUILayout.Space(5);
        }
        
        private static void CreateGlobalWorldStateAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create GlobalWorldState Asset",
                "GlobalWorldState",
                "asset",
                "Choose location for the GlobalWorldState asset",
                "Assets/ImmersiveNPCs/Data");
            
            if (string.IsNullOrEmpty(path)) return;
            
            var asset = ScriptableObject.CreateInstance<GlobalWorldState>();
            asset.gameName = "My Game";
            asset.baseContext = "You are an NPC in a fantasy game world.";
            
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
        
        private static void CreateNpcProfileAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create NpcProfile Asset",
                "NewNpcProfile",
                "asset",
                "Choose location for the NpcProfile asset",
                "Assets/ImmersiveNPCs/Data/NPCs");
            
            if (string.IsNullOrEmpty(path)) return;
            
            var asset = ScriptableObject.CreateInstance<NpcProfile>();
            asset.npcId = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace(" ", "_");
            asset.displayName = "New NPC";
            
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
#endif
