# ============================================================
# delete-releases.ps1 — Delete oldest N GitHub releases + tags
# ============================================================
# Usage:
#   .\scripts\delete-releases.ps1 -Token "ghp_YOUR_PAT_HERE"
#   .\scripts\delete-releases.ps1 -Token "ghp_YOUR_PAT_HERE" -DryRun
#
# Required PAT scopes: repo (Full control of private repositories)
# Behavior: Deletes the OLDEST N releases and their remote Git tags.
# ============================================================

param(
    [Parameter(Mandatory = $true)]
    [string]$Token,

    [Parameter(Mandatory = $false)]
    [int]$MaxToDelete = 45,

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$Owner   = "trailblazer-99"
$Repo    = "Lumiere"
$Headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

Write-Host ""
Write-Host "🔍 Fetching all releases from $Owner/$Repo ..." -ForegroundColor Cyan

# --- Paginate and collect ALL releases ---
$page        = 1
$allReleases = @()

do {
    $url = "https://api.github.com/repos/$Owner/$Repo/releases?per_page=100&page=$page"
    try {
        $batch = Invoke-RestMethod -Uri $url -Headers $Headers -Method GET
    } catch {
        Write-Host "❌ Failed to fetch releases (page $page): $_" -ForegroundColor Red
        exit 1
    }
    $allReleases += $batch
    $page++
} while ($batch.Count -eq 100)

if ($allReleases.Count -eq 0) {
    Write-Host "✅ No releases found. Nothing to delete." -ForegroundColor Green
    exit 0
}

Write-Host "📋 Total releases found: $($allReleases.Count)" -ForegroundColor Yellow

# --- Sort OLDEST first, take the first N ---
$sorted   = $allReleases | Sort-Object { [datetime]$_.published_at } -Ascending
$toDelete = $sorted | Select-Object -First $MaxToDelete

Write-Host ""
Write-Host "🗓️  Oldest $($toDelete.Count) release(s) targeted for deletion:" -ForegroundColor Yellow
$toDelete | ForEach-Object {
    Write-Host ("   • [{0,-20}] {1,-40} Published: {2}" -f $_.tag_name, $_.name, $_.published_at) -ForegroundColor Gray
}

if ($DryRun) {
    Write-Host ""
    Write-Host "🧪 DRY RUN — no changes made. Re-run without -DryRun to apply." -ForegroundColor Magenta
    exit 0
}

# --- Confirm before proceeding ---
Write-Host ""
Write-Host "⚠️  This will permanently delete $($toDelete.Count) releases AND their remote tags." -ForegroundColor Red
$confirm = Read-Host "   Type YES to continue"
if ($confirm -ne "YES") {
    Write-Host "🚫 Aborted." -ForegroundColor Yellow
    exit 0
}

# --- Delete releases ---
Write-Host ""
Write-Host "🗑️  Deleting releases..." -ForegroundColor Cyan
$deletedReleases = 0
$failedReleases  = 0
$tagsToDelete    = @()

foreach ($release in $toDelete) {
    $deleteUrl = "https://api.github.com/repos/$Owner/$Repo/releases/$($release.id)"
    try {
        Invoke-RestMethod -Uri $deleteUrl -Headers $Headers -Method DELETE | Out-Null
        Write-Host "   ✅ Release deleted: [$($release.tag_name)] $($release.name)" -ForegroundColor Green
        $tagsToDelete += $release.tag_name
        $deletedReleases++
    } catch {
        Write-Host "   ❌ Release failed:  [$($release.tag_name)] — $_" -ForegroundColor Red
        $failedReleases++
    }
    Start-Sleep -Milliseconds 150
}

# --- Delete remote tags via GitHub API ---
Write-Host ""
Write-Host "🏷️  Deleting remote tags..." -ForegroundColor Cyan
$deletedTags = 0
$failedTags  = 0

foreach ($tag in $tagsToDelete) {
    $tagUrl = "https://api.github.com/repos/$Owner/$Repo/git/refs/tags/$tag"
    try {
        Invoke-RestMethod -Uri $tagUrl -Headers $Headers -Method DELETE | Out-Null
        Write-Host "   ✅ Tag deleted:    $tag" -ForegroundColor Green
        $deletedTags++
    } catch {
        Write-Host "   ❌ Tag failed:     $tag — $_" -ForegroundColor Red
        $failedTags++
    }
    Start-Sleep -Milliseconds 150
}

# --- Prune local tag refs to stay in sync ---
Write-Host ""
Write-Host "🔄 Pruning stale local tag refs..." -ForegroundColor Cyan
git fetch --prune --prune-tags origin 2>&1 | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }

# --- Summary ---
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host "📊 Summary" -ForegroundColor Cyan
Write-Host "   Releases — Deleted: $deletedReleases  |  Failed: $failedReleases" -ForegroundColor White
Write-Host "   Tags     — Deleted: $deletedTags      |  Failed: $failedTags" -ForegroundColor White
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
