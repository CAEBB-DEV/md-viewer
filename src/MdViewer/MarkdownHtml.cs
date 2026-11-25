using System;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Platform;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace MdViewer;

/// <summary>
/// Visor web para documentos con HTML: los convierte con Markdig (dialecto de GitHub) a una página con estilo
/// GitHub. Los documentos sin HTML siguen en el visor nativo, que es más rápido.
/// </summary>
static partial class MarkdownHtml
{
    // Mismo dialecto que GitHub: tablas, listas de tareas, notas al pie, alertas [!NOTE], ~sub~ ^sup^ ==mark==,
    // autoenlaces y anclas de títulos con el formato de GitHub (#características).
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAdvancedExtensions()
        .UseAlertBlocks()
        .Build();

    // Solo el script propio de la página puede ejecutarse: los <script> y on...= dentro del .md quedan bloqueados.
    static readonly string Nonce = Guid.NewGuid().ToString("N");

    static readonly Lazy<string> Css = new(() =>
    {
        using var reader = new StreamReader(AssetLoader.Open(new Uri("avares://MdViewer/Assets/preview.css")));
        return reader.ReadToEnd();
    });

    // Tema y ancho se cambian sin recargar (InvokeScript); agrega el botón de copiar a cada bloque de código.
    const string Script = """
        window.setTheme = dark => document.documentElement.dataset.theme = dark ? 'dark' : 'light';
        window.setWidth = px => document.documentElement.style.setProperty('--max-width', px + 'px');
        const icon = '<svg viewBox="0 0 16 16"><path d="M5.5,1.5H13.5A1,1 0 0 1 14.5,2.5V10.5A1,1 0 0 1 13.5,11.5H5.5A1,1 0 0 1 4.5,10.5V2.5A1,1 0 0 1 5.5,1.5Z M4.5,4.5H2.5A1,1 0 0 0 1.5,5.5V13.5A1,1 0 0 0 2.5,14.5H10.5A1,1 0 0 0 11.5,13.5V11.5"/></svg>';
        for (const pre of document.querySelectorAll('pre')) {
          const button = document.createElement('button');
          button.className = 'copy'; button.title = 'Copiar'; button.innerHTML = icon;
          button.onclick = () => {
            const text = document.createElement('textarea');
            text.value = (pre.querySelector('code') ?? pre).innerText;
            document.body.append(text); text.select(); document.execCommand('copy'); text.remove();
          };
          pre.append(button);
        }
        """;

    // Etiqueta HTML (<p align="center">, </div>, <br/>) o comentario <!-- -->. Los autoenlaces <https://…> no cuentan.
    [GeneratedRegex(@"<!--|</?[A-Za-z][A-Za-z0-9-]*(?:\s[^<>]*)?/?>")]
    private static partial Regex HtmlTag();

    // Bloques de código (``` / ~~~) y código en línea: su contenido no es HTML real
    [GeneratedRegex(@"(?ms)^[ \t]*(`{3,}|~{3,}).*?^[ \t]*\1|`[^`\n]*`")]
    private static partial Regex Code();

    /// <summary>True si el documento tiene HTML fuera del código: se muestra con el visor web.</summary>
    public static bool ContainsHtml(string markdown) =>
        markdown.Contains('<') && HtmlTag().IsMatch(Code().Replace(markdown, ""));

    /// <summary>
    /// Escribe la página en un archivo temporal y devuelve su dirección. Se abre como archivo (no como texto) para
    /// que el navegador permita cargar las imágenes locales; &lt;base&gt; las resuelve desde la carpeta del documento.
    /// </summary>
    public static Uri WritePage(string markdown, string? documentFolder, bool dark, double maxWidth)
    {
        var page = Path.Combine(Path.GetTempPath(), "MdViewer", "vista.html");
        Directory.CreateDirectory(Path.GetDirectoryName(page)!);
        var baseHref = documentFolder is null
            ? ""
            : $"<base href=\"{new Uri(Path.TrimEndingDirectorySeparator(documentFolder) + Path.DirectorySeparatorChar).AbsoluteUri}\">";
        File.WriteAllText(page, ToHtml(markdown, baseHref, dark, maxWidth));
        return new Uri(page);
    }

    static string ToHtml(string markdown, string baseHref, bool dark, double maxWidth) => $$"""
        <!DOCTYPE html>
        <html data-theme="{{(dark ? "dark" : "light")}}" style="--max-width:{{maxWidth:0}}px">
        <head>
        <meta charset="utf-8">
        {{baseHref}}
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src * data: file:; media-src * file:; style-src 'unsafe-inline'; script-src 'nonce-{{Nonce}}'">
        <style>{{Css.Value}}</style>
        </head>
        <body>
        {{Markdig.Markdown.ToHtml(markdown, Pipeline)}}
        <script nonce="{{Nonce}}">{{Script}}</script>
        </body>
        </html>
        """;
}
