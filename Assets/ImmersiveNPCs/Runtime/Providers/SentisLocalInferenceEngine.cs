using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using Unity.InferenceEngine.Tokenization;
using Unity.InferenceEngine.Tokenization.Parsers.HuggingFace;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Experimental generic Sentis runner for causal language models exported as .sentis.
    /// Models with cache/past-key-value inputs usually need a model-specific runner.
    /// </summary>
    public sealed class SentisLocalInferenceEngine : ILocalInferenceEngine, IDisposable
    {
        private readonly AIConversationSettings settings;
        private readonly object syncRoot = new object();

        private Model model;
        private Worker worker;
        private ITokenizer tokenizer;
        private HashSet<int> stopTokenIds = new HashSet<int>();
        private System.Random random;
        private string loadedModelPath;
        private string status = "Sentis model not loaded.";
        private LocalEngineLoadingState loadingState = LocalEngineLoadingState.NotInitialized;

        public SentisLocalInferenceEngine(AIConversationSettings settings)
        {
            this.settings = settings;
            int seed = settings != null ? settings.localInProcessSeed : 0;
            random = seed != 0 ? new System.Random(seed) : new System.Random();
        }

        public bool IsReady => loadingState == LocalEngineLoadingState.Ready && worker != null && tokenizer != null;
        public string Status => status;
        public LocalEngineLoadingState LoadingState => loadingState;

        public Task<bool> PreloadModelAsync(string modelPath, CancellationToken ct)
        {
            lock (syncRoot)
            {
                if (IsReady && string.Equals(loadedModelPath, modelPath, StringComparison.Ordinal))
                {
                    return Task.FromResult(true);
                }

                try
                {
                    ct.ThrowIfCancellationRequested();
                    loadingState = LocalEngineLoadingState.Loading;
                    status = "Loading Sentis model...";

                    LoadModel(modelPath, ct);

                    loadedModelPath = modelPath;
                    loadingState = LocalEngineLoadingState.Ready;
                    status = "Sentis model ready: " + Path.GetFileName(modelPath);
                    return Task.FromResult(true);
                }
                catch (OperationCanceledException)
                {
                    loadingState = LocalEngineLoadingState.NotInitialized;
                    status = "Sentis model load cancelled.";
                    throw;
                }
                catch (Exception ex)
                {
                    DisposeWorker();
                    loadingState = LocalEngineLoadingState.Failed;
                    status = "Sentis model load failed: " + ex.Message;
                    AILogger.Warn(status);
                    return Task.FromResult(false);
                }
            }
        }

        public async Task<bool> WaitUntilReadyAsync(int timeoutMs, CancellationToken ct)
        {
            if (IsReady)
            {
                return true;
            }

            DateTime deadline = timeoutMs > 0 ? DateTime.UtcNow.AddMilliseconds(timeoutMs) : DateTime.MaxValue;
            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                if (IsReady)
                {
                    return true;
                }

                if (loadingState == LocalEngineLoadingState.Failed)
                {
                    return false;
                }

                await Task.Delay(50, ct).ConfigureAwait(false);
            }

            return IsReady;
        }

        public async Task<string> GenerateAsync(LocalInferenceRequest request, CancellationToken ct)
        {
            if (!IsReady)
            {
                string path = !string.IsNullOrEmpty(request.modelPath) ? request.modelPath : loadedModelPath;
                bool loaded = await PreloadModelAsync(path, ct).ConfigureAwait(false);
                if (!loaded)
                {
                    return string.Empty;
                }
            }

            lock (syncRoot)
            {
                return GenerateInternal(request, ct);
            }
        }

        public Task<float[]> EmbedAsync(LocalEmbeddingRequest request, CancellationToken ct)
        {
            status = "Sentis embeddings require a dedicated embedding model runner.";
            return Task.FromResult<float[]>(null);
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                DisposeWorker();
                tokenizer = null;
                loadingState = LocalEngineLoadingState.NotInitialized;
                status = "Sentis model disposed.";
            }
        }

        private void LoadModel(string modelPath, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                throw new InvalidOperationException("No Sentis model selected.");
            }

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("Sentis model file not found.", modelPath);
            }

            string extension = Path.GetExtension(modelPath);
            if (!string.Equals(extension, ".sentis", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("The generic Sentis backend loads serialized .sentis files. Import ONNX/PyTorch models into Unity or export them to .sentis first.");
            }

            string tokenizerPath = ResolveTokenizerPath(modelPath);
            if (!File.Exists(tokenizerPath))
            {
                throw new FileNotFoundException("Hugging Face tokenizer JSON not found.", tokenizerPath);
            }

            ct.ThrowIfCancellationRequested();

            DisposeWorker();
            model = ModelLoader.Load(modelPath);
            if (model == null)
            {
                throw new InvalidOperationException("Sentis failed to load the model.");
            }

            BackendType backend = ResolveBackend(settings != null ? settings.localSentisDevice : LocalSentisDeviceMode.GPUCompute);
            worker = new Worker(model, backend);

            string tokenizerJson = File.ReadAllText(tokenizerPath);
            tokenizer = HuggingFaceParser.GetDefault().Parse(tokenizerJson);
            stopTokenIds = ParseStopTokenIds(settings != null ? settings.localSentisStopTokenIds : string.Empty);
        }

        private string GenerateInternal(LocalInferenceRequest request, CancellationToken ct)
        {
            if (!IsReady)
            {
                return string.Empty;
            }

            string prompt = request.prompt;
            if (string.IsNullOrEmpty(prompt))
            {
                prompt = (request.systemPrompt ?? string.Empty) + "\n\n" + (request.userPrompt ?? string.Empty);
            }

            IEncoding encoding = tokenizer.Encode(prompt ?? string.Empty);
            List<int> tokens = new List<int>(encoding.GetIds());
            int maxContextTokens = settings != null ? Math.Max(1, settings.localSentisMaxContextTokens) : 2048;
            TrimLeft(tokens, maxContextTokens);

            int maxTokens = request.maxTokens > 0 ? request.maxTokens : 256;
            List<int> generated = new List<int>(maxTokens);
            for (int i = 0; i < maxTokens; i++)
            {
                ct.ThrowIfCancellationRequested();
                int nextToken = RunNextToken(tokens, request.temperature, settings != null ? settings.topP : 1f);
                if (stopTokenIds.Contains(nextToken))
                {
                    break;
                }

                tokens.Add(nextToken);
                generated.Add(nextToken);
                TrimLeft(tokens, maxContextTokens);
            }

            return generated.Count > 0 ? tokenizer.Decode(generated, skipSpecialTokens: true) : string.Empty;
        }

        private int RunNextToken(List<int> tokenIds, float temperature, float topP)
        {
            if (tokenIds == null || tokenIds.Count == 0)
            {
                throw new InvalidOperationException("Cannot run Sentis generation without input tokens.");
            }

            int seqLen = tokenIds.Count;
            int[] ids = tokenIds.ToArray();
            int[] mask = new int[seqLen];
            int[] positions = new int[seqLen];
            int[] tokenTypes = new int[seqLen];
            for (int i = 0; i < seqLen; i++)
            {
                mask[i] = 1;
                positions[i] = i;
            }

            using (Tensor<int> inputIds = new Tensor<int>(new TensorShape(1, seqLen), ids))
            using (Tensor<int> attentionMask = new Tensor<int>(new TensorShape(1, seqLen), mask))
            using (Tensor<int> positionIds = new Tensor<int>(new TensorShape(1, seqLen), positions))
            using (Tensor<int> tokenTypeIds = new Tensor<int>(new TensorShape(1, seqLen), tokenTypes))
            {
                Tensor[] inputs = BuildInputs(inputIds, attentionMask, positionIds, tokenTypeIds);
                worker.Schedule(inputs);

                Tensor output = string.IsNullOrEmpty(settings != null ? settings.localSentisLogitsOutputName : string.Empty)
                    ? worker.PeekOutput()
                    : worker.PeekOutput(settings.localSentisLogitsOutputName);

                Tensor<float> logitsTensor = output as Tensor<float>;
                if (logitsTensor == null)
                {
                    throw new NotSupportedException("Sentis logits output must be a float tensor.");
                }

                using (Tensor<float> readable = logitsTensor.ReadbackAndClone())
                {
                    float[] logits = readable.DownloadToArray();
                    return SampleNextToken(logits, readable.shape, temperature, topP);
                }
            }
        }

        private Tensor[] BuildInputs(Tensor<int> inputIds, Tensor<int> attentionMask, Tensor<int> positionIds, Tensor<int> tokenTypeIds)
        {
            if (model.inputs == null || model.inputs.Count == 0)
            {
                throw new NotSupportedException("Sentis model has no inputs.");
            }

            Tensor[] inputs = new Tensor[model.inputs.Count];
            for (int i = 0; i < model.inputs.Count; i++)
            {
                string name = model.inputs[i].name ?? string.Empty;
                string lower = name.ToLowerInvariant();

                if (model.inputs.Count == 1 || IsInputName(name, settings != null ? settings.localSentisInputIdsName : "input_ids") || lower.Contains("input_ids"))
                {
                    inputs[i] = inputIds;
                }
                else if (IsInputName(name, settings != null ? settings.localSentisAttentionMaskName : "attention_mask") || lower.Contains("attention_mask"))
                {
                    inputs[i] = attentionMask;
                }
                else if (lower.Contains("position_ids"))
                {
                    inputs[i] = positionIds;
                }
                else if (lower.Contains("token_type_ids"))
                {
                    inputs[i] = tokenTypeIds;
                }
                else
                {
                    throw new NotSupportedException("The generic Sentis backend cannot drive model input '" + name + "'. Use a model-specific Sentis runner for cache/past-key-value models.");
                }
            }

            return inputs;
        }

        private int SampleNextToken(float[] logits, TensorShape shape, float temperature, float topP)
        {
            if (logits == null || logits.Length == 0)
            {
                throw new InvalidOperationException("Sentis model returned empty logits.");
            }

            int vocab = shape.rank > 0 ? shape[shape.rank - 1] : logits.Length;
            if (vocab <= 0 || vocab > logits.Length)
            {
                vocab = logits.Length;
            }

            int offset = logits.Length - vocab;
            if (temperature <= 0.001f)
            {
                return ArgMax(logits, offset, vocab);
            }

            double[] weights = new double[vocab];
            int[] indices = new int[vocab];
            float safeTemperature = Math.Max(0.001f, temperature);
            float maxLogit = float.NegativeInfinity;
            for (int i = 0; i < vocab; i++)
            {
                float value = logits[offset + i];
                if (!float.IsNaN(value) && value > maxLogit)
                {
                    maxLogit = value;
                }
                indices[i] = i;
            }

            if (float.IsNegativeInfinity(maxLogit))
            {
                return 0;
            }

            double total = 0d;
            for (int i = 0; i < vocab; i++)
            {
                float value = logits[offset + i];
                if (float.IsNaN(value))
                {
                    weights[i] = 0d;
                    continue;
                }

                double weight = Math.Exp((value - maxLogit) / safeTemperature);
                weights[i] = weight;
                total += weight;
            }

            if (total <= 0d)
            {
                return ArgMax(logits, offset, vocab);
            }

            if (topP > 0f && topP < 0.999f)
            {
                Array.Sort(indices, (left, right) => weights[right].CompareTo(weights[left]));
                double cutoff = total * topP;
                double partial = 0d;
                int count = 0;
                while (count < indices.Length && partial < cutoff)
                {
                    partial += weights[indices[count]];
                    count++;
                }

                return SampleFromSorted(weights, indices, Math.Max(1, count), partial);
            }

            return SampleFromFullDistribution(weights, total);
        }

        private int SampleFromFullDistribution(double[] weights, double total)
        {
            double target = random.NextDouble() * total;
            double cumulative = 0d;
            for (int i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                if (target <= cumulative)
                {
                    return i;
                }
            }

            return Math.Max(0, weights.Length - 1);
        }

        private int SampleFromSorted(double[] weights, int[] sortedIndices, int count, double total)
        {
            double target = random.NextDouble() * total;
            double cumulative = 0d;
            for (int i = 0; i < count; i++)
            {
                int tokenId = sortedIndices[i];
                cumulative += weights[tokenId];
                if (target <= cumulative)
                {
                    return tokenId;
                }
            }

            return sortedIndices[Math.Max(0, count - 1)];
        }

        private static int ArgMax(float[] values, int offset, int count)
        {
            int best = 0;
            float bestValue = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                float value = values[offset + i];
                if (!float.IsNaN(value) && value > bestValue)
                {
                    bestValue = value;
                    best = i;
                }
            }

            return best;
        }

        private string ResolveTokenizerPath(string modelPath)
        {
            string configured = settings != null && !string.IsNullOrWhiteSpace(settings.localSentisTokenizerFile)
                ? settings.localSentisTokenizerFile
                : "tokenizer.json";

            return ResolveTokenizerPath(modelPath, configured);
        }

        private static string ResolveTokenizerPath(string modelPath, string configured)
        {
            if (Path.IsPathRooted(configured))
            {
                return configured;
            }

            string modelFolder = Path.GetDirectoryName(modelPath);
            return Path.Combine(modelFolder ?? string.Empty, configured);
        }

        private static BackendType ResolveBackend(LocalSentisDeviceMode mode)
        {
            switch (mode)
            {
                case LocalSentisDeviceMode.CPU:
                    return BackendType.CPU;
                case LocalSentisDeviceMode.GPUPixel:
                    return BackendType.GPUPixel;
                case LocalSentisDeviceMode.GPUCompute:
                default:
                    return BackendType.GPUCompute;
            }
        }

        private static HashSet<int> ParseStopTokenIds(string raw)
        {
            HashSet<int> ids = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ids;
            }

            string[] parts = raw.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private static bool IsInputName(string actual, string expected)
        {
            return !string.IsNullOrEmpty(actual)
                && !string.IsNullOrEmpty(expected)
                && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void TrimLeft(List<int> values, int maxCount)
        {
            if (values == null || maxCount <= 0 || values.Count <= maxCount)
            {
                return;
            }

            values.RemoveRange(0, values.Count - maxCount);
        }

        private void DisposeWorker()
        {
            if (worker != null)
            {
                worker.Dispose();
                worker = null;
            }

            model = null;
        }
    }
}
