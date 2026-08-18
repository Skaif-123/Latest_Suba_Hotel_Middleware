using System;
using Newtonsoft.Json;

namespace AgentSyncConsole.Helpers
{
    /// <summary>
    /// Pure static formatting helpers, ported unmodified from the original
    /// prettyPrint(value) / safeStringifyError(error) helpers. All actual
    /// logging now goes through Microsoft.Extensions.Logging (ILogger&lt;T&gt;),
    /// injected per-class via DI and backed by Serilog - see Program.cs.
    /// </summary>
    public static class AppLogger
    {
        /// <summary>Equivalent of the original prettyPrint(value) helper.</summary>
        public static string PrettyPrint(object value)
        {
            try
            {
                var stringified = JsonConvert.SerializeObject(value, Formatting.Indented);
                if (stringified != null)
                {
                    return stringified;
                }
            }
            catch
            {
                // fall through
            }

            return value?.ToString() ?? "null";
        }

        /// <summary>Equivalent of the original safeStringifyError(error) helper.</summary>
        public static string SafeStringifyError(Exception error)
        {
            if (error == null)
            {
                return "null";
            }

            try
            {
                var stringified = JsonConvert.SerializeObject(new
                {
                    Type = error.GetType().FullName,
                    error.Message,
                    error.StackTrace
                }, Formatting.Indented);

                if (!string.IsNullOrEmpty(stringified) && stringified != "{}")
                {
                    return stringified;
                }
            }
            catch
            {
                // fall through
            }

            return error.ToString();
        }
    }
}
