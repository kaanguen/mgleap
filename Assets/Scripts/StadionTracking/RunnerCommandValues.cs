using System;
using Newtonsoft.Json.Linq;

namespace StadionTracking
{
    /// <summary>Typed, bounded values accepted by the RunningServer command protocol.</summary>
    internal static class RunnerCommandValues
    {
        internal static float Decimal(JObject p, string key, float fallback, float min, float max) =>
            ReadNumber(p, key, fallback, min, max, false);

        internal static float Number(JObject p, string key, float fallback, float min, float max) =>
            ReadNumber(p, key, fallback, min, max, true);

        private static float ReadNumber(JObject p, string key, float fallback, float min, float max, bool integerOnly)
        {
            var token = p[key];
            if (token == null) return fallback;
            if (token.Type != JTokenType.Integer && (integerOnly || token.Type != JTokenType.Float))
                throw new ArgumentException(key + (integerOnly ? " must be an integer." : " must be numeric."));
            double value = (double)token;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(key, $"Expected {min}..{max}.");
            return (float)value;
        }

        internal static bool Boolean(JObject p, string key)
        {
            if (p[key]?.Type != JTokenType.Boolean) throw new ArgumentException(key + " must be a boolean.");
            return (bool)p[key];
        }
    }
}
