using System;
using StadionTracking;

internal static class GeometryChecks
{
    private static void Require(bool condition, string label)
    {

        if (!condition) throw new Exception(label);
    }

    // Check track geometry, map projection, pause and the exact 400-metre finish.
    public static void Main()
    {

        var track = new StadiumTrackGeometry();
        Require(Math.Abs(track.Length(1) - 400.0012193042) < 0.000001, "400m measurement line");
        Require(track.Length(8) > 453 && track.Length(8) < 454, "outer lane length");
        Require(track.DetectLane(0, 0) == 0, "infield rejected");
        Require(track.DetectLane(0, 50) == 0, "outside rejected");

        for (int lane = 1; lane <= 8; lane++)
        {
            double length = track.Length(lane);
            double bend = Math.PI * track.Radius(lane);

            foreach (double seam in new[] { 0.0, bend, bend + StadiumTrackGeometry.StraightLength,
                2 * bend + StadiumTrackGeometry.StraightLength, length })
            {
                var before = track.Evaluate(lane, seam - 1e-7);
                var after = track.Evaluate(lane, seam + 1e-7);
                Require(Distance(before, after) < 1e-6, "position continuous at seam");
                Require(Math.Abs(before.DirectionX - after.DirectionX) < 1e-6 &&
                    Math.Abs(before.DirectionZ - after.DirectionZ) < 1e-6, "heading continuous at seam");
            }

            for (int i = 0; i < 2000; i++)
            {
                double s = i * length / 2000;
                var point = track.Evaluate(lane, s);
                Require(track.DetectLane(point.X, point.Z) == lane, "lane detected around oval");
                double projected = track.Project(lane, point.X, point.Z);
                Require(Distance(point, track.Evaluate(lane, projected)) < 1e-8, "projection roundtrip");
                Require(Distance(point, track.Evaluate(lane, s + length)) < 1e-8, "one lap returns to entry");
                Require(Distance(point, track.Evaluate(lane, s - length)) < 1e-8, "negative wrap");
                Require(Math.Abs(point.DirectionX * point.DirectionX + point.DirectionZ * point.DirectionZ - 1) < 1e-10,
                    "unit tangent");
            }
        }

        var random = new Random(42);
        for (int i = 0; i < 80; i++)
        {
            int lane = 1 + i % 8;
            double x = random.NextDouble() * 240 - 120;
            double z = random.NextDouble() * 160 - 80;
            var closest = track.Evaluate(lane, track.Project(lane, x, z));
            double exact = Math.Sqrt((closest.X - x) * (closest.X - x) + (closest.Z - z) * (closest.Z - z));
            for (int j = 0; j < 10000; j++)
            {
                var sample = track.Evaluate(lane, track.Length(lane) * j / 10000);
                double candidate = Math.Sqrt((sample.X - x) * (sample.X - x) + (sample.Z - z) * (sample.Z - z));
                Require(exact <= candidate + 1e-8, "projection is global minimum");
            }
        }

        foreach (int framesPerSecond in new[] { 30, 60, 120 })
        {
            var run = new StadiumRunProgress();
            for (int frame = 0; frame < framesPerSecond * 10; frame++)
                run.Advance(3, 1.0 / framesPerSecond);
            Require(Math.Abs(run.TravelledMetres - 30) < 1e-8, "frame-rate-independent running");
            run.Advance(0, 5);
            Require(Math.Abs(run.TravelledMetres - 30) < 1e-8, "pause keeps distance");
            run.Advance(6, 100);
            Require(run.TravelledMetres == 400 && run.IsFinished, "stop exactly at 400m");
            run.Advance(6, 10);
            Require(run.TravelledMetres == 400, "finished run cannot continue");
            var end = track.Evaluate(8, 125 + run.TravelledMetres);
            Require(Distance(end, track.Evaluate(8, 525)) < 1e-8, "400m finish on outer lane");
        }
        var invalidRun = new StadiumRunProgress();
        invalidRun.Advance(double.NaN, 1);
        invalidRun.Advance(3, double.PositiveInfinity);
        invalidRun.Advance(-3, 1);
        Require(invalidRun.TravelledMetres == 0, "invalid input does not poison distance");

        for (int lane = 1; lane <= 8; lane++)
        {
            double r = track.Radius(lane);
            var top = SimpleTrackMap.ToUv(0, r);
            Require(Math.Abs(top.U - 0.5) < 1e-10, "map top is centred");
            Require(Math.Abs(top.V - (0.5 + 36.8 * 8 / 768)) < 1e-10, "all lanes share one map line");
            var right = SimpleTrackMap.ToUv(84.39 / 2 + r, 0);
            Require(Math.Abs(right.U - (0.5 + (84.39 / 2 + 36.8) * 8 / 1536)) < 1e-10,
                "map outer curve uses single-lane radius");
            Require(Math.Abs(right.V - 0.5) < 1e-10, "map right curve height");
        }
        Console.WriteLine("PASS: 8 lanes, 16000 path samples, seams, heading, wrap, 80 global projections; running, pause and exact finish at 30/60/120 FPS.");
    }

    private static double Distance(StadiumTrackGeometry.Sample a, StadiumTrackGeometry.Sample b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
