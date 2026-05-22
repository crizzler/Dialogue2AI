using System;
using UnityEngine;

namespace ImmersiveNPCs
{
    public static class ApiKeyResolver
    {
        public static string Resolve(AIConversationSettings settings)
        {
            if (settings == null)
            {
                return string.Empty;
            }

            switch (settings.apiKeyMode)
            {
                case ApiKeyMode.EnvVarName:
                    if (LooksLikeApiKey(settings.apiKeyEnvVar))
                    {
                        return settings.apiKeyEnvVar.Trim();
                    }

                    return Environment.GetEnvironmentVariable(settings.apiKeyEnvVar ?? string.Empty) ?? string.Empty;
                case ApiKeyMode.TextAssetReference:
                    return settings.apiKeyTextAsset != null ? settings.apiKeyTextAsset.text.Trim() : string.Empty;
                case ApiKeyMode.InlineText:
                    return settings.apiKeyText != null ? settings.apiKeyText.Trim() : string.Empty;
                default:
                    return string.Empty;
            }
        }

        public static string ResolveCloud(AIConversationSettings settings)
        {
            if (settings == null)
            {
                return string.Empty;
            }

            switch (settings.apiKeyMode)
            {
                case ApiKeyMode.EnvVarName:
                    if (LooksLikeApiKey(settings.apiKeyEnvVar))
                    {
                        return settings.apiKeyEnvVar.Trim();
                    }

                    string configured = Environment.GetEnvironmentVariable(settings.apiKeyEnvVar ?? string.Empty) ?? string.Empty;
                    if (!string.IsNullOrEmpty(configured))
                    {
                        return configured;
                    }

                    string defaultEnvVar = GetDefaultCloudApiKeyEnvVar(settings.cloudProvider);
                    if (!string.IsNullOrEmpty(defaultEnvVar) && defaultEnvVar != settings.apiKeyEnvVar)
                    {
                        return Environment.GetEnvironmentVariable(defaultEnvVar) ?? string.Empty;
                    }

                    return string.Empty;
                case ApiKeyMode.TextAssetReference:
                    return settings.apiKeyTextAsset != null ? settings.apiKeyTextAsset.text.Trim() : string.Empty;
                case ApiKeyMode.InlineText:
                    return settings.apiKeyText != null ? settings.apiKeyText.Trim() : string.Empty;
                default:
                    return string.Empty;
            }
        }

        public static bool LooksLikeApiKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            return trimmed.StartsWith("sk-", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("sk-ant-", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("anthropic-", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetDefaultCloudApiKeyEnvVar(CloudProviderMode provider)
        {
            switch (provider)
            {
                case CloudProviderMode.Claude:
                    return "ANTHROPIC_API_KEY";
                case CloudProviderMode.DeepSeek:
                    return "DEEPSEEK_API_KEY";
                case CloudProviderMode.OpenAI:
                default:
                    return "OPENAI_API_KEY";
            }
        }
    }
}
