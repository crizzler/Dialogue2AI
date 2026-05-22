namespace ImmersiveNPCs
{
    /// <summary>
    /// Quality presets that control the complexity vs latency tradeoff.
    /// These map to internal budgets, timeouts, and validation strictness.
    /// </summary>
    public enum QualityPreset
    {
        /// <summary>
        /// Fastest responses, minimal context, no planning phase.
        /// Good for: combat barks, ambient chatter, low-end hardware.
        /// </summary>
        FastSmall,

        /// <summary>
        /// Default mode. Moderate context, optional planning, reasonable validation.
        /// Good for: most NPCs, side quests, shops.
        /// </summary>
        Balanced,

        /// <summary>
        /// Larger context window, stricter validation, memory writes enabled.
        /// Good for: companion NPCs, recurring characters, important NPCs.
        /// </summary>
        DeepConversation,

        /// <summary>
        /// Maximum quality. Full context tiers, planning required, strict grounding.
        /// Good for: main quest NPCs, cinematics, critical story moments.
        /// </summary>
        CinematicQuality
    }
}
