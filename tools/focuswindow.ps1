param([string]$Name = "FamilyTimeChef.Studio")

Add-Type -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
"@ -Name W -Namespace N -ErrorAction SilentlyContinue

$p = Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "no process $Name"; exit 1 }

$h = $p.MainWindowHandle
if ([N.W]::IsIconic($h)) { [N.W]::ShowWindowAsync($h, 9) | Out-Null }
[N.W]::ShowWindowAsync($h, 9) | Out-Null
[N.W]::BringWindowToTop($h) | Out-Null
[N.W]::SetForegroundWindow($h) | Out-Null
Write-Output ("focused " + $Name + " hwnd=" + $h)
