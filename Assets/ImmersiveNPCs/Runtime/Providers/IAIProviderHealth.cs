namespace ImmersiveNPCs
{
    public interface IAIProviderHealth
    {
        bool IsAvailable { get; }
        string Status { get; }
    }
}
