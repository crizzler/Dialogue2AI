using System.Threading;
using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public interface IPerceptionProvider
    {
        Task<PerceptionSnapshot> CaptureAsync(PerceptionRequest request, CancellationToken ct);
    }

    public struct PerceptionRequest
    {
        public UnityEngine.Vector3 origin;
        public float radius;
        public int maxSignals;
        public UnityEngine.LayerMask layerMask;
    }
}
