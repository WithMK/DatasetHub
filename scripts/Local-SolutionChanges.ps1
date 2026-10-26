param([ValidateSet('Hide', 'Show', 'Status')][string]$Mode = 'Hide')
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repository
try {
    & git rev-parse --is-inside-work-tree | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Git 저장소가 아닙니다.' }
    & git ls-files --error-unmatch -- DatasetHub.sln | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'DatasetHub.sln이 Git 추적 대상이 아닙니다.' }
    switch ($Mode) {
        Hide {
            & git update-index --skip-worktree -- DatasetHub.sln
            if ($LASTEXITCODE -ne 0) { throw '로컬 변경 숨기기에 실패했습니다.' }
            Write-Host '이 PC에서 DatasetHub.sln의 로컬 수정 표시를 숨겼습니다.'
        }
        Show {
            & git update-index --no-skip-worktree -- DatasetHub.sln
            if ($LASTEXITCODE -ne 0) { throw '로컬 변경 표시 복구에 실패했습니다.' }
            Write-Host 'DatasetHub.sln의 로컬 수정 표시를 다시 켰습니다.'
        }
        Status {
            & git ls-files -v -- DatasetHub.sln
            Write-Host '첫 글자가 S이면 로컬 수정 표시가 숨겨진 상태입니다.'
        }
    }
}
finally { Pop-Location }
