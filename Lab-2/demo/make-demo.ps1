# Создаёт тестовую исходную директорию для демонстрации:
#   source\ folder1\ folder2\ folder3\   — в каждой папке файлы docx, exe, txt.
# Часть файлов подходит под маски из BackupService.ini (*.docx, *.exe, file?.txt, important.txt),
# часть — нет (notesN.txt, file10.txt).
#
# Запуск:  powershell -ExecutionPolicy Bypass -File demo\make-demo.ps1 [-Root C:\BackupDemo\source]
param(
    [string]$Root = 'C:\BackupDemo\source'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$hello = Join-Path $PSScriptRoot '..\bin\hello.exe'
if (-not (Test-Path $hello)) { throw "Run build.bat first: $hello not found" }

# Минимальный корректный документ Word (docx — это ZIP с тремя XML-частями)
function New-Docx([string]$Path, [string]$Text) {
    $parts = [ordered]@{
        '[Content_Types].xml' = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>'
        '_rels/.rels'         = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'
        'word/document.xml'   = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>' + $Text + '</w:t></w:r></w:p></w:body></w:document>'
    }
    $fs = [IO.File]::Create($Path)
    $zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $parts.Keys) {
            $w = New-Object IO.StreamWriter($zip.CreateEntry($name).Open(), (New-Object Text.UTF8Encoding($false)))
            $w.Write($parts[$name])
            $w.Dispose()
        }
    } finally {
        $zip.Dispose()
        $fs.Dispose()
    }
}

$dirs = @(
    $Root,
    (Join-Path $Root 'folder1'),
    (Join-Path $Root 'folder1\folder2'),
    (Join-Path $Root 'folder1\folder2\folder3')
)

for ($i = 0; $i -lt $dirs.Count; $i++) {
    $d = $dirs[$i]
    New-Item -ItemType Directory -Force $d | Out-Null
    New-Docx (Join-Path $d "report$i.docx") "Report $i (level $i)"
    Copy-Item $hello (Join-Path $d "app$i.exe")
    Set-Content (Join-Path $d "file$i.txt")  "file$i.txt - matches mask file?.txt" -Encoding UTF8
    Set-Content (Join-Path $d "notes$i.txt") "notes$i.txt - does NOT match any mask" -Encoding UTF8
}
Set-Content (Join-Path $Root 'important.txt') 'important.txt - included by exact name' -Encoding UTF8
Set-Content (Join-Path $Root 'file10.txt')    'file10.txt - does NOT match file?.txt (? is exactly one char)' -Encoding UTF8

Write-Host "Created test tree in $Root"
Get-ChildItem $Root -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($Root.Length + 1) }
