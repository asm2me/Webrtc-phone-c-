$base = "C:\Users\asm2m\Webrtc c#\bin\Debug\net481"
Get-ChildItem $base -Filter "*.dll" | ForEach-Object {
    try {
        $a = [System.Reflection.Assembly]::LoadFrom($_.FullName)
        $refs = $a.GetReferencedAssemblies() | Where-Object { $_.Name -match "Memory|Buffers|Unsafe" }
        if ($refs) {
            Write-Host "=== $($_.Name) ==="
            $refs | ForEach-Object { Write-Host "  $($_.FullName)" }
        }
    } catch { }
}
