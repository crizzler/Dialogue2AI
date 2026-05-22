using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public sealed class LocalInferenceEnginePlaceholder : ILocalInferenceEngine
    {
        public bool IsReady => true;
        public string Status => "Placeholder";
        public LocalEngineLoadingState LoadingState => LocalEngineLoadingState.Ready;

        public Task<bool> PreloadModelAsync(string modelPath, CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public Task<bool> WaitUntilReadyAsync(int timeoutMs, CancellationToken ct)
        {
            return Task.FromResult(true);
        }

        public async Task<string> GenerateAsync(LocalInferenceRequest request, CancellationToken ct)
        {
            await Task.Delay(50, ct).ConfigureAwait(false);

            string seedInput = (request.prompt ?? string.Empty) + "|" + request.npcId;
            string hash = CacheKeyHasher.ComputeHash(seedInput);
            int seed = 0;
            if (hash.Length >= 8)
            {
                seed = int.Parse(hash.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            Random rng = new Random(seed);
            string npcLine = BuildNpcLine(rng, request.npcId);
            string[] options = BuildOptions(rng, request.slots);

            StringBuilder builder = new StringBuilder(256);
            builder.Append("{\"npc_line\":\"");
            builder.Append(Escape(npcLine));
            builder.Append("\",\"options\":[");
            for (int i = 0; i < options.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append("\"").Append(Escape(options[i])).Append("\"");
            }
            builder.Append("],\"mood\":\"");
            builder.Append("calm");
            builder.Append("\",\"memory_delta\":\"\"}");
            return builder.ToString();
        }

        public async Task<float[]> EmbedAsync(LocalEmbeddingRequest request, CancellationToken ct)
        {
            await Task.Delay(10, ct).ConfigureAwait(false);

            string seedInput = request.text ?? string.Empty;
            string hash = CacheKeyHasher.ComputeHash(seedInput);
            int seed = 0;
            if (hash.Length >= 8)
            {
                seed = int.Parse(hash.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            Random rng = new Random(seed);
            int size = 256;
            float[] vector = new float[size];
            for (int i = 0; i < size; i++)
            {
                vector[i] = (float)rng.NextDouble();
            }
            return vector;
        }

        private static string BuildNpcLine(Random rng, string npcId)
        {
            string[] openers = { "I hear you", "That is interesting", "Hmm", "Let me think", "Well" };
            string[] verbs = { "consider", "see", "understand", "notice", "remember" };
            string[] nouns = { "the road ahead", "your request", "the town", "the plan", "the situation" };
            return openers[rng.Next(openers.Length)] + ", I " + verbs[rng.Next(verbs.Length)] + " " + nouns[rng.Next(nouns.Length)] + ".";
        }

        private static string[] BuildOptions(Random rng, int slots)
        {
            string[] verbs = { "Ask", "Challenge", "Wait", "Agree", "Refuse", "Listen" };
            string[] nouns = { "for details", "about rumors", "for help", "to trade", "to leave", "about the past" };
            string[] options = new string[Math.Max(1, slots)];
            for (int i = 0; i < options.Length; i++)
            {
                options[i] = verbs[rng.Next(verbs.Length)] + " " + nouns[rng.Next(nouns.Length)];
            }
            return options;
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
