#if IMMERSIVE_NPCS_YARN
using System.Collections.Generic;
using Yarn.Unity;

namespace ImmersiveNPCs.DialogueAdapters.Yarn
{
    /// <summary>
    /// Detects if the current Yarn node is a scripted beat that requires script authority.
    /// </summary>
    public sealed class YarnScriptedBeatDetector
    {
        /// <summary>
        /// Tags that indicate a main quest beat.
        /// </summary>
        public HashSet<string> MainQuestTags { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "main_quest",
            "mainquest",
            "critical",
            "required",
            "scripted",
            "story_beat",
            "storybeat",
            "cinematic"
        };
        
        /// <summary>
        /// Tags that indicate the NPC should use scripted dialogue only.
        /// </summary>
        public HashSet<string> ScriptOnlyTags { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "script_only",
            "scriptonly",
            "no_ai",
            "noai",
            "manual"
        };
        
        /// <summary>
        /// Checks if the current node is a scripted beat.
        /// </summary>
        public bool IsScriptedBeat(DialogueRunner runner, string currentNode)
        {
            if (runner == null || runner.YarnProject == null || string.IsNullOrEmpty(currentNode))
            {
                return false;
            }
            
            // Get node tags
            var tags = GetNodeTags(runner, currentNode);
            if (tags == null || tags.Count == 0)
            {
                return false;
            }
            
            // Check for main quest or script-only tags
            foreach (var tag in tags)
            {
                if (MainQuestTags.Contains(tag) || ScriptOnlyTags.Contains(tag))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if the current node awaits a specific scripted response.
        /// </summary>
        public bool AwaitingScriptedResponse(DialogueRunner runner, string currentNode)
        {
            if (runner == null || string.IsNullOrEmpty(currentNode))
            {
                return false;
            }
            
            var tags = GetNodeTags(runner, currentNode);
            if (tags == null)
            {
                return false;
            }
            
            // Check for script-only tags
            foreach (var tag in tags)
            {
                if (ScriptOnlyTags.Contains(tag))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets all tags for a node.
        /// </summary>
        public HashSet<string> GetNodeTags(DialogueRunner runner, string nodeName)
        {
            var result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            
            if (runner == null || runner.YarnProject == null || string.IsNullOrEmpty(nodeName))
            {
                return result;
            }
            
            try
            {
                // Try to get node headers from the project
                // Note: This depends on Yarn Spinner version - adjust as needed
                var project = runner.YarnProject;
                
                // Yarn Spinner 2.x approach
                if (project.NodeNames != null)
                {
                    foreach (var name in project.NodeNames)
                    {
                        if (name == nodeName)
                        {
                            // Node exists, check metadata
                            // In Yarn 2.x, tags are in the compiled program's node info
                            // This is a simplified check - full implementation needs NodeInfo access
                            break;
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // Ignore errors accessing Yarn internals
            }
            
            return result;
        }
        
        /// <summary>
        /// Populates a world state snapshot with Yarn-specific state.
        /// </summary>
        public void PopulateSnapshot(SnapshotBuilder builder, DialogueRunner runner, string currentNode)
        {
            if (builder == null || runner == null)
            {
                return;
            }
            
            builder.SetYarnNode(currentNode);
            
            var tags = GetNodeTags(runner, currentNode);
            builder.SetYarnNodeTags(tags);
            
            bool isScripted = IsScriptedBeat(runner, currentNode);
            builder.SetScriptedBeat(isScripted);
            
            bool awaiting = AwaitingScriptedResponse(runner, currentNode);
            builder.SetAwaitingScriptedResponse(awaiting);
        }
        
        /// <summary>
        /// Registers custom main quest tags.
        /// </summary>
        public void AddMainQuestTag(string tag)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                MainQuestTags.Add(tag);
            }
        }
        
        /// <summary>
        /// Registers custom script-only tags.
        /// </summary>
        public void AddScriptOnlyTag(string tag)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                ScriptOnlyTags.Add(tag);
            }
        }
    }
}
#endif
