using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ImmersiveNPCs
{
    [AddComponentMenu("Immersive NPCs/Perception/Unity Scene Perception")]
    public class UnityScenePerceptionProvider : MonoBehaviour, IPerceptionProvider
    {
        [SerializeField] private LayerMask layerMask = ~0;
        [SerializeField] private int maxColliders = 32;

        private Collider[] buffer;

        private void Awake()
        {
            buffer = new Collider[Mathf.Max(8, maxColliders)];
        }

        public Task<PerceptionSnapshot> CaptureAsync(PerceptionRequest request, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled<PerceptionSnapshot>(ct);
            }

            int count = Physics.OverlapSphereNonAlloc(request.origin, request.radius, buffer, request.layerMask, QueryTriggerInteraction.Ignore);
            List<PerceptionSignal> signals = new List<PerceptionSignal>(count);

            for (int i = 0; i < count; i++)
            {
                Collider col = buffer[i];
                if (col == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(request.origin, col.transform.position);
                signals.Add(new PerceptionSignal
                {
                    tag = col.tag,
                    name = col.name,
                    distance = distance
                });
            }

            signals.Sort((a, b) => a.distance.CompareTo(b.distance));

            int maxSignals = request.maxSignals > 0 ? request.maxSignals : signals.Count;
            if (signals.Count > maxSignals)
            {
                signals.RemoveRange(maxSignals, signals.Count - maxSignals);
            }

            PerceptionSnapshot snapshot = new PerceptionSnapshot
            {
                signals = signals
            };
            snapshot.RebuildSummary();
            return Task.FromResult(snapshot);
        }

        public LayerMask LayerMask => layerMask;
    }
}
