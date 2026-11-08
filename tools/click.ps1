param([string]$Name = "FamilyTime")

Add-Type -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, System.UIntPtr e);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
"@ -Name M -Namespace N -ErrorAction SilentlyContinue

$p = Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "no process $Name"; exit 1 }

$h = $p.MainWindowHandle
$r = New-Object N.M+RECT
[N.M]::GetWindowRect($h, [ref]$r) | Out-Null

$cx = [int](($r.Left + $r.Right) / 2)
$cy = [int](($r.Top + $r.Bottom) / 2)

[N.M]::BringWindowToTop($h) | Out-Null
[N.M]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500
[N.M]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 250
[N.M]::mouse_event(0x0002, 0, 0, 0, [System.UIntPtr]::Zero)
Start-Sleep -Milliseconds 90
[N.M]::mouse_event(0x0004, 0, 0, 0, [System.UIntPtr]::Zero)

Write-Output ("clicked " + $cx + "," + $cy + " on " + $Name)
