# Immersive NPCs Quickstart

This guide gets one Yarn Spinner NPC running with AI-generated dialogue and fixed player option slots.

## 1. Requirements
- Install Yarn Spinner before using the included dialogue adapter, template wizard, and example workflow.
- Choose at least one AI backend:
  - Cloud: OpenAI, Claude, or DeepSeek.
  - Local server: Ollama or an OpenAI-compatible local server such as LM Studio.
  - Local file: In-Process for `.gguf`, or Sentis for `.sentis` plus `tokenizer.json`.
- Unity AI Inference/Sentis is optional. Immersive NPCs compiles without it; install `com.unity.ai.inference` only if you select the Sentis backend.

## 2. Create Settings
1. Open `Edit > Project Settings > Immersive NPCs`.
2. Click `Create Settings Asset` if one does not exist.
3. Set `Provider`:
   - `Cloud` for the simplest API-backed setup.
   - `Local` for local-only generation.
   - `Race` to ask local and cloud providers and use whichever responds first.
4. If using `Local`, choose the backend:
   - `InProcess`: use `.gguf` models through the bundled llama.cpp plugin.
   - `Sentis`: use `.sentis` models and a Hugging Face `tokenizer.json`. Sentis does not load `.gguf`.
   - `Ollama`: use a running Ollama server.
   - `OpenAICompatible`: use LM Studio or another local OpenAI-compatible server.
5. Set generation basics:
   - `Response Slots`: number of player options the template should show.
   - `Max Tokens`: maximum response size.
   - `Language`: output language, usually `en`.

## 3. Configure A Backend

### Cloud
1. Set `Provider` to `Cloud`.
2. Set `Cloud Provider` to `OpenAI`, `Claude`, or `DeepSeek`.
3. The endpoint and model fields update automatically when you change cloud provider. You can still edit them manually afterward.
4. Set `API Key Mode`:
   - `EnvVarName` is recommended.
   - In `EnvVarName` mode, enter the environment variable name, for example `DEEPSEEK_API_KEY`, not the raw key.
   - `TextAssetReference` is useful only for local testing.
   - `InlineText` is also available for local testing and stores the key in the settings asset.

Default cloud values:
- OpenAI: `https://api.openai.com/v1/responses`, `OPENAI_API_KEY`.
- Claude: `https://api.anthropic.com/v1/messages`, `ANTHROPIC_API_KEY`.
- DeepSeek: `https://api.deepseek.com/chat/completions`, `DEEPSEEK_API_KEY`.

Cloud memory embeddings currently use the OpenAI-compatible embeddings endpoint. If you choose Claude or DeepSeek for dialogue, use local/fallback memory embeddings or disable memory embeddings.

### Local GGUF
1. Set `Provider` to `Local`.
2. Set `Backend` to `InProcess`.
3. Put `.gguf` files under `Assets/StreamingAssets/ImmersiveNPCs/Models`.
4. Use `Rescan Local Models`, then select the model.
5. Adjust context size, threads, and GPU preference as needed.

### Local Sentis
1. Set `Provider` to `Local`.
2. Set `Backend` to `Sentis`.
3. Put the `.sentis` model under `Assets/StreamingAssets/ImmersiveNPCs/Models`.
4. Put `tokenizer.json` next to the selected `.sentis` model, or set `Tokenizer File`.
5. Use `Rescan Local Models`, then select the `.sentis` model.

Sentis LLM availability on Hugging Face is limited compared with GGUF. In the Hugging Face Model Browser, use broad Sentis searches such as `Phi`, `SmolLM`, or `TinyStories`, or open the Hugging Face Sentis text-generation filter from the browser. For common local chat models, GGUF with `InProcess`, Ollama, or LM Studio is usually easier.

### Ollama
1. Start Ollama outside Unity.
2. Set `Provider` to `Local`.
3. Set `Backend` to `Ollama`.
4. Set `Endpoint`, usually `http://localhost:11434/api/chat`.
5. Set `Model`, for example `llama3`.

### LM Studio Or Another OpenAI-Compatible Server
1. Start the local server outside Unity.
2. Set `Provider` to `Local`.
3. Set `Backend` to `OpenAICompatible`.
4. Set `Endpoint`, usually `http://localhost:1234/v1/chat/completions`.
5. Set `Model` to the model name exposed by the server.

## 4. Create World And NPC Assets
1. Create a world state asset:
   - `Assets > Create > Immersive NPCs > Global World State`, or use the button in Project Settings under `Advanced Settings > Context & Behavior`.
   - Fill in `Game Name`, `Base Context`, location, time, weather, and any important lore.
2. Create an NPC profile:
   - `Assets > Create > Immersive NPCs > NPC Profile`, or use the button in Project Settings.
   - Set `Npc Id`. This must match the `npcId` used in the Yarn template.
   - Fill in `Display Name`, `Persona Prompt`, and `Speaking Style`.
3. Create an NPC profile database:
   - `Assets > Create > Immersive NPCs > NPC Profile Database`.
   - Add your NPC profile to `Profiles`.
   - You can also use the database context menu `Auto-Populate from Project`.

## 5. Generate A Yarn Template
1. Open `Tools > Immersive NPCs > Dialogue Template Wizard`.
2. Set `NPC Id` to the same value as the NPC profile, for example `merchant_greta`.
3. Set `Slots` to the same count as Project Settings `Response Slots`.
4. Choose an output folder that is included by your `.yarnproject`.
5. Click `Generate Template`.

The generated Yarn file uses:

```yarn
<<ai_prefetch npcId="merchant_greta" slots=4>>
{$ai_npc_line}

-> {$ai_opt_0}
    <<jump AI_Opt_0>>
```

`ai_prefetch` asks the AI provider for the next NPC line and options. `ai_choose` records which option the player selected and prefetches the next turn.

## 6. Set Up The Scene
1. Add an empty GameObject named `ImmersiveNPCs`.
2. Add `AIConversationManager`.
3. Assign:
   - `Settings`: your `AIConversationSettings` asset.
   - `NPC Profile Database`: your database asset.
   - `Global World State`: your world state asset.
4. Add a GameObject for Yarn dialogue.
5. Add `DialogueCommandBridge`.
6. Assign:
   - `Manager`: the `AIConversationManager`.
   - `Dialogue Asset`: your `.yarnproject`, not an individual `.yarn` file.
   - `Default Npc Id`: the same NPC ID used in the template.
   - `Default Slots`: the same slot count used in the template.
7. If no Yarn presenter is configured, the bridge adds a simple OnGUI debug presenter at runtime.

## 7. Run Dialogue
1. Enter Play Mode.
2. Start the Yarn node generated by the wizard, usually `AI_Hub`.
3. The first turn may wait while the provider or local model starts.
4. Choose an option. The bridge records the choice and prefetches the next AI turn.

## Troubleshooting
- No dialogue starts: assign a `.yarnproject` to `Dialogue Asset`, not a `.yarn` file.
- No generated text: check Project Settings provider configuration and API key or local server status.
- NPC says `...` and every option says `Continue`: the provider failed and the fallback response was displayed. For DeepSeek, set `Cloud Provider` to `DeepSeek` and provide `DEEPSEEK_API_KEY`, `TextAssetReference`, or `InlineText`.
- No local model is found: put models under the configured model folder and click `Rescan Local Models`.
- Sentis model does not load: confirm the selected file is `.sentis` and that `tokenizer.json` is present.
- GGUF does not load with Sentis: use the `InProcess` backend instead.
- Empty options: keep the template `slots` value and Project Settings `Response Slots` aligned.
- Slow first response: local models may need time to preload. Lower context size or max tokens if needed.
