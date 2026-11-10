namespace MultiSnip.Tests;

/// <summary>Minimal self-checking test helper (no test framework needed).</summary>
internal static class T
{
    public static int Failures;

    public static void Check(string name, bool ok, string extra = "")
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name} {extra}");
        if (!ok) Failures++;
    }
}
