# Re-apply Starry Night after Spotify updates itself (Windows).
$sp = "$env:LOCALAPPDATA\spicetify\spicetify.exe"
if (-not (Test-Path $sp)) { $sp = "spicetify" }
& $sp backup apply
if ($LASTEXITCODE -ne 0) { & $sp restore backup apply }
