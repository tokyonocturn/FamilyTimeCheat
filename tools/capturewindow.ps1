param([string]$Name = "FamilyTimeChef.Studio", [string]$Out = "artifacts/window.png")

Add-Type -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
"@ -Name W -Namespace N -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing

$p = Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "no process $Name"; exit 1 }

$h = $p.MainWindowHandle
if ($h -eq 0) { Write-Output "no main window yet"; exit 2 }

$r = New-Object N.W+RECT
[N.W]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
if ($w -le 0 -or $ht -le 0) { Write-Output "bad window rect"; exit 3 }

$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
# 2 == PW_RENDERFULLCONTENT, needed for composited windows
[N.W]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc)

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose()
$bmp.Dispose()
Write-Output ("captured " + $w + "x" + $ht + " -> " + $Out)
