Add-Type -AssemblyName System.Drawing
$source = 'C:\Users\soura\Downloads\Ignite.png'
$assets = 'C:\Users\soura\source\repos\FluentMediaPlayer\Assets'
$files = Get-ChildItem -Path $assets -Filter *.png
$srcImg = [System.Drawing.Image]::FromFile($source)

foreach ($file in $files) {
    $targetImg = [System.Drawing.Image]::FromFile($file.FullName)
    $w = $targetImg.Width
    $h = $targetImg.Height
    $targetImg.Dispose()

    $newBmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($newBmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $srcW = $srcImg.Width
    $srcH = $srcImg.Height
    
    $ratio = [Math]::Min($w / $srcW, $h / $srcH)
    # If ratio > 1, maybe we scale up, but if we don't want to scale up past original, we can use min(ratio, 1). For icons we usually scale up if needed.
    $drawW = $srcW * $ratio
    $drawH = $srcH * $ratio
    $drawX = ($w - $drawW) / 2.0
    $drawY = ($h - $drawH) / 2.0

    $g.DrawImage($srcImg, [float]$drawX, [float]$drawY, [float]$drawW, [float]$drawH)
    $g.Dispose()
    
    $newBmp.Save($file.FullName, [System.Drawing.Imaging.ImageFormat]::Png)
    $newBmp.Dispose()
    Write-Host "Resized and saved $($file.Name) as ${w}x${h}"
}
$srcImg.Dispose()
