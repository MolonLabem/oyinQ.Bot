$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$project = Get-Content -LiteralPath (Join-Path $repository 'oyinQ.Bot.csproj') -Raw
foreach ($resource in $project.Project.ItemGroup.EmbeddedResource) {
    if (-not $resource.Include) { continue }
    $relative = [string]$resource.Include
    if (-not (Test-Path -LiteralPath (Join-Path $repository $relative))) { throw "Нет обязательного ресурса: $relative" }
    & git -C $repository ls-files --error-unmatch -- $relative
    if ($LASTEXITCODE -ne 0) { throw "Обязательный ресурс отсутствует в Git-индексе: $relative" }
    & git -C $repository check-ignore --quiet --no-index -- $relative
    if ($LASTEXITCODE -eq 0) { throw "Обязательный ресурс исключён из Git: $relative" }
    if ($LASTEXITCODE -gt 1) { throw 'Ошибка проверки git check-ignore' }
}
Write-Output 'Обязательные встроенные ресурсы присутствуют в Git-индексе и не исключены.'
