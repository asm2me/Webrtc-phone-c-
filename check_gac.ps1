Get-ChildItem 'C:\Windows\Microsoft.NET\assembly' -Recurse -Filter 'System.Memory.dll' -ErrorAction SilentlyContinue | ForEach-Object {
    $name = [System.Reflection.AssemblyName]::GetAssemblyName($_.FullName)
    Write-Host "$($_.FullName) => $($name.FullName)"
}
