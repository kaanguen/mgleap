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
        Console.WriteLine("PASS: measured records, slow first effort, improvements, slower efforts and partial-section exclusion.");
    }
}
