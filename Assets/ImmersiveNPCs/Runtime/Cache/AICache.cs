using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public sealed class AICache
    {
        private readonly LruCache<string, AICacheEntry> memoryCache;
        private DiskCache diskCache;
        private int hits;
        private int misses;

        public AICache(int memoryEntries)
        {
            memoryCache = new LruCache<string, AICacheEntry>(memoryEntries);
        }

        public int Hits => hits;
        public int Misses => misses;

        public void ConfigureDisk(string rootPath, TimeSpan ttl)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                diskCache = null;
                return;
            }

            diskCache = new DiskCache(rootPath, ttl);
        }

        public void SetMemoryCapacity(int capacity)
        {
            memoryCache.SetCapacity(capacity);
        }

        public async Task<TurnResult> TryGetAsync(string key, CancellationToken ct)
        {
            if (memoryCache.TryGetValue(key, out AICacheEntry entry))
            {
                if (!IsExpired(entry) && !IsFallback(entry))
                {
                    hits++;
                    return entry.result?.Clone();
                }

                memoryCache.Remove(key);
            }

            if (diskCache != null)
            {
                AICacheEntry diskEntry = await diskCache.TryGetAsync(key, ct).ConfigureAwait(false);
                if (diskEntry != null)
                {
                    if (!IsExpired(diskEntry) && !IsFallback(diskEntry))
                    {
                        hits++;
                        memoryCache.AddOrUpdate(key, diskEntry);
                        return diskEntry.result?.Clone();
                    }

                    diskCache.Remove(key);
                }
            }

            misses++;
            return null;
        }

        public async Task StoreAsync(string key, TurnResult result, CancellationToken ct)
        {
            if (result == null)
            {
                return;
            }

            AICacheEntry entry = new AICacheEntry
            {
                createdUtcTicks = DateTime.UtcNow.Ticks,
                result = result.Clone()
            };

            memoryCache.AddOrUpdate(key, entry);

            if (diskCache != null)
            {
                await diskCache.StoreAsync(key, entry, ct).ConfigureAwait(false);
            }
        }

        private bool IsExpired(AICacheEntry entry)
        {
            return entry == null || entry.result == null;
        }

        private static bool IsFallback(AICacheEntry entry)
        {
            return entry != null && entry.result != null && entry.result.isFallback;
        }
    }
}
