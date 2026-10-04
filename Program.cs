using System;
using Velopack;

namespace AuraLauncher;

public static class Program
{
    public static string[] StartupArgs { get; private set; } = Array.Empty<string>();

    [STAThread]
    public static void Main(string[] args)
    {
        StartupArgs = args;

        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
