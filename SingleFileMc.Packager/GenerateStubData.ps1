# Prefer the NativeAOT publish output (bin\Release\...\win-x64\publish\SingleFileMc.exe, a standalone
# native exe). Recursive enumeration order is not guaranteed, so never take the first match blindly:
# the Debug JIT build is a framework-dependent ~160KB exe that cannot run standalone when packaged.
$candidates = Get-ChildItem -Path "$PSScriptRoot\..\SingleFileMc\bin" -Recurse -Filter SingleFileMc.exe -ErrorAction SilentlyContinue
$stub = $candidates | Where-Object { $_.FullName -match '\\publish\\' } |
    Sort-Object { $_.FullName -match '\\Release\\' } -Descending |
    Select-Object -First 1
if (-not $stub) {
    $stub = $candidates | Where-Object { $_.FullName -match '\\Release\\' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}
if (-not $stub) {
    $stub = $candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

if (-not $stub) {
    Write-Host "Stub file not found, generating empty stub."
    $code = @'
namespace SingleFileMc.Packager;
internal static class StubData
{
    internal static readonly byte[]? Binary = null;
}
'@
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'StubData.g.cs'), $code)
    exit 0
}

$bytes = [IO.File]::ReadAllBytes($stub.FullName)
$b64 = [Convert]::ToBase64String($bytes)
$code = @"
namespace SingleFileMc.Packager;
internal static class StubData
{
    internal static readonly byte[] Binary = System.Convert.FromBase64String(@"$b64");
}
"@
$outPath = Join-Path $PSScriptRoot 'StubData.g.cs'
[IO.File]::WriteAllText($outPath, $code)
Write-Host "Generated StubData.g.cs ($($bytes.Length) bytes) from $($stub.FullName)"
