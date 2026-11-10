// Tests for the platform-independent parts of Multi Snip. Run with "dotnet run" in this folder;
// the exit code is the number of failures.
using MultiSnip.Tests;

ScrollStitcherTests.Run();
CoreTests.Run();
Console.WriteLine(T.Failures == 0 ? "All tests passed." : $"{T.Failures} test(s) failed.");
return T.Failures;
