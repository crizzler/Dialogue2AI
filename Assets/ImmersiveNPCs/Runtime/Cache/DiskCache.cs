using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    public sealed class DiskCache
    {
        private readonly string rootPath;
        private readonly TimeSpan ttl;

        public DiskCache(string rootPath, TimeSpan ttl)
        {
            this.rootPath = rootPath;
            this.ttl = ttl;
        }

        public async Task<AICacheEntry> TryGetAsync(string key, CancellationToken ct)
        {
            string path = GetPathForKey(key);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                AICacheEntry entry = JsonUtility.FromJson<AICacheEntry>(json);
                if (entry == null)
                {
                    return null;
                }

                if (IsExpired(entry))
                {
                    TryDelete(path);
                    return null;
                }

                return entry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task StoreAsync(string key, AICacheEntry entry, CancellationToken ct)
        {
            try
            {
                Directory.CreateDirectory(rootPath);
                string json = JsonUtility.ToJson(entry);
                string path = GetPathForKey(key);
                await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        public void Remove(string key)
        {
            string path = GetPathForKey(key);
            TryDelete(path);
        }

        private string GetPathForKey(string key)
        {
            return Path.Combine(rootPath, key + ".json");
        }

        private bool IsExpired(AICacheEntry entry)
        {
            if (ttl <= TimeSpan.Zero)
            {
                return false;
            }

            DateTime createdUtc = new DateTime(entry.createdUtcTicks, DateTimeKind.Utc);
            return (DateTime.UtcNow - createdUtc) > ttl;
        }

        private void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
            }
        }
    }
}
