using System;
using StadionTracking;
internal static class MeasuredRecordsChecks
{
    static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    static bool Near(double? value, double expected) => value.HasValue && Math.Abs(value.Value-expected)<.00001;
    public static void Main()
    {
        var t = new LapCheckpointTracker(400,250);
        Check(!t.RecordedLapSeconds.HasValue && !t.Recorded100mSeconds.HasValue && !t.Recorded1KmSeconds.HasValue,"Targets must not be reported as records");
        t.Update(0,0); t.Update(100,40); t.Update(400,160); t.Update(1000,400);
        Check(Near(t.Recorded100mSeconds,40) && Near(t.RecordedLapSeconds,160) && Near(t.Recorded1KmSeconds,400),"Slower-than-target full sections must still set measured records");
        Check(!t.BestLapIsExample && t.GhostRecording.HasRecording && Math.Abs(t.BestLapSeconds-160)<.00001,
            "First complete lap becomes ghost even when slower than target");
        t.GhostRecording.Evaluate(20,400,t.BestLapSeconds,out double ghostDistance,out double ghostSpeed);
        Check(Math.Abs(ghostDistance-50)<.00001 && Math.Abs(ghostSpeed-2.5)<.00001,"Ghost follows measured slower best lap");
        int samples=t.GhostRecording.BestSampleCount;
        t.SetTargetPace(180);
        Check(Near(t.RecordedLapSeconds,160) && Math.Abs(t.BestLapSeconds-160)<.00001 && t.GhostRecording.BestSampleCount==samples,
            "Changing target pace preserves measured best lap and ghost samples");
        t.Update(2000,600);
        Check(Near(t.Recorded100mSeconds,20) && Near(t.RecordedLapSeconds,80) && Near(t.Recorded1KmSeconds,200),"Faster measured sections replace the records");
        t.Update(3000,1100);
        Check(Near(t.Recorded100mSeconds,20) && Near(t.RecordedLapSeconds,80) && Near(t.Recorded1KmSeconds,200),"Slower sections must preserve records");
        var partial = new LapCheckpointTracker();
        partial.Update(350,0); partial.Update(400,5);
        Check(!partial.Recorded100mSeconds.HasValue && !partial.RecordedLapSeconds.HasValue,"Partial opening segments cannot set records");
        partial.Update(1000,65);
        Check(!partial.Recorded1KmSeconds.HasValue,"Partial opening kilometre cannot set a record");
        partial.Update(2000,165);
        Check(Near(partial.Recorded1KmSeconds,100),"Following full kilometre is eligible");
        var target = new LapCheckpointTracker(400,300);
        target.Update(0,0); target.Update(100,30);
        target.SetTargetPace(400);
        Check(target.BestLapIsExample && Math.Abs(target.BestLapSeconds-160)<.00001 && Math.Abs(target.LapElapsedSeconds-30)<.00001,
            "Fallback pace changes without resetting elapsed progress");
        target.Update(400,120);
        Check(target.GhostRecording.HasRecording && Near(target.RecordedLapSeconds,120), "Partial lap samples survive pace change");
        foreach(double invalid in new[]{0d,-1,double.NaN,double.PositiveInfinity}) {
            bool rejected=false;
            try { target.SetTargetPace(invalid); } catch(ArgumentOutOfRangeException) { rejected=true; }
            Check(rejected && Near(target.RecordedLapSeconds,120),"Invalid pace cannot erase recording");
        }
        Console.WriteLine("PASS: measured records, slow first effort, improvements, slower efforts and partial-section exclusion.");
    }
}
