using System;
using StadionTracking;
internal static class RunnerSpeedInputChecks
{
    static void Check(bool b,string label){if(!b)throw new Exception(label);}
    static void Near(double a,double b){Check(Math.Abs(a-b)<.000001,"unexpected speed");}
    public static void Main()
    {
        var input=new RunnerSpeedInput();
        Near(input.SpeedKmh(12.5,0),12.5);
        input.ReportTreadmill(9,0); Near(input.SpeedKmh(15,1),15);
        Check(input.SelectSource(RunnerSpeedSource.Treadmill),"source transition");
        Near(input.SpeedKmh(15,1),0); // Old samples cannot carry over when switching sources.
        input.ReportTreadmill(8.3,10); Near(input.SpeedKmh(15,11),8.3);
        Near(input.SpeedKmh(15,12.01),0);
        input.ReportTreadmill(0,13); Check(input.HasFreshTreadmill(13.1),"zero belt speed is still a live signal");
        Near(input.SpeedKmh(15,13.1),0);
        Check(!input.ReportTreadmill(double.NaN,14),"invalid signal rejected");
        Near(input.SpeedKmh(15,14),0);
        Check(!input.ReportTreadmill(-2,15),"negative signal rejected");
        input.ReportTreadmill(10,16); input.InvalidateTreadmill(); Near(input.SpeedKmh(15,16),0);
        input.SelectSource(RunnerSpeedSource.FixedTest); Near(input.SpeedKmh(0,17),0); Near(input.SpeedKmh(17.2,18),17.2);
        var ghost = new GhostTestMotion();
        ghost.SetSpeed(12,0);
        input.SelectSource(RunnerSpeedSource.Treadmill);
        Near(input.SpeedKmh(10,20),0);
        ghost.Advance(3,true);
        Near(ghost.Distance,10); // A running avatar must continue even without a belt signal.
        ghost.Advance(3,false);
        Near(ghost.Distance,10); // Only explicit pause freezes it.
        var profile = new LapGhostRecording();
        profile.EvaluateContinuous(200,400,100,out double distance,out double avatarSpeed);
        Near(distance,800); Near(avatarSpeed,4);
        profile.EvaluateContinuous(225,400,100,out distance,out avatarSpeed);
        Near(distance,900); Near(avatarSpeed,4); // Multiple laps while the user remains at zero.
        Console.WriteLine("PASS: independent fixed speed, source changes, belt speed, no signal, stale/invalid signal and zero speed.");
    }
}
