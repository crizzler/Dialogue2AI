using ImmersiveNPCs;
using NUnit.Framework;

namespace ImmersiveNPCs.Tests
{
    public class AICacheKeyTests
    {
        [Test]
        public void CacheKeyHasher_IsStable()
        {
            string input = "npc|summary|choice|perception|4|en";
            string hash1 = CacheKeyHasher.ComputeHash(input);
            string hash2 = CacheKeyHasher.ComputeHash(input);
            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void CacheKey_ChangesWithInput()
        {
            PerceptionSnapshot perception = new PerceptionSnapshot
            {
                summary = "nearby: tree",
                cacheKey = "abc"
            };

            string key1 = AICacheKey.BuildKey("npc", "sum", "choice", perception, 4, "en", string.Empty);
            string key2 = AICacheKey.BuildKey("npc", "sum", "choice2", perception, 4, "en", string.Empty);
            Assert.AreNotEqual(key1, key2);
        }
    }
}
