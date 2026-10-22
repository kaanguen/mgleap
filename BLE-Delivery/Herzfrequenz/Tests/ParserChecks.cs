using System;
public static class ParserChecks
{
    static int count;
    static void Expect(int expected, params byte[] packet)
    {
        if (HeartRateParser.Parse(packet) != expected) throw new Exception("Falscher bpm-Wert");
        count++;
    }
    static void Reject(params byte[] packet)
    {
        try { HeartRateParser.Parse(packet); }
        catch (FormatException) { count++; return; }
        throw new Exception("Ungültiges Paket akzeptiert");
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        count++;
    }
    public static void Main()
    {
        Expect(72, 0, 72);
        Expect(300, 1, 44, 1);
        Expect(72, 6, 72);
        Expect(72, 24, 72, 1, 0, 0, 4);
        Reject();
        Reject(1, 72);
        Reject(8, 72, 1);
        Reject(16, 72);
        Reject(16, 72, 0);
        Reject(0, 72, 0);
        Reject(32, 72);
        Reject(4, 72);
        // Manufacturer's documented example: 68 bpm plus two RR intervals.
        var example = HeartRateParser.ParseMeasurement(new byte[] {0x10, 0x44, 0x33, 0x03, 0x29, 0x03});
        Check(example.Bpm == 68, "cosinuss bpm");
        Check(example.RrMilliseconds.Length == 2, "RR count");
        Check(Math.Abs(example.RrMilliseconds[0] - 799.8046875) < 0.000001, "RR 1 conversion");
        Check(Math.Abs(example.RrMilliseconds[1] - 790.0390625) < 0.000001, "RR 2 conversion");
        Check(example.ContactDetected == null, "Unsupported contact is unknown");
        var optional = HeartRateParser.ParseMeasurement(new byte[] {0x19, 0x2C, 0x01, 0x34, 0x12, 0x00, 0x04});
        Check(optional.Bpm == 300, "16-bit bpm with optional fields");
        Check(optional.EnergyKiloJoules == 0x1234, "Energy little endian");
        Check(optional.RrMilliseconds.Length == 1 && optional.RrMilliseconds[0] == 1000, "RR after energy");
        var simple = HeartRateParser.ParseMeasurement(new byte[] {0x06, 72});
        Check(simple.ContactDetected == true && simple.RrMilliseconds.Length == 0, "Contact and absent RR");
        Console.WriteLine(count + " Parser-Prüfungen erfolgreich.");
    }
}
