using System;
public static class ExtraParserChecks
{
    static int count;
    static void Check(bool condition,string message) { if (!condition) throw new Exception(message); count++; }
    static void Near(double actual,double expected,string message) { Check(Math.Abs(actual-expected)<0.000001,message); }
    static void Reject(Action action) { try { action(); } catch (FormatException) { count++; return; } throw new Exception("Ungültiges Paket akzeptiert"); }
    public static void Main()
    {
        var t=CosinussParsers.Temperature(new byte[] {4,0x6A,8,0,0xFE,3});
        Near(t.Celsius,21.54,"Hersteller-Temperaturbeispiel"); Check(t.Type==3,"Temperaturtyp Ohr");
        Near(CosinussParsers.Temperature(new byte[] {0,0x83,0xFF,0xFF,0xFF}).Celsius,-12.5,"Vorzeichen Mantisse");
        Near(CosinussParsers.Temperature(new byte[] {1,0xDA,3,0,0xFF}).Celsius,37,"Fahrenheit");
        Near(CosinussParsers.Float11073(0x0100000C,out var special),120,"Positiver Exponent");
        Check(double.IsNaN(CosinussParsers.Float11073(0x007FFFFF,out special)) && special=="NaN","FLOAT NaN");
        Check(double.IsNaN(CosinussParsers.Float11073(0x00800000,out special)) && special=="NRes","FLOAT NRes");
        Check(double.IsPositiveInfinity(CosinussParsers.Float11073(0x007FFFFE,out special)),"FLOAT +Infinity");
        Check(double.IsNegativeInfinity(CosinussParsers.Float11073(0x00800002,out special)),"FLOAT -Infinity");
        var stamp=CosinussParsers.Temperature(new byte[] {6,0x6A,8,0,0xFE,0xEA,7,10,22,12,30,0,3});
        Check(stamp.Timestamp=="2026-10-22 12:30:00" && stamp.Type==3,"Optionale Temperaturfelder");
        Reject(() => CosinussParsers.Temperature(new byte[] {0,1,2}));
        Reject(() => CosinussParsers.Temperature(new byte[] {8,0,0,0,0}));
        var p=CosinussParsers.Plx(new byte[] {16,96,0,0xFF,7,0x23,0xE0},CosinussParsers.PlxEncoding.Standard11073);
        Near(p.Spo2,96,"Hersteller-SpO2-Beispiel"); Near(p.Perfusion,0.35,"Perfusion"); Check(double.IsNaN(p.Pulse),"Nicht verwendeter Puls ist NaN");
        var all=CosinussParsers.Plx(new byte[] {31,96,0,68,0,97,0,70,0,95,0,66,0,0,0x20,8,0,0,0x23,0xE0},CosinussParsers.PlxEncoding.Standard11073);
        Check(all.FastSpo2==97 && all.SlowPulse==66 && all.MeasurementStatus==0x2000 && all.DeviceStatus==8,"Alle PLX-Felder");
        Near(CosinussParsers.ShortFloat(0xF001),0.1,"SFLOAT negativer Exponent"); Near(CosinussParsers.ShortFloat(0x0FFF),-1,"SFLOAT negative Mantisse");
        Check(double.IsNaN(CosinussParsers.ShortFloat(0x800)),"SFLOAT NRes");
        Check(double.IsPositiveInfinity(CosinussParsers.ShortFloat(0x7FE)),"SFLOAT +Infinity");
        Check(double.IsNegativeInfinity(CosinussParsers.ShortFloat(0x802)),"SFLOAT -Infinity");
        var legacy=CosinussParsers.Plx(new byte[] {16,0,0x56,0,0x7E,0,0x38},CosinussParsers.PlxEncoding.LegacyIeeeHalf);
        Near(legacy.Spo2,96,"Legacy Half SpO2"); Near(legacy.Perfusion,0.5,"Legacy Half Perfusion");
        Near(CosinussParsers.HalfFloat(1),Math.Pow(2,-24),"Half subnormal"); Near(CosinussParsers.HalfFloat(0xC000),-2,"Half Vorzeichen");
        Check(double.IsNaN(CosinussParsers.HalfFloat(0x7E00)),"Half NaN");
        Reject(() => CosinussParsers.Plx(new byte[] {16,96,0,68,0,1},CosinussParsers.PlxEncoding.Standard11073));
        Reject(() => CosinussParsers.Plx(new byte[] {32,96,0,68,0},CosinussParsers.PlxEncoding.Standard11073));
        Check(CosinussParsers.Battery(new byte[] {96})==96,"Akku");
        Reject(() => CosinussParsers.Battery(new byte[] {255}));
        var quality=CosinussParsers.Sensor(new byte[] {0x27,0,0,0,0,0,0,0,49});
        Check(quality.Quality==49 && quality.IsMaximumQuality,"Signalqualität max");
        Check(CosinussParsers.Sensor(new byte[] {7,11}).ErrorCode==11,"Sensorcode");
        Reject(() => CosinussParsers.Sensor(new byte[] {0x27,0}));
        Console.WriteLine(count+" zusätzliche Parser-Prüfungen erfolgreich.");
    }
}
