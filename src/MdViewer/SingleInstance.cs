using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;

namespace MdViewer;

/// <summary>
/// Una sola instancia por usuario. La primera queda escuchando en un named pipe; las siguientes le envían
/// la ruta del archivo (o una línea vacía para solo traerla al frente) y terminan.
/// </summary>
static class SingleInstance
{
    static readonly string Name = "MdViewer-" + Environment.UserName;
    static Mutex? _mutex; // se mantiene vivo mientras la app esté abierta

    /// <summary>
    /// True si esta es la primera instancia (debe arrancar la app). False si el archivo se entregó a la
    /// instancia ya abierta y este proceso debe terminar.
    /// </summary>
    public static bool TryBecomePrimary(string[] args)
    {
        _mutex = new Mutex(initiallyOwned: true, Name, out var isFirst);
        if (isFirst) return true;

        var path = args.Length > 0 ? Path.GetFullPath(args[0]) : ""; // relativa al directorio de quien la lanzó

        // Reintentos rápidos (~600 ms máximo en lugar de 7 segundos) si la primera instancia está terminando de iniciar
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(100);
                if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(-1); // deja que la otra ventana pase al frente
                using var writer = new StreamWriter(client);
                writer.WriteLine(path);
                return false;
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                Thread.Sleep(50);
            }
        }

        // Si la instancia previa no responde (ej. proceso colgado o zombie), arrancamos esta instancia
        return true;
    }

    /// <summary>Escucha a las instancias siguientes; <paramref name="onOpen"/> recibe la ruta (o null) en un hilo de fondo.</summary>
    public static void Listen(Action<string?> onOpen) => new Thread(() =>
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.CurrentUserOnly);
                server.WaitForConnection();
                using var reader = new StreamReader(server);
                var line = reader.ReadLine();
                try
                {
                    onOpen(string.IsNullOrWhiteSpace(line) ? null : line);
                }
                catch { }
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (Exception)
            {
                Thread.Sleep(250);
            }
        }
    }) { IsBackground = true, Name = "MdViewer.SingleInstance" }.Start();

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
