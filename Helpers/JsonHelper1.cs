using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AgentSyncConsole.Helpers
{
    /// <summary>
    /// Replaces JSON.parse()/JSON.stringify() with Newtonsoft.Json, and
    /// reproduces JS's forgiving parseInt(x) || 0 semantics used throughout
    /// the original code for mobileNo/phoneNo/zip fields (leading numeric
    /// characters are parsed, everything else -> 0, never throws).
    /// </summary>
    public static class JsonHelper1
    {
        private static readonly Regex LeadingIntPattern = new Regex(@"^[+-]?\d+", RegexOptions.Compiled);

        public static JObject ParseJObject(string json)
        {
            // Throws on invalid JSON - caller catches this exactly like the
            // original try { JSON.parse(row.agent) } catch (parseErr) block.
            return JObject.Parse(json);
        }

        public static int ParseIntOrZero(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return 0;
            }

            var raw = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
            return ParseIntOrZero(raw);
        }

        public static int ParseIntOrZero(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return 0;
            }

            var match = LeadingIntPattern.Match(raw.Trim());

            if (match.Success && int.TryParse(match.Value, out var result))
            {
                return result;
            }

            return 0;
        }
    }
}
