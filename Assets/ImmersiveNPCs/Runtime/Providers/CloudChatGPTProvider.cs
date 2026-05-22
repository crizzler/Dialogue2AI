using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    public sealed class CloudLLMProvider : IAIProvider, IAIProviderHealth
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string AnthropicVersion = "2023-06-01";

        private readonly AIConversationSettings settings;

        public CloudLLMProvider(AIConversationSettings settings)
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

                string key = ApiKeyResolver.ResolveCloud(settings);
                return !string.IsNullOrEmpty(key)
                    && !string.IsNullOrEmpty(settings.cloudEndpoint)
                    && !string.IsNullOrEmpty(settings.cloudModelName);
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

                if (string.IsNullOrEmpty(settings.cloudEndpoint))
                {
                    return "Missing endpoint";
                }

                if (string.IsNullOrEmpty(settings.cloudModelName))
                {
                    return "Missing model";
                }

                string key = ApiKeyResolver.ResolveCloud(settings);
                if (string.IsNullOrEmpty(key))
                {
                    return settings.apiKeyMode == ApiKeyMode.EnvVarName
                        ? "Missing API key (" + ApiKeyResolver.GetDefaultCloudApiKeyEnvVar(settings.cloudProvider) + ")"
                        : "Missing API key";
                }

                return "Ready";
            }
        }

        public async Task<TurnResult> GenerateTurnAsync(AIContext context, CancellationToken ct)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string apiKey = ApiKeyResolver.ResolveCloud(settings);
            if (string.IsNullOrEmpty(apiKey))
            {
                string expected = settings.apiKeyMode == ApiKeyMode.EnvVarName
                    ? " Expected environment variable: " + ApiKeyResolver.GetDefaultCloudApiKeyEnvVar(settings.cloudProvider) + "."
                    : string.Empty;
                throw new InvalidOperationException("API key is missing." + expected);
            }

            string endpoint = settings.cloudEndpoint;
            CloudProviderMode provider = settings.cloudProvider;
            bool useResponses = provider == CloudProviderMode.OpenAI
                && endpoint.IndexOf("responses", StringComparison.OrdinalIgnoreCase) >= 0
                && endpoint.IndexOf("chat/completions", StringComparison.OrdinalIgnoreCase) < 0;
            string payload = BuildPayload(provider, context, useResponses);

            for (int attempt = 0; attempt <= settings.retryCount; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                ConfigureAuthenticationHeaders(request, provider, apiKey);
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

                        throw new InvalidOperationException("Cloud request failed: " + response.StatusCode);
                    }

                    string outputText = ExtractOutputText(provider, responseBody, useResponses);

                    TurnResult result = ParseOutput(outputText, context.slots);
                    result.metadata = new ProviderMetadata
                    {
                        providerName = GetProviderName(provider),
                        latencyMs = stopwatch.ElapsedMilliseconds,
                        modelName = settings.cloudModelName
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

            throw new InvalidOperationException("Cloud request failed.");
        }

        private string BuildPayload(CloudProviderMode provider, AIContext context, bool useResponses)
        {
            switch (provider)
            {
                case CloudProviderMode.Claude:
                    return BuildClaudeMessagesPayload(context);
                case CloudProviderMode.DeepSeek:
                    return BuildChatCompletionsPayload(context);
                case CloudProviderMode.OpenAI:
                default:
                    return useResponses ? BuildResponsesPayload(context) : BuildChatCompletionsPayload(context);
            }
        }

        private static void ConfigureAuthenticationHeaders(HttpRequestMessage request, CloudProviderMode provider, string apiKey)
        {
            if (provider == CloudProviderMode.Claude)
            {
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", AnthropicVersion);
                return;
            }

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        private static string ExtractOutputText(CloudProviderMode provider, string responseBody, bool useResponses)
        {
            switch (provider)
            {
                case CloudProviderMode.Claude:
                    return ExtractClaudeMessagesText(responseBody);
                case CloudProviderMode.DeepSeek:
                    return ExtractChatCompletionText(responseBody);
                case CloudProviderMode.OpenAI:
                default:
                    return useResponses ? ExtractResponsesText(responseBody) : ExtractChatCompletionText(responseBody);
            }
        }

        private static string GetProviderName(CloudProviderMode provider)
        {
            switch (provider)
            {
                case CloudProviderMode.Claude:
                    return "Claude";
                case CloudProviderMode.DeepSeek:
                    return "DeepSeek";
                case CloudProviderMode.OpenAI:
                default:
                    return "OpenAI";
            }
        }

        private string BuildResponsesPayload(AIContext context)
        {
            int maxTokens = settings.maxTokens < 1 ? 1 : settings.maxTokens;
            StringBuilder builder = new StringBuilder(1024);
            builder.Append('{');
            builder.Append("\"model\":\"").Append(Escape(settings.cloudModelName)).Append("\",");
            builder.Append("\"input\":[");
            AppendResponseMessage(builder, "system", context.systemPrompt);
            builder.Append(',');
            AppendResponseMessage(builder, "user", context.userPrompt);
            builder.Append("],");
            builder.Append("\"temperature\":").Append(FormatFloat(settings.temperature)).Append(',');
            builder.Append("\"max_output_tokens\":").Append(maxTokens).Append(',');
            builder.Append("\"top_p\":").Append(FormatFloat(settings.topP)).Append(',');
            builder.Append("\"presence_penalty\":").Append(FormatFloat(settings.presencePenalty)).Append(',');
            builder.Append("\"frequency_penalty\":").Append(FormatFloat(settings.frequencyPenalty));
            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendResponseMessage(StringBuilder builder, string role, string content)
        {
            builder.Append('{');
            builder.Append("\"role\":\"").Append(role).Append("\",");
            builder.Append("\"content\":[{\"type\":\"input_text\",\"text\":\"");
            builder.Append(Escape(content));
            builder.Append("\"}]}" );
        }

        private string BuildChatCompletionsPayload(AIContext context)
        {
            int maxTokens = settings.maxTokens < 1 ? 1 : settings.maxTokens;
            StringBuilder builder = new StringBuilder(1024);
            builder.Append('{');
            builder.Append("\"model\":\"").Append(Escape(settings.cloudModelName)).Append("\",");
            builder.Append("\"messages\":[");
            builder.Append("{\"role\":\"system\",\"content\":\"").Append(Escape(context.systemPrompt)).Append("\"},");
            builder.Append("{\"role\":\"user\",\"content\":\"").Append(Escape(context.userPrompt)).Append("\"}");
            builder.Append("],");
            builder.Append("\"temperature\":").Append(FormatFloat(settings.temperature)).Append(',');
            builder.Append("\"max_tokens\":").Append(maxTokens).Append(',');
            builder.Append("\"top_p\":").Append(FormatFloat(settings.topP)).Append(',');
            builder.Append("\"presence_penalty\":").Append(FormatFloat(settings.presencePenalty)).Append(',');
            builder.Append("\"frequency_penalty\":").Append(FormatFloat(settings.frequencyPenalty));
            builder.Append('}');
            return builder.ToString();
        }

        private string BuildClaudeMessagesPayload(AIContext context)
        {
            int maxTokens = settings.maxTokens < 1 ? 1 : settings.maxTokens;
            StringBuilder builder = new StringBuilder(1024);
            builder.Append('{');
            builder.Append("\"model\":\"").Append(Escape(settings.cloudModelName)).Append("\",");
            builder.Append("\"max_tokens\":").Append(maxTokens).Append(',');
            builder.Append("\"temperature\":").Append(FormatFloat(settings.temperature)).Append(',');
            builder.Append("\"top_p\":").Append(FormatFloat(settings.topP)).Append(',');
            builder.Append("\"system\":\"").Append(Escape(context.systemPrompt)).Append("\",");
            builder.Append("\"messages\":[");
            builder.Append("{\"role\":\"user\",\"content\":\"").Append(Escape(context.userPrompt)).Append("\"}");
            builder.Append(']');
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

        private static string ExtractResponsesText(string responseBody)
        {
            OpenAIResponsesResponse parsed = JsonUtility.FromJson<OpenAIResponsesResponse>(responseBody);
            if (parsed != null)
            {
                if (!string.IsNullOrEmpty(parsed.output_text))
                {
                    return parsed.output_text;
                }

                if (parsed.output != null && parsed.output.Length > 0)
                {
                    for (int i = 0; i < parsed.output.Length; i++)
                    {
                        var item = parsed.output[i];
                        if (item != null && item.content != null)
                        {
                            for (int j = 0; j < item.content.Length; j++)
                            {
                                var content = item.content[j];
                                if (content != null && !string.IsNullOrEmpty(content.text))
                                {
                                    return content.text;
                                }
                            }
                        }
                    }
                }
            }

            return responseBody;
        }

        private static string ExtractChatCompletionText(string responseBody)
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

        private static string ExtractClaudeMessagesText(string responseBody)
        {
            ClaudeMessagesResponse parsed = JsonUtility.FromJson<ClaudeMessagesResponse>(responseBody);
            if (parsed != null && parsed.content != null)
            {
                for (int i = 0; i < parsed.content.Length; i++)
                {
                    ClaudeContent content = parsed.content[i];
                    if (content != null && !string.IsNullOrEmpty(content.text))
                    {
                        return content.text;
                    }
                }
            }

            return responseBody;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
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
        private class OpenAIResponsesResponse
        {
            public string output_text;
            public OutputItem[] output;
        }

        [Serializable]
        private class OutputItem
        {
            public OutputContent[] content;
        }

        [Serializable]
        private class OutputContent
        {
            public string type;
            public string text;
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

        [Serializable]
        private class ClaudeMessagesResponse
        {
            public ClaudeContent[] content;
        }

        [Serializable]
        private class ClaudeContent
        {
            public string type;
            public string text;
        }
    }
}
