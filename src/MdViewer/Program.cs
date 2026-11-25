using System;
using Avalonia;

namespace MdViewer;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Si ya hay una ventana abierta, se le entrega el archivo y este proceso termina
        if (!SingleInstance.TryBecomePrimary(args)) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Lo usa también el previsualizador de XAML del IDE
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
