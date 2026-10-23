using System;
using StadionTracking;

internal static class LocomotionChecks
{
    private static void Check(float speed, float expectedBlend, float expectedRate,
        float walk = 1.5f, float run = 3f, float sprint = 6f)
    {
        LocomotionTiming.Evaluate(speed, walk, run, sprint, out float blend, out float rate);
        if (Math.Abs(blend - expectedBlend) > 0.00001f || Math.Abs(rate - expectedRate) > 0.00001f)
            throw new Exception($"At {speed} m/s: blend={blend}, rate={rate}");
        float naturalSpeed = blend <= 0.25f ? walk : blend < 0.5f
            ? walk + (run - walk) * (blend - 0.25f) / 0.25f
            : run + (sprint - run) * (blend - 0.5f) / 0.5f;
        if (speed > 0f && Math.Abs(naturalSpeed * rate - speed) > 0.00001f)
            throw new Exception("Foot cadence does not match travel speed");
    }

    public static void Main()
    {
        Check(0f, 0f, 1f);
        Check(0.015f, 0.25f, 0.01f);
        Check(0.75f, 0.25f, 0.5f);
        Check(1.5f, 0.25f, 1f);
        Check(2.25f, 0.375f, 1f);
        Check(3f, 0.5f, 1f);
        Check(4.5f, 0.75f, 1f);
        Check(6f, 1f, 1f);
        Check(9f, 1f, 1.5f);
        Check(3f, 0.375f, 1f, 2f, 4f, 8f);
        Check(6f, 0.75f, 1f, 2f, 4f, 8f);
        Check(12f, 1f, 1.5f, 2f, 4f, 8f);
        Console.WriteLine("PASS: idle, slow walking, gait boundaries, blends, sprint scaling and calibrated clip speeds.");
    }
}
