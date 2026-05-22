using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    public sealed class LocalLlamaEngine : ILocalInferenceEngine, IDisposable
    {
        private readonly AIConversationSettings settings;
        private readonly SemaphoreSlim generationLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim loadLock = new SemaphoreSlim(1, 1);
        private readonly object sync = new object();
        private IntPtr context;
        private string loadedModelPath = string.Empty;
        private string status = "Not initialized";
        private bool loggedMissingPlugin;
        private bool loggedMissingChatTemplate;
        private string lastReportedError;
        private bool? loggingEnabled;
        private const int MaxGpuLayers = 999;
        private string lastBackendSummary;
        private volatile bool disposing;
        private volatile bool disposed;
        private volatile LocalEngineLoadingState loadingState = LocalEngineLoadingState.NotInitialized;
        private readonly TaskCompletionSource<bool> modelReadyTcs = new TaskCompletionSource<bool>();
        private int loadAttemptCount;
        private int actualContextSize; // Track the actual context size that was successfully loaded

        public LocalLlamaEngine(AIConversationSettings settings)
        {
            this.settings = settings;
        }

        public bool IsReady => context != IntPtr.Zero && IsNativeContextReady();
        public bool IsDisposed => disposed;
        public string Status => status;
        public LocalEngineLoadingState LoadingState => loadingState;
        public int ActualContextSize => actualContextSize; // Expose for debugging/logging

        /// <summary>
        /// Checks if the native context reports itself as ready.
        /// This provides an additional safety check at the native layer.
        /// </summary>
        private bool IsNativeContextReady()
        {
            if (context == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return LlamaNative.IsReady(context) != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the native context is still loading.
        /// </summary>
        private bool IsNativeContextLoading()
        {
            if (context == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return LlamaNative.IsLoading(context) != 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> PreloadModelAsync(string modelPath, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                status = "Model path not set.";
                loadingState = LocalEngineLoadingState.Failed;
                TrySetModelReadyResult(false);
                return false;
            }

            if (disposed || disposing)
            {
                status = "Disposed.";
                loadingState = LocalEngineLoadingState.Failed;
                TrySetModelReadyResult(false);
                return false;
            }

            // If already ready with same model, return immediately
            if (loadingState == LocalEngineLoadingState.Ready && IsModelPathMatch(modelPath))
            {
                return true;
            }

            // If currently loading, wait for it
            if (loadingState == LocalEngineLoadingState.Loading)
            {
                return await WaitForCurrentLoadAsync(ct).ConfigureAwait(false);
            }

            await loadLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock
                if (loadingState == LocalEngineLoadingState.Ready && IsModelPathMatch(modelPath))
                {
                    return true;
                }

                loadingState = LocalEngineLoadingState.Loading;
                loadAttemptCount = 0;

                int maxRetries = settings != null ? settings.localInProcessLoadRetryCount : 2;
                int retryDelayMs = settings != null ? settings.localInProcessLoadRetryDelayMs : 1000;
                int timeoutSeconds = settings != null ? settings.localInProcessLoadTimeoutSeconds : 120;

                Exception lastException = null;

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    if (ct.IsCancellationRequested)
                    {
                        status = "Loading cancelled.";
                        loadingState = LocalEngineLoadingState.Failed;
                        TrySetModelReadyResult(false);
                        return false;
                    }

                    if (attempt > 0)
                    {
                        int delay = retryDelayMs * (1 << (attempt - 1)); // Exponential backoff
                        AILogger.Log($"Retrying model load (attempt {attempt + 1}/{maxRetries + 1}) after {delay}ms...");
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }

                    loadAttemptCount = attempt + 1;

                    try
                    {
                        using var timeoutCts = timeoutSeconds > 0
                            ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
                            : new CancellationTokenSource();
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                        bool success = await Task.Run(() => LoadModelInternal(modelPath), linkedCts.Token).ConfigureAwait(false);

                        if (success)
                        {
                            status = "Ready";
                            loadingState = LocalEngineLoadingState.Ready;
                            TrySetModelReadyResult(true);
                            return true;
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        lastException = new TimeoutException($"Model loading timed out after {timeoutSeconds} seconds.");
                        status = "Model loading timed out.";
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        status = "Model loading error: " + ex.Message;
                    }
                }

                loadingState = LocalEngineLoadingState.Failed;
                if (lastException != null)
                {
                    ReportErrorOnce("Model loading failed after " + (maxRetries + 1) + " attempts: " + lastException.Message);
                }
                TrySetModelReadyResult(false);
                return false;
            }
            finally
            {
                loadLock.Release();
            }
        }

        public async Task<bool> WaitUntilReadyAsync(int timeoutMs, CancellationToken ct)
        {
            if (loadingState == LocalEngineLoadingState.Ready)
            {
                return true;
            }

            if (loadingState == LocalEngineLoadingState.Failed)
            {
                return false;
            }

            if (loadingState == LocalEngineLoadingState.NotInitialized)
            {
                // Model hasn't started loading yet; caller should call PreloadModelAsync first
                return false;
            }

            try
            {
                using var timeoutCts = timeoutMs > 0
                    ? new CancellationTokenSource(timeoutMs)
                    : new CancellationTokenSource();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                Task completedTask = await Task.WhenAny(
                    modelReadyTcs.Task,
                    Task.Delay(Timeout.Infinite, linkedCts.Token)
                ).ConfigureAwait(false);

                if (completedTask == modelReadyTcs.Task)
                {
                    return await modelReadyTcs.Task.ConfigureAwait(false);
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                return loadingState == LocalEngineLoadingState.Ready;
            }
        }

        private async Task<bool> WaitForCurrentLoadAsync(CancellationToken ct)
        {
            try
            {
                Task completedTask = await Task.WhenAny(
                    modelReadyTcs.Task,
                    Task.Delay(Timeout.Infinite, ct)
                ).ConfigureAwait(false);

                if (completedTask == modelReadyTcs.Task)
                {
                    return await modelReadyTcs.Task.ConfigureAwait(false);
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                return loadingState == LocalEngineLoadingState.Ready;
            }
        }

        private void TrySetModelReadyResult(bool success)
        {
            modelReadyTcs.TrySetResult(success);
        }

        private bool IsModelPathMatch(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath) || string.IsNullOrEmpty(loadedModelPath))
            {
                return false;
            }

            try
            {
                string resolved = Path.GetFullPath(modelPath);
                return string.Equals(resolved, loadedModelPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(modelPath, loadedModelPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        private bool LoadModelInternal(string modelPath)
        {
            SyncLoggingSetting();

            string resolvedPath = modelPath;
            try
            {
                resolvedPath = Path.GetFullPath(modelPath);
            }
            catch (Exception)
            {
            }

            if (Directory.Exists(resolvedPath))
            {
                status = "Model path is a directory. Select a .gguf file: " + resolvedPath;
                loadingState = LocalEngineLoadingState.Failed;
                ReportErrorOnce(status);
                return false;
            }

            if (!File.Exists(resolvedPath))
            {
                status = "Model file not found: " + resolvedPath;
                loadingState = LocalEngineLoadingState.Failed;
                ReportErrorOnce(status);
                return false;
            }

            if (HasGgufExtension(resolvedPath) && !LooksLikeGguf(resolvedPath))
            {
                status = "Model file is not a valid GGUF: " + resolvedPath;
                loadingState = LocalEngineLoadingState.Failed;
                ReportErrorOnce(status);
                return false;
            }

            lock (sync)
            {
                if (context != IntPtr.Zero && string.Equals(loadedModelPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
                {
                    loadingState = LocalEngineLoadingState.Ready;
                    return true;
                }

                ReleaseContext();

                // Get initial configuration
                int targetContextSize = settings != null ? settings.localInProcessContextSize : 8192;
                int threads = settings != null ? settings.localInProcessThreads : 4;
                int useMmap = settings != null && settings.localInProcessUseMmap ? 1 : 0;
                int useMlock = settings != null && settings.localInProcessUseMlock ? 1 : 0;
                int seed = settings != null ? settings.localInProcessSeed : 0;
                bool autoConfig = settings == null || settings.localInProcessAutoConfig;
                
                // Determine context sizes to try
                int[] contextSizesToTry;
                
                if (autoConfig)
                {
                    // Auto-adjust context size based on model file size and available VRAM
                    long modelFileSizeBytes = GetModelFileSize(resolvedPath);
                    int recommendedContextSize = CalculateRecommendedContextSize(modelFileSizeBytes, targetContextSize);
                    contextSizesToTry = GetContextSizesToTry(recommendedContextSize, targetContextSize);
                }
                else
                {
                    // Use exactly what the user configured
                    contextSizesToTry = new int[] { targetContextSize };
                }
                
                bool preferGpu = settings != null && settings.localInProcessDevice == LocalInProcessDeviceMode.GPUPreferred;

                foreach (int contextSize in contextSizesToTry)
                {
                    bool triedCpuFallback = false;

                    while (true)
                    {
                        bool useCpu = triedCpuFallback || !preferGpu;
                        LlamaModelConfig config = new LlamaModelConfig
                        {
                            contextSize = contextSize,
                            threads = threads,
                            useMmap = useMmap,
                            useMlock = useMlock,
                            seed = seed
                        };
                        ApplyDeviceSelection(ref config);
                        if (useCpu)
                        {
                            config.gpuLayers = 0;
                            config.allowHostMemory = 1;
                        }

                        if (autoConfig)
                        {
                            string mode = useCpu ? "CPU" : "GPU";
                            AILogger.Log($"[AutoConfig] Trying to load model ({mode}) with context size {contextSize}...");
                        }
                        
                        try
                        {
                            context = LlamaNative.CreateContext(resolvedPath, ref config);
                            
                            if (context != IntPtr.Zero)
                            {
                                // Store the actual context size that was successfully loaded
                                actualContextSize = contextSize;
                                
                                if (autoConfig && contextSize < targetContextSize)
                                {
                                    AILogger.Warn($"[AutoConfig] Loaded with reduced context size {contextSize} (requested {targetContextSize}) due to VRAM constraints.");
                                }
                                else if (autoConfig)
                                {
                                    AILogger.Log($"[AutoConfig] Model loaded successfully with context size {contextSize}.");
                                }
                                break; // Success!
                            }
                            
                            string error = TryGetLastError();
                            if (autoConfig && IsOutOfMemoryError(error))
                            {
                                if (!useCpu && preferGpu)
                                {
                                    AILogger.Warn("[AutoConfig] GPU load failed (out of memory), retrying on CPU...");
                                    triedCpuFallback = true;
                                    continue; // retry same context size on CPU
                                }
                                
                                AILogger.Log($"[AutoConfig] Context size {contextSize} failed (out of memory), trying smaller...");
                                break; // move to smaller context size
                            }
                            
                            // Non-memory error, don't retry
                            status = "Failed to load model: " + error;
                            loadingState = LocalEngineLoadingState.Failed;
                            ReportErrorOnce(status);
                            return false;
                        }
                        catch (DllNotFoundException)
                        {
                            status = "Native plugin not found. Add the in-process backend plugin to the project.";
                            loadingState = LocalEngineLoadingState.Failed;
                            LogMissingPluginOnce();
                            return false;
                        }
                        catch (EntryPointNotFoundException)
                        {
                            status = "Native plugin mismatch. The expected entry points were not found.";
                            loadingState = LocalEngineLoadingState.Failed;
                            LogMissingPluginOnce();
                            return false;
                        }
                        catch (Exception ex)
                        {
                            status = "Failed to load model: " + ex.Message;
                            loadingState = LocalEngineLoadingState.Failed;
                            return false;
                        }
                    }

                    if (context != IntPtr.Zero)
                    {
                        break;
                    }
                }

                if (context == IntPtr.Zero)
                {
                    status = "Failed to load model: Could not fit model in available memory. Try a smaller model or reduce context size.";
                    loadingState = LocalEngineLoadingState.Failed;
                    ReportErrorOnce(status);
                    return false;
                }

                loadedModelPath = resolvedPath;
                status = "Ready";
                loadingState = LocalEngineLoadingState.Ready;
                TrySetModelReadyResult(true);
                LogBackendSummaryIfNeeded();
                return true;
            }
        }

        public async Task<string> GenerateAsync(LocalInferenceRequest request, CancellationToken ct)
        {
            if (request.modelPath == null || request.modelPath.Length == 0)
            {
                status = "No model selected.";
                return string.Empty;
            }

            if (disposed || disposing)
            {
                status = "Disposed.";
                return string.Empty;
            }

            // If model is loading, wait for it to finish before attempting generation
            if (loadingState == LocalEngineLoadingState.Loading)
            {
                int timeoutMs = settings != null ? settings.localInProcessLoadTimeoutSeconds * 1000 : 120000;
                bool ready = await WaitUntilReadyAsync(timeoutMs, ct).ConfigureAwait(false);
                if (!ready)
                {
                    status = "Model not ready for inference.";
                    return string.Empty;
                }
            }

            // If model hasn't been preloaded yet, do it now (lazy loading with hardening)
            if (loadingState == LocalEngineLoadingState.NotInitialized || loadingState == LocalEngineLoadingState.Failed)
            {
                bool loaded = await PreloadModelAsync(request.modelPath, ct).ConfigureAwait(false);
                if (!loaded)
                {
                    return string.Empty;
                }
            }

            await generationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (disposed || disposing)
                {
                    status = "Disposed.";
                    return string.Empty;
                }
                return await Task.Run(() => GenerateInternal(request, ct), ct).ConfigureAwait(false);
            }
            finally
            {
                generationLock.Release();
            }
        }

        public async Task<float[]> EmbedAsync(LocalEmbeddingRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.text))
            {
                return null;
            }

            if (request.modelPath == null || request.modelPath.Length == 0)
            {
                status = "No model selected.";
                return null;
            }

            if (disposed || disposing)
            {
                status = "Disposed.";
                return null;
            }

            // If model is loading, wait for it to finish before attempting embedding
            if (loadingState == LocalEngineLoadingState.Loading)
            {
                int timeoutMs = settings != null ? settings.localInProcessLoadTimeoutSeconds * 1000 : 120000;
                bool ready = await WaitUntilReadyAsync(timeoutMs, ct).ConfigureAwait(false);
                if (!ready)
                {
                    status = "Model not ready for embedding.";
                    return null;
                }
            }

            // If model hasn't been preloaded yet, do it now (lazy loading with hardening)
            if (loadingState == LocalEngineLoadingState.NotInitialized || loadingState == LocalEngineLoadingState.Failed)
            {
                bool loaded = await PreloadModelAsync(request.modelPath, ct).ConfigureAwait(false);
                if (!loaded)
                {
                    return null;
                }
            }

            await generationLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (disposed || disposing)
                {
                    status = "Disposed.";
                    return null;
                }
                return await Task.Run(() => EmbedInternal(request, ct), ct).ConfigureAwait(false);
            }
            finally
            {
                generationLock.Release();
            }
        }

        public void Dispose()
        {
            if (disposed || disposing)
            {
                return;
            }

            disposing = true;
            loadingState = LocalEngineLoadingState.Failed;
            TrySetModelReadyResult(false);
            CancelGeneration();

            bool lockAcquired = false;
            try
            {
                lockAcquired = generationLock.Wait(TimeSpan.FromSeconds(5));
                if (!lockAcquired)
                {
                    AILogger.Warn("Timed out waiting for generation to finish during dispose.");
                }

                // With domain reload disabled, we can safely release native resources
                // The CUDA context remains stable and llama_free works correctly
                ReleaseContext();
                disposed = true;
                AILogger.Log("LocalLlamaEngine disposed and GPU VRAM released.");
            }
            catch (Exception ex)
            {
                AILogger.Warn($"Error during LocalLlamaEngine dispose: {ex.Message}");
                disposed = true;
            }
            finally
            {
                if (lockAcquired)
                {
                    try { generationLock.Release(); } catch { }
                }
                try { generationLock.Dispose(); } catch { }
                try { loadLock.Dispose(); } catch { }
                disposing = false;
            }
        }

        private string GenerateInternal(LocalInferenceRequest request, CancellationToken ct)
        {
            if (!EnsureModelLoaded(request.modelPath))
            {
                ReportErrorOnce(status);
                return string.Empty;
            }

            if (ct.IsCancellationRequested)
            {
                return string.Empty;
            }

            // Validate prompts fit within context and get safe max tokens
            int maxTokens = ValidateAndTruncatePrompts(ref request, request.maxTokens);
            if (maxTokens <= 0)
            {
                maxTokens = ResolveMaxTokens(request.maxTokens);
            }

            LlamaGenerationConfig generation = new LlamaGenerationConfig
            {
                maxTokens = maxTokens,
                temperature = request.temperature,
                topP = settings != null ? settings.topP : 1f
            };

            int outputCapacity = Clamp(maxTokens * 8, 512, 16384);
            StringBuilder output = new StringBuilder(outputCapacity);

            using (ct.Register(CancelGeneration))
            {
                try
                {
                    int result;
                    if (TryGenerateChat(request, ref generation, output, out result))
                    {
                        if (result <= 0)
                        {
                            status = "Generation failed: " + TryGetLastError();
                            ReportErrorOnce(status);
                            return string.Empty;
                        }
                    }
                    else
                    {
                        result = LlamaNative.Generate(context, request.prompt ?? string.Empty, ref generation, output, output.Capacity);
                        if (result <= 0)
                        {
                            status = "Generation failed: " + TryGetLastError();
                            ReportErrorOnce(status);
                            return string.Empty;
                        }
                    }
                }
                catch (DllNotFoundException)
                {
                    status = "Native plugin not found. Add the in-process backend plugin to the project.";
                    LogMissingPluginOnce();
                    return string.Empty;
                }
                catch (EntryPointNotFoundException)
                {
                    status = "Native plugin mismatch. The expected entry points were not found.";
                    LogMissingPluginOnce();
                    return string.Empty;
                }
                catch (Exception ex)
                {
                    status = "Generation error: " + ex.Message;
                    ReportErrorOnce(status);
                    return string.Empty;
                }
            }

            status = "Ready";
            return output.ToString();
        }

        private float[] EmbedInternal(LocalEmbeddingRequest request, CancellationToken ct)
        {
            if (!EnsureModelLoaded(request.modelPath))
            {
                ReportErrorOnce(status);
                return null;
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            using (ct.Register(CancelGeneration))
            {
                try
                {
                    int size = LlamaNative.GetEmbeddingSize(context);
                    if (size <= 0)
                    {
                        status = "Embedding size is invalid.";
                        ReportErrorOnce(status);
                        return null;
                    }

                    int bytes = sizeof(float) * size;
                    IntPtr buffer = Marshal.AllocHGlobal(bytes);
                    try
                    {
                        int written = LlamaNative.Embed(context, request.text ?? string.Empty, buffer, size);
                        if (written <= 0)
                        {
                            status = "Embedding failed: " + TryGetLastError();
                            ReportErrorOnce(status);
                            return null;
                        }

                        float[] result = new float[written];
                        Marshal.Copy(buffer, result, 0, written);
                        status = "Ready";
                        return result;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                catch (DllNotFoundException)
                {
                    status = "Native plugin not found. Add the in-process backend plugin to the project.";
                    LogMissingPluginOnce();
                    return null;
                }
                catch (EntryPointNotFoundException)
                {
                    status = "Native plugin mismatch. Embedding entry points not found.";
                    LogMissingPluginOnce();
                    return null;
                }
                catch (Exception ex)
                {
                    status = "Embedding error: " + ex.Message;
                    ReportErrorOnce(status);
                    return null;
                }
            }
        }

        private bool TryGenerateChat(LocalInferenceRequest request, ref LlamaGenerationConfig generation, StringBuilder output, out int result)
        {
            result = 0;

            bool hasChat = !string.IsNullOrEmpty(request.systemPrompt) || !string.IsNullOrEmpty(request.userPrompt);
            if (!hasChat)
            {
                return false;
            }

            try
            {
                result = LlamaNative.GenerateChat(
                    context,
                    request.systemPrompt ?? string.Empty,
                    request.userPrompt ?? string.Empty,
                    (int)request.chatTemplateMode,
                    ref generation,
                    output,
                    output.Capacity);
                return true;
            }
            catch (EntryPointNotFoundException)
            {
                if (!loggedMissingChatTemplate)
                {
                    loggedMissingChatTemplate = true;
                    AILogger.Warn("Native plugin does not support chat templates. Falling back to raw prompt.");
                }
                return false;
            }
        }

        private bool EnsureModelLoaded(string modelPath)
        {
            // If model is already loaded for the same path, just return true
            if (context != IntPtr.Zero && IsModelPathMatch(modelPath))
            {
                return true;
            }

            // If model was preloaded via PreloadModelAsync, we're already ready
            if (loadingState == LocalEngineLoadingState.Ready && IsModelPathMatch(modelPath))
            {
                return true;
            }

            // Fall back to synchronous loading (legacy path)
            return LoadModelInternal(modelPath);
        }

        private void ReleaseContext()
        {
            if (context == IntPtr.Zero)
            {
                return;
            }

            try
            {
                LlamaNative.DestroyContext(context);
            }
            catch (Exception)
            {
            }
            context = IntPtr.Zero;
            loadedModelPath = string.Empty;
        }

        private void CancelGeneration()
        {
            if (context == IntPtr.Zero)
            {
                return;
            }

            try
            {
                LlamaNative.Cancel(context);
            }
            catch (Exception)
            {
            }
        }

        private string TryGetLastError()
        {
            try
            {
                StringBuilder buffer = new StringBuilder(512);
                int length = LlamaNative.GetLastError(buffer, buffer.Capacity);
                if (length > 0)
                {
                    return buffer.ToString();
                }
            }
            catch (Exception)
            {
            }
            return "Unknown error";
        }

        private void LogMissingPluginOnce()
        {
            if (loggedMissingPlugin)
            {
                return;
            }

            loggedMissingPlugin = true;
            AILogger.Warn("In-process backend plugin missing or incompatible. See documentation for setup.");
        }

        private void SyncLoggingSetting()
        {
            bool enabled = settings != null && settings.enableInProcessLogging;
            if (loggingEnabled.HasValue && loggingEnabled.Value == enabled)
            {
                return;
            }

            loggingEnabled = enabled;
            InProcessNativeLog.SetLoggingEnabled(enabled);
        }

        private void LogBackendSummaryIfNeeded()
        {
            string summary = TryGetBackendSummary();
            if (string.IsNullOrEmpty(summary) || summary == lastBackendSummary)
            {
                return;
            }

            lastBackendSummary = summary;

            if (settings != null && settings.localInProcessDevice == LocalInProcessDeviceMode.GPUPreferred)
            {
                if (!summary.Contains("CUDA", StringComparison.OrdinalIgnoreCase))
                {
                    AILogger.Warn("GPU preferred, but CUDA backend not detected. Falling back to CPU. " + summary);
                    return;
                }
            }

            AILogger.Log("In-process backend summary: " + summary);
        }

        private string TryGetBackendSummary()
        {
            try
            {
                StringBuilder buffer = new StringBuilder(512);
                int length = LlamaNative.GetBackendSummary(buffer, buffer.Capacity);
                if (length > 0)
                {
                    return buffer.ToString();
                }
            }
            catch (DllNotFoundException)
            {
                LogMissingPluginOnce();
            }
            catch (EntryPointNotFoundException)
            {
                LogMissingPluginOnce();
            }
            catch (Exception ex)
            {
                AILogger.Warn("Failed to read backend summary: " + ex.Message);
            }

            return string.Empty;
        }

        private void ApplyDeviceSelection(ref LlamaModelConfig config)
        {
            if (settings == null)
            {
                config.gpuLayers = 0;
                config.allowHostMemory = 1;
                return;
            }

            if (settings.localInProcessDevice == LocalInProcessDeviceMode.GPUPreferred)
            {
                int layers = settings.localInProcessGpuLayers;
                config.gpuLayers = layers > 0 ? layers : MaxGpuLayers;
                config.allowHostMemory = settings.localInProcessAllowHostMemory ? 1 : 0;
            }
            else
            {
                config.gpuLayers = 0;
                config.allowHostMemory = 1;
            }
        }

        private static bool HasGgufExtension(string path)
        {
            return path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeGguf(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                if (stream.Length < 4)
                {
                    return false;
                }

                Span<byte> header = stackalloc byte[4];
                int read = stream.Read(header);
                if (read < 4)
                {
                    return false;
                }

                return header[0] == (byte)'G' && header[1] == (byte)'G' && header[2] == (byte)'U' && header[3] == (byte)'F';
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ReportErrorOnce(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || message == lastReportedError)
            {
                return;
            }

            lastReportedError = message;
            AILogger.Warn(message);
            
            // Notify any listeners about memory errors (used by editor tools)
            if (IsOutOfMemoryError(message))
            {
                OnMemoryError?.Invoke(message);
            }
        }
        
        /// <summary>
        /// Event raised when an out-of-memory error occurs during model loading.
        /// Used by editor tools to show helpful dialogs.
        /// </summary>
        public static event Action<string> OnMemoryError;

        private int ResolveMaxTokens(int requested)
        {
            if (requested > 0)
            {
                return requested;
            }

            int fallback = settings != null ? settings.localInProcessDefaultMaxTokens : 256;
            // Use actual loaded context size, not requested - this prevents crashes from context overflow
            int contextSize = actualContextSize > 0 ? actualContextSize : (settings != null ? settings.localInProcessContextSize : 8192);
            int maxAllowed = Mathf.Max(64, contextSize - 128);
            return Clamp(fallback, 64, maxAllowed);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        /// <summary>
        /// Estimates token count from character length.
        /// Average ratio varies by model/language but ~3.5-4 chars per token is typical for English.
        /// We use 3 chars per token (conservative) to avoid overflow.
        /// </summary>
        private static int EstimateTokenCount(int charCount)
        {
            return charCount > 0 ? (charCount + 2) / 3 : 0;
        }

        /// <summary>
        /// Truncates a prompt to fit within a target token count.
        /// Returns truncated string if needed, or original if it fits.
        /// </summary>
        private static string TruncatePromptIfNeeded(string prompt, int maxTokens)
        {
            if (string.IsNullOrEmpty(prompt) || maxTokens <= 0)
            {
                return prompt;
            }

            int estimated = EstimateTokenCount(prompt.Length);
            if (estimated <= maxTokens)
            {
                return prompt;
            }

            // Calculate max chars (reverse estimate: tokens * 3)
            int maxChars = maxTokens * 3;

            // Try to truncate at a sentence boundary
            int truncateAt = maxChars;
            if (truncateAt < prompt.Length)
            {
                // Look for sentence end in last 200 chars before limit
                int searchStart = Mathf.Max(0, truncateAt - 200);
                int lastPeriod = prompt.LastIndexOf('.', truncateAt - 1, truncateAt - searchStart);
                int lastQuestion = prompt.LastIndexOf('?', truncateAt - 1, truncateAt - searchStart);
                int lastExclaim = prompt.LastIndexOf('!', truncateAt - 1, truncateAt - searchStart);

                int sentenceEnd = Mathf.Max(lastPeriod, Mathf.Max(lastQuestion, lastExclaim));
                if (sentenceEnd > searchStart)
                {
                    truncateAt = sentenceEnd + 1;
                }
            }

            string truncated = prompt.Substring(0, truncateAt);
            AILogger.Warn($"[Truncation] Prompt truncated from ~{estimated} to ~{EstimateTokenCount(truncated.Length)} tokens to fit context size.");
            return truncated;
        }

        /// <summary>
        /// Validates and truncates prompts in a request to ensure they fit within context.
        /// Returns the maximum tokens available for generation after accounting for prompts.
        /// </summary>
        private int ValidateAndTruncatePrompts(ref LocalInferenceRequest request, int requestedMaxTokens)
        {
            int contextSize = actualContextSize > 0 ? actualContextSize : (settings != null ? settings.localInProcessContextSize : 8192);
            
            // Reserve tokens for generation (at least 64)
            int minGenerationTokens = 64;
            int maxPromptTokens = contextSize - minGenerationTokens;
            
            if (maxPromptTokens <= 0)
            {
                AILogger.Warn("[Truncation] Context size too small for meaningful generation.");
                return 0;
            }

            // Estimate total prompt tokens
            int systemTokens = EstimateTokenCount((request.systemPrompt ?? string.Empty).Length);
            int userTokens = EstimateTokenCount((request.userPrompt ?? string.Empty).Length);
            int promptTokens = EstimateTokenCount((request.prompt ?? string.Empty).Length);
            int totalPromptTokens = systemTokens + userTokens + promptTokens;

            if (totalPromptTokens <= maxPromptTokens)
            {
                // Prompts fit, return requested max tokens (capped to available space)
                int availableForGeneration = contextSize - totalPromptTokens;
                return Mathf.Min(requestedMaxTokens > 0 ? requestedMaxTokens : availableForGeneration, availableForGeneration);
            }

            AILogger.Warn($"[Truncation] Prompts (~{totalPromptTokens} tokens) exceed context ({contextSize}). Truncating user prompt.");

            // Truncate user prompt to fit (keep system prompt intact as it defines behavior)
            int systemAndBuffer = systemTokens + minGenerationTokens;
            int maxUserTokens = contextSize - systemAndBuffer;
            
            if (maxUserTokens > 0)
            {
                request.userPrompt = TruncatePromptIfNeeded(request.userPrompt, maxUserTokens);
            }
            else
            {
                // Even system prompt is too long - truncate both
                int halfContext = contextSize / 2;
                request.systemPrompt = TruncatePromptIfNeeded(request.systemPrompt, halfContext - minGenerationTokens);
                request.userPrompt = TruncatePromptIfNeeded(request.userPrompt, halfContext - minGenerationTokens);
            }

            // Recalculate available tokens
            int newSystemTokens = EstimateTokenCount((request.systemPrompt ?? string.Empty).Length);
            int newUserTokens = EstimateTokenCount((request.userPrompt ?? string.Empty).Length);
            int newTotalTokens = newSystemTokens + newUserTokens;
            int newAvailable = contextSize - newTotalTokens;

            return Mathf.Max(minGenerationTokens, newAvailable);
        }
        
        #region Auto-Configuration Helpers
        
        /// <summary>
        /// Gets the model file size in bytes.
        /// </summary>
        private static long GetModelFileSize(string modelPath)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(modelPath);
                return fileInfo.Exists ? fileInfo.Length : 0;
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Estimates recommended context size based on model file size.
        /// Larger models need more VRAM, leaving less room for context.
        /// </summary>
        private int CalculateRecommendedContextSize(long modelFileSizeBytes, int requestedContextSize)
        {
            if (modelFileSizeBytes == 0)
            {
                return requestedContextSize; // Can't estimate, use requested
            }
            
            // Convert to GB for easier calculation
            double modelSizeGB = modelFileSizeBytes / (1024.0 * 1024.0 * 1024.0);
            
            // Estimate parameters from file size (rough heuristic)
            // Q4 quantized: ~0.5 bytes per param, so params ≈ fileSize * 2
            // Q8 quantized: ~1 byte per param, so params ≈ fileSize
            // We'll assume Q4-Q5 average: params ≈ fileSize * 1.5
            double estimatedParamsB = modelSizeGB * 1.5;
            
            // KV cache memory per token depends on model architecture
            // For typical 7B models: ~0.125 MB per 1K context tokens
            // For 4B models: ~0.075 MB per 1K context tokens
            // Formula: kv_cache_mb ≈ params_B * 0.02 * (context_size / 1000)
            
            // Recommended context sizes based on model size (conservative estimates)
            // These leave headroom for Unity, the game, and other processes
            int recommendedContext;
            
            if (estimatedParamsB >= 13)
            {
                // 13B+ models: very limited context
                recommendedContext = 2048;
            }
            else if (estimatedParamsB >= 7)
            {
                // 7B models: moderate context
                recommendedContext = 4096;
            }
            else if (estimatedParamsB >= 3)
            {
                // 3-7B models: good context
                recommendedContext = 6144;
            }
            else
            {
                // <3B models: can handle larger context
                recommendedContext = 8192;
            }
            
            AILogger.Log($"[AutoConfig] Model size: {modelSizeGB:F1} GB, estimated params: ~{estimatedParamsB:F1}B, recommended context: {recommendedContext}");
            
            // Return the smaller of requested and recommended
            return Math.Min(requestedContextSize, recommendedContext);
        }
        
        /// <summary>
        /// Generates a list of context sizes to try, from recommended down to minimum.
        /// </summary>
        private static int[] GetContextSizesToTry(int recommendedSize, int requestedSize)
        {
            // Start with the smaller of recommended and requested
            int startSize = Math.Min(recommendedSize, requestedSize);
            
            // Define the sizes to try (powers of 2, plus some intermediate values)
            int[] allSizes = { 8192, 6144, 4096, 3072, 2048, 1024, 512 };
            
            // Filter to sizes <= startSize and return
            System.Collections.Generic.List<int> sizesToTry = new System.Collections.Generic.List<int>();
            
            foreach (int size in allSizes)
            {
                if (size <= startSize)
                {
                    sizesToTry.Add(size);
                }
            }
            
            // Ensure we have at least the minimum
            if (sizesToTry.Count == 0)
            {
                sizesToTry.Add(512);
            }
            
            return sizesToTry.ToArray();
        }
        
        /// <summary>
        /// Checks if the error message indicates an out-of-memory condition.
        /// </summary>
        private static bool IsOutOfMemoryError(string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return false;
            }
            
            string lower = error.ToLowerInvariant();
            return lower.Contains("out of memory") ||
                   lower.Contains("cudamalloc failed") ||
                   lower.Contains("failed to allocate") ||
                   lower.Contains("kv cache") ||
                   lower.Contains("oom") ||
                   lower.Contains("memory allocation") ||
                   lower.Contains("not enough memory");
        }
        
        #endregion
    }
}
