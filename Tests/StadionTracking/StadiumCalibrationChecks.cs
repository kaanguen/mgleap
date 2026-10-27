using System;
using StadionTracking;
internal static class StadiumCalibrationChecks
{
    static void Near(double a,double b,string label) { if(Math.Abs(a-b)>.00001)throw new Exception(label+": "+a+" != "+b); }
    public static void Main()
    {
        var track=new StadiumTrackGeometry();
        var a=track.EvaluateAlignedLane(1,0);
        var b=track.EvaluateAlignedLane(1,20);
        double lx=b.X-a.X,lz=b.Z-a.Z;
        foreach(double yaw in new[]{-170d,-90,0,40,179})
        {
            double r=yaw*Math.PI/180;
            double wx=Math.Cos(r)*lx+Math.Sin(r)*lz,wz=-Math.Sin(r)*lx+Math.Cos(r)*lz;
            Near(StadiumCalibration.SolveYaw(lx,lz,wx,wz),yaw,"two-point rigid orientation");
        }
        bool failed=false;
        try{StadiumCalibration.SolveYaw(lx,lz,1,1);}catch(ArgumentException){failed=true;}
        if(!failed)throw new Exception("tiny baseline accepted");
        failed=false;
        try{StadiumCalibration.SolveYaw(lx,lz,100,100);}catch(ArgumentException){failed=true;}
        if(!failed)throw new Exception("wrong measured distance accepted");
        foreach(double reference in new[]{0d,80,150,250,390,400,810})
        {
            var inner=track.EvaluateAlignedLane(1,reference);
            for(int lane=1;lane<=8;lane++)
            {
                var sample=track.EvaluateAlignedLane(lane,reference);
                double dx=sample.X-inner.X,dz=sample.Z-inner.Z;
                Near(Math.Sqrt(dx*dx+dz*dz),track.Radius(lane)-track.Radius(1),"lane separation on straight/curve");
                Near(dx*inner.DirectionX+dz*inner.DirectionZ,0,"lanes abreast");
                Near(sample.DirectionX,inner.DirectionX,"same heading");
            }
        }
        if (track.LaneSpeedScale(8, 10) <= track.LaneSpeedScale(1, 10))
            throw new Exception("Outer bend speed must account for the longer path.");
        Near(track.LaneSpeedScale(8, 160), track.LaneSpeedScale(1, 160), "same speed along straights");
        Near(StadiumCalibration.WrappedDelta(399,1),2,"lap crossing");
        Near(StadiumCalibration.WrappedDelta(1,399),-2,"backward crossing");
        Near(track.EvaluateAlignedLane(8,0).X,track.EvaluateAlignedLane(8,400).X,"outer lane seam");
        Console.WriteLine("PASS: rigid two-point fit, baseline rejection, eight lanes on straights/curves, lane seam and lap direction.");
    }
}
