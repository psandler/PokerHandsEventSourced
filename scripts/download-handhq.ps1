<#
.SYNOPSIS
Downloads HandHQ hand-history files (.phhs) from the phh-dataset on GitHub.

.DESCRIPTION
With no arguments, downloads a sample: the first few files from every
site/stakes folder, so every site and stake level is covered. Use -All for
everything (~16 GB). Filters use short names (-Site PS, -Stakes 50NL); run
-List to see them.

Files keep the dataset's layout under the destination:
  <Destination>\handhq\<site-stakes folder>\<bb>\<file>.phhs

Files already downloaded with the right size are skipped, so re-running
resumes an interrupted download or tops up a sample to -All.
Needs PowerShell 7+ (parallel downloads).

.PARAMETER All
Download every file in the selected folders instead of a sample.

.PARAMETER SampleSize
Files per folder when sampling (default 3). Ignored with -All.

.PARAMETER Site
Only these sites: PS, FTP, PTY, IPN, ONG, ABS. Default: all.

.PARAMETER Stakes
Only these stakes, e.g. 25NL, 50NL, 100NL, 200NL, 400NL, 600NL, 1000NL. Default: all.

.PARAMETER Parallel
How many files to download at once (default 8).

.PARAMETER Destination
Where the data goes. Defaults to the git-ignored _phh-dataset-local folder in the repo root.

.PARAMETER List
Show the folders (site, stakes, files, size, how many are already downloaded) and exit.

.EXAMPLE
./scripts/download-handhq.ps1                          # sample: 3 files from every folder
./scripts/download-handhq.ps1 -List
./scripts/download-handhq.ps1 -Site PS -Stakes 50NL -All
./scripts/download-handhq.ps1 -All                     # everything, ~16 GB
#>
param(
    [switch] $All,
    [int] $SampleSize = 3,
    [string[]] $Site,
    [string[]] $Stakes,
    [int] $Parallel = 8,
    [string] $Destination = (Join-Path $PSScriptRoot '..' '_phh-dataset-local'),
    [switch] $List
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Needs PowerShell 7+ (pwsh).' }

$repo = 'uoftcprg/phh-dataset'
$branch = 'main'
$headers = @{ 'User-Agent' = 'PokerEventSourced download-handhq.ps1' }
$Destination = [System.IO.Path]::GetFullPath($Destination)

function Get-Tree([string] $sha, [switch] $Recursive) {
    $url = "https://api.github.com/repos/$repo/git/trees/$sha"
    if ($Recursive) { $url += '?recursive=1' }
    Invoke-RestMethod -Uri $url -Headers $headers
}

# main -> data -> handhq (recursive): three API calls in total.
$root = Get-Tree $branch
$data = Get-Tree ($root.tree | Where-Object path -eq 'data').sha
$handhq = Get-Tree ($data.tree | Where-Object path -eq 'handhq').sha -Recursive
if ($handhq.truncated) { throw 'GitHub truncated the file listing.' }

# Folder names look like PS-2009-07-01_2009-07-23_50NLH_OBFU.
$files = $handhq.tree |
    Where-Object { $_.type -eq 'blob' -and $_.path -like '*.phhs' } |
    ForEach-Object {
        $folder = $_.path.Split('/')[0]
        [pscustomobject]@{
            Path   = $_.path
            Size   = [long] $_.size
            Folder = $folder
            Site   = $folder.Split('-')[0]
            Stakes = [regex]::Match($folder, '_(\d+)NLH_').Groups[1].Value + 'NL'
            Number = [int] [regex]::Match($_.path, 'handhq_(\d+)-').Groups[1].Value
            Target = Join-Path $Destination 'handhq' $_.path
        }
    }

function Test-Downloaded($file) {
    (Test-Path -LiteralPath $file.Target) -and (Get-Item -LiteralPath $file.Target).Length -eq $file.Size
}

if ($Site) { $files = $files | Where-Object { $Site -contains $_.Site } }
if ($Stakes) { $files = $files | Where-Object { $Stakes -contains $_.Stakes } }
if (-not $files) { throw 'No files match. Run with -List to see the sites and stakes.' }

$groups = $files | Group-Object Folder | Sort-Object { $_.Group[0].Site }, { [int]($_.Group[0].Stakes -replace 'NL') }

if ($List) {
    $groups | ForEach-Object {
        [pscustomobject]@{
            Site       = $_.Group[0].Site
            Stakes     = $_.Group[0].Stakes
            Files      = $_.Count
            MB         = [int](($_.Group | Measure-Object Size -Sum).Sum / 1MB)
            Downloaded = @($_.Group | Where-Object { Test-Downloaded $_ }).Count
        }
    } | Format-Table -AutoSize
    return
}

$selected = $groups | ForEach-Object {
    $sorted = $_.Group | Sort-Object Number
    if ($All) { $sorted } else { $sorted | Select-Object -First $SampleSize }
}
$todo = @($selected | Where-Object { -not (Test-Downloaded $_) })
$skipped = @($selected).Count - $todo.Count
$mb = [math]::Round(($todo | Measure-Object Size -Sum).Sum / 1MB)
Write-Host "$(@($selected).Count) files selected: $skipped already here, $($todo.Count) to download (~$mb MB)."

$failed = $todo | ForEach-Object -ThrottleLimit $Parallel -Parallel {
    $file = $_
    $encoded = ($file.Path.Split('/') | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/'
    $url = "https://raw.githubusercontent.com/$using:repo/$using:branch/data/handhq/$encoded"
    $partial = "$($file.Target).partial"
    New-Item -ItemType Directory -Force (Split-Path $file.Target) | Out-Null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest -Uri $url -Headers $using:headers -OutFile $partial
            if ((Get-Item -LiteralPath $partial).Length -ne $file.Size) { throw 'size mismatch' }
            Move-Item -Force -LiteralPath $partial -Destination $file.Target
            Write-Host "Downloaded $($file.Path)"
            return
        }
        catch {
            if ($attempt -eq 3) {
                Remove-Item -LiteralPath $partial -ErrorAction SilentlyContinue
                Write-Warning "Failed $($file.Path): $_"
                $file.Path  # emitted = failed
            }
            else { Start-Sleep -Seconds (2 * $attempt) }
        }
    }
}

$failedCount = @($failed).Count
Write-Host "Done: $($todo.Count - $failedCount) downloaded, $skipped already here, $failedCount failed. Data folder: $Destination"
if ($failedCount) { Write-Host 'Re-run the same command to retry the failed files.'; exit 1 }
