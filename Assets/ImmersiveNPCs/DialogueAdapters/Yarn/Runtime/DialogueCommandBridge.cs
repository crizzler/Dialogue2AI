#if IMMERSIVE_NPCS_YARN
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Yarn.Unity;
using ImmersiveNPCs;

namespace ImmersiveNPCs.DialogueAdapters.Yarn
{
    [AddComponentMenu("Immersive NPCs/Dialogue Bridge")]
    public class DialogueCommandBridge : MonoBehaviour
    {
        [SerializeField] private AIConversationManager manager;
        [SerializeField] private UnityEngine.Object dialogueAsset;
        [SerializeField] private string defaultNpcId = "npc_1";
        [SerializeField] private int defaultSlots = 4;

        private DialogueRunner dialogueRunner;
        private string lastNpcId;

        private void Awake()
        {
            if (manager == null)
            {
                manager = FindFirstObjectByType<AIConversationManager>();
            }

            dialogueRunner = GetComponent<DialogueRunner>();
            if (dialogueRunner == null)
            {
                dialogueRunner = gameObject.AddComponent<DialogueRunner>();
                AILogger.Warn("Dialogue runtime component was missing and has been added.", this);
            }

            if (dialogueAsset is YarnProject project)
            {
                dialogueRunner.SetProject(project);
            }
            else if (dialogueAsset != null)
            {
                AILogger.Warn("Dialogue Asset is not a Yarn Project. Assign a .yarnproject asset, not a .yarn file.", this);
            }

            if (dialogueRunner.YarnProject == null)
            {
                AILogger.Warn("Dialogue Runner has no Yarn Project assigned; dialogue will not run.", this);
            }

            RegisterCommands();
            EnsurePresenters();
        }

        private void RegisterCommands()
        {
            dialogueRunner.AddCommandHandler<string[]>("ai_prefetch", AiPrefetchFromArgs);
            dialogueRunner.AddCommandHandler<string[]>("ai_choose", AiChooseFromArgs);
            
            // Reset dialogue log when a new conversation starts
            dialogueRunner.onDialogueStart.AddListener(OnDialogueStart);
        }
        
        private void OnDialogueStart()
        {
            AILogger.ResetDialogueLog();
        }

        private async Task AiPrefetchFromArgs(params string[] args)
        {
            if (manager == null)
            {
                return;
            }

            string npcId = null;
            int slots = 0;

            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string raw = args[i];
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    if (TrySplitNamedArgument(raw, out string key, out string value))
                    {
                        if (IsKeyMatch(key, "npcid", "npc", "id"))
                        {
                            npcId = value;
                            continue;
                        }

                        if (IsKeyMatch(key, "slots", "slotcount"))
                        {
                            if (TryParseInt(value, out int parsedSlots))
                            {
                                slots = parsedSlots;
                            }
                            continue;
                        }
                    }
                    else
                    {
                        if (npcId == null)
                        {
                            npcId = Unquote(raw);
                        }
                        else if (slots <= 0 && TryParseInt(raw, out int parsedSlots))
                        {
                            slots = parsedSlots;
                        }
                    }
                }
            }

            string resolvedNpcId = string.IsNullOrEmpty(npcId) ? defaultNpcId : npcId;
            lastNpcId = resolvedNpcId;
            int resolvedSlots = slots > 0 ? slots : defaultSlots;
            AILogger.Log($"ai_prefetch npcId={resolvedNpcId} slots={resolvedSlots}", this);
            TurnResult result = await manager.PrefetchAsync(resolvedNpcId, resolvedSlots, CancellationToken.None);
            ApplyTurnResult(result, resolvedSlots);
        }

        private void AiChooseFromArgs(params string[] args)
        {
            if (manager == null)
            {
                return;
            }

            int slot = -1;

            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string raw = args[i];
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    if (TrySplitNamedArgument(raw, out string key, out string value))
                    {
                        if (IsKeyMatch(key, "slot", "slotindex", "index"))
                        {
                            if (TryParseInt(value, out int parsedSlot))
                            {
                                slot = parsedSlot;
                                break;
                            }
                        }
                    }
                    else if (TryParseInt(raw, out int parsedSlot))
                    {
                        slot = parsedSlot;
                        break;
                    }
                }
            }

            if (slot < 0)
            {
                AILogger.Warn("ai_choose missing slot index; expected <<ai_choose slot=0>> or <<ai_choose 0>>.", this);
                return;
            }

            string npcId = string.IsNullOrEmpty(lastNpcId) ? defaultNpcId : lastNpcId;
            AILogger.Log($"ai_choose npcId={npcId} slot={slot}", this);
            manager.RecordChoice(npcId, slot);
            WriteLastChoice(slot);
        }

        private void ApplyTurnResult(TurnResult result, int slots)
        {
            if (dialogueRunner == null || dialogueRunner.VariableStorage == null || result == null)
            {
                if (dialogueRunner == null || dialogueRunner.VariableStorage == null || manager == null || manager.Settings == null)
                {
                    return;
                }

                result = AIOutputValidator.CreateFallback(slots, manager.Settings);
            }

            var storage = dialogueRunner.VariableStorage;
            storage.SetValue(AIDialogueVariables.NpcLine, result.npcLine ?? string.Empty);
            storage.SetValue(AIDialogueVariables.OptionCount, (float)slots);

            int optionCount = result.options != null ? result.options.Count : 0;
            AILogger.Log($"Applied turn: options={optionCount} slots={slots}", this);
            
            // Log the complete dialogue turn for debugging
            string npcId = string.IsNullOrEmpty(lastNpcId) ? defaultNpcId : lastNpcId;
            AILogger.LogDialogueTurn(npcId, result.npcLine, result.options);

            for (int i = 0; i < slots; i++)
            {
                string optionValue = string.Empty;
                if (result.options != null && i < result.options.Count)
                {
                    optionValue = result.options[i];
                }

                if (string.IsNullOrWhiteSpace(optionValue))
                {
                    optionValue = "Continue";
                }

                storage.SetValue(AIDialogueVariables.OptionPrefix + i, optionValue);
            }
        }

        private void WriteLastChoice(int slotIndex)
        {
            if (dialogueRunner == null || dialogueRunner.VariableStorage == null)
            {
                return;
            }

            string npcId = string.IsNullOrEmpty(lastNpcId) ? defaultNpcId : lastNpcId;
            TurnResult last = manager != null ? manager.GetLastGenerated(npcId) : null;
            if (last == null || last.options == null)
            {
                return;
            }

            if (slotIndex < 0 || slotIndex >= last.options.Count)
            {
                return;
            }

            string choiceText = last.options[slotIndex];
            dialogueRunner.VariableStorage.SetValue(AIDialogueVariables.LastChoice, choiceText);
            
            // Log the player's choice for debugging
            AILogger.LogPlayerChoice(npcId, slotIndex, choiceText);
        }

        private void EnsurePresenters()
        {
            if (dialogueRunner == null)
            {
                return;
            }

            bool hasPresenter = false;
            foreach (var presenter in dialogueRunner.DialoguePresenters)
            {
                if (presenter != null)
                {
                    hasPresenter = true;
                    break;
                }
            }

            if (hasPresenter)
            {
                return;
            }

            var debugPresenter = GetComponent<OnGuiDialoguePresenter>();
            if (debugPresenter == null)
            {
                debugPresenter = gameObject.AddComponent<OnGuiDialoguePresenter>();
            }

            dialogueRunner.DialoguePresenters = new DialoguePresenterBase[] { debugPresenter };
            AILogger.Warn("No dialogue presenters found. Added OnGUI debug presenter.", this);
        }

        private static bool TrySplitNamedArgument(string arg, out string key, out string value)
        {
            key = null;
            value = null;

            if (string.IsNullOrWhiteSpace(arg))
            {
                return false;
            }

            int separatorIndex = arg.IndexOf('=');
            if (separatorIndex <= 0)
            {
                return false;
            }

            key = arg.Substring(0, separatorIndex).Trim();
            value = Unquote(arg.Substring(separatorIndex + 1).Trim());
            return !string.IsNullOrEmpty(key);
        }

        private static bool IsKeyMatch(string key, string option1, string option2, string option3 = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            string normalized = key.Trim().ToLowerInvariant();
            return normalized == option1 || normalized == option2 || (!string.IsNullOrEmpty(option3) && normalized == option3);
        }

        private static bool TryParseInt(string value, out int parsed)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                parsed = 0;
                return false;
            }

            return int.TryParse(value.Trim(), out parsed);
        }

        private static string Unquote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            value = value.Trim();
            if (value.Length >= 2)
            {
                char first = value[0];
                char last = value[value.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    return value.Substring(1, value.Length - 2);
                }
            }

            return value;
        }
    }
}
#endif
