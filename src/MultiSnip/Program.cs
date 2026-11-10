namespace MultiSnip;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--self-test")
        {
            ApplicationConfiguration.Initialize();
            return SelfTest.Run(args.Length > 1 ? args[1] : null);
        }

        using var mutex = new Mutex(true, @"Local\WebProgress.AI.MultiSnip", out bool firstInstance);
        if (!firstInstance)
        {
            // Already running: ask that instance to come to the front.
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
