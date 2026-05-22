using System.Threading;
using System.Threading.Tasks;
using ImmersiveNPCs;
using NUnit.Framework;

namespace ImmersiveNPCs.Tests
{
    public class RaceProviderTests
    {
        private class TestProvider : IAIProvider
        {
            private readonly TaskCompletionSource<TurnResult> tcs = new TaskCompletionSource<TurnResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool WasCancelled { get; private set; }

            public Task<TurnResult> GenerateTurnAsync(AIContext context, CancellationToken ct)
            {
                ct.Register(() =>
                {
                    WasCancelled = true;
                    tcs.TrySetCanceled();
                });
                return tcs.Task;
            }

            public void Complete(TurnResult result)
            {
                tcs.TrySetResult(result);
            }
        }

        [Test]
        public async Task RaceProvider_CancelsLoser()
        {
            var fast = new TestProvider();
            var slow = new TestProvider();
            var race = new RaceProvider(fast, slow);

            AIContext context = new AIContext { npcId = "npc", slots = 2 };

            Task<TurnResult> raceTask = race.GenerateTurnAsync(context, CancellationToken.None);
            fast.Complete(new TurnResult { npcLine = "fast" });
            TurnResult result = await raceTask;

            Assert.AreEqual("fast", result.npcLine);
            await Task.Delay(20);
            Assert.IsTrue(slow.WasCancelled);
        }
    }
}
