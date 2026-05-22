# Tiered Context Pipeline - Migration Notes

## Overview

This update introduces a tiered context pipeline to ImmersiveNPCs that provides:
1. **Tiered context memory** - Curated context instead of raw scrollback
2. **Explicit memory writes** - Only commit-worthy events are stored
3. **Grounded world-state validation** - Claims validated against snapshots
4. **Latency pipeline** - Optional plan-then-generate two-stage flow
5. **Script authority hierarchy** - Scripted dialogue wins for main quest beats
6. **Developer-facing quality presets** - No VRAM talk, just choose a preset

## Backwards Compatibility

**All new features are opt-in.** The default behavior is unchanged:

```csharp
// Default settings - existing behavior preserved
enableTieredContext = false;  // Legacy pipeline used
qualityPreset = QualityPreset.Balanced;
```

To enable the new pipeline:

```csharp
settings.enableTieredContext = true;
settings.qualityPreset = QualityPreset.Balanced; // or FastSmall, DeepConversation, CinematicQuality
```

## What Stayed Compatible

| Component | Status | Notes |
|-----------|--------|-------|
| `IAIProvider` interface | ✅ Unchanged | Existing providers work without modification |
| `AIConversationService.GetOrGenerateAsync()` | ✅ Unchanged | Same signature, routes internally |
| `AIConversationState` | ✅ Unchanged | Raw scrollback still supported |
| `AIContextBuilder` | ✅ Unchanged | Used when `enableTieredContext = false` |
| `AIOutputValidator` | ✅ Unchanged | Quality checks still applied |
| `LocalLlamaEngine` locking | ✅ Preserved | Generation lock semantics unchanged |
| `TurnResult` | ✅ Unchanged | Same structure returned |
| `NpcProfile` | ✅ New | Rich per-NPC configuration, replaces NPCPersona |
| `GlobalWorldState` | ✅ New | Centralized world context for all NPCs |

## New Interfaces (Optional)

Providers can optionally implement these for enhanced functionality:

```csharp
// For streaming support (optional)
public interface IStreamingInferenceProvider
{
    IAsyncEnumerable<StreamingChunk> GenerateStreamAsync(AIContext context, CancellationToken ct);
}

// For planning support (optional)
public interface IPlanningProvider
{
    Task<string> PlanAsync(string planPrompt, int maxTokens, CancellationToken ct);
}
```

Both `LocalLLMProvider` and `OllamaProvider` now implement `IPlanningProvider`.

## Quality Presets

| Preset | Planning | Memory Writes | Streaming | Validation |
|--------|----------|---------------|-----------|------------|
| FastSmall | ❌ | ❌ | ❌ | Lenient |
| Balanced | ✅ | ✅ | ❌ | Moderate |
| DeepConversation | ✅ | ✅ | ✅ | Moderate |
| CinematicQuality | ✅ | ✅ | ✅ | Strict |

## Context Tiers

When `enableTieredContext = true`, context is assembled from:

| Tier | Content | Default Budget |
|------|---------|----------------|
| A | Scene facts (location, time, weather) | 256 tokens |
| B | NPC identity (persona, relationships) | 512 tokens |
| C | Episodic memory (commit-worthy events) | 384 tokens |
| D | Retrieval (RAG snippets) | 256 tokens |

## Memory Events (Commit-Worthy)

Only these event types are persisted:

- `PlayerNameRevealed` - Player told their name
- `PromiseMade` - NPC or player made a promise
- `ThreatMade` - Threat was issued
- `RelationshipShift` - Significant relationship change
- `QuestDecision` - Quest choice was made
- `LoreRevelation` - Important world fact revealed
- `ItemExchange` - Item given or received
- `SecretShared` - Secret was shared

Small talk is **not** stored.

## World State Validation

When `enableWorldStateValidation = true`, responses are checked against:

- Valid locations (can't mention places that don't exist)
- Known NPCs (can't reference unknown characters)
- Player inventory (can't claim player has items they don't)
- Active quest flags

Set validation strictness:
```csharp
settings.validationStrictness = ResponseValidator.StrictnessLevel.Moderate;
```

## Script Authority

When `enableScriptAuthority = true`:

- **Main quest beats**: Scripted dialogue wins
- **Side chatter**: LLM response wins

Register required responses:
```csharp
var arbiter = conversationService.ScriptArbiter;
arbiter.RegisterRequiredResponse("quest_start", "Welcome, traveler. I have a task for you.");
```

With Yarn Spinner, use node tags:
```yarn
title: QuestStart
tags: main_quest, act1
---
<<set $quest_state = "active">>
===
```

## Populating World State

Set up a snapshot builder before generation:

```csharp
var builder = new SnapshotBuilder()
    .Begin()
    .SetLocation("Harbor")
    .SetTimeOfDay("Evening")
    .SetActiveQuest("find_artifact")
    .AddValidLocation("Harbor")
    .AddValidLocation("Tavern")
    .AddKnownNpc("Captain Morgan")
    .SetScriptedBeat(false);

conversationService.CurrentSnapshotBuilder = builder;
```

## Debugging

Enable timing and validation logs:

```csharp
settings.enableTimingLogs = true;      // Pipeline stage latencies
settings.enableValidationLogs = true;  // Validation violations
```

Sample timing output:
```
[TieredPipeline] Local In-Process | SnapshotBuild=2ms ContextAssemble=5ms IntentPlanning=150ms ResponseGeneration=1200ms Validation=10ms | Total=1367ms
```

## Upgrade Steps

1. **No changes required** for existing functionality
2. To enable tiered context:
   ```csharp
   settings.enableTieredContext = true;
   settings.qualityPreset = QualityPreset.Balanced;
   ```
3. To use world state validation:
   ```csharp
   settings.enableWorldStateValidation = true;
   // Populate CurrentSnapshotBuilder before each turn
   ```
4. To use structured memory:
   ```csharp
   settings.enableStructuredMemory = true;
   // Access via: conversationService.StructuredMemory
   ```

## File Inventory

### New Files

**Runtime/Settings/**
- `QualityPreset.cs` - Quality preset enum

**Runtime/Context/**
- `WorldStateSnapshot.cs` - Immutable world state snapshot
- `ContextTierBudgets.cs` - Token budgets per tier
- `SnapshotBuilder.cs` - Fluent builder for snapshots
- `ContextAssembler.cs` - Assembles tiered context

**Runtime/Memory/**
- `MemoryEvent.cs` - Structured memory events
- `MemoryStore.cs` - Per-NPC event storage
- `MemoryWritePolicy.cs` - Determines commit-worthy events
- `MemorySummarizer.cs` - Compresses episodic memories

**Runtime/Pipeline/**
- `IntentPlan.cs` - Planning phase data structures
- `IntentPlanner.cs` - Stage 1 planning
- `ResponseValidator.cs` - World state validation
- `ScriptAuthorityArbiter.cs` - Script vs LLM arbitration
- `PolicyResolver.cs` - Maps presets to policies
- `PipelineLatencyTracker.cs` - Latency tracking

**Runtime/Providers/**
- `IStreamingInferenceProvider.cs` - Optional streaming interface

**DialogueAdapters/Yarn/Runtime/**
- `YarnScriptedBeatDetector.cs` - Yarn tag-based beat detection

**Tests/Runtime/**
- `TieredContextPipelineTests.cs` - Test harness

### Modified Files

- `AIConversationSettings.cs` - Added tiered context fields
- `AIConversationService.cs` - Added tiered pipeline routing
- `LocalLLMProvider.cs` - Added `IPlanningProvider`
- `OllamaProvider.cs` - Added `IPlanningProvider`

## Known Limitations

1. **Streaming** is interface-only; actual streaming requires native layer updates
2. **Planning** uses same model as generation (no separate tiny model yet)
3. **Memory persistence** is in-memory only (no disk save/load yet)
4. **Validation repair** is basic (simple string replacement)

## Future Work

- [ ] Disk persistence for structured memory
- [ ] Separate tiny model for planning phase
- [ ] Full native streaming support in LocalLlamaEngine
- [ ] More sophisticated validation repair with model retry
- [ ] Integration with Game Creator 2 variables for snapshot population
