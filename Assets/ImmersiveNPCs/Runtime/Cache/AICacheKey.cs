using System.Text;

namespace ImmersiveNPCs
{
    public static class AICacheKey
    {
        public static string BuildKey(string npcId, string summary, string lastChoice, PerceptionSnapshot perception, int slots, string language, string memoryKey = "")
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append(npcId ?? string.Empty).Append('|');
            builder.Append(summary ?? string.Empty).Append('|');
            builder.Append(lastChoice ?? string.Empty).Append('|');
            builder.Append(perception != null ? perception.cacheKey : string.Empty).Append('|');
            builder.Append(slots).Append('|');
            builder.Append(language ?? string.Empty).Append('|');
            builder.Append(memoryKey ?? string.Empty);
            return CacheKeyHasher.ComputeHash(builder.ToString());
        }
    }
}
