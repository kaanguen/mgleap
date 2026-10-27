using System;
using StadionTracking;
internal static class FloorMarkerPlacementChecks
{
    static void Check(bool b,string message){if(!b)throw new Exception(message);}
    static void Near(double a,double b){Check(Math.Abs(a-b)<.00001,"wrong detected floor height");}
    public static void Main()
    {
        Check(FloorMarkerPlacement.CanUseScan(3, 10, 0), "valid scan must remain usable when the plane arrives later");
        Check(FloorMarkerPlacement.CanUseScan(30, 10, 10), "scan captured at request time should be usable");
        Check(!FloorMarkerPlacement.CanUseScan(30, 10, 11), "explicit rescan must reject an earlier scan");
        Check(!FloorMarkerPlacement.CanUseScan(1, 20, 0), "single unstable observation must not place a track");
        Check(FloorMarkerPlacement.TryProject(2,.42,3,0,.4,0,0,1,0,.15,out double height),"floor marker should be accepted");
        Near(height,.4);
        Check(FloorMarkerPlacement.TryProject(2,.22,3,0,0,0,-.1,1,0,.15,out height),"gentle floor tilt should be accepted");
        Near(height,.2);
        Check(!FloorMarkerPlacement.TryProject(2,.8,3,0,.4,0,0,1,0,.15,out height),"floating marker must not anchor to a distant floor");
        Check(!FloorMarkerPlacement.TryProject(0,0,0,0,0,0,1,0,0,.15,out height),"wall must not be treated as floor");
        Check(!FloorMarkerPlacement.TryProject(0,0,0,0,0,0,0,-1,0,.15,out height),"ceiling must not be treated as floor");
        Check(!FloorMarkerPlacement.TryProject(double.NaN,0,0,0,0,0,0,1,0,.15,out height),"invalid marker pose must be rejected");
        Check(FloorMarkerPlacement.TryIntersectFloor(0,1.6,0,0,.6,1,0,0,0,0,1,0,10,out double x,out double y,out double z),"floor ray should correct an underestimated marker depth");
        Near(x,0); Near(y,0); Near(z,1.6);
        Check(!FloorMarkerPlacement.TryIntersectFloor(0,1.6,0,0,1.6,1,0,0,0,0,1,0,10,out x,out y,out z),"horizontal ray must be rejected");
        Check(!FloorMarkerPlacement.TryIntersectFloor(0,1.6,0,0,2,1,0,0,0,0,1,0,10,out x,out y,out z),"upward ray must be rejected");
        Check(!FloorMarkerPlacement.TryIntersectFloor(0,1.6,0,0,1.5,10,0,0,0,0,1,0,10,out x,out y,out z),"distant intersection must be rejected");
        Console.WriteLine("PASS: detected floor projection, tilted floor, floating marker/wall/ceiling rejection and invalid-pose rejection.");
    }
}
