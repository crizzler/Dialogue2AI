#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace ImmersiveNPCs.Editor
{
    internal static class HuggingFaceApiClient
    {
        private const string ApiBase = "https://huggingface.co/api";

        public static async Task<List<HuggingFaceModel>> SearchModelsAsync(string query, int limit, bool filterGguf, bool filterSentis, bool filterTextGeneration, string token)
        {
            string url = BuildSearchUrl(query, limit, filterGguf, filterSentis, filterTextGeneration);
            string json = await GetTextAsync(url, token);
            HuggingFaceModelList list = JsonHelper.FromJsonArray<HuggingFaceModelList, HuggingFaceModel>(json);
            return list != null && list.items != null ? new List<HuggingFaceModel>(list.items) : new List<HuggingFaceModel>();
        }

        public static async Task<List<HuggingFaceFile>> GetModelFilesAsync(string modelId, string token)
        {
            if (string.IsNullOrEmpty(modelId))
            {
                return new List<HuggingFaceFile>();
            }

            string url = ApiBase + "/models/" + EscapePath(modelId) + "/tree/main?recursive=1";
            string json = await GetTextAsync(url, token);
            HuggingFaceFileList list = JsonHelper.FromJsonArray<HuggingFaceFileList, HuggingFaceFile>(json);
            return list != null && list.items != null ? new List<HuggingFaceFile>(list.items) : new List<HuggingFaceFile>();
        }

        public static string BuildDownloadUrl(string modelId, string filePath)
        {
            return "https://huggingface.co/" + EscapePath(modelId) + "/resolve/main/" + EscapePath(filePath);
        }

        private static string BuildSearchUrl(string query, int limit, bool filterGguf, bool filterSentis, bool filterTextGeneration)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(query))
            {
                parts.Add("search=" + UnityWebRequest.EscapeURL(query));
            }

            if (filterGguf)
            {
                parts.Add("filter=gguf");
            }

            if (filterSentis)
            {
                parts.Add("filter=unity-sentis");
            }

            if (filterTextGeneration)
            {
                parts.Add("pipeline_tag=text-generation");
            }

            if (limit > 0)
            {
                parts.Add("limit=" + limit);
            }

            string suffix = parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
            return ApiBase + "/models" + suffix;
        }

        private static async Task<string> GetTextAsync(string url, string token)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                if (!string.IsNullOrEmpty(token))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + token);
                }

                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                await AwaitRequestAsync(op);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string message = request.error;
                    if (request.responseCode == 429)
                    {
                        message = "Rate limited by Hugging Face. Try again later or add a token.";
                    }
                    else if (request.responseCode == 401 || request.responseCode == 403)
                    {
                        message = "Authentication required (gated models or token missing).";
                    }
                    throw new Exception(message);
                }

                return request.downloadHandler.text;
            }
        }

        private static Task AwaitRequestAsync(UnityWebRequestAsyncOperation op)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            op.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }

        private static string EscapePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = UnityWebRequest.EscapeURL(parts[i]);
            }

            return string.Join("/", parts);
        }
    }

    internal static class JsonHelper
    {
        public static TWrapper FromJsonArray<TWrapper, TItem>(string json)
            where TWrapper : class, new()
        {
            if (string.IsNullOrEmpty(json))
            {
                return new TWrapper();
            }

            string wrapped = "{\"items\":" + json + "}";
            return UnityEngine.JsonUtility.FromJson<TWrapper>(wrapped) ?? new TWrapper();
        }
    }
}
#endif
