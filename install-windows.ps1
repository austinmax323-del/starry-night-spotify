# Starry Night - Windows install. Run from the repo folder in PowerShell.
$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
if (-not (Get-Command spicetify -ErrorAction SilentlyContinue) -and -not (Test-Path "$env:LOCALAPPDATA\spicetify\spicetify.exe")) {
  iwr -useb https://raw.githubusercontent.com/spicetify/cli/main/install.ps1 | iex
}
$sp = "$env:LOCALAPPDATA\spicetify\spicetify.exe"; if (-not (Test-Path $sp)) { $sp = "spicetify" }
& $sp config-dir | Out-Null
$cfg = "$env:APPDATA\spicetify"
foreach ($d in "Themes","Extensions","CustomApps") { Copy-Item "$repo\$d\*" "$cfg\$d" -Recurse -Force }

# Free stand-ins for the macOS fonts: Libre Caslon Text (Big Caslon), Jost (Futura), Nunito Sans (Avenir Next)
$fd = "$env:LOCALAPPDATA\Microsoft\Windows\Fonts"; New-Item -ItemType Directory -Force $fd | Out-Null
$base = "https://raw.githubusercontent.com/google/fonts/main/ofl"
$fonts = @{
  "LibreCaslonText.ttf" = "$base/librecaslontext/LibreCaslonText%5Bwght%5D.ttf"
  "LibreCaslonText-Italic.ttf" = "$base/librecaslontext/LibreCaslonText-Italic%5Bwght%5D.ttf"
  "Jost.ttf" = "$base/jost/Jost%5Bwght%5D.ttf"
  "Jost-Italic.ttf" = "$base/jost/Jost-Italic%5Bwght%5D.ttf"
  "NunitoSans.ttf" = "$base/nunitosans/NunitoSans%5BYTLC,opsz,wdth,wght%5D.ttf"
  "NunitoSans-Italic.ttf" = "$base/nunitosans/NunitoSans-Italic%5BYTLC,opsz,wdth,wght%5D.ttf"
}
foreach ($f in $fonts.Keys) {
  Invoke-WebRequest $fonts[$f] -OutFile "$fd\$f" -UseBasicParsing
  New-ItemProperty "HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts" -Name "$($f -replace '\.ttf','') (TrueType)" -Value "$fd\$f" -Force | Out-Null
}

& $sp config current_theme StarryNight color_scheme Base inject_css 1 replace_colors 1 inject_theme_js 1 `
  extensions "hide-npv.js|starry-fx.js|starry-intro.js|theme-switcher.js|visualizer-button.js|starry-win.js" `
  custom_apps "marketplace|backmusic" expose_apis 1 home_config 1 experimental_features 1
& $sp backup apply
