using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public interface IAIProvider
    {
        Task<TurnResult> GenerateTurnAsync(AIContext context, CancellationToken ct);
    }
}
