#include "immersivenpcs_llama.h"

#include "llama.h"
#include "ggml.h"

#include <atomic>
#include <cmath>
#include <cstring>
#include <cstdio>
#include <ctime>
#include <mutex>
#include <string>
#include <vector>

// File logging for crash diagnostics
static std::mutex g_fileMutex;
static FILE* g_logFile = nullptr;
static std::string g_logFilePath;

static void WriteToLogFile(const char* prefix, const char* text)
{
    std::lock_guard<std::mutex> lock(g_fileMutex);
    if (!g_logFile)
    {
        return;
    }
    
    // Get timestamp
    time_t now = time(nullptr);
    struct tm* timeinfo = localtime(&now);
    char timestamp[32];
    strftime(timestamp, sizeof(timestamp), "%H:%M:%S", timeinfo);
    
    fprintf(g_logFile, "[%s] %s%s", timestamp, prefix, text);
    fflush(g_logFile);  // Flush immediately so we don't lose data on crash
}

static void OpenLogFile()
{
    std::lock_guard<std::mutex> lock(g_fileMutex);
    if (g_logFile)
    {
        return;  // Already open
    }
    
    // Try to open in /tmp first, then current directory
    const char* paths[] = {
        "/tmp/immersivenpcs_llama.log",
        "./immersivenpcs_llama.log"
    };
    
    for (const char* path : paths)
    {
        g_logFile = fopen(path, "a");
        if (g_logFile)
        {
            g_logFilePath = path;
            time_t now = time(nullptr);
            fprintf(g_logFile, "\n\n========== SESSION START: %s ==========\n", ctime(&now));
            fflush(g_logFile);
            break;
        }
    }
}

static void CloseLogFile()
{
    std::lock_guard<std::mutex> lock(g_fileMutex);
    if (g_logFile)
    {
        fprintf(g_logFile, "========== SESSION END ==========\n");
        fclose(g_logFile);
        g_logFile = nullptr;
    }
}

struct ImnpcContext
{
    llama_model* model = nullptr;
    llama_context* ctx = nullptr;
    llama_context* embed_ctx = nullptr;
    const llama_vocab* vocab = nullptr;
    std::atomic<bool> cancel{false};
    std::atomic<bool> ready{false};  // True when model is fully loaded and ready
    std::atomic<bool> loading{false}; // True during model loading
    std::mutex contextMutex;  // Protects all context operations
    int seed = 0;
    int threads = 4;
    int contextSize = 2048;
    std::string lastError;
    
    // Embedding support - determined at model load time
    // -1 = UNSPECIFIED (causal model), 0 = NONE, 1 = MEAN, 2 = CLS, 3 = LAST
    int modelPoolingType = -1;
    bool useManualEmbedding = false;  // True for causal models without native embedding support
};

static std::mutex g_errorMutex;
static std::string g_lastError;
static std::mutex g_logMutex;
static std::string g_lastLog;
static std::atomic<bool> g_loggingEnabled{true};
static std::once_flag g_backendInit;
static std::mutex g_backendMutex;  // Protects backend operations during init
static std::atomic<bool> g_backendReady{false};  // True when backend is fully initialized
static const size_t kMaxLogBytes = 8192;

static void SetGlobalError(const std::string& message)
{
    std::lock_guard<std::mutex> lock(g_errorMutex);
    g_lastError = message;
}

static std::string GetGlobalError()
{
    std::lock_guard<std::mutex> lock(g_errorMutex);
    return g_lastError;
}

static void SetContextError(ImnpcContext* ctx, const std::string& message)
{
    if (ctx)
    {
        ctx->lastError = message;
    }
    SetGlobalError(message);
}

static bool AbortCallback(void* data)
{
    ImnpcContext* ctx = static_cast<ImnpcContext*>(data);
    if (!ctx)
    {
        return false;
    }
    return ctx->cancel.load();
}

static void AppendLog(enum ggml_log_level level, const char* text)
{
    if (!text)
    {
        return;
    }

    std::string line(text);
    if (line.empty())
    {
        return;
    }

    // Write to file for crash diagnostics
    const char* levelPrefix = "";
    switch (level)
    {
        case GGML_LOG_LEVEL_ERROR: levelPrefix = "[ERROR] "; break;
        case GGML_LOG_LEVEL_WARN:  levelPrefix = "[WARN]  "; break;
        case GGML_LOG_LEVEL_INFO:  levelPrefix = "[INFO]  "; break;
        case GGML_LOG_LEVEL_DEBUG: levelPrefix = "[DEBUG] "; break;
        default: levelPrefix = ""; break;
    }
    WriteToLogFile(levelPrefix, text);

    if (level == GGML_LOG_LEVEL_ERROR)
    {
        SetGlobalError(line);
    }

    std::lock_guard<std::mutex> lock(g_logMutex);
    g_lastLog.append(line);
    if (g_lastLog.size() > kMaxLogBytes)
    {
        g_lastLog.erase(0, g_lastLog.size() - kMaxLogBytes);
    }
}

static void LogCallback(enum ggml_log_level level, const char* text, void* /*user_data*/)
{
    if (!g_loggingEnabled.load())
    {
        return;
    }
    AppendLog(level, text);
}

static bool TokenizePrompt(ImnpcContext* ctx, const char* prompt, std::vector<llama_token>& outTokens)
{
    if (!ctx || !ctx->vocab)
    {
        return false;
    }

    const char* text = prompt ? prompt : "";
    int32_t textLen = static_cast<int32_t>(std::strlen(text));
    int32_t required = llama_tokenize(ctx->vocab, text, textLen, nullptr, 0, true, true);
    if (required == 0)
    {
        outTokens.clear();
        return true;
    }
    if (required < 0)
    {
        required = -required;
    }

    outTokens.resize(required);
    int32_t count = llama_tokenize(ctx->vocab, text, textLen, outTokens.data(), static_cast<int32_t>(outTokens.size()), true, true);
    if (count < 0)
    {
        SetContextError(ctx, "Tokenization failed.");
        return false;
    }

    outTokens.resize(count);
    return true;
}

static void AppendTokenText(const llama_vocab* vocab, llama_token token, std::string& output)
{
    char buffer[256];
    int32_t written = llama_token_to_piece(vocab, token, buffer, sizeof(buffer), 0, false);
    if (written > 0)
    {
        output.append(buffer, static_cast<size_t>(written));
    }
}

enum ImnpcChatTemplateMode
{
    ImnpcChatTemplate_Auto = 0,
    ImnpcChatTemplate_ChatML = 1,
    ImnpcChatTemplate_Raw = 2
};

static std::string BuildRawPrompt(const std::string& system, const std::string& user)
{
    if (user.empty())
    {
        return system;
    }

    if (system.empty())
    {
        return user;
    }

    return system + "\n\n" + user;
}

static std::string BuildPromptFromChat(ImnpcContext* ctx, const char* systemPrompt, const char* userPrompt, int templateMode)
{
    std::string system = systemPrompt ? systemPrompt : "";
    std::string user = userPrompt ? userPrompt : "";

    if (templateMode == ImnpcChatTemplate_Raw)
    {
        return BuildRawPrompt(system, user);
    }

    const char* tmpl = nullptr;
    if (templateMode == ImnpcChatTemplate_ChatML)
    {
        tmpl = "chatml";
    }
    else if (ctx && ctx->model)
    {
        tmpl = llama_model_chat_template(ctx->model, nullptr);
    }

    if (!tmpl || tmpl[0] == '\0')
    {
        return BuildRawPrompt(system, user);
    }

    llama_chat_message messages[2];
    size_t msgCount = 0;
    if (!system.empty())
    {
        messages[msgCount++] = { "system", system.c_str() };
    }
    if (!user.empty())
    {
        messages[msgCount++] = { "user", user.c_str() };
    }

    if (msgCount == 0)
    {
        return std::string();
    }

    int32_t required = llama_chat_apply_template(tmpl, messages, msgCount, true, nullptr, 0);
    if (required <= 0)
    {
        return BuildRawPrompt(system, user);
    }

    std::string buffer;
    buffer.resize(static_cast<size_t>(required) + 1);
    int32_t written = llama_chat_apply_template(tmpl, messages, msgCount, true, buffer.data(), static_cast<int32_t>(buffer.size()));
    if (written <= 0)
    {
        return BuildRawPrompt(system, user);
    }

    buffer.resize(static_cast<size_t>(written));
    return buffer;
}

// Internal generation function - MUST be called with ctx->contextMutex already held
static int GenerateWithPromptUnlocked(ImnpcContext* ctx, const char* prompt, const LlamaGenerationConfig* cfg, char* outText, int outCapacity)
{
    WriteToLogFile("[GEN] ", "GenerateWithPromptUnlocked entered\n");
    
    // Caller must have already validated ctx and acquired the lock

    if (!ctx->ctx || !ctx->model || !ctx->vocab)
    {
        WriteToLogFile("[GEN] ", "ERROR: Context not initialized\n");
        SetContextError(ctx, "Context is not initialized.");
        return -1;
    }

    if (!outText || outCapacity <= 0)
    {
        WriteToLogFile("[GEN] ", "ERROR: Output buffer invalid\n");
        SetContextError(ctx, "Output buffer is invalid.");
        return -1;
    }

    ctx->cancel.store(false);
    ctx->lastError.clear();

    WriteToLogFile("[GEN] ", "Setting threads and clearing memory...\n");
    llama_set_n_threads(ctx->ctx, ctx->threads, ctx->threads);
    llama_memory_clear(llama_get_memory(ctx->ctx), true);

    WriteToLogFile("[GEN] ", "Tokenizing prompt...\n");
    std::vector<llama_token> tokens;
    if (!TokenizePrompt(ctx, prompt, tokens))
    {
        WriteToLogFile("[GEN] ", "ERROR: Tokenization failed\n");
        return -1;
    }
    
    char tokenMsg[64];
    snprintf(tokenMsg, sizeof(tokenMsg), "Tokenized %zu tokens\n", tokens.size());
    WriteToLogFile("[GEN] ", tokenMsg);

    if (!tokens.empty())
    {
        WriteToLogFile("[GEN] ", "Decoding prompt batch...\n");
        llama_batch batch = llama_batch_get_one(tokens.data(), static_cast<int32_t>(tokens.size()));
        int32_t decodeResult = llama_decode(ctx->ctx, batch);
        
        char decodeMsg[64];
        snprintf(decodeMsg, sizeof(decodeMsg), "Prompt decode result: %d\n", decodeResult);
        WriteToLogFile("[GEN] ", decodeMsg);
        
        if (decodeResult == 2)
        {
            return 0;
        }
        if (decodeResult != 0)
        {
            WriteToLogFile("[GEN] ", "ERROR: Prompt decode failed\n");
            SetContextError(ctx, "Prompt decode failed.");
            return -1;
        }
    }

    int maxTokens = cfg ? cfg->maxTokens : 0;
    if (maxTokens <= 0)
    {
        WriteToLogFile("[GEN] ", "ERROR: Max tokens must be positive\n");
        SetContextError(ctx, "Max tokens must be positive.");
        return -1;
    }

    // Check for context overflow and limit maxTokens accordingly
    int promptTokens = static_cast<int>(tokens.size());
    int availableTokens = ctx->contextSize - promptTokens - 4; // 4 token safety margin
    if (availableTokens < 32)
    {
        char overflowMsg[128];
        snprintf(overflowMsg, sizeof(overflowMsg), "Context overflow: prompt=%d, context_size=%d, available=%d\n", 
                 promptTokens, ctx->contextSize, availableTokens);
        WriteToLogFile("[GEN] ", overflowMsg);
        SetContextError(ctx, "Prompt too long for context size.");
        return -1;
    }
    if (maxTokens > availableTokens)
    {
        char limitMsg[128];
        snprintf(limitMsg, sizeof(limitMsg), "Limiting maxTokens from %d to %d due to context size\n", maxTokens, availableTokens);
        WriteToLogFile("[GEN] ", limitMsg);
        maxTokens = availableTokens;
    }

    float temperature = cfg ? cfg->temperature : 0.8f;
    float topP = cfg ? cfg->topP : 0.95f;
    if (temperature <= 0.0f)
    {
        temperature = 0.8f;
    }
    if (topP <= 0.0f)
    {
        topP = 0.95f;
    }

    WriteToLogFile("[GEN] ", "Creating sampler chain...\n");
    llama_sampler_chain_params sparams = llama_sampler_chain_default_params();
    llama_sampler* sampler = llama_sampler_chain_init(sparams);
    if (!sampler)
    {
        WriteToLogFile("[GEN] ", "ERROR: Failed to create sampler chain\n");
        SetContextError(ctx, "Failed to create sampler chain.");
        return -1;
    }
    
    llama_sampler_chain_add(sampler, llama_sampler_init_top_k(40));
    llama_sampler_chain_add(sampler, llama_sampler_init_top_p(topP, 1));
    llama_sampler_chain_add(sampler, llama_sampler_init_temp(temperature));
    uint32_t seed = ctx->seed == 0 ? static_cast<uint32_t>(llama_time_us()) : static_cast<uint32_t>(ctx->seed);
    llama_sampler_chain_add(sampler, llama_sampler_init_dist(seed));

    WriteToLogFile("[GEN] ", "Starting token generation loop...\n");
    std::string output;
    output.reserve(static_cast<size_t>(outCapacity));

    for (int i = 0; i < maxTokens; i++)
    {
        if (ctx->cancel.load())
        {
            WriteToLogFile("[GEN] ", "Generation cancelled\n");
            llama_sampler_free(sampler);
            return 0;
        }

        // Log every 10 tokens to avoid spamming
        if (i % 10 == 0)
        {
            char loopMsg[64];
            snprintf(loopMsg, sizeof(loopMsg), "Generating token %d/%d...\n", i, maxTokens);
            WriteToLogFile("[GEN] ", loopMsg);
        }

        llama_token token = llama_sampler_sample(sampler, ctx->ctx, -1);
        llama_sampler_accept(sampler, token);

        if (llama_vocab_is_eog(ctx->vocab, token))
        {
            WriteToLogFile("[GEN] ", "End of generation token reached\n");
            break;
        }

        AppendTokenText(ctx->vocab, token, output);

        llama_batch batch = llama_batch_get_one(&token, 1);
        int32_t decodeResult = llama_decode(ctx->ctx, batch);
        if (decodeResult == 2)
        {
            WriteToLogFile("[GEN] ", "Decode returned 2 (cancelled)\n");
            llama_sampler_free(sampler);
            return 0;
        }
        if (decodeResult != 0)
        {
            char errMsg[64];
            snprintf(errMsg, sizeof(errMsg), "Token decode failed at token %d, result=%d\n", i, decodeResult);
            WriteToLogFile("[GEN] ", errMsg);
            SetContextError(ctx, "Token decode failed.");
            llama_sampler_free(sampler);
            return -1;
        }

        if (static_cast<int>(output.size()) >= outCapacity - 1)
        {
            WriteToLogFile("[GEN] ", "Output buffer full\n");
            break;
        }
    }

    WriteToLogFile("[GEN] ", "Generation loop complete, freeing sampler...\n");
    llama_sampler_free(sampler);

    int copyLen = static_cast<int>(output.size());
    if (copyLen >= outCapacity)
    {
        copyLen = outCapacity - 1;
    }

    char finalMsg[128];
    snprintf(finalMsg, sizeof(finalMsg), "Generation complete, output length: %d\n", copyLen);
    WriteToLogFile("[GEN] ", finalMsg);

    std::memcpy(outText, output.data(), static_cast<size_t>(copyLen));
    outText[copyLen] = '\0';
    return copyLen;
}

// Public-facing generation function with full locking
static int GenerateWithPrompt(ImnpcContext* ctx, const char* prompt, const LlamaGenerationConfig* cfg, char* outText, int outCapacity)
{
    if (!ctx)
    {
        SetGlobalError("Context is null.");
        return -1;
    }

    // Check if context is still loading
    if (ctx->loading.load())
    {
        SetContextError(ctx, "Context is still loading. Wait for model to be ready.");
        return -1;
    }

    // Check if context is ready
    if (!ctx->ready.load())
    {
        SetContextError(ctx, "Context is not ready. Model may have failed to load.");
        return -1;
    }

    // Lock the context for the entire operation
    std::lock_guard<std::mutex> lock(ctx->contextMutex);

    return GenerateWithPromptUnlocked(ctx, prompt, cfg, outText, outCapacity);
}

extern "C" {

void* imnpc_llama_create(const char* modelPath, const LlamaModelConfig* config)
{
    // Open log file for crash diagnostics
    OpenLogFile();
    WriteToLogFile("[IMNPC] ", "imnpc_llama_create called\n");

    // Thread-safe backend initialization with full synchronization
    {
        std::lock_guard<std::mutex> backendLock(g_backendMutex);
        std::call_once(g_backendInit, []() {
            WriteToLogFile("[IMNPC] ", "Initializing llama backend...\n");
            llama_backend_init();
            ggml_backend_load_all();
            llama_log_set(LogCallback, nullptr);
            // Give GPU backends time to fully initialize
            // This is particularly important for CUDA which has async initialization
            g_backendReady.store(true);
            WriteToLogFile("[IMNPC] ", "Backend initialized successfully\n");
        });
    }

    // Wait for backend to be fully ready
    while (!g_backendReady.load())
    {
        // Spin wait - should be very brief
    }

    if (!modelPath || modelPath[0] == '\0')
    {
        SetGlobalError("Model path is empty.");
        return nullptr;
    }

    ImnpcContext* ctx = new ImnpcContext();
    ctx->loading.store(true);
    
    if (config)
    {
        ctx->seed = config->seed;
        ctx->threads = config->threads > 0 ? config->threads : 4;
        ctx->contextSize = config->contextSize > 0 ? config->contextSize : 2048;
    }

    llama_model_params mparams = llama_model_default_params();
    if (config)
    {
        mparams.n_gpu_layers = config->gpuLayers;
        mparams.use_mmap = config->useMmap != 0;
        mparams.use_mlock = config->useMlock != 0;
        mparams.no_host = config->allowHostMemory == 0;
    }

    // Model loading - this is the slow part
    ctx->model = llama_model_load_from_file(modelPath, mparams);
    if (!ctx->model)
    {
        std::string detail = GetGlobalError();
        if (!detail.empty() && detail != "Failed to load model.")
        {
            SetContextError(ctx, "Failed to load model. " + detail);
        }
        else
        {
            SetContextError(ctx, "Failed to load model.");
        }
        ctx->loading.store(false);
        delete ctx;
        return nullptr;
    }

    llama_context_params cparams = llama_context_default_params();
    cparams.n_ctx = ctx->contextSize;
    cparams.n_threads = ctx->threads;
    cparams.n_threads_batch = ctx->threads;
    cparams.abort_callback = AbortCallback;
    cparams.abort_callback_data = ctx;

    ctx->ctx = llama_init_from_model(ctx->model, cparams);
    if (!ctx->ctx)
    {
        SetContextError(ctx, "Failed to create context.");
        llama_model_free(ctx->model);
        ctx->model = nullptr;
        ctx->loading.store(false);
        delete ctx;
        return nullptr;
    }

    ctx->vocab = llama_model_get_vocab(ctx->model);
    ctx->lastError.clear();
    
    // Detect model's native pooling type for embedding support
    // This determines how we'll handle embeddings for this model
    // Try to read from model metadata first, fall back to checking architecture
    char poolingBuf[32] = {0};
    int32_t poolingLen = llama_model_meta_val_str(ctx->model, "llama.pooling_type", poolingBuf, sizeof(poolingBuf));
    
    if (poolingLen > 0)
    {
        // Got pooling type from metadata
        ctx->modelPoolingType = std::atoi(poolingBuf);
    }
    else
    {
        // No pooling type in metadata - assume it's a causal model (-1)
        ctx->modelPoolingType = LLAMA_POOLING_TYPE_UNSPECIFIED;
    }
    
    // If model doesn't have native pooling support (pooling_type = -1/UNSPECIFIED or 0/NONE),
    // we'll use manual embedding extraction (like ollama does for causal models)
    ctx->useManualEmbedding = (ctx->modelPoolingType == LLAMA_POOLING_TYPE_UNSPECIFIED || 
                               ctx->modelPoolingType == LLAMA_POOLING_TYPE_NONE);
    
    char poolingMsg[128];
    snprintf(poolingMsg, sizeof(poolingMsg), "Model pooling type: %d, useManualEmbedding: %s\n", 
             ctx->modelPoolingType, ctx->useManualEmbedding ? "true" : "false");
    WriteToLogFile("[IMNPC] ", poolingMsg);
    
    // Mark as ready only after everything is fully initialized
    ctx->loading.store(false);
    ctx->ready.store(true);
    
    return ctx;
}

void imnpc_llama_destroy(void* handle)
{
    WriteToLogFile("[IMNPC] ", "imnpc_llama_destroy called\n");
    
    ImnpcContext* ctx = static_cast<ImnpcContext*>(handle);
    if (!ctx)
    {
        WriteToLogFile("[IMNPC] ", "destroy: ctx is null\n");
        return;
    }

    WriteToLogFile("[IMNPC] ", "destroy: marking as not ready...\n");
    
    // Mark as not ready to prevent any new operations
    ctx->ready.store(false);
    ctx->cancel.store(true);  // Cancel any ongoing operations

    WriteToLogFile("[IMNPC] ", "destroy: acquiring mutex...\n");
    
    // Lock to wait for any ongoing operations to finish
    std::lock_guard<std::mutex> lock(ctx->contextMutex);
    
    WriteToLogFile("[IMNPC] ", "destroy: mutex acquired, freeing resources...\n");

    // WORKAROUND: llama.cpp's CUDA backend crashes when freeing contexts that share
    // a model. This appears to be a known issue with shared GPU resources.
    // Instead of crashing, we intentionally leak the contexts and model.
    // The OS will reclaim all memory when the process exits.
    // This is a common pattern for Unity native plugins with problematic cleanup.
    
    // Just null out the pointers to prevent any further use
    WriteToLogFile("[IMNPC] ", "destroy: clearing context pointers (not freeing due to CUDA issues)\n");
    ctx->ctx = nullptr;
    ctx->embed_ctx = nullptr;
    ctx->model = nullptr;
    
    // Note: We intentionally do NOT call llama_free() or llama_model_free()
    // because it causes crashes with CUDA. The memory will be freed when
    // the process terminates.

    WriteToLogFile("[IMNPC] ", "destroy: deleting context struct\n");
    delete ctx;
    WriteToLogFile("[IMNPC] ", "destroy: done\n");
}

int imnpc_llama_generate(void* handle, const char* prompt, const LlamaGenerationConfig* cfg, char* outText, int outCapacity)
{
    WriteToLogFile("[IMNPC] ", "imnpc_llama_generate called\n");
    
    ImnpcContext* ctx = static_cast<ImnpcContext*>(handle);
    if (!ctx)
    {
        WriteToLogFile("[IMNPC] ", "ERROR: Context is null\n");
        SetGlobalError("Context is null.");
        if (outText && outCapacity > 0) outText[0] = '\0';
        return -1;
    }

    // Check loading/ready state before doing anything
    if (ctx->loading.load())
    {
        WriteToLogFile("[IMNPC] ", "ERROR: Context is still loading\n");
        SetContextError(ctx, "Context is still loading.");
        if (outText && outCapacity > 0) outText[0] = '\0';
        return -1;
    }
    if (!ctx->ready.load())
    {
        WriteToLogFile("[IMNPC] ", "ERROR: Context is not ready\n");
        SetContextError(ctx, "Context is not ready.");
        if (outText && outCapacity > 0) outText[0] = '\0';
        return -1;
    }

    WriteToLogFile("[IMNPC] ", "Calling GenerateWithPrompt...\n");
    const char* safePrompt = prompt ? prompt : "";
    int result = GenerateWithPrompt(ctx, safePrompt, cfg, outText, outCapacity);
    
    char resultMsg[64];
    snprintf(resultMsg, sizeof(resultMsg), "GenerateWithPrompt returned: %d\n", result);
    WriteToLogFile("[IMNPC] ", resultMsg);
    
    return result;
}

int imnpc_llama_generate_chat(void* handle, const char* systemPrompt, const char* userPrompt, int templateMode, const LlamaGenerationConfig* cfg, char* outText, int outCapacity)
{
    WriteToLogFile("[IMNPC] ", "imnpc_llama_generate_chat called\n");
    
    ImnpcContext* ctx = static_cast<ImnpcContext*>(handle);
    if (!ctx)
    {
        WriteToLogFile("[IMNPC] ", "ERROR: Context is null\n");
        SetGlobalError("Context is null.");
        if (outText && outCapacity > 0) outText[0] = '\0';
        return -1;
    }

    // Check loading/ready state before doing anything
    if (ctx->loading.load())
    {
        WriteToLogFile("[IMNPC] ", "ERROR: Context is still loading\n");
        SetContextError(ctx, "Context is still loading.");
        if (outText && outCapacity > 0) outText[0] = '\0';
        return -1;
    }
    if (!ctx->ready.load())
    {
        WriteToLogFile("[IMNPC] ", "ERROR: Context is not ready\n");
        SetContextError(ctx, "Context is not ready.");
        if (outText && outCapacity > 0) outText[0] = '\0';
        return -1;
    }

    WriteToLogFile("[IMNPC] ", "Acquiring context mutex for generate_chat...\n");
    
    // Lock the context mutex before accessing ctx->model in BuildPromptFromChat
    std::lock_guard<std::mutex> lock(ctx->contextMutex);
    
    WriteToLogFile("[IMNPC] ", "Mutex acquired, building prompt from chat...\n");

    std::string prompt = BuildPromptFromChat(ctx, systemPrompt, userPrompt, templateMode);
    if (prompt.empty())
    {
        WriteToLogFile("[IMNPC] ", "Chat template failed, using raw prompt\n");
        prompt = BuildRawPrompt(systemPrompt ? systemPrompt : "", userPrompt ? userPrompt : "");
    }
    
    WriteToLogFile("[IMNPC] ", "Prompt built successfully\n");

    // Note: GenerateWithPrompt will try to lock again, so we need to use a recursive approach
    // or refactor. For now, let's call the internal generation directly since we already hold the lock.
    
    if (!ctx->ctx || !ctx->model || !ctx->vocab)
    {
        WriteToLogFile("[IMNPC] ", "ERROR: Context not initialized\n");
        SetContextError(ctx, "Context is not initialized.");
        if (outText && outCapacity > 0) outText[0] = '\0';
        return -1;
    }

    if (!outText || outCapacity <= 0)
    {
        WriteToLogFile("[IMNPC] ", "ERROR: Output buffer invalid\n");
        SetContextError(ctx, "Output buffer is invalid.");
        return -1;
    }

    ctx->cancel.store(false);
    ctx->lastError.clear();

    WriteToLogFile("[IMNPC] ", "Calling GenerateWithPromptUnlocked...\n");
    
    // Call the generation logic directly (we already hold the lock)
    int result = GenerateWithPromptUnlocked(ctx, prompt.c_str(), cfg, outText, outCapacity);
    
    char resultMsg[64];
    snprintf(resultMsg, sizeof(resultMsg), "GenerateWithPromptUnlocked returned: %d\n", result);
    WriteToLogFile("[IMNPC] ", resultMsg);
    
    return result;
}

int imnpc_llama_embedding_size(void* handle)
{
    WriteToLogFile("[IMNPC] ", "imnpc_llama_embedding_size called\n");
    
    ImnpcContext* ctx = static_cast<ImnpcContext*>(handle);
    if (!ctx)
    {
        WriteToLogFile("[IMNPC] ", "embedding_size: ctx is null\n");
        return 0;
    }

    // Check if context is still loading
    if (ctx->loading.load())
    {
        WriteToLogFile("[IMNPC] ", "embedding_size: still loading\n");
        return 0;
    }

    // Check if context is ready
    if (!ctx->ready.load())
    {
        WriteToLogFile("[IMNPC] ", "embedding_size: not ready\n");
        return 0;
    }

    // Lock the context for the read operation
    std::lock_guard<std::mutex> lock(ctx->contextMutex);

    if (!ctx->model)
    {
        WriteToLogFile("[IMNPC] ", "embedding_size: model is null\n");
        return 0;
    }

    int size = llama_model_n_embd(ctx->model);
    char msg[64];
    snprintf(msg, sizeof(msg), "embedding_size: returning %d\n", size);
    WriteToLogFile("[IMNPC] ", msg);
    return size;
}

int imnpc_llama_embed(void* handle, const char* text, float* outEmbedding, int maxElements)
{
    WriteToLogFile("[IMNPC] ", "imnpc_llama_embed called\n");
    
    ImnpcContext* ctx = static_cast<ImnpcContext*>(handle);
    if (!ctx)
    {
        WriteToLogFile("[IMNPC] ", "embed: ctx is null\n");
        SetGlobalError("Context is null.");
        return -1;
    }

    // Check if context is still loading
    if (ctx->loading.load())
    {
        WriteToLogFile("[IMNPC] ", "embed: still loading\n");
        SetContextError(ctx, "Context is still loading. Wait for model to be ready.");
        return -1;
    }

    // Check if context is ready
    if (!ctx->ready.load())
    {
        WriteToLogFile("[IMNPC] ", "embed: not ready\n");
        SetContextError(ctx, "Context is not ready. Model may have failed to load.");
        return -1;
    }

    WriteToLogFile("[IMNPC] ", "embed: acquiring mutex...\n");
    
    // Lock the context for the entire operation
    std::lock_guard<std::mutex> lock(ctx->contextMutex);
    
    WriteToLogFile("[IMNPC] ", "embed: mutex acquired\n");

    if (!ctx->model || !ctx->vocab)
    {
        WriteToLogFile("[IMNPC] ", "embed: model or vocab is null\n");
        SetContextError(ctx, "Context is not initialized.");
        return -1;
    }

    if (!outEmbedding || maxElements <= 0)
    {
        WriteToLogFile("[IMNPC] ", "embed: output buffer invalid\n");
        SetContextError(ctx, "Embedding output buffer is invalid.");
        return -1;
    }

    ctx->cancel.store(false);
    ctx->lastError.clear();

    // Check model's embedding support
    char modeMsg[128];
    snprintf(modeMsg, sizeof(modeMsg), "embed: modelPoolingType=%d, useManualEmbedding=%s\n", 
             ctx->modelPoolingType, ctx->useManualEmbedding ? "true" : "false");
    WriteToLogFile("[IMNPC] ", modeMsg);

    // For causal models (like Qwen3) that don't natively support embeddings,
    // we need to use embeddings=true and extract from hidden states
    // For native embedding models, use their pooling support

    // Create embedding context if needed (on first call)
    // We persist this context to avoid the crash that occurs when freeing
    // a CUDA context while the model is shared with another context
    if (!ctx->embed_ctx)
    {
        WriteToLogFile("[IMNPC] ", "embed: creating embedding context...\n");
        
        llama_context_params eparams = llama_context_default_params();
        eparams.n_ctx = ctx->contextSize;
        eparams.n_threads = ctx->threads;
        eparams.n_threads_batch = ctx->threads;
        eparams.abort_callback = AbortCallback;
        eparams.abort_callback_data = ctx;
        
        // Enable embeddings mode - this exposes hidden states
        eparams.embeddings = true;
        
        if (ctx->useManualEmbedding)
        {
            // For causal models: use no pooling, we'll extract from last token
            eparams.pooling_type = LLAMA_POOLING_TYPE_NONE;
            WriteToLogFile("[IMNPC] ", "embed: using POOLING_NONE for causal model\n");
        }
        else
        {
            // For native embedding models: use their pooling type
            eparams.pooling_type = static_cast<enum llama_pooling_type>(ctx->modelPoolingType);
            char poolMsg[64];
            snprintf(poolMsg, sizeof(poolMsg), "embed: using native pooling type %d\n", ctx->modelPoolingType);
            WriteToLogFile("[IMNPC] ", poolMsg);
        }

        ctx->embed_ctx = llama_init_from_model(ctx->model, eparams);
        if (!ctx->embed_ctx)
        {
            WriteToLogFile("[IMNPC] ", "embed: failed to create embedding context\n");
            SetContextError(ctx, "Failed to create embedding context.");
            return -1;
        }
        WriteToLogFile("[IMNPC] ", "embed: embedding context created\n");
    }

    WriteToLogFile("[IMNPC] ", "embed: setting threads and clearing memory...\n");
    llama_set_n_threads(ctx->embed_ctx, ctx->threads, ctx->threads);
    llama_memory_clear(llama_get_memory(ctx->embed_ctx), true);

    WriteToLogFile("[IMNPC] ", "embed: tokenizing...\n");
    std::vector<llama_token> tokens;
    if (!TokenizePrompt(ctx, text, tokens))
    {
        WriteToLogFile("[IMNPC] ", "embed: tokenization failed\n");
        return -1;
    }
    
    if (tokens.empty())
    {
        WriteToLogFile("[IMNPC] ", "embed: no tokens\n");
        SetContextError(ctx, "No tokens to embed.");
        return -1;
    }
    
    char tokenMsg[64];
    snprintf(tokenMsg, sizeof(tokenMsg), "embed: tokenized %zu tokens\n", tokens.size());
    WriteToLogFile("[IMNPC] ", tokenMsg);

    int32_t n_tokens = static_cast<int32_t>(tokens.size());
    int32_t embd_size = llama_model_n_embd(ctx->model);
    
    // Create a proper batch with all required fields
    WriteToLogFile("[IMNPC] ", "embed: creating batch...\n");
    
    llama_batch batch = llama_batch_init(n_tokens, 0, 1);
    
    if (!batch.token)
    {
        WriteToLogFile("[IMNPC] ", "embed: failed to init batch\n");
        SetContextError(ctx, "Failed to initialize embedding batch.");
        return -1;
    }
    
    // Fill batch - for manual embedding, mark only last token for output
    // For native embedding models, mark all tokens
    for (int32_t i = 0; i < n_tokens; i++)
    {
        batch.token[i] = tokens[static_cast<size_t>(i)];
        batch.pos[i] = i;
        batch.n_seq_id[i] = 1;
        batch.seq_id[i][0] = 0;
        
        if (ctx->useManualEmbedding)
        {
            // For causal models: only need output for last token
            batch.logits[i] = (i == n_tokens - 1) ? 1 : 0;
        }
        else
        {
            // For native embedding models: need output for all tokens (for pooling)
            batch.logits[i] = 1;
        }
    }
    batch.n_tokens = n_tokens;
    
    char batchMsg[128];
    snprintf(batchMsg, sizeof(batchMsg), "embed: batch created with %d tokens, calling decode...\n", n_tokens);
    WriteToLogFile("[IMNPC] ", batchMsg);
    
    int32_t decodeResult = llama_decode(ctx->embed_ctx, batch);
    llama_batch_free(batch);
    
    char decodeMsg[64];
    snprintf(decodeMsg, sizeof(decodeMsg), "embed: decode result = %d\n", decodeResult);
    WriteToLogFile("[IMNPC] ", decodeMsg);
    
    if (decodeResult == 2)
    {
        WriteToLogFile("[IMNPC] ", "embed: decode cancelled\n");
        return 0;
    }
    if (decodeResult != 0)
    {
        WriteToLogFile("[IMNPC] ", "embed: decode failed\n");
        SetContextError(ctx, "Embedding decode failed.");
        return -1;
    }
    
    // Synchronize to ensure all GPU operations are complete before accessing embeddings
    WriteToLogFile("[IMNPC] ", "embed: synchronizing...\n");
    llama_synchronize(ctx->embed_ctx);
    WriteToLogFile("[IMNPC] ", "embed: synchronize complete\n");
    
    // Get embeddings
    WriteToLogFile("[IMNPC] ", "embed: getting embeddings...\n");
    float* embedding = nullptr;
    
    if (ctx->useManualEmbedding)
    {
        // For causal models: get embedding at last token position
        embedding = llama_get_embeddings_ith(ctx->embed_ctx, n_tokens - 1);
        if (!embedding)
        {
            WriteToLogFile("[IMNPC] ", "embed: llama_get_embeddings_ith returned null, trying llama_get_embeddings\n");
            // Fallback: try to get from general embeddings array
            float* all_embeddings = llama_get_embeddings(ctx->embed_ctx);
            if (all_embeddings)
            {
                embedding = all_embeddings + (n_tokens - 1) * embd_size;
            }
        }
    }
    else
    {
        // For native embedding models: get pooled embedding for sequence 0
        embedding = llama_get_embeddings_seq(ctx->embed_ctx, 0);
        if (!embedding)
        {
            WriteToLogFile("[IMNPC] ", "embed: llama_get_embeddings_seq returned null, trying llama_get_embeddings\n");
            embedding = llama_get_embeddings(ctx->embed_ctx);
        }
    }
    
    if (!embedding)
    {
        WriteToLogFile("[IMNPC] ", "embed: no embedding available\n");
        SetContextError(ctx, "Embedding output not available. This model may not support embeddings.");
        // Don't free embed_ctx - it's persisted for reuse
        return -1;
    }
    
    int count = embd_size;
    if (count > maxElements)
    {
        count = maxElements;
    }
    
    char countMsg[128];
    snprintf(countMsg, sizeof(countMsg), "embed: embd_size=%d, count=%d, maxElements=%d, embedding ptr=%p, outEmbedding ptr=%p\n", 
             embd_size, count, maxElements, (void*)embedding, (void*)outEmbedding);
    WriteToLogFile("[IMNPC] ", countMsg);
    
    // Verify output buffer is valid
    if (!outEmbedding)
    {
        WriteToLogFile("[IMNPC] ", "embed: ERROR - outEmbedding is null!\n");
        // Don't free embed_ctx - it's persisted for reuse
        SetContextError(ctx, "Output embedding buffer is null.");
        return -1;
    }
    
    // Normalize the embedding (L2 normalization)
    // First copy to output buffer to ensure we're working with valid memory
    WriteToLogFile("[IMNPC] ", "embed: copying to output buffer...\n");
    
    // Copy in smaller chunks to isolate any crash point
    for (int i = 0; i < count; i++)
    {
        outEmbedding[i] = embedding[i];
    }
    WriteToLogFile("[IMNPC] ", "embed: copy complete\n");
    
    WriteToLogFile("[IMNPC] ", "embed: calculating norm...\n");
    float norm = 0.0f;
    for (int i = 0; i < count; i++)
    {
        norm += outEmbedding[i] * outEmbedding[i];
    }
    norm = sqrtf(norm);
    
    char normMsg[64];
    snprintf(normMsg, sizeof(normMsg), "embed: norm=%f\n", norm);
    WriteToLogFile("[IMNPC] ", normMsg);
    
    if (norm > 0.0f)
    {
        WriteToLogFile("[IMNPC] ", "embed: normalizing values...\n");
        for (int i = 0; i < count; i++)
        {
            outEmbedding[i] = outEmbedding[i] / norm;
        }
        WriteToLogFile("[IMNPC] ", "embed: normalization complete\n");
    }
    // If norm is 0, embedding is already copied (all zeros)
    
    // NOTE: We do NOT free embed_ctx here - it's persisted and reused for subsequent calls
    // Freeing it while the model is shared with the main context causes CUDA crashes
    // The embed_ctx will be freed when the main context is destroyed
    WriteToLogFile("[IMNPC] ", "embed: keeping embed_ctx for reuse\n");
    
    char resultMsg[64];
    snprintf(resultMsg, sizeof(resultMsg), "embed: returning %d elements (normalized)\n", count);
    WriteToLogFile("[IMNPC] ", resultMsg);
    
    return count;
}

void imnpc_llama_cancel(void* handle)
{
    WriteToLogFile("[IMNPC] ", "imnpc_llama_cancel called\n");
    
    ImnpcContext* ctx = static_cast<ImnpcContext*>(handle);
    if (!ctx)
    {
        WriteToLogFile("[IMNPC] ", "cancel: ctx is null\n");
        return;
    }
    ctx->cancel.store(true);
    WriteToLogFile("[IMNPC] ", "cancel: done\n");
}

int imnpc_llama_is_ready(void* handle)
{
    ImnpcContext* ctx = static_cast<ImnpcContext*>(handle);
    if (!ctx)
    {
        return 0;
    }
    return ctx->ready.load() ? 1 : 0;
}

int imnpc_llama_is_loading(void* handle)
{
    ImnpcContext* ctx = static_cast<ImnpcContext*>(handle);
    if (!ctx)
    {
        return 0;
    }
    return ctx->loading.load() ? 1 : 0;
}

int imnpc_llama_last_error(char* buffer, int capacity)
{
    if (!buffer || capacity <= 0)
    {
        return 0;
    }

    std::lock_guard<std::mutex> lock(g_errorMutex);
    int length = static_cast<int>(g_lastError.size());
    if (length <= 0)
    {
        std::lock_guard<std::mutex> logLock(g_logMutex);
        length = static_cast<int>(g_lastLog.size());
        if (length <= 0)
        {
            buffer[0] = '\0';
            return 0;
        }

        if (length >= capacity)
        {
            length = capacity - 1;
        }

        std::memcpy(buffer, g_lastLog.data(), static_cast<size_t>(length));
        buffer[length] = '\0';
        return length;
    }

    if (length >= capacity)
    {
        length = capacity - 1;
    }

    std::memcpy(buffer, g_lastError.data(), static_cast<size_t>(length));
    buffer[length] = '\0';
    return length;
}

int imnpc_llama_backend_summary(char* buffer, int capacity)
{
    if (!buffer || capacity <= 0)
    {
        return 0;
    }

    size_t count = ggml_backend_reg_count();
    std::string summary = "backends=" + std::to_string(count);
    if (count > 0)
    {
        summary.append(" [");
        for (size_t i = 0; i < count; i++)
        {
            ggml_backend_reg_t reg = ggml_backend_reg_get(i);
            if (!reg)
            {
                continue;
            }

            const char* name = ggml_backend_reg_name(reg);
            size_t devCount = ggml_backend_reg_dev_count(reg);
            if (name && name[0] != '\0')
            {
                summary.append(name);
            }
            else
            {
                summary.append("Unknown");
            }
            summary.push_back('(');
            summary.append(std::to_string(devCount));
            summary.push_back(')');

            if (i + 1 < count)
            {
                summary.append(", ");
            }
        }
        summary.append("]");
    }

    int length = static_cast<int>(summary.size());
    if (length >= capacity)
    {
        length = capacity - 1;
    }

    std::memcpy(buffer, summary.data(), static_cast<size_t>(length));
    buffer[length] = '\0';
    return length;
}

void imnpc_llama_set_logging(int enabled)
{
    g_loggingEnabled.store(enabled != 0);
    if (enabled)
    {
        llama_log_set(LogCallback, nullptr);
    }
    else
    {
        llama_log_set(nullptr, nullptr);
    }
}

int imnpc_llama_get_log(char* buffer, int capacity, int clear)
{
    if (!buffer || capacity <= 0)
    {
        return 0;
    }

    std::lock_guard<std::mutex> lock(g_logMutex);
    int length = static_cast<int>(g_lastLog.size());
    if (length <= 0)
    {
        buffer[0] = '\0';
        return 0;
    }

    if (length >= capacity)
    {
        length = capacity - 1;
    }

    std::memcpy(buffer, g_lastLog.data(), static_cast<size_t>(length));
    buffer[length] = '\0';

    if (clear)
    {
        g_lastLog.clear();
    }

    return length;
}

} // extern "C"
