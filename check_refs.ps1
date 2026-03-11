$base = "C:\Users\asm2m\Webrtc c#\bin\Debug\net481"
$dlls = @("System.Text.Json", "SIPSorcery", "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Bcl.AsyncInterfaces")
foreach ($d in $dlls) {
    $path = "$base\$d.dll"
    if (Test-Path $path) {
        $a = [System.Reflection.Assembly]::LoadFrom($path)
        $refs = $a.GetReferencedAssemblies() | Where-Object { $_.Name -match "Memory|Buffers|Unsafe" }
        if ($refs) {
            Write-Host "=== $d ==="
            $refs | ForEach-Object { Write-Host "  $($_.FullName)" }
        }
    }
}
