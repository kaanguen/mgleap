using System;

namespace StadionTracking
{

    public sealed class StadiumTrackGeometry
    {

        public const double StraightLength = 84.39;

        public const double InnerRadius = 36.50;
        public const double LaneWidth = 1.22;
        public const int LaneCount = 8;

        public readonly struct Sample
        {
            public readonly double X, Z, DirectionX, DirectionZ;
            public Sample(double x, double z, double directionX, double directionZ)
            {
                X = x;
                Z = z;
                DirectionX = directionX;
                DirectionZ = directionZ;
            }
        }

        // Return the measurement-line radius for the selected lane.
        public double Radius(int lane)
        {
            if (lane < 1 || lane > LaneCount)
                throw new ArgumentOutOfRangeException(nameof(lane));

            return InnerRadius + (lane == 1 ? 0.30 : (lane - 1) * LaneWidth + 0.20);
        }

        // Two straights plus two semicircles; outer lanes are longer.
        public double Length(int lane) => 2 * StraightLength + 2 * Math.PI * Radius(lane);

        // Find the lane from local X/Z coordinates; zero means off track.
        public int DetectLane(double x, double z)
        {

            double nearestX = Math.Max(-StraightLength / 2, Math.Min(StraightLength / 2, x));
            double dx = x - nearestX;

            double outward = Math.Sqrt(dx * dx + z * z) - InnerRadius;

            return outward < 0 || outward >= LaneCount * LaneWidth
                ? 0 : (int)Math.Floor(outward / LaneWidth) + 1;
        }

        // Convert distance into a local position and unit tangent, wrapping at the lap end.
        public Sample Evaluate(int lane, double metres)
        {

            double r = Radius(lane), h = StraightLength / 2, bend = Math.PI * r;
            double length = Length(lane);

            double s = ((metres % length) + length) % length;
            if (s < bend)
            {

                double angle = -Math.PI / 2 + s / r;

                return new Sample(h + r * Math.Cos(angle), r * Math.Sin(angle),
                    -Math.Sin(angle), Math.Cos(angle));
            }

            if (s < bend + StraightLength)
                return new Sample(h - (s - bend), r, -1, 0);
            if (s < 2 * bend + StraightLength)
            {

                double angle = Math.PI / 2 + (s - bend - StraightLength) / r;
                return new Sample(-h + r * Math.Cos(angle), r * Math.Sin(angle),
                    -Math.Sin(angle), Math.Cos(angle));
            }

            return new Sample(-h + (s - 2 * bend - StraightLength), -r, 1, 0);
        }

        // Return the distance along the nearest point on the lane path.
        public double Project(int lane, double x, double z)
        {
            double r = Radius(lane), h = StraightLength / 2, bend = Math.PI * r;

            double clampedX = Math.Max(-h, Math.Min(h, x));
            double best = bend + h - clampedX;

            double bestDistance = double.PositiveInfinity;
            Consider(best);
            Consider(2 * bend + StraightLength + clampedX + h);

            double rightAngle = Math.Atan2(z, x - h);
            if (rightAngle >= -Math.PI / 2 && rightAngle <= Math.PI / 2)
                Consider((rightAngle + Math.PI / 2) * r);

            double leftAngle = Math.Atan2(z, x + h);
            if (leftAngle < 0)
                leftAngle += 2 * Math.PI;
            if (leftAngle >= Math.PI / 2 && leftAngle <= 3 * Math.PI / 2)
                Consider(bend + StraightLength + (leftAngle - Math.PI / 2) * r);
            return best;

            void Consider(double metres)
            {
                Sample sample = Evaluate(lane, metres);
                double dx = sample.X - x, dz = sample.Z - z;
                double distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = metres;
                }
            }
        }
    }
}
