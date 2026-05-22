using System;
using System.Text;

namespace ImmersiveNPCs
{
    public static class InProcessNativeLog
    {
        private const int BufferSize = 8192;
        private static bool? loggingEnabled;
        private static bool warnedMissing;

        public static void SetLoggingEnabled(bool enabled)
        {
            if (loggingEnabled.HasValue && loggingEnabled.Value == enabled)
            {
                return;
            }

            loggingEnabled = enabled;
            try
            {
                LlamaNative.SetLogging(enabled ? 1 : 0);
            }
            catch (DllNotFoundException)
            {
                WarnMissingPlugin();
            }
            catch (EntryPointNotFoundException)
            {
                WarnMissingPlugin();
            }
            catch (Exception ex)
            {
                AILogger.Warn("Failed to toggle native logging: " + ex.Message);
            }
        }

        public static string ReadAndClear()
        {
            try
            {
                StringBuilder buffer = new StringBuilder(BufferSize);
                int length = LlamaNative.GetLog(buffer, buffer.Capacity, 1);
                if (length > 0)
                {
                    return buffer.ToString();
                }
            }
            catch (DllNotFoundException)
            {
                WarnMissingPlugin();
            }
            catch (EntryPointNotFoundException)
            {
                WarnMissingPlugin();
            }
            catch (Exception ex)
            {
                AILogger.Warn("Failed to read native log: " + ex.Message);
            }

            return string.Empty;
        }

        private static void WarnMissingPlugin()
        {
            if (warnedMissing)
            {
                return;
            }

            warnedMissing = true;
            AILogger.Warn("In-process backend plugin missing or outdated. Rebuild the native plugin to enable logging.");
        }
    }
}
