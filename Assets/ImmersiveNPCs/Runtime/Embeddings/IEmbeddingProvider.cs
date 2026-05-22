using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public interface IEmbeddingProvider
    {
        bool IsAvailable { get; }
        string Status { get; }
        Task<float[]> EmbedAsync(string text, CancellationToken ct);
    }
}
