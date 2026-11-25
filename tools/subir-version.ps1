# Sube el segundo número de <Version> del .csproj (1.3 -> 1.4). Lo ejecutan las tareas "Instalador: ..." antes de publicar.
$ErrorActionPreference = 'Stop'
$csproj = Join-Path $PSScriptRoot '..\src\MdViewer\MdViewer.csproj'
$xml = [IO.File]::ReadAllText($csproj)
$match = [regex]::Match($xml, '<Version>(\d+)\.(\d+)</Version>')
if (-not $match.Success) { throw "No se encontró <Version>x.y</Version> en $csproj" }
$version = '{0}.{1}' -f $match.Groups[1].Value, ([int]$match.Groups[2].Value + 1)
[IO.File]::WriteAllText($csproj, $xml.Remove($match.Index, $match.Length).Insert($match.Index, "<Version>$version</Version>"))
"Versión: $version"