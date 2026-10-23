using System;

namespace StadionTracking
{

    public sealed class StadiumRunProgress
    {
        public StadiumRunProgress(double lapLengthMetres = 400.0)
        {
            if (lapLengthMetres <= 0 || double.IsNaN(lapLengthMetres) || double.IsInfinity(lapLengthMetres))
                throw new ArgumentOutOfRangeException(nameof(lapLengthMetres));
            LapLengthMetres = lapLengthMetres;
        }

        public double LapLengthMetres { get; }
        public double TravelledMetres { get; private set; }
        public double CurrentSpeed { get; private set; }
        public long CompletedLaps => (long)Math.Floor(TravelledMetres / LapLengthMetres);
        public long CurrentLap => CompletedLaps + 1;
        public double MetresInCurrentLap => TravelledMetres % LapLengthMetres;
        private double _distanceCorrection;

        // Continue over any number of laps; zero speed pauses without resetting progress.
        public void Advance(double metresPerSecond, double deltaSeconds)
        {
            CurrentSpeed = 0;
            if (metresPerSecond <= 0 || deltaSeconds <= 0 ||
                double.IsNaN(metresPerSecond) || double.IsInfinity(metresPerSecond) ||
                double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
                return;

            double distance = metresPerSecond * deltaSeconds - _distanceCorrection;
            double total = TravelledMetres + distance;
            if (double.IsInfinity(total) || double.IsNaN(total))
                return;
            _distanceCorrection = (total - TravelledMetres) - distance;
            TravelledMetres = total;
            CurrentSpeed = metresPerSecond;
        }
    }
}
