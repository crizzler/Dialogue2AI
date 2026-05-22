using System.Collections.Generic;
using System.Text;

namespace ImmersiveNPCs
{
    public class PerceptionSnapshot
    {
        public string summary;
        public string cacheKey;
        public List<PerceptionSignal> signals = new List<PerceptionSignal>();

        public static PerceptionSnapshot Empty()
        {
            return new PerceptionSnapshot
            {
                summary = string.Empty,
                cacheKey = string.Empty
            };
        }

        public void RebuildSummary()
        {
            StringBuilder builder = new StringBuilder(256);
            for (int i = 0; i < signals.Count; i++)
            {
                var signal = signals[i];
                builder.Append(signal.tag);
                if (!string.IsNullOrEmpty(signal.name))
                {
                    builder.Append(" (").Append(signal.name).Append(')');
                }
                builder.Append(" at ").Append(signal.distance.ToString("0.0")).Append("m");
                if (i < signals.Count - 1)
                {
                    builder.Append(", ");
                }
            }
            summary = builder.ToString();
            cacheKey = CacheKeyHasher.ComputeHash(summary ?? string.Empty);
        }
    }

    public struct PerceptionSignal
    {
        public string tag;
        public string name;
        public float distance;
    }
}
