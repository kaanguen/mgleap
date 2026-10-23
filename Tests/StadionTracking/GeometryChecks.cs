using System;
using StadionTracking;

internal static class GeometryChecks
{
    private static void Require(bool condition, string label)
    {

        if (!condition) throw new Exception(label);
    }

    // Check track geometry, map projection, continuous laps and pause/resume.
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
            Require(run.CurrentSpeed == 0, "paused speed is zero");
            run.Advance(6, 100);
            Require(Math.Abs(run.TravelledMetres - 630) < 1e-8, "continues past 400m");
            Require(run.CompletedLaps == 1 && run.CurrentLap == 2, "second lap displayed");
            Require(run.CurrentSpeed == 6, "current speed follows applied movement");
            run.Advance(6, 10);
            Require(Math.Abs(run.TravelledMetres - 690) < 1e-8, "resume preserves distance");

            var boundaryRun = new StadiumRunProgress();
            Require(boundaryRun.CurrentLap == 1 && boundaryRun.CompletedLaps == 0, "start on first lap");
            for (int frame = 0; frame < framesPerSecond * 200; frame++)
                boundaryRun.Advance(6, 1.0 / framesPerSecond);
            Require(boundaryRun.CompletedLaps == 3 && boundaryRun.CurrentLap == 4, "exact multi-lap boundary");
            Require(boundaryRun.MetresInCurrentLap < 1e-8, "lap distance wraps at boundary");
        }
        for (int lane = 1; lane <= 8; lane++)
        {
            double length = track.Length(lane);
            var run = new StadiumRunProgress(length);
            run.Advance(1, length - 0.001);
            Require(run.CompletedLaps == 0, "no early lap completion");
            run.Advance(1, 0.002);
            Require(run.CompletedLaps == 1 && run.CurrentLap == 2, "lane-specific lap length");
            run.Advance(1, length * 4);
            Require(run.CompletedLaps == 5 && run.CurrentLap == 6, "multiple laps in one frame");
            var end = track.Evaluate(lane, 125 + run.TravelledMetres);
            Require(Distance(end, track.Evaluate(lane, 125.001)) < 1e-8, "position wraps after complete laps");
        }
        var invalidRun = new StadiumRunProgress();
        invalidRun.Advance(double.NaN, 1);
        invalidRun.Advance(3, double.PositiveInfinity);
        invalidRun.Advance(-3, 1);
        invalidRun.Advance(double.MaxValue, double.MaxValue);
        Require(invalidRun.TravelledMetres == 0, "invalid input does not poison distance");
        Require(invalidRun.CurrentSpeed == 0, "invalid movement reports zero current speed");

        Require(Math.Abs(StadiumPacing.SpeedForLapTime(400, 4) - 400.0 / 240) < 1e-12,
            "four-minute pace for 400 metres");
        Require(StadiumPacing.SpeedForLapTime(400, 0) == 0 &&
            StadiumPacing.SpeedForLapTime(400, double.NaN) == 0, "invalid target time is rejected");
        foreach (int fps in new[] { 30, 60, 120 })
        {
            foreach (int lane in new[] { 1, 8 })
            {
                double lapLength = track.Length(lane);
                double pace = StadiumPacing.SpeedForLapTime(lapLength, 4);
                var timedRun = new StadiumRunProgress(lapLength);
                for (int frame = 0; frame < fps * 240; frame++)
                    timedRun.Advance(pace, 1.0 / fps);
                Require(Math.Abs(timedRun.TravelledMetres - lapLength) < 1e-8,
                    "target time covers one actual lap at every frame rate");
                timedRun.Advance(0, 10);
                Require(Math.Abs(timedRun.TravelledMetres - lapLength) < 1e-8,
                    "target-time pause preserves progress");
                timedRun.Advance(pace, 240);
                Require(Math.Abs(timedRun.TravelledMetres - 2 * lapLength) < 1e-8,
                    "target pacing preserves continuous laps");
            }
        }

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
        Console.WriteLine("PASS: geometry, map, continuous laps and pause/resume; four-minute target pacing on inner/outer lanes at 30/60/120 FPS.");
    }

    private static double Distance(StadiumTrackGeometry.Sample a, StadiumTrackGeometry.Sample b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
