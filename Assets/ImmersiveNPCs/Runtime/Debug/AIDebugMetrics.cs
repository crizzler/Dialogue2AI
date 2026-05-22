using System;
using System.Threading;

namespace ImmersiveNPCs
{
    public sealed class AIDebugMetrics
    {
        private int cacheHits;
        private int cacheMisses;
        private int inflightRequests;

        public string lastProvider;
        public long lastLatencyMs;
        public string lastCacheKey;
        public bool lastFromCache;
        public DateTime lastUpdatedUtc;

        public int CacheHits => cacheHits;
        public int CacheMisses => cacheMisses;
        public int InflightRequests => inflightRequests;

        public void IncrementHits()
        {
            Interlocked.Increment(ref cacheHits);
        }

        public void IncrementMisses()
        {
            Interlocked.Increment(ref cacheMisses);
        }

        public void IncrementInflight()
        {
            Interlocked.Increment(ref inflightRequests);
        }

        public void DecrementInflight()
        {
            Interlocked.Decrement(ref inflightRequests);
        }
    }
}
