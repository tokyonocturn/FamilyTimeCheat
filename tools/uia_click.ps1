param(
    [string]$Process = "FamilyTimeChef.Studio",
    [string]$Button = "Spawn food",
    [switch]$List,
    [switch]$Invoke,
    [switch]$Dump
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$p = Get-Process -Name $Process -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "no process $Process"; exit 1 }

$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
if (-not $root) { Write-Output "no automation root"; exit 1 }

if ($Dump) {
    # All three action buttons in one pass, so the caller never has to spawn a process
    # between taking focus and sending the click.
    foreach ($n in @("Spawn food", "Feed hungry children now", "Give water now")) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $n)
        $e = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($e) {
            $r = $e.Current.BoundingRectangle
            Write-Output ("{0}|{1}|{2}|{3}" -f $n, [int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2), ($e.Current.IsEnabled))
        }
    }
    exit 0
}

if ($List) {
    $all = $root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
        if ($e.Current.ControlType.ProgrammaticName -like "*Button*") {
            Write-Output ("button: " + $e.Current.Name)
        }
    }
    exit 0
}

$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $Button)

$el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if (-not $el) { Write-Output ("button not found: " + $Button); exit 2 }

if ($Invoke) {
    # The game owns a fullscreen window, so a physical click cannot reach this control.
    # Invoke raises the control's own activation path, which is what a click triggers.
    $pattern = $null
    if (-not $el.TryGetCurrentPattern(
            [System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        Write-Output ("button has no Invoke pattern: " + $Button)
        exit 3
    }
    $pattern.Invoke()
    Write-Output ("invoked: " + $Button)
    exit 0
}

$r = $el.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2)
$cy = [int]($r.Y + $r.Height / 2)
Write-Output ("{0} {1}" -f $cx, $cy)
