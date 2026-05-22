using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Experimental generic Sentis runner for causal language models exported as .sentis.
    /// Uses reflection so Immersive NPCs still compiles when Unity AI Inference/Sentis is not installed.
    /// Models with cache/past-key-value inputs usually need a model-specific runner.
    /// </summary>
    public sealed class SentisLocalInferenceEngine : ILocalInferenceEngine, IDisposable
    {
        private const string MissingSentisMessage =
            "Unity AI Inference/Sentis is not installed. Install package com.unity.ai.inference to use the Sentis backend.";

        private readonly AIConversationSettings settings;
        private readonly object syncRoot = new object();

        private SentisApi sentis;
        private object model;
        private object worker;
        private object tokenizer;
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

            if (!SentisApi.IsAvailable)
            {
                status = MissingSentisMessage;
            }
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
            sentis = SentisApi.CreateOrThrow();

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
            model = sentis.LoadModel(modelPath);
            if (model == null)
            {
                throw new InvalidOperationException("Sentis failed to load the model.");
            }

            object backend = sentis.ResolveBackend(settings != null ? settings.localSentisDevice : LocalSentisDeviceMode.GPUCompute);
            worker = sentis.CreateWorker(model, backend);

            string tokenizerJson = File.ReadAllText(tokenizerPath);
            tokenizer = sentis.CreateTokenizer(tokenizerJson);
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

            List<int> tokens = sentis.Encode(tokenizer, prompt ?? string.Empty);
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

            return generated.Count > 0 ? sentis.Decode(tokenizer, generated, true) : string.Empty;
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

            object shape = sentis.CreateTensorShape(1, seqLen);
            object inputIds = null;
            object attentionMask = null;
            object positionIds = null;
            object tokenTypeIds = null;
            object readable = null;

            try
            {
                inputIds = sentis.CreateIntTensor(shape, ids);
                attentionMask = sentis.CreateIntTensor(shape, mask);
                positionIds = sentis.CreateIntTensor(shape, positions);
                tokenTypeIds = sentis.CreateIntTensor(shape, tokenTypes);

                Array inputs = BuildInputs(inputIds, attentionMask, positionIds, tokenTypeIds);
                sentis.Schedule(worker, inputs);

                object output = string.IsNullOrEmpty(settings != null ? settings.localSentisLogitsOutputName : string.Empty)
                    ? sentis.PeekOutput(worker)
                    : sentis.PeekOutput(worker, settings.localSentisLogitsOutputName);

                if (!sentis.IsFloatTensor(output))
                {
                    throw new NotSupportedException("Sentis logits output must be a float tensor.");
                }

                readable = sentis.ReadbackAndClone(output);
                float[] logits = sentis.DownloadFloatArray(readable);
                return SampleNextToken(logits, sentis.GetTensorShape(readable), temperature, topP);
            }
            finally
            {
                DisposeIfNeeded(readable);
                DisposeIfNeeded(tokenTypeIds);
                DisposeIfNeeded(positionIds);
                DisposeIfNeeded(attentionMask);
                DisposeIfNeeded(inputIds);
            }
        }

        private Array BuildInputs(object inputIds, object attentionMask, object positionIds, object tokenTypeIds)
        {
            IList modelInputs = sentis.GetModelInputs(model);
            if (modelInputs == null || modelInputs.Count == 0)
            {
                throw new NotSupportedException("Sentis model has no inputs.");
            }

            Array inputs = sentis.CreateTensorArray(modelInputs.Count);
            for (int i = 0; i < modelInputs.Count; i++)
            {
                string name = sentis.GetModelInputName(modelInputs[i]) ?? string.Empty;
                string lower = name.ToLowerInvariant();
                object tensor;

                if (modelInputs.Count == 1 || IsInputName(name, settings != null ? settings.localSentisInputIdsName : "input_ids") || lower.Contains("input_ids"))
                {
                    tensor = inputIds;
                }
                else if (IsInputName(name, settings != null ? settings.localSentisAttentionMaskName : "attention_mask") || lower.Contains("attention_mask"))
                {
                    tensor = attentionMask;
                }
                else if (lower.Contains("position_ids"))
                {
                    tensor = positionIds;
                }
                else if (lower.Contains("token_type_ids"))
                {
                    tensor = tokenTypeIds;
                }
                else
                {
                    throw new NotSupportedException("The generic Sentis backend cannot drive model input '" + name + "'. Use a model-specific Sentis runner for cache/past-key-value models.");
                }

                inputs.SetValue(tensor, i);
            }

            return inputs;
        }

        private int SampleNextToken(float[] logits, TensorShapeInfo shape, float temperature, float topP)
        {
            if (logits == null || logits.Length == 0)
            {
                throw new InvalidOperationException("Sentis model returned empty logits.");
            }

            int vocab = shape.Rank > 0 ? shape[shape.Rank - 1] : logits.Length;
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

        private static void DisposeIfNeeded(object value)
        {
            IDisposable disposable = value as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }

        private void DisposeWorker()
        {
            DisposeIfNeeded(worker);
            worker = null;
            model = null;
        }

        private struct TensorShapeInfo
        {
            private readonly int[] dimensions;

            public TensorShapeInfo(int[] dimensions)
            {
                this.dimensions = dimensions ?? new int[0];
            }

            public int Rank => dimensions.Length;

            public int this[int index] => dimensions[index];
        }

        private sealed class SentisApi
        {
            private const string InferenceAssembly = "Unity.InferenceEngine";
            private const string TokenizationAssembly = "Unity.InferenceEngine.Tokenization";

            private readonly Type modelLoaderType;
            private readonly Type workerType;
            private readonly Type tensorBaseType;
            private readonly Type tensorIntType;
            private readonly Type tensorFloatType;
            private readonly Type tensorShapeType;
            private readonly Type backendType;
            private readonly Type parserType;

            private readonly ConstructorInfo workerConstructor;
            private readonly ConstructorInfo intTensorConstructor;
            private readonly ConstructorInfo tensorShape2DConstructor;
            private readonly MethodInfo loadModelMethod;
            private readonly MethodInfo scheduleMethod;
            private readonly MethodInfo peekOutputMethod;
            private readonly MethodInfo peekOutputByNameMethod;
            private readonly MethodInfo readbackAndCloneMethod;
            private readonly MethodInfo downloadFloatArrayMethod;
            private readonly MethodInfo parserGetDefaultMethod;
            private readonly MethodInfo parserParseMethod;
            private readonly MethodInfo tokenizerEncodeMethod;
            private readonly MethodInfo tokenizerDecodeMethod;
            private readonly MethodInfo encodingGetIdsMethod;
            private readonly PropertyInfo tensorShapeProperty;
            private readonly PropertyInfo tensorShapeRankProperty;
            private readonly PropertyInfo tensorShapeItemProperty;
            private readonly FieldInfo modelInputsField;
            private readonly FieldInfo modelInputNameField;

            private SentisApi()
            {
                modelLoaderType = GetRequiredType("Unity.InferenceEngine.ModelLoader", InferenceAssembly);
                Type modelType = GetRequiredType("Unity.InferenceEngine.Model", InferenceAssembly);
                workerType = GetRequiredType("Unity.InferenceEngine.Worker", InferenceAssembly);
                tensorBaseType = GetRequiredType("Unity.InferenceEngine.Tensor", InferenceAssembly);
                Type tensorGenericType = GetRequiredType("Unity.InferenceEngine.Tensor`1", InferenceAssembly);
                tensorIntType = tensorGenericType.MakeGenericType(typeof(int));
                tensorFloatType = tensorGenericType.MakeGenericType(typeof(float));
                tensorShapeType = GetRequiredType("Unity.InferenceEngine.TensorShape", InferenceAssembly);
                backendType = GetRequiredType("Unity.InferenceEngine.BackendType", InferenceAssembly);
                parserType = GetRequiredType("Unity.InferenceEngine.Tokenization.Parsers.HuggingFace.HuggingFaceParser", TokenizationAssembly);
                Type tokenizerType = GetRequiredType("Unity.InferenceEngine.Tokenization.ITokenizer", TokenizationAssembly);
                Type encodingType = GetRequiredType("Unity.InferenceEngine.Tokenization.IEncoding", TokenizationAssembly);

                workerConstructor = Require(workerType.GetConstructor(new[] { modelType, backendType }), "Worker(Model, BackendType)");
                intTensorConstructor = Require(tensorIntType.GetConstructor(new[] { tensorShapeType, typeof(int[]), typeof(int) }), "Tensor<int>(TensorShape, int[], int)");
                tensorShape2DConstructor = Require(tensorShapeType.GetConstructor(new[] { typeof(int), typeof(int) }), "TensorShape(int, int)");
                loadModelMethod = Require(modelLoaderType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null), "ModelLoader.Load(string)");
                scheduleMethod = Require(workerType.GetMethod("Schedule", new[] { tensorBaseType.MakeArrayType() }), "Worker.Schedule(Tensor[])");
                peekOutputMethod = Require(workerType.GetMethod("PeekOutput", Type.EmptyTypes), "Worker.PeekOutput()");
                peekOutputByNameMethod = Require(workerType.GetMethod("PeekOutput", new[] { typeof(string) }), "Worker.PeekOutput(string)");
                readbackAndCloneMethod = Require(tensorFloatType.GetMethod("ReadbackAndClone", Type.EmptyTypes), "Tensor<float>.ReadbackAndClone()");
                downloadFloatArrayMethod = Require(tensorFloatType.GetMethod("DownloadToArray", Type.EmptyTypes), "Tensor<float>.DownloadToArray()");
                parserGetDefaultMethod = Require(parserType.GetMethod("GetDefault", BindingFlags.Public | BindingFlags.Static), "HuggingFaceParser.GetDefault()");
                parserParseMethod = Require(parserType.GetMethod("Parse", new[] { typeof(string) }), "HuggingFaceParser.Parse(string)");
                tokenizerEncodeMethod = Require(tokenizerType.GetMethod("Encode", new[] { typeof(string), typeof(string), typeof(bool) }), "ITokenizer.Encode(string, string, bool)");
                tokenizerDecodeMethod = Require(tokenizerType.GetMethod("Decode", new[] { typeof(IReadOnlyList<int>), typeof(bool) }), "ITokenizer.Decode(IReadOnlyList<int>, bool)");
                encodingGetIdsMethod = Require(encodingType.GetMethod("GetIds", new[] { typeof(ICollection<int>) }), "IEncoding.GetIds(ICollection<int>)");
                tensorShapeProperty = Require(tensorBaseType.GetProperty("shape"), "Tensor.shape");
                tensorShapeRankProperty = Require(tensorShapeType.GetProperty("rank"), "TensorShape.rank");
                tensorShapeItemProperty = Require(tensorShapeType.GetProperty("Item", new[] { typeof(int) }), "TensorShape indexer");
                modelInputsField = Require(modelType.GetField("inputs"), "Model.inputs");
                Type inputType = GetRequiredType("Unity.InferenceEngine.Model+Input", InferenceAssembly);
                modelInputNameField = Require(inputType.GetField("name"), "Model.Input.name");
            }

            public static bool IsAvailable => TryGetType("Unity.InferenceEngine.ModelLoader", InferenceAssembly) != null
                && TryGetType("Unity.InferenceEngine.Tokenization.Parsers.HuggingFace.HuggingFaceParser", TokenizationAssembly) != null;

            public static SentisApi CreateOrThrow()
            {
                if (!IsAvailable)
                {
                    throw new NotSupportedException(MissingSentisMessage);
                }

                return new SentisApi();
            }

            public object LoadModel(string modelPath)
            {
                return Invoke(loadModelMethod, null, modelPath);
            }

            public object ResolveBackend(LocalSentisDeviceMode mode)
            {
                string name;
                switch (mode)
                {
                    case LocalSentisDeviceMode.CPU:
                        name = "CPU";
                        break;
                    case LocalSentisDeviceMode.GPUPixel:
                        name = "GPUPixel";
                        break;
                    case LocalSentisDeviceMode.GPUCompute:
                    default:
                        name = "GPUCompute";
                        break;
                }

                return Enum.Parse(backendType, name);
            }

            public object CreateWorker(object model, object backend)
            {
                return workerConstructor.Invoke(new[] { model, backend });
            }

            public object CreateTokenizer(string tokenizerJson)
            {
                object parser = Invoke(parserGetDefaultMethod, null);
                return Invoke(parserParseMethod, parser, tokenizerJson);
            }

            public List<int> Encode(object tokenizer, string prompt)
            {
                object encoding = Invoke(tokenizerEncodeMethod, tokenizer, prompt, null, true);
                List<int> ids = new List<int>();
                Invoke(encodingGetIdsMethod, encoding, ids);
                return ids;
            }

            public string Decode(object tokenizer, IReadOnlyList<int> tokenIds, bool skipSpecialTokens)
            {
                return (string)Invoke(tokenizerDecodeMethod, tokenizer, tokenIds, skipSpecialTokens);
            }

            public object CreateTensorShape(int batch, int sequenceLength)
            {
                return tensorShape2DConstructor.Invoke(new object[] { batch, sequenceLength });
            }

            public object CreateIntTensor(object shape, int[] values)
            {
                return intTensorConstructor.Invoke(new object[] { shape, values, 0 });
            }

            public Array CreateTensorArray(int length)
            {
                return Array.CreateInstance(tensorBaseType, length);
            }

            public IList GetModelInputs(object model)
            {
                return modelInputsField.GetValue(model) as IList;
            }

            public string GetModelInputName(object input)
            {
                return modelInputNameField.GetValue(input) as string;
            }

            public void Schedule(object worker, Array inputs)
            {
                Invoke(scheduleMethod, worker, inputs);
            }

            public object PeekOutput(object worker)
            {
                return Invoke(peekOutputMethod, worker);
            }

            public object PeekOutput(object worker, string outputName)
            {
                return Invoke(peekOutputByNameMethod, worker, outputName);
            }

            public bool IsFloatTensor(object tensor)
            {
                return tensor != null && tensorFloatType.IsInstanceOfType(tensor);
            }

            public object ReadbackAndClone(object tensor)
            {
                return Invoke(readbackAndCloneMethod, tensor);
            }

            public float[] DownloadFloatArray(object tensor)
            {
                return (float[])Invoke(downloadFloatArrayMethod, tensor);
            }

            public TensorShapeInfo GetTensorShape(object tensor)
            {
                object shape = tensorShapeProperty.GetValue(tensor);
                int rank = (int)tensorShapeRankProperty.GetValue(shape);
                int[] dimensions = new int[Math.Max(0, rank)];
                for (int i = 0; i < dimensions.Length; i++)
                {
                    dimensions[i] = (int)tensorShapeItemProperty.GetValue(shape, new object[] { i });
                }

                return new TensorShapeInfo(dimensions);
            }

            private static object Invoke(MethodInfo method, object target, params object[] args)
            {
                try
                {
                    return method.Invoke(target, args);
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException != null)
                    {
                        throw ex.InnerException;
                    }

                    throw;
                }
            }

            private static Type GetRequiredType(string typeName, string assemblyName)
            {
                Type type = TryGetType(typeName, assemblyName);
                if (type == null)
                {
                    throw new NotSupportedException(MissingSentisMessage);
                }

                return type;
            }

            private static Type TryGetType(string typeName, string assemblyName)
            {
                return Type.GetType(typeName + ", " + assemblyName, false);
            }

            private static T Require<T>(T value, string memberName) where T : class
            {
                if (value == null)
                {
                    throw new MissingMemberException("Unity AI Inference/Sentis API member not found: " + memberName);
                }

                return value;
            }
        }
    }
}
