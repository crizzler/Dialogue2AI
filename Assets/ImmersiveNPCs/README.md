# Immersive NPCs: Living Dialogue System

## Requirements
- Yarn Spinner is required for the included dialogue adapter, example scene, and template workflow.
- Unity AI Inference/Sentis is optional. Immersive NPCs compiles without it; install `com.unity.ai.inference` only if you choose the Sentis local backend.

## License
Immersive NPCs is released under the MIT License. See `Assets/ImmersiveNPCs/LICENSE.md`.

## Quick start (5 minutes)
For a full first-time setup walkthrough, see `Assets/ImmersiveNPCs/Documentation/QUICKSTART.md`.

1) Open Project Settings -> Immersive NPCs and create a settings asset.
2) Choose Provider Mode and configure a cloud, local server, GGUF, or Sentis backend.
3) Run Tools -> Immersive NPCs -> Dialogue Template Wizard to generate a template file.
4) Create a GlobalWorldState asset, an NpcProfile asset, and an NpcProfileDatabase.
5) Set the NpcProfile `Npc Id` to match the template `npcId`.
6) Add an AIConversationManager and DialogueCommandBridge to a scene.
7) Assign the generated `.yarnproject`, then start dialogue using the generated template node.

## Authoring the living dialogue loop
The template uses fixed option slots and dynamic text:
- Variables: $ai_npc_line, $ai_opt_0..$ai_opt_{N-1}
- Commands: <<ai_prefetch npcId="..." slots=N>> and <<ai_choose slot=i>>

The hub node calls ai_prefetch, then displays options using variables. Each option node calls ai_choose and jumps back to the hub. This keeps the graph static while the text stays dynamic.

## Caching and prefetch
- The system caches results keyed by NPC, summary, last choice, perception snapshot, slots, and language.
- Prefetch is triggered by ai_choose so the next hub turn is often ready.
- Optional speculative prefetch can generate likely follow-ups per option.

## Local model setup
- Choose a local backend in Project Settings: In-Process (GGUF), Sentis, Placeholder, Ollama, or OpenAI-compatible (LM Studio).
- In-Process (GGUF):
  - Drop `.gguf` models into `Assets/StreamingAssets/ImmersiveNPCs/Models`.
  - Select the model in Project Settings and adjust context size/threads.
  - You can also use the Hugging Face Model Browser (Project Settings -> Immersive NPCs or Tools menu) to search and download GGUFs.
  - Requires the native in-process backend plugin (llama.cpp-based). See the setup notes below.
- Placeholder: drop models into the folder set in Project Settings (.gguf, .ggml, .bin) and swap in a real engine via ILocalInferenceEngine.
- Sentis:
  - The base package compiles without Unity AI Inference/Sentis installed. If you select Sentis without the package, Project Settings and runtime status report that `com.unity.ai.inference` is missing.
  - Sentis does not load `.gguf`. Use In-Process for GGUF models.
  - Sentis LLM availability is much smaller than GGUF availability. Search broadly for Unity Sentis text-generation models, for example Phi, SmolLM, or TinyStories.
  - Drop `.sentis` models into `Assets/StreamingAssets/ImmersiveNPCs/Models`.
  - ONNX/PyTorch models must be imported or exported through Unity Sentis first so Unity has a `.sentis` model asset/file to run.
  - Put a Hugging Face `tokenizer.json` next to the selected `.sentis` model, or configure the tokenizer path in Project Settings.
  - The included Sentis glue loads the `.sentis` file, parses `tokenizer.json`, runs a simple autoregressive generation loop, and supports common causal-LM inputs such as `input_ids`, `attention_mask`, `position_ids`, and `token_type_ids`.
  - Models exported with KV-cache inputs, custom tensor names, or model-specific generation requirements may need a dedicated Sentis runner.
- Ollama: set the local endpoint and model name (defaults to `http://localhost:11434/api/chat`).
- OpenAI-compatible: set the local endpoint and model name (defaults to `http://localhost:1234/v1/chat/completions`).
- For LM Studio, you can set Max Tokens to `-1` to let the server choose the output length.

### Preparing a Sentis LLM from an existing model
Sentis can run a model only after Unity can import it as a Sentis `ModelAsset` or load it as a `.sentis` file. A `.gguf` file is not a useful source for this workflow; GGUF is meant for llama.cpp-style runtimes. If you only have GGUF, use the In-Process backend. If you want Sentis, start from the original Hugging Face/PyTorch/SafeTensors model or an ONNX export.

Recommended workflow
1. Choose a small decoder-only text-generation model. Sentis is best suited to compact models; large chat models are usually easier through GGUF, Ollama, or LM Studio.
2. Export or obtain an ONNX/PyTorch model that Unity Sentis can import. Prefer exports without KV-cache inputs for the generic Immersive NPCs runner.
3. Import the model file into Unity by placing it under `Assets/`.
4. Select the imported model asset in the Project window.
5. In the Inspector, click `Serialize To StreamingAssets`. Sentis writes a `.sentis` file to `Assets/StreamingAssets/`.
6. Move the `.sentis` file into `Assets/StreamingAssets/ImmersiveNPCs/Models`.
7. Copy the matching Hugging Face `tokenizer.json` from the same model repo next to the `.sentis` file, or assign it in Project Settings.
8. Open Project Settings -> Immersive NPCs, set the local backend to `Sentis`, select the `.sentis` model, and verify the tokenizer path.

Validation checklist
- The model should expose a token input such as `input_ids`. The included runner can also provide common optional inputs such as `attention_mask`, `position_ids`, and `token_type_ids`.
- If your model uses different input or output tensor names, configure the names in Project Settings.
- The output should include logits for the next token. If the logits output name is unusual, configure it in Project Settings.
- Models exported with `past_key_values`, custom cache tensors, or model-specific sampling requirements may import correctly but still need a dedicated Sentis runner.
- If Unity fails to import the model, the most common causes are unsupported operators, unsupported dynamic shapes, or a model that is too large for the selected runtime device.

### In-process backend setup (llama.cpp)
Bundled binaries (recommended)
1) The package ships with prebuilt native libraries for Windows, Linux, and macOS.
2) No download is required; just set Local Backend to In-Process and restart Unity if needed.

Optional: reinstall or custom build
1) If the plugin is missing or you want to rebuild it, use the installer button in Project Settings.
   - The installer downloads from a GitHub release. You can change the URL in `Assets/ImmersiveNPCs/Editor/Installers/InProcessBackendInstaller.cs` if needed.

Manual build
1) Build the native plugin for your target platform(s) using llama.cpp.
2) Name the library `immersivenpcs_llama` (`.dll`, `.so`, or `.dylib`).
3) Place the library under `Assets/ImmersiveNPCs/Plugins/<Platform>/`.
4) Restart Unity so the plugin is picked up.

macOS note
- If you rebuild on macOS, place the output at `Assets/ImmersiveNPCs/Plugins/Mac/libimmersivenpcs_llama.dylib`.

The plugin must export the following C entry points:
```
imnpc_llama_create
imnpc_llama_destroy
imnpc_llama_generate
imnpc_llama_cancel
imnpc_llama_last_error
```

## Cloud setup
- Set the cloud provider, endpoint, and model name in Project Settings.
- Supported cloud providers:
  - OpenAI: default endpoint `https://api.openai.com/v1/responses`, API key env var `OPENAI_API_KEY`.
  - Claude: Anthropic Messages endpoint `https://api.anthropic.com/v1/messages`, API key env var `ANTHROPIC_API_KEY`.
  - DeepSeek: OpenAI-compatible chat completions endpoint `https://api.deepseek.com/chat/completions`, API key env var `DEEPSEEK_API_KEY`.
- Changing the Cloud Provider dropdown automatically fills the recommended endpoint, model, and environment variable name.
- Configure API key mode:
  - EnvVarName: read from an environment variable. The field value should be the variable name, for example `DEEPSEEK_API_KEY`, not the key itself.
  - TextAssetReference: reference a local TextAsset (for testing only).
  - InlineText: paste a key into a masked field for local testing only. The key is stored in the settings asset.
- Cloud embeddings currently use the OpenAI-compatible embeddings endpoint and are enabled only when the cloud provider is OpenAI. With Claude or DeepSeek, use local/fallback memory embeddings or disable memory embeddings.

## Troubleshooting
- No output: verify the settings asset exists and the provider is available.
- NPC says `...` and all options say `Continue`: generation failed and the fallback turn was shown. Check the Console for the provider error. For DeepSeek, set Provider Mode to Cloud or Race, Cloud Provider to DeepSeek, and provide a `DEEPSEEK_API_KEY` environment variable, API key TextAsset, or InlineText key.
- Empty options: ensure ai_prefetch slots matches the template option count.
- Long responses: reduce max tokens or max line length.
- Slow responses: enable cache, lower max tokens, or try Race mode.

## Known limitations
- In-process inference requires a native plugin; the package includes binaries for Windows/Linux/macOS but will fall back to safe defaults if missing.
- Sentis does not load `.gguf` files. Use Sentis models (`.sentis`) or imported ONNX/PyTorch exports, plus the tokenizer/generation glue required by the model.
- Speculative prefetch can increase compute cost.
- Determinism depends on the chosen provider and temperature.

## Files
- Settings: Assets/ImmersiveNPCs/Resources/AIConversationSettings.asset
- Templates: Assets/ImmersiveNPCs/Examples/Content
- Third party licenses: Assets/ImmersiveNPCs/ThirdParty (includes Yarn Spinner and ggml/llama.cpp for in-process backend)
