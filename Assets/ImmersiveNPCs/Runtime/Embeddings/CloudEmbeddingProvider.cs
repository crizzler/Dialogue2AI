using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    public sealed class CloudEmbeddingProvider : IEmbeddingProvider
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private readonly AIConversationSettings settings;

        public CloudEmbeddingProvider(AIConversationSettings settings)
        {
            this.settings = settings;
        }

        public bool IsAvailable
        {
            get
            {
                if (settings == null || settings.cloudProvider != CloudProviderMode.OpenAI)
                {
                    return false;
                }

                string key = ApiKeyResolver.ResolveCloud(settings);
                return !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(settings.embeddingEndpoint);
            }
        }

        public string Status
        {
            get
            {
                if (settings == null || string.IsNullOrEmpty(settings.embeddingEndpoint))
                {
                    return "Missing embedding endpoint";
                }

                if (settings.cloudProvider != CloudProviderMode.OpenAI)
                {
                    return "Cloud embeddings require OpenAI";
                }

                string key = ApiKeyResolver.ResolveCloud(settings);
                return string.IsNullOrEmpty(key) ? "Missing API key" : "Ready";
            }
        }

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string apiKey = ApiKeyResolver.ResolveCloud(settings);
            if (string.IsNullOrEmpty(apiKey))
            {
                return null;
            }

            string payload = BuildPayload(text);
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.embeddingEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var timeoutCts = new CancellationTokenSource(settings.requestTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            HttpResponseMessage response = await httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Embedding request failed: " + response.StatusCode);
            }

            OpenAIEmbeddingResponse parsed = JsonUtility.FromJson<OpenAIEmbeddingResponse>(responseBody);
            if (parsed != null && parsed.data != null && parsed.data.Length > 0)
            {
                return parsed.data[0].embedding;
            }

            return null;
        }

        private string BuildPayload(string text)
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append('{');
            builder.Append("\"model\":\"").Append(Escape(settings.embeddingModelName)).Append("\",");
            builder.Append("\"input\":\"").Append(Escape(text)).Append("\"");
            builder.Append('}');
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        [Serializable]
        private class OpenAIEmbeddingResponse
        {
            public EmbeddingData[] data;
        }

        [Serializable]
        private class EmbeddingData
        {
            public float[] embedding;
        }
    }
}
