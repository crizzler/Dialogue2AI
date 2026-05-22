namespace ImmersiveNPCs
{
    public enum MemoryScopeMode
    {
        GlobalOnly = 0,
        PerNpcOnly = 1,
        GlobalAndNpc = 2
    }

    public enum EmbeddingProviderMode
    {
        Auto = 0,
        Local = 1,
        Cloud = 2
    }

    public enum MemorySourceType
    {
        PlayerChoice = 0,
        NpcReply = 1,
        DesignerNote = 2
    }
}
