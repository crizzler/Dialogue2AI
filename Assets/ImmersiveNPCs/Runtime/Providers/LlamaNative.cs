using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ImmersiveNPCs
{
    internal static class LlamaNative
    {
        internal const string LibraryName = "immersivenpcs_llama";

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr CreateContext([MarshalAs(UnmanagedType.LPStr)] string modelPath, ref LlamaModelConfig config);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DestroyContext(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_generate", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Generate(IntPtr context, [MarshalAs(UnmanagedType.LPStr)] string prompt, ref LlamaGenerationConfig config, StringBuilder output, int outputCapacity);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_generate_chat", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GenerateChat(
            IntPtr context,
            [MarshalAs(UnmanagedType.LPStr)] string systemPrompt,
            [MarshalAs(UnmanagedType.LPStr)] string userPrompt,
            int templateMode,
            ref LlamaGenerationConfig config,
            StringBuilder output,
            int outputCapacity);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_embedding_size", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetEmbeddingSize(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_embed", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Embed(IntPtr context, [MarshalAs(UnmanagedType.LPStr)] string text, IntPtr output, int maxElements);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_cancel", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Cancel(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_is_ready", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int IsReady(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_is_loading", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int IsLoading(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_last_error", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetLastError(StringBuilder buffer, int capacity);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_backend_summary", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetBackendSummary(StringBuilder buffer, int capacity);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_set_logging", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SetLogging(int enabled);

        [DllImport(LibraryName, EntryPoint = "imnpc_llama_get_log", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int GetLog(StringBuilder buffer, int capacity, int clear);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LlamaModelConfig
    {
        public int contextSize;
        public int threads;
        public int gpuLayers;
        public int useMmap;
        public int useMlock;
        public int seed;
        public int allowHostMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LlamaGenerationConfig
    {
        public int maxTokens;
        public float temperature;
        public float topP;
    }
}
