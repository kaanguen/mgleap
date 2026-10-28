using System;
using Newtonsoft.Json.Linq;
using StadionTracking;

internal static class CommandValueChecks
{
    public static void Main()
    {
        var payload = new JObject();
        Require(RunnerCommandValues.Number(payload, "lane", 3, 1, 8) == 3, "absent value uses fallback");
        foreach (int lane in new[] { 1, 8 })
        {
            payload["lane"] = lane;
            Require(RunnerCommandValues.Number(payload, "lane", 3, 1, 8) == lane, "inclusive integer bounds");
        }
        foreach (JToken value in new JToken[] { 0, 9, 1.5, "3", true, JValue.CreateNull(), double.NaN, double.PositiveInfinity })
        {
            payload["lane"] = value;
            Reject(() => RunnerCommandValues.Number(payload, "lane", 3, 1, 8), "invalid lane rejected");
        }
        foreach (double distance in new[] { .5, 2.25, 20 })
        {
            payload["distance"] = distance;
            Require(RunnerCommandValues.Decimal(payload, "distance", 2, .5f, 20) == (float)distance,
                "decimal distance and inclusive bounds");
        }
        payload["distance"] = 2;
        Require(RunnerCommandValues.Decimal(payload, "distance", 1, .5f, 20) == 2, "decimal accepts integers");
        foreach (JToken value in new JToken[] { .49, 20.01, "2", false, JValue.CreateNull(), double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            payload["distance"] = value;
            Reject(() => RunnerCommandValues.Decimal(payload, "distance", 2, .5f, 20), "invalid distance rejected");
        }
        foreach (bool visible in new[] { false, true })
        {
            payload["visible"] = visible;
            Require(RunnerCommandValues.Boolean(payload, "visible") == visible, "boolean retained");
        }
        foreach (JToken value in new JToken[] { 1, "true", JValue.CreateNull() })
        {
            payload["visible"] = value;
            Reject(() => RunnerCommandValues.Boolean(payload, "visible"), "non-boolean rejected");
        }
        Reject(() => RunnerCommandValues.Boolean(new JObject(), "visible"), "missing boolean rejected");
        Console.WriteLine("PASS: command values, numeric types, bounds, nulls, booleans and non-finite inputs.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception(message);
    }
}
