Add-Type -AssemblyName System.Drawing

$sourcePath = 'C:\Users\soura\.gemini\antigravity\brain\c3b39e7a-1748-4bdf-a26b-47195ffbdfea\media__1783354277347.jpg'
$assetsFolder = 'c:\Users\soura\source\repos\FluentMediaPlayer\Assets'

if (-not (Test-Path $sourcePath)) {
    Write-Error "Source image not found at $sourcePath"
    exit 1
}

$srcImg = [System.Drawing.Image]::FromFile($sourcePath)

$files = Get-ChildItem -Path $assetsFolder -Filter *.png

foreach ($file in $files) {
    # Read the current size of the asset
    $targetImg = [System.Drawing.Image]::FromFile($file.FullName)
    $w = $targetImg.Width
    $h = $targetImg.Height
    $targetImg.Dispose()

    # Create new bitmap with target size
    $newBmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($newBmp)
    
    # Enable high quality rendering
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $srcW = $srcImg.Width
    $srcH = $srcImg.Height

    # For splash screen (non-square), we want to fit the logo in the center
    # For square icons, we want to cover or fit
    $ratio = [Math]::Min($w / $srcW, $h / $srcH)
    
    $drawW = $srcW * $ratio
    $drawH = $srcH * $ratio
    $drawX = ($w - $drawW) / 2.0
    $drawY = ($h - $drawH) / 2.0

    # Draw the image
    $g.DrawImage($srcImg, [float]$drawX, [float]$drawY, [float]$drawW, [float]$drawH)
    $g.Dispose()

    # Save over the old file
    $newBmp.Save($file.FullName, [System.Drawing.Imaging.ImageFormat]::Png)
    $newBmp.Dispose()

    Write-Host "Replaced $($file.Name) with resized ($w x $h) version of official logo."
}

$srcImg.Dispose()
Write-Host "All assets updated successfully!"
