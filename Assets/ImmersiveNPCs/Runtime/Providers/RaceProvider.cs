using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public sealed class RaceProvider : IAIProvider, IAIProviderHealth
    {
        private readonly IAIProvider localProvider;
        private readonly IAIProvider cloudProvider;

        public RaceProvider(IAIProvider localProvider, IAIProvider cloudProvider)
        {
            this.localProvider = localProvider;
            this.cloudProvider = cloudProvider;
        }

        public bool IsAvailable => IsProviderAvailable(localProvider) || IsProviderAvailable(cloudProvider);
        public string Status => IsAvailable ? "Ready" : "No available providers";

        public async Task<TurnResult> GenerateTurnAsync(AIContext context, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            bool localAvailable = IsProviderAvailable(localProvider);
            bool cloudAvailable = IsProviderAvailable(cloudProvider);

            Task<TurnResult> localTask = localAvailable ? localProvider.GenerateTurnAsync(context, cts.Token) : Task.FromException<TurnResult>(new InvalidOperationException("Local provider unavailable"));
            Task<TurnResult> cloudTask = cloudAvailable ? cloudProvider.GenerateTurnAsync(context, cts.Token) : Task.FromException<TurnResult>(new InvalidOperationException("Cloud provider unavailable"));

            List<Task<TurnResult>> pending = new List<Task<TurnResult>> { localTask, cloudTask };
            Exception lastError = null;

            while (pending.Count > 0)
            {
                Task<TurnResult> finished = await Task.WhenAny(pending).ConfigureAwait(false);
                pending.Remove(finished);

                if (finished.IsCanceled)
                {
                    continue;
                }

                if (finished.IsFaulted)
                {
                    lastError = finished.Exception != null ? finished.Exception.GetBaseException() : null;
                    continue;
                }

                TurnResult result = finished.Result;
                cts.Cancel();
                return result;
            }

            throw lastError ?? new InvalidOperationException("All providers failed.");
        }

        private static bool IsProviderAvailable(IAIProvider provider)
        {
            if (provider == null)
            {
                return false;
            }

            if (provider is IAIProviderHealth health)
            {
                return health.IsAvailable;
            }

            return true;
        }
    }
}
