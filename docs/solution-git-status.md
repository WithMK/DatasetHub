# Visual Studio에서 DatasetHub.sln이 수정됨으로 보일 때

`DatasetHub.sln`은 팀이 함께 사용하는 **추적 파일**입니다. `.gitignore`는 새로 생긴 파일에는 적용되지만 이미 Git에 등록된 이 솔루션의 변경 표시를 없애지 못합니다. 이 저장소는 `.gitattributes`에서 솔루션의 Windows 줄바꿈을 고정합니다. 새 변경을 받은 뒤 IDE를 닫고 아래 명령으로 파일을 정상 상태로 돌려보세요. 이 명령은 **해당 파일에서 본인이 의도적으로 수정한 내용도 버립니다.** 먼저 `git diff -- DatasetHub.sln`으로 확인하세요.

```powershell
git diff -- DatasetHub.sln
git restore -- DatasetHub.sln
```

Visual Studio가 이 PC에서 솔루션을 계속 고쳐서 로컬 변경을 커밋 대상에서 빼고 싶다면, 저장소 루트에서 **한 번** 실행하세요.

```powershell
./scripts/Local-SolutionChanges.ps1 -Mode Hide
```

이 설정은 본인의 Git 작업 복사본에서만 유효합니다. 솔루션 프로젝트를 추가하거나 동료의 솔루션 변경을 가져와야 한다면, 먼저 아래 명령으로 표시를 복원하고 `git status`와 `git diff`를 확인하세요. 숨긴 상태에서는 해당 파일의 업데이트가 Git 병합을 방해할 수 있습니다.

```powershell
./scripts/Local-SolutionChanges.ps1 -Mode Show
```

해당 파일 이외의 빌드 출력은 이미 `.gitignore`의 `.vs/`, `**/bin/`, `**/obj/`, `run/` 규칙으로 제외됩니다.
