#if IMMERSIVE_NPCS_YARN
using UnityEngine;
using Yarn.Unity;

#nullable enable

namespace ImmersiveNPCs.DialogueAdapters.Yarn
{
    [AddComponentMenu("Immersive NPCs/Debug/OnGUI Dialogue Presenter")]
    public sealed class OnGuiDialoguePresenter : DialoguePresenterBase
    {
        [SerializeField] private bool showPresenter = true;
        [SerializeField] private Rect windowRect = new Rect(10, 220, 520, 260);
        [SerializeField] private string windowTitle = "Immersive NPCs Dialogue";
        [SerializeField] private float lineAreaHeight = 120f;
        [SerializeField] private bool scrollLineText = true;

        private string currentLine = string.Empty;
        private DialogueOption[]? currentOptions;
        private YarnTaskCompletionSource<DialogueOption?>? optionsTcs;
        private int optionsVersion;
        private GUIStyle? lineStyle;
        private GUIStyle? optionStyle;
        private Vector2 lineScroll;

        public override YarnTask OnDialogueStartedAsync()
        {
            currentLine = string.Empty;
            return YarnTask.CompletedTask;
        }

        public override YarnTask OnDialogueCompleteAsync()
        {
            currentLine = string.Empty;
            currentOptions = null;
            optionsTcs = null;
            return YarnTask.CompletedTask;
        }

        public override YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
        {
            currentLine = ResolveLineText(line);
            return YarnTask.CompletedTask;
        }

        public override YarnTask<DialogueOption?> RunOptionsAsync(DialogueOption[] dialogueOptions, LineCancellationToken cancellationToken)
        {
            AILogger.Log($"RunOptionsAsync options={dialogueOptions?.Length ?? 0}", this);
            currentOptions = dialogueOptions;
            optionsTcs = new YarnTaskCompletionSource<DialogueOption?>();
            int version = ++optionsVersion;

            if (cancellationToken.NextContentToken.CanBeCanceled)
            {
                cancellationToken.NextContentToken.Register(() => CancelOptions(version));
            }

            return optionsTcs.Task;
        }

        private void CancelOptions(int version)
        {
            if (version != optionsVersion)
            {
                return;
            }

            YarnTaskCompletionSource<DialogueOption?>? tcs = optionsTcs;
            currentOptions = null;
            optionsTcs = null;
            tcs?.TrySetResult(null);
        }

        private void OnGUI()
        {
            if (!showPresenter)
            {
                return;
            }

            EnsureStyles();
            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, windowTitle);
        }

        private void DrawWindow(int id)
        {
            if (scrollLineText)
            {
                lineScroll = GUILayout.BeginScrollView(lineScroll, GUILayout.Height(lineAreaHeight));
            }

            if (!string.IsNullOrEmpty(currentLine))
            {
                GUILayout.Label(currentLine, lineStyle!);
            }
            else
            {
                GUILayout.Label("Waiting for dialogue...", lineStyle!);
            }

            if (scrollLineText)
            {
                GUILayout.EndScrollView();
            }

            var options = currentOptions;
            if (options != null && options.Length > 0)
            {
                GUILayout.Space(8);
                for (int i = 0; i < options.Length; i++)
                {
                    DialogueOption? option = options[i];
                    if (option == null)
                    {
                        continue;
                    }
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = option.IsAvailable;

                    string optionText = ResolveLineText(option.Line);
                    if (GUILayout.Button(optionText, optionStyle!))
                    {
                        SelectOption(option);
                    }

                    GUI.enabled = wasEnabled;
                }
            }
            else
            {
                GUILayout.Space(8);
                GUILayout.Label("Waiting for options...", lineStyle!);
            }

            GUI.DragWindow();
        }

        private void SelectOption(DialogueOption option)
        {
            if (optionsTcs == null)
            {
                return;
            }

            YarnTaskCompletionSource<DialogueOption?> tcs = optionsTcs;
            currentOptions = null;
            optionsTcs = null;
            tcs.TrySetResult(option);
        }

        private void EnsureStyles()
        {
            if (lineStyle == null)
            {
                lineStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    fontSize = 14
                };
            }

            if (optionStyle == null)
            {
                optionStyle = new GUIStyle(GUI.skin.button)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleLeft
                };
            }
        }

        private static string ResolveLineText(LocalizedLine? line)
        {
            if (line == null)
            {
                return string.Empty;
            }

            string text = line.TextWithoutCharacterName.Text;
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }

            return line.RawText ?? string.Empty;
        }
    }
}
#endif
