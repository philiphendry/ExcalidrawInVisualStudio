# Paths
$csprojPath = "..\ExcalidrawInVisualStudio\ExcalidrawInVisualStudio.csproj"
$editorFolder = "..\ExcalidrawInVisualStudio\Editor"

# Markers in the csproj file
$startTag = "<!-- AUTO-GENERATED-CONTENT-START -->"
$endTag = "<!-- AUTO-GENERATED-CONTENT-END -->"

# Read the original .csproj content
$originalContent = Get-Content -Path $csprojPath -Raw

# Split content around the markers
$pattern = "$([regex]::Escape($startTag)).*?$([regex]::Escape($endTag))"
$regex = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
$hasMarkers = $regex.IsMatch($originalContent)

if (-not $hasMarkers) {
    Write-Error "Markers not found in the .csproj file. Add $startTag and $endTag around the dynamic content block."
    exit 1
}

# Find all files in the Editor directory
$files = Get-ChildItem -Path $editorFolder -Recurse -File

# Generate new <Content> entries
$contentLines = (
    $files | ForEach-Object {
        $relativePath = $_.FullName.Replace((Resolve-Path "..\ExcalidrawInVisualStudio").Path + "\", "")
        "  <Content Include=`"$relativePath`">`n    <IncludeInVSIX>true</IncludeInVSIX>`n  </Content>"
    }
) -join "`n"


# Build the new content block
$newBlock = "$startTag`n$contentLines`n  $endTag"

# Replace the old content block
$updatedContent = $regex.Replace($originalContent, $newBlock)

# Save it back
Set-Content -Path $csprojPath -Value $updatedContent -Encoding UTF8

Write-Host ".csproj updated with $($files.Count) content item(s)."
