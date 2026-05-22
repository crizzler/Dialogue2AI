using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ImmersiveNPCs
{
    /// <summary>
    /// Tracks latency for pipeline stages.
    /// Used by test harness and optionally in production with enableTimingLogs.
    /// </summary>
    public sealed class PipelineLatencyTracker : IDisposable
    {
        private readonly Dictionary<string, Stopwatch> activeTimers = new();
        private readonly Dictionary<string, long> completedTimings = new();
        private readonly StringBuilder logBuilder = new();
        private readonly bool isEnabled;
        
        private long totalMs;
        private string currentStage;
        
        public PipelineLatencyTracker(bool enabled = true)
        {
            isEnabled = enabled;
        }
        
        /// <summary>
        /// Start timing a named stage.
        /// </summary>
        public void Begin(string stageName)
        {
            if (!isEnabled) return;
            
            currentStage = stageName;
            
            if (!activeTimers.TryGetValue(stageName, out var sw))
            {
                sw = new Stopwatch();
                activeTimers[stageName] = sw;
            }
            
            sw.Restart();
        }
        
        /// <summary>
        /// End timing the current stage.
        /// </summary>
        public long End(string stageName = null)
        {
            if (!isEnabled) return 0;
            
            stageName ??= currentStage;
            
            if (stageName == null || !activeTimers.TryGetValue(stageName, out var sw))
            {
                return 0;
            }
            
            sw.Stop();
            long elapsed = sw.ElapsedMilliseconds;
            
            completedTimings[stageName] = elapsed;
            totalMs += elapsed;
            
            return elapsed;
        }
        
        /// <summary>
        /// Get elapsed time for a specific stage (even if still running).
        /// </summary>
        public long GetElapsed(string stageName)
        {
            if (completedTimings.TryGetValue(stageName, out long completed))
            {
                return completed;
            }
            
            if (activeTimers.TryGetValue(stageName, out var sw))
            {
                return sw.ElapsedMilliseconds;
            }
            
            return 0;
        }
        
        /// <summary>
        /// Get all recorded timings.
        /// </summary>
        public IReadOnlyDictionary<string, long> GetTimings()
        {
            return completedTimings;
        }
        
        /// <summary>
        /// Total time across all stages.
        /// </summary>
        public long TotalMs => totalMs;
        
        /// <summary>
        /// Build human-readable summary.
        /// </summary>
        public string BuildSummary()
        {
            if (!isEnabled || completedTimings.Count == 0)
            {
                return "No timing data recorded.";
            }
            
            logBuilder.Clear();
            logBuilder.AppendLine("=== Pipeline Latency Summary ===");
            
            foreach (var kvp in completedTimings)
            {
                float percentage = totalMs > 0 ? (kvp.Value * 100f / totalMs) : 0;
                logBuilder.AppendFormat("  {0}: {1}ms ({2:F1}%)\n", kvp.Key, kvp.Value, percentage);
            }
            
            logBuilder.AppendFormat("  TOTAL: {0}ms", totalMs);
            
            return logBuilder.ToString();
        }
        
        /// <summary>
        /// Log summary to Unity console.
        /// </summary>
        public void LogSummary()
        {
            if (!isEnabled) return;
            
            UnityEngine.Debug.Log(BuildSummary());
        }
        
        public void Dispose()
        {
            activeTimers.Clear();
            completedTimings.Clear();
        }
    }
    
    /// <summary>
    /// Named stages for the tiered context pipeline.
    /// </summary>
    public static class PipelineStages
    {
        public const string SnapshotBuild = "SnapshotBuild";
        public const string ContextAssemble = "ContextAssemble";
        public const string IntentPlanning = "IntentPlanning";
        public const string ResponseGeneration = "ResponseGeneration";
        public const string Validation = "Validation";
        public const string MemoryWrite = "MemoryWrite";
        public const string ScriptArbitration = "ScriptArbitration";
        public const string Total = "Total";
    }
    
    /// <summary>
    /// Latency budget for a quality preset.
    /// Tracks whether stages are meeting their budget.
    /// </summary>
    public class LatencyBudget
    {
        public int planningBudgetMs = 200;
        public int generationBudgetMs = 3000;
        public int validationBudgetMs = 50;
        public int totalBudgetMs = 4000;
        
        private readonly List<string> budgetViolations = new();
        
        /// <summary>
        /// Check if a stage is within budget.
        /// </summary>
        public bool IsWithinBudget(string stage, long actualMs)
        {
            int budget = GetBudgetFor(stage);
            if (budget <= 0) return true;
            
            if (actualMs > budget)
            {
                budgetViolations.Add($"{stage}: {actualMs}ms exceeded {budget}ms budget");
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Get budget for a specific stage.
        /// </summary>
        public int GetBudgetFor(string stage)
        {
            switch (stage)
            {
                case PipelineStages.IntentPlanning:
                    return planningBudgetMs;
                case PipelineStages.ResponseGeneration:
                    return generationBudgetMs;
                case PipelineStages.Validation:
                    return validationBudgetMs;
                case PipelineStages.Total:
                    return totalBudgetMs;
                default:
                    return 0; // No budget constraint
            }
        }
        
        /// <summary>
        /// Get all budget violations.
        /// </summary>
        public IReadOnlyList<string> GetViolations() => budgetViolations;
        
        /// <summary>
        /// Check if all stages are within budget.
        /// </summary>
        public bool AllWithinBudget => budgetViolations.Count == 0;
        
        /// <summary>
        /// Create budget from quality preset.
        /// </summary>
        public static LatencyBudget FromPreset(QualityPreset preset)
        {
            switch (preset)
            {
                case QualityPreset.FastSmall:
                    return new LatencyBudget
                    {
                        planningBudgetMs = 0, // No planning
                        generationBudgetMs = 1500,
                        validationBudgetMs = 20,
                        totalBudgetMs = 2000
                    };
                    
                case QualityPreset.Balanced:
                    return new LatencyBudget
                    {
                        planningBudgetMs = 200,
                        generationBudgetMs = 3000,
                        validationBudgetMs = 50,
                        totalBudgetMs = 4000
                    };
                    
                case QualityPreset.DeepConversation:
                    return new LatencyBudget
                    {
                        planningBudgetMs = 500,
                        generationBudgetMs = 5000,
                        validationBudgetMs = 100,
                        totalBudgetMs = 7000
                    };
                    
                case QualityPreset.CinematicQuality:
                    return new LatencyBudget
                    {
                        planningBudgetMs = 1000,
                        generationBudgetMs = 10000,
                        validationBudgetMs = 200,
                        totalBudgetMs = 15000
                    };
                    
                default:
                    return new LatencyBudget();
            }
        }
    }
}
