param(
    [string]$StubFile
)

if (-not $StubFile -or -not (Test-Path $StubFile)) {
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

$bytes = [IO.File]::ReadAllBytes($StubFile)
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
Write-Host "Generated StubData.g.cs ($($bytes.Length) bytes) from $StubFile"