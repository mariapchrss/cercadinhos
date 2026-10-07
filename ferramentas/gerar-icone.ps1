# Desenha o ícone do Cercadinhos (quadrado arredondado rosa → roxo com quatro caixinhas brancas)
# e salva em assets\Cercadinhos.ico com vários tamanhos.
Add-Type -AssemblyName System.Drawing
$pasta = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
New-Item -ItemType Directory -Force $pasta | Out-Null

function Arredondado([System.Drawing.RectangleF]$r, [single]$raio) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $raio * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Desenhar([int]$n) {
    $bmp = New-Object System.Drawing.Bitmap $n, $n
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $m = [single]($n * 0.04)
    $fundo = New-Object System.Drawing.RectangleF $m, $m, ($n - 2 * $m), ($n - 2 * $m)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $fundo, ([System.Drawing.Color]::FromArgb(255, 122, 184)), ([System.Drawing.Color]::FromArgb(134, 88, 255)), 45
    $caminho = Arredondado $fundo ([single]($n * 0.23))
    $g.FillPath($grad, $caminho)

    # quatro caixinhas: a de cima à esquerda mais larga, como uma área de trabalho arrumada
    $branco = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 255, 255, 255))
    $leve = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(170, 255, 255, 255))
    $a = [single]($n * 0.20); $b = [single]($n * 0.80); $v = [single]($n * 0.06)
    $meioX = [single]($n * 0.56); $meioY = [single]($n * 0.47)
    $r = [single]($n * 0.07)
    $caixas = @(
        @($a, $a, ($meioX - $a - $v / 2), ($meioY - $a - $v / 2), $branco),
        @(($meioX + $v / 2), $a, ($b - $meioX - $v / 2), ($meioY - $a - $v / 2), $leve),
        @($a, ($meioY + $v / 2), ($b - $meioX - $v / 2), ($b - $meioY - $v / 2), $leve),
        @(($a + ($b - $meioX - $v / 2) + $v), ($meioY + $v / 2), ($meioX - $a - $v / 2), ($b - $meioY - $v / 2), $branco)
    )
    foreach ($c in $caixas) {
        $rect = New-Object System.Drawing.RectangleF ([single]$c[0]), ([single]$c[1]), ([single]$c[2]), ([single]$c[3])
        $g.FillPath($c[4], (Arredondado $rect $r))
    }
    $g.Dispose()
    return $bmp
}

$tamanhos = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($t in $tamanhos) {
    $ms = New-Object System.IO.MemoryStream
    (Desenhar $t).Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}

$ico = Join-Path $pasta 'Cercadinhos.ico'
$fs = [System.IO.File]::Create($ico)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$tamanhos.Count)
$pos = 6 + 16 * $tamanhos.Count
for ($i = 0; $i -lt $tamanhos.Count; $i++) {
    $t = $tamanhos[$i]
    $w.Write([byte]($t % 256)); $w.Write([byte]($t % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$pos)
    $pos += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
(Desenhar 256).Save((Join-Path $pasta 'Cercadinhos.png'), [System.Drawing.Imaging.ImageFormat]::Png)
"Ícone salvo em $ico"
