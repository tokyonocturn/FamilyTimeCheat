Add-Type -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
"@ -Name W -Namespace N -ErrorAction SilentlyContinue

$p = Get-Process FamilyTime -ErrorAction SilentlyContinue
if (-not $p) { Write-Output "no process"; exit 1 }

$h = $p.MainWindowHandle
if ([N.W]::IsIconic($h)) { [N.W]::ShowWindowAsync($h, 9) | Out-Null }
[N.W]::ShowWindowAsync($h, 9) | Out-Null
[N.W]::SetForegroundWindow($h) | Out-Null
Write-Output ("focused hwnd=" + $h)
