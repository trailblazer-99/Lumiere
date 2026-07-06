$files = Get-ChildItem -Path "ViewModels" -Filter "*.cs" -Recurse

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    
    $newContent = [System.Text.RegularExpressions.Regex]::Replace($content, '\[ObservableProperty\]\s+private\s+([^ ]+)\s+(_?)([a-z])([a-zA-Z0-9_]*)(\s*=\s*[^;]+)?\s*;', {
        param($match)
        $type = $match.Groups[1].Value
        $firstChar = $match.Groups[3].Value.ToUpper()
        $rest = $match.Groups[4].Value
        $init = $match.Groups[5].Value
        return "[ObservableProperty] public partial $type $($firstChar)$($rest) { get; set; }$init"
    })
    
    if ($content -cne $newContent) {
        Set-Content -Path $file.FullName -Value $newContent -NoNewline
        Write-Host "Updated $($file.Name)"
    }
}
