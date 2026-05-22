#pragma once

#if defined(_WIN32)
#define IMNPC_LLAMA_EXPORT __declspec(dllexport)
#else
#define IMNPC_LLAMA_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct LlamaModelConfig
{
    int contextSize;
    int threads;
    int gpuLayers;
    int useMmap;
    int useMlock;
    int seed;
    int allowHostMemory;
} LlamaModelConfig;

typedef struct LlamaGenerationConfig
{
    int maxTokens;
    float temperature;
    float topP;
} LlamaGenerationConfig;

IMNPC_LLAMA_EXPORT void* imnpc_llama_create(const char* modelPath, const LlamaModelConfig* config);
IMNPC_LLAMA_EXPORT void imnpc_llama_destroy(void* ctx);
IMNPC_LLAMA_EXPORT int imnpc_llama_generate(void* ctx, const char* prompt, const LlamaGenerationConfig* cfg, char* outText, int outCapacity);
IMNPC_LLAMA_EXPORT int imnpc_llama_generate_chat(void* ctx, const char* systemPrompt, const char* userPrompt, int templateMode, const LlamaGenerationConfig* cfg, char* outText, int outCapacity);
IMNPC_LLAMA_EXPORT int imnpc_llama_embedding_size(void* ctx);
IMNPC_LLAMA_EXPORT int imnpc_llama_embed(void* ctx, const char* text, float* outEmbedding, int maxElements);
IMNPC_LLAMA_EXPORT void imnpc_llama_cancel(void* ctx);
IMNPC_LLAMA_EXPORT int imnpc_llama_is_ready(void* ctx);
IMNPC_LLAMA_EXPORT int imnpc_llama_is_loading(void* ctx);
IMNPC_LLAMA_EXPORT int imnpc_llama_last_error(char* buffer, int capacity);
IMNPC_LLAMA_EXPORT int imnpc_llama_backend_summary(char* buffer, int capacity);
IMNPC_LLAMA_EXPORT void imnpc_llama_set_logging(int enabled);
IMNPC_LLAMA_EXPORT int imnpc_llama_get_log(char* buffer, int capacity, int clear);

#ifdef __cplusplus
}
#endif
