param([string]$Name = "FamilyTime", [int]$Vk = 0x20, [int]$Count = 1)

Add-Type -MemberDefinition @"
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int c);
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, System.UIntPtr dwExtraInfo);
"@ -Name K -Namespace N -ErrorAction SilentlyContinue

$p = Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "no process $Name"; exit 1 }

$h = $p.MainWindowHandle
[N.K]::ShowWindowAsync($h, 9) | Out-Null
[N.K]::BringWindowToTop($h) | Out-Null
[N.K]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 500

for ($i = 0; $i -lt $Count; $i++) {
    [N.K]::keybd_event([byte]$Vk, 0, 0, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [N.K]::keybd_event([byte]$Vk, 0, 2, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 700
}

Write-Output ("sent " + $Count + " key(s) vk=0x" + $Vk.ToString("X2") + " to " + $Name)
