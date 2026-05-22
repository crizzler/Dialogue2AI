using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    public sealed class OllamaProvider : IAIProvider, IAIProviderHealth, IPlanningProvider
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private readonly AIConversationSettings settings;

        public OllamaProvider(AIConversationSettings settings)
        {
            this.settings = settings;
        }

        public bool IsAvailable => settings != null && !string.IsNullOrEmpty(settings.ollamaEndpoint) && !string.IsNullOrEmpty(settings.ollamaModelName);

        public string Status
        {
            get
            {
                if (settings == null)
                {
                    return "Missing settings";
                }

                if (string.IsNullOrEmpty(settings.ollamaEndpoint))
                {
                    return "Missing endpoint";
                }

                if (string.IsNullOrEmpty(settings.ollamaModelName))
                {
                    return "Missing model";
                }

                return "Ready";
            }
        }

        public async Task<TurnResult> GenerateTurnAsync(AIContext context, CancellationToken ct)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int maxTokens = settings.maxTokens <= 0 ? -1 : settings.maxTokens;

            OllamaChatRequest payload = new OllamaChatRequest
            {
                model = settings.ollamaModelName,
                messages = new[]
                {
                    new OllamaMessage { role = "system", content = context.systemPrompt },
                    new OllamaMessage { role = "user", content = context.userPrompt }
                },
                options = new OllamaOptions
                {
                    temperature = settings.temperature,
                    top_p = settings.topP,
                    num_predict = maxTokens
                },
                stream = false
            };

            string json = JsonUtility.ToJson(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, settings.ollamaEndpoint);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var timeoutCts = new CancellationTokenSource(settings.requestTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            HttpResponseMessage response = await httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Ollama request failed: " + response.StatusCode);
            }

            string outputText = ExtractOllamaText(responseBody);
            TurnResult result = ParseOutput(outputText, context.slots);
            result.metadata = new ProviderMetadata
            {
                providerName = "Ollama",
                latencyMs = stopwatch.ElapsedMilliseconds,
                modelName = settings.ollamaModelName
            };

            return result;
        }

        private TurnResult ParseOutput(string outputText, int slots)
        {
            if (AIOutputValidator.TryParse(outputText, out TurnResult parsed))
            {
                return AIOutputValidator.Sanitize(parsed, slots, settings);
            }

            string json = AIOutputValidator.ExtractJsonSubstring(outputText);
            if (AIOutputValidator.TryParse(json, out parsed))
            {
                return AIOutputValidator.Sanitize(parsed, slots, settings);
            }

            return AIOutputValidator.CreateFallback(slots, settings);
        }

        private string ExtractOllamaText(string responseBody)
        {
            OllamaChatResponse parsed = JsonUtility.FromJson<OllamaChatResponse>(responseBody);
            if (parsed != null)
            {
                if (parsed.message != null && !string.IsNullOrEmpty(parsed.message.content))
                {
                    return parsed.message.content;
                }

                if (!string.IsNullOrEmpty(parsed.response))
                {
                    return parsed.response;
                }
            }

            return responseBody;
        }

        [Serializable]
        private class OllamaChatRequest
        {
            public string model;
            public OllamaMessage[] messages;
            public OllamaOptions options;
            public bool stream;
        }

        [Serializable]
        private class OllamaMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class OllamaOptions
        {
            public float temperature;
            public float top_p;
            public int num_predict;
        }

        [Serializable]
        private class OllamaChatResponse
        {
            public OllamaMessage message;
            public string response;
        }
        
        // === IPlanningProvider Implementation ===
        
        public bool SupportsPlanning => IsAvailable;
        
        public async Task<string> PlanAsync(AIContext planContext, int maxTokens, CancellationToken ct)
        {
            if (!IsAvailable)
            {
                return null;
            }
            
            int planMaxTokens = Math.Min(maxTokens, 128);
            
            OllamaChatRequest payload = new OllamaChatRequest
            {
                model = settings.ollamaModelName,
                messages = new[]
                {
                    new OllamaMessage { role = "system", content = planContext?.systemPrompt ?? string.Empty },
                    new OllamaMessage { role = "user", content = planContext?.userPrompt ?? "Plan the response." }
                },
                options = new OllamaOptions
                {
                    temperature = 0.3f, // Lower temperature for planning
                    top_p = settings.topP,
                    num_predict = planMaxTokens
                },
                stream = false
            };
            
            try
            {
                string json = JsonUtility.ToJson(payload);
                
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.ollamaEndpoint);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                
                using var timeoutCts = new CancellationTokenSource(5000); // 5 second timeout for planning
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                
                HttpResponseMessage response = await httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                
                return ExtractOllamaText(responseBody)?.Trim();
            }
            catch (Exception ex)
            {
                AILogger.Warn("[Ollama Planning] Failed: " + ex.Message);
                return null;
            }
        }
    }
}
