using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    public sealed class OpenAICompatibleLocalProvider : IAIProvider, IAIProviderHealth
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private readonly AIConversationSettings settings;

        public OpenAICompatibleLocalProvider(AIConversationSettings settings)
        {
            this.settings = settings;
        }

        public bool IsAvailable
        {
            get
            {
                if (settings == null)
                {
                    return false;
                }

                if (string.IsNullOrEmpty(settings.localOpenAIEndpoint) || string.IsNullOrEmpty(settings.localOpenAIModelName))
                {
                    return false;
                }

                if (!settings.localOpenAIUseApiKey)
                {
                    return true;
                }

                return !string.IsNullOrEmpty(ApiKeyResolver.Resolve(settings));
            }
        }

        public string Status
        {
            get
            {
                if (settings == null)
                {
                    return "Missing settings";
                }

                if (string.IsNullOrEmpty(settings.localOpenAIEndpoint))
                {
                    return "Missing endpoint";
                }

                if (string.IsNullOrEmpty(settings.localOpenAIModelName))
                {
                    return "Missing model";
                }

                if (settings.localOpenAIUseApiKey && string.IsNullOrEmpty(ApiKeyResolver.Resolve(settings)))
                {
                    return "Missing API key";
                }

                return "Ready";
            }
        }

        public async Task<TurnResult> GenerateTurnAsync(AIContext context, CancellationToken ct)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string payload = BuildChatCompletionsPayload(context);

            for (int attempt = 0; attempt <= settings.retryCount; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Post, settings.localOpenAIEndpoint);

                if (settings.localOpenAIUseApiKey)
                {
                    string apiKey = ApiKeyResolver.Resolve(settings);
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        throw new InvalidOperationException("API key is missing.");
                    }
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }

                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var timeoutCts = new CancellationTokenSource(settings.requestTimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                try
                {
                    HttpResponseMessage response = await httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        if (attempt < settings.retryCount)
                        {
                            await Task.Delay(settings.retryBackoffMs * (attempt + 1), ct).ConfigureAwait(false);
                            continue;
                        }

                        throw new InvalidOperationException("Local OpenAI-compatible request failed: " + response.StatusCode);
                    }

                    string outputText = ExtractChatCompletionText(responseBody);
                    TurnResult result = ParseOutput(outputText, context.slots);
                    result.metadata = new ProviderMetadata
                    {
                        providerName = "OpenAI-Compatible",
                        latencyMs = stopwatch.ElapsedMilliseconds,
                        modelName = settings.localOpenAIModelName
                    };

                    return result;
                }
                catch (Exception)
                {
                    if (attempt >= settings.retryCount)
                    {
                        throw;
                    }

                    await Task.Delay(settings.retryBackoffMs * (attempt + 1), ct).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("Local OpenAI-compatible request failed.");
        }

        private string BuildChatCompletionsPayload(AIContext context)
        {
            int maxTokens = settings.maxTokens <= 0 ? -1 : settings.maxTokens;
            StringBuilder builder = new StringBuilder(1024);
            builder.Append('{');
            builder.Append("\"model\":\"").Append(Escape(settings.localOpenAIModelName)).Append("\",");
            builder.Append("\"messages\":[");
            builder.Append("{\"role\":\"system\",\"content\":\"").Append(Escape(context.systemPrompt)).Append("\"},");
            builder.Append("{\"role\":\"user\",\"content\":\"").Append(Escape(context.userPrompt)).Append("\"}");
            builder.Append("],");
            builder.Append("\"temperature\":").Append(settings.temperature.ToString("0.###")).Append(',');
            builder.Append("\"max_tokens\":").Append(maxTokens).Append(',');
            builder.Append("\"top_p\":").Append(settings.topP.ToString("0.###")).Append(',');
            builder.Append("\"presence_penalty\":").Append(settings.presencePenalty.ToString("0.###")).Append(',');
            builder.Append("\"frequency_penalty\":").Append(settings.frequencyPenalty.ToString("0.###"));
            builder.Append('}');
            return builder.ToString();
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

        private string ExtractChatCompletionText(string responseBody)
        {
            OpenAIChatResponse parsed = JsonUtility.FromJson<OpenAIChatResponse>(responseBody);
            if (parsed != null && parsed.choices != null && parsed.choices.Length > 0)
            {
                var message = parsed.choices[0].message;
                if (message != null && !string.IsNullOrEmpty(message.content))
                {
                    return message.content;
                }
            }

            return responseBody;
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
        private class OpenAIChatResponse
        {
            public Choice[] choices;
        }

        [Serializable]
        private class Choice
        {
            public Message message;
        }

        [Serializable]
        private class Message
        {
            public string content;
        }
    }
}
