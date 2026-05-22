#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace ImmersiveNPCs.Editor
{
    public sealed class HuggingFaceModelBrowserWindow : EditorWindow
    {
        private const int MaxResultsLimit = 100;
        private const string DefaultSearch = "qwen";
        private const string SentisTextGenerationUrl = "https://huggingface.co/models?other=unity-sentis&pipeline_tag=text-generation";

        private AIConversationSettings settings;
        private string searchQuery = DefaultSearch;
        private bool filterGguf = true;
        private bool filterSentis;
        private bool filterTextGeneration = true;
        private bool showGated = false;
        private int limit = 25;
        private bool isSearching;
        private string errorMessage;
        private string statusMessage;

        private Vector2 resultsScroll;
        private Vector2 filesScroll;

        private List<HuggingFaceModel> results = new List<HuggingFaceModel>();
        private readonly Dictionary<string, List<HuggingFaceFile>> filesByModel = new Dictionary<string, List<HuggingFaceFile>>();

        private DownloadJob activeDownload;

        [MenuItem("Tools/Immersive NPCs/Hugging Face Model Browser")]
        public static void Open()
        {
            HuggingFaceModelBrowserWindow window = GetWindow<HuggingFaceModelBrowserWindow>("HF Model Browser");
            window.minSize = new Vector2(620, 520);
        }

        private void OnEnable()
        {
            settings = AISettingsAssetUtility.FindAnySettings();
            if (settings != null)
            {
                limit = Mathf.Clamp(settings.huggingFaceSearchLimit, 5, MaxResultsLimit);
                if (settings.localBackend == LocalBackendMode.Sentis)
                {
                    filterGguf = false;
                    filterSentis = true;
                    searchQuery = string.Empty;
                }
            }
        }

        private void OnDisable()
        {
            if (activeDownload != null)
            {
                activeDownload.Dispose();
                activeDownload = null;
            }
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawSearchControls();
            DrawStatus();
            DrawResults();
            DrawActiveDownload();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Hugging Face Model Browser", EditorStyles.boldLabel);

            if (settings == null)
            {
                EditorGUILayout.HelpBox("No settings asset found. Create one in Project Settings -> Immersive NPCs.", MessageType.Warning);
                if (GUILayout.Button("Create Settings Asset"))
                {
                    settings = AISettingsAssetUtility.CreateSettingsAsset();
                }
                EditorGUILayout.Space();
                return;
            }

            if (settings.localBackend != LocalBackendMode.InProcess && settings.localBackend != LocalBackendMode.Sentis)
            {
                EditorGUILayout.HelpBox("Model search is intended for local file backends: In-Process (GGUF) or Sentis (.sentis).", MessageType.Info);
            }
            else if (settings.localBackend == LocalBackendMode.Sentis)
            {
                EditorGUILayout.HelpBox("Sentis downloads need a .sentis model and a tokenizer.json in the same model folder.", MessageType.Info);
            }

            if (!settings.localModelFolder.Contains("StreamingAssets"))
            {
                EditorGUILayout.HelpBox("For builds, place models under StreamingAssets.", MessageType.Warning);
            }
        }

        private void DrawSearchControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search", GUILayout.Width(52));
            searchQuery = EditorGUILayout.TextField(searchQuery);
            if (GUILayout.Button("Search", GUILayout.Width(80)))
            {
                _ = RunSearchAsync();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            filterGguf = EditorGUILayout.ToggleLeft("GGUF only", filterGguf, GUILayout.Width(110));
            filterSentis = EditorGUILayout.ToggleLeft("Sentis", filterSentis, GUILayout.Width(90));
            filterTextGeneration = EditorGUILayout.ToggleLeft("Text-generation", filterTextGeneration, GUILayout.Width(140));
            showGated = EditorGUILayout.ToggleLeft("Include gated", showGated, GUILayout.Width(120));
            EditorGUILayout.LabelField("Limit", GUILayout.Width(40));
            int newLimit = EditorGUILayout.IntSlider(limit, 5, MaxResultsLimit);
            if (newLimit != limit)
            {
                limit = newLimit;
                if (settings != null)
                {
                    settings.huggingFaceSearchLimit = limit;
                    EditorUtility.SetDirty(settings);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (filterSentis)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Sentis LLMs", GUILayout.Width(80));
                if (GUILayout.Button("Browse HF", GUILayout.Width(90)))
                {
                    Application.OpenURL(SentisTextGenerationUrl);
                }
                if (GUILayout.Button("Phi", GUILayout.Width(60)))
                {
                    searchQuery = "phi";
                    _ = RunSearchAsync();
                }
                if (GUILayout.Button("SmolLM", GUILayout.Width(80)))
                {
                    searchQuery = "smollm";
                    _ = RunSearchAsync();
                }
                if (GUILayout.Button("TinyStories", GUILayout.Width(100)))
                {
                    searchQuery = "tiny stories";
                    _ = RunSearchAsync();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox("Sentis LLM availability is limited compared with GGUF. Use broad searches such as Phi, SmolLM, or TinyStories; many common chat models are GGUF/Transformers only.", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Local Models Folder", GUILayout.Width(200)))
            {
                string folderPath = PathUtility.ResolveProjectPath(settings.localModelFolder);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
                EditorUtility.RevealInFinder(folderPath);
            }

            if (GUILayout.Button("Rescan Local Models", GUILayout.Width(160)))
            {
                ModelScanner.ScanModels(settings.localModelFolder, settings.localBackend);
            }
            EditorGUILayout.EndHorizontal();

            if (settings != null && settings.huggingFaceUseToken && string.IsNullOrEmpty(ResolveToken()))
            {
                EditorGUILayout.HelpBox("Hugging Face token is enabled but not found. Searches may be rate-limited or fail for gated models.", MessageType.Warning);
            }
        }

        private void DrawStatus()
        {
            if (isSearching)
            {
                EditorGUILayout.HelpBox("Searching...", MessageType.Info);
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                EditorGUILayout.HelpBox(errorMessage, MessageType.Error);
            }
        }

        private void DrawResults()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);

            resultsScroll = EditorGUILayout.BeginScrollView(resultsScroll);
            if (results == null || results.Count == 0)
            {
                EditorGUILayout.LabelField("No results yet.");
            }
            else
            {
                for (int i = 0; i < results.Count; i++)
                {
                    DrawModelCard(results[i]);
                    GUILayout.Space(4);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawModelCard(HuggingFaceModel model)
        {
            if (model == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");
            string modelId = ResolveModelId(model);
            EditorGUILayout.LabelField(modelId ?? "(unknown)", EditorStyles.boldLabel);

            string meta = $"Downloads: {model.downloads} | Likes: {model.likes}";
            if (model.gated)
            {
                meta += " | Gated";
            }
            EditorGUILayout.LabelField(meta);

            string tags = BuildTagSummary(model.tags);
            if (!string.IsNullOrEmpty(tags))
            {
                EditorGUILayout.LabelField(tags, EditorStyles.miniLabel);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Model Page", GUILayout.Width(140)))
            {
                if (!string.IsNullOrEmpty(modelId))
                {
                    Application.OpenURL("https://huggingface.co/" + modelId);
                }
            }

            if (GUILayout.Button("Load Files", GUILayout.Width(100)))
            {
                _ = LoadFilesAsync(model);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(modelId) && filesByModel.TryGetValue(modelId, out List<HuggingFaceFile> files))
            {
                DrawFileList(model, files);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawFileList(HuggingFaceModel model, List<HuggingFaceFile> files)
        {
            if (files == null || files.Count == 0)
            {
                EditorGUILayout.LabelField("No matching model files found.");
                return;
            }

            filesScroll = EditorGUILayout.BeginScrollView(filesScroll, GUILayout.Height(140));
            for (int i = 0; i < files.Count; i++)
            {
                HuggingFaceFile file = files[i];
                if (file == null)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(file.path, GUILayout.MinWidth(220));
                EditorGUILayout.LabelField(FormatSize(file.size), GUILayout.Width(90));

                using (new EditorGUI.DisabledScope(activeDownload != null))
                {
                    if (GUILayout.Button("Download", GUILayout.Width(90)))
                    {
                        StartDownload(model, file);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawActiveDownload()
        {
            if (activeDownload == null)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Active Download", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(activeDownload.DisplayName);

            Rect rect = GUILayoutUtility.GetRect(1, 18);
            EditorGUI.ProgressBar(rect, activeDownload.Progress, Mathf.RoundToInt(activeDownload.Progress * 100f) + "%");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel", GUILayout.Width(90)))
            {
                activeDownload.Cancel();
            }
            EditorGUILayout.LabelField(activeDownload.Status);
            EditorGUILayout.EndHorizontal();
        }

        private async Task RunSearchAsync()
        {
            if (isSearching)
            {
                return;
            }

            errorMessage = string.Empty;
            statusMessage = string.Empty;
            isSearching = true;
            results.Clear();
            Repaint();

            try
            {
                string token = ResolveToken();
                List<HuggingFaceModel> models = await HuggingFaceApiClient.SearchModelsAsync(searchQuery, limit, filterGguf, filterSentis, filterTextGeneration, token);
                if (!showGated)
                {
                    models.RemoveAll(m => m != null && m.gated);
                }
                results = models;
                statusMessage = $"Found {results.Count} models.";
            }
            catch (Exception ex)
            {
                errorMessage = "Search failed: " + ex.Message;
            }
            finally
            {
                isSearching = false;
                Repaint();
            }
        }

        private async Task LoadFilesAsync(HuggingFaceModel model)
        {
            if (model == null)
            {
                return;
            }

            errorMessage = string.Empty;
            string modelId = ResolveModelId(model);
            if (string.IsNullOrEmpty(modelId))
            {
                return;
            }

            statusMessage = "Loading files for " + modelId + "...";
            Repaint();

            try
            {
                string token = ResolveToken();
                List<HuggingFaceFile> files = await HuggingFaceApiClient.GetModelFilesAsync(modelId, token);
                files.RemoveAll(f => !ShouldShowFile(f));
                filesByModel[modelId] = files;
                statusMessage = $"Found {files.Count} matching files.";
            }
            catch (Exception ex)
            {
                errorMessage = "File listing failed: " + ex.Message;
            }
            finally
            {
                Repaint();
            }
        }

        private void StartDownload(HuggingFaceModel model, HuggingFaceFile file)
        {
            if (settings == null || model == null || file == null)
            {
                return;
            }

            string folder = PathUtility.ResolveProjectPath(settings.localModelFolder);
            string modelId = ResolveModelId(model);
            if (string.IsNullOrEmpty(modelId))
            {
                return;
            }

            string modelFolder = Path.Combine(folder, SanitizePath(modelId));
            string relativePath = file.path.Replace('/', Path.DirectorySeparatorChar);
            string outputPath = Path.Combine(modelFolder, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            string token = ResolveToken();
            string url = HuggingFaceApiClient.BuildDownloadUrl(modelId, file.path);

            activeDownload = new DownloadJob(modelId, file.path, outputPath, url, token, _ => Repaint(), () =>
            {
                string relative = GetRelativeModelPath(folder, outputPath);
                if (ShouldSelectDownloadedFile(file.path))
                {
                    settings.selectedLocalModel = relative;
                    EditorUtility.SetDirty(settings);
                }
                AssetDatabase.Refresh();
            }, error =>
            {
                errorMessage = error;
                Repaint();
            }, () =>
            {
                activeDownload = null;
                Repaint();
            });

            activeDownload.Start();
        }

        private string ResolveToken()
        {
            if (settings == null || !settings.huggingFaceUseToken)
            {
                return string.Empty;
            }

            string token = string.Empty;
            if (!string.IsNullOrEmpty(settings.huggingFaceTokenEnvVar))
            {
                token = Environment.GetEnvironmentVariable(settings.huggingFaceTokenEnvVar);
            }

            if (string.IsNullOrEmpty(token) && settings.huggingFaceTokenAsset != null)
            {
                token = settings.huggingFaceTokenAsset.text;
            }

            return string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
        }

        private bool ShouldShowFile(HuggingFaceFile file)
        {
            if (file == null || string.IsNullOrEmpty(file.path))
            {
                return false;
            }

            if (filterGguf && file.path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (filterSentis)
            {
                if (file.path.EndsWith(".sentis", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(Path.GetFileName(file.path), "tokenizer.json", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!filterGguf && !filterSentis)
            {
                return file.path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                    || file.path.EndsWith(".sentis", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(file.path), "tokenizer.json", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool ShouldSelectDownloadedFile(string filePath)
        {
            return !string.IsNullOrEmpty(filePath)
                && (filePath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                    || filePath.EndsWith(".sentis", StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildTagSummary(string[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                return string.Empty;
            }

            List<string> shown = new List<string>();
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i];
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }

                if (tag.StartsWith("license:", StringComparison.OrdinalIgnoreCase) || tag == "gguf" || tag == "unity-sentis" || tag == "text-generation")
                {
                    shown.Add(tag);
                }
                if (shown.Count >= 4)
                {
                    break;
                }
            }

            return shown.Count > 0 ? string.Join(" · ", shown) : string.Empty;
        }

        private static string FormatSize(long size)
        {
            if (size <= 0)
            {
                return "-";
            }

            double mb = size / (1024.0 * 1024.0);
            if (mb < 1024)
            {
                return mb.ToString("0.0") + " MB";
            }
            double gb = mb / 1024.0;
            return gb.ToString("0.0") + " GB";
        }

        private static string SanitizePath(string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
            {
                return "model";
            }

            return modelId.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string ResolveModelId(HuggingFaceModel model)
        {
            if (model == null)
            {
                return string.Empty;
            }

            return !string.IsNullOrEmpty(model.id) ? model.id : model.modelId;
        }

        private static string GetRelativeModelPath(string baseFolder, string fullPath)
        {
            string relative = Path.GetRelativePath(baseFolder, fullPath);
            return relative.Replace('\\', '/');
        }

        private sealed class DownloadJob : IDisposable
        {
            private readonly Action onComplete;
            private readonly Action<string> onError;
            private readonly Action onFinished;
            private readonly Action<float> onProgress;
            private readonly string url;
            private readonly string token;
            private UnityWebRequest request;
            private UnityWebRequestAsyncOperation op;

            public string DisplayName { get; }
            public string Status { get; private set; }
            public float Progress => request != null ? request.downloadProgress : 0f;

            public DownloadJob(string modelId, string filePath, string outputPath, string url, string token, Action<float> onProgress, Action onComplete, Action<string> onError, Action onFinished)
            {
                DisplayName = $"{modelId} / {Path.GetFileName(filePath)}";
                this.url = url;
                this.token = token;
                this.onProgress = onProgress;
                this.onComplete = onComplete;
                this.onError = onError;
                this.onFinished = onFinished;

                request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET)
                {
                    downloadHandler = new DownloadHandlerFile(outputPath, true)
                };
            }

            public void Start()
            {
                if (!string.IsNullOrEmpty(token))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + token);
                }

                Status = "Downloading...";
                op = request.SendWebRequest();
                EditorApplication.update += Update;
            }

            public void Cancel()
            {
                Status = "Cancelled";
                request?.Abort();
            }

            private void Update()
            {
                if (request == null || op == null)
                {
                    Finish();
                    return;
                }

                if (!op.isDone)
                {
                    onProgress?.Invoke(request.downloadProgress);
                    return;
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Status = "Complete";
                    onComplete?.Invoke();
                }
                else
                {
                    Status = "Failed";
                    string error = request.error;
                    if (request.responseCode == 401 || request.responseCode == 403)
                    {
                        error = "Download failed: authentication required (gated model).";
                    }
                    onError?.Invoke(error);
                }

                Finish();
            }

            private void Finish()
            {
                EditorApplication.update -= Update;
                Dispose();
                onFinished?.Invoke();
            }

            public void Dispose()
            {
                if (request != null)
                {
                    request.Dispose();
                    request = null;
                }
            }
        }
    }
}
#endif
