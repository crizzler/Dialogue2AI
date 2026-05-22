using System.Threading.Tasks;

namespace ImmersiveNPCs
{
    public static class TaskExtensions
    {
        public static void Forget(this Task task)
        {
            if (task == null)
            {
                return;
            }

            task.ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    AILogger.Warn("Background task failed: " + t.Exception.GetBaseException().Message);
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
