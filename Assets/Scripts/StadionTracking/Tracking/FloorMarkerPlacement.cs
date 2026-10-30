using System;
namespace StadionTracking
{
    /// <summary>Validate and project a floor marker without using headset/body height.</summary>
    public static class FloorMarkerPlacement
    {
        // A scan remains usable while the asynchronous plane search completes.
        // Only an explicit new scan request invalidates the previous marker observations.
        public static bool CanUseScan(int samples, double lastSeenTime, double scanRequestedAt) =>
            samples >= 3 && !double.IsNaN(lastSeenTime) && !double.IsInfinity(lastSeenTime) && lastSeenTime >= scanRequestedAt;

        public static bool TryIntersectFloor(double cameraX, double cameraY, double cameraZ,
            double markerX, double markerY, double markerZ,
            double planeX, double planeY, double planeZ,
            double normalX, double normalY, double normalZ, double maximumRange,
            out double hitX, out double hitY, out double hitZ)
        {
            hitX = hitY = hitZ = 0;
            foreach (var v in new[] {cameraX,cameraY,cameraZ,markerX,markerY,markerZ,planeX,planeY,planeZ,normalX,normalY,normalZ,maximumRange})
                if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            double n = Math.Sqrt(normalX*normalX + normalY*normalY + normalZ*normalZ);
            if (n < .0001 || maximumRange <= 0) return false;
            normalX /= n; normalY /= n; normalZ /= n;
            if (normalY < .9659) return false;
            double dx=markerX-cameraX, dy=markerY-cameraY, dz=markerZ-cameraZ;
            double rayLength=Math.Sqrt(dx*dx+dy*dy+dz*dz);
            if (rayLength < .0001) return false;
            double denominator=normalX*dx+normalY*dy+normalZ*dz;
            double above=normalX*(cameraX-planeX)+normalY*(cameraY-planeY)+normalZ*(cameraZ-planeZ);
            if (above < .2 || denominator >= -.01*rayLength) return false;
            double factor=-above/denominator;
            if (factor <= 0 || factor*rayLength > maximumRange) return false;
            hitX=cameraX+factor*dx; hitY=cameraY+factor*dy; hitZ=cameraZ+factor*dz;
            return true;
        }

        public static bool TryProject(double x, double y, double z,
            double planeX, double planeY, double planeZ,
            double normalX, double normalY, double normalZ,
            double maximumDistance, out double surfaceY)
        {
            surfaceY = 0;
            foreach (var value in new[] { x,y,z,planeX,planeY,planeZ,normalX,normalY,normalZ,maximumDistance })
                if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            double length = Math.Sqrt(normalX*normalX + normalY*normalY + normalZ*normalZ);
            if (length < .0001 || maximumDistance <= 0) return false;
            normalX /= length; normalY /= length; normalZ /= length;
            if (normalY < .9659) return false;
            double distance = normalX*(x-planeX) + normalY*(y-planeY) + normalZ*(z-planeZ);
            if (Math.Abs(distance) > maximumDistance) return false;
            surfaceY = planeY - (normalX*(x-planeX) + normalZ*(z-planeZ))/normalY;
            return true;
        }
    }
}
