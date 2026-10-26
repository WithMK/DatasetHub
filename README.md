# DatasetHub

설비 PC의 표준 `.log`를 중앙 PC로 가져와 버전별로 보관하고 기본 11필드를 SQLite에 등록하는 Windows 시험용 프로그램입니다. 현재는 **로그 수집 단계**이며 이미지·JSON 수집과 Dataset 생성은 아직 구현 전입니다. 개발 내용은 [PR #1](https://github.com/WithMK/DatasetHub/pull/1)의 `feat/log-collector-prototype` 브랜치에 있습니다. `main` 브랜치에는 아직 이 실행 프로그램이 없습니다.

## EXE를 바로 실행하기

Windows x64에서 [최신 시험용 ZIP 다운로드](https://github.com/WithMK/DatasetHub/actions/runs/40422042841/artifacts/12696604837)를 사용하거나, 아래의 Visual Studio 빌드 방법을 사용합니다. CI 아티팩트는 보관 기한이 있으므로 링크가 만료되면 [PR의 최신 통과한 Actions 실행](https://github.com/WithMK/DatasetHub/actions)에서 `DatasetHub-Windows-x64`를 받으세요. 다운로드한 ZIP 안의 `DatasetHub-Windows-x64.zip`을 **한 번 더 압축 해제**한 뒤 `DatasetHub.Desktop.exe`를 실행합니다. 이 ZIP은 .NET 런타임을 포함하므로 VS와 SDK 설치가 필요하지 않습니다.

Visual Studio 2026으로 빌드할 경우:

1. `feat/log-collector-prototype` 브랜치를 내려받아 저장소 루트의 **`DatasetHub.sln`**을 엽니다. VS의 `.NET 데스크톱 개발` 워크로드와 .NET 10 SDK가 필요합니다.
2. **빌드 → 솔루션 빌드**를 누릅니다. NuGet 복원에 실패하면 패키지 접근 설정을 확인합니다.
3. 탐색기에서 **`run\Debug\DatasetHub.Desktop.exe`**를 실행합니다. Release로 빌드했다면 `run\Release\DatasetHub.Desktop.exe`입니다. 설정은 솔루션 루트의 **`config\`**에서 읽습니다. 솔루션 폴더 이름이 `DataCollect`라면 `DataCollect\config\`입니다.

실행 파일 창 제목이 `DatasetHub · 표준 로그 수집 v0.2.1`인지 확인하세요. 오래된 EXE의 `찾아보기` 종료 문제가 남아 있다면 브랜치를 업데이트하고 솔루션을 **정리 → 다시 빌드**하세요. VS를 사용한 실행 안내는 [docs/visual-studio.md](docs/visual-studio.md)에 있습니다.

## 모의 로그로 첫 수집 확인

VS 빌드본은 **솔루션 루트(`DataCollect`)**의 PowerShell에서 아래처럼 실행합니다. ZIP 실행본은 압축을 해제한 프로그램 폴더에서 같은 명령을 사용합니다.

```powershell
.\scripts\New-MockLogs.ps1
.\run\Debug\DatasetHub.Desktop.exe
```

ZIP 실행본에서는 두 번째 명령을 `.\DatasetHub.Desktop.exe`로 바꾸세요. 스크립트는 첫 회에만 실행합니다. 동일한 `demo.log`가 있으면 덮어쓰지 않고 종료합니다. UI에는 솔루션 루트의 `config\collector.local-example.json`이 자동 표시됩니다. ZIP 실행본은 압축 해제 폴더의 `config\`를 사용합니다.

1. **시작일과 종료일을 모두 `2026-10-26`**으로 선택하고 **수집 시작**을 누릅니다.
2. 첫 실행에서 `새 복사본 1`, `파싱 1행`을 확인합니다.
3. 다시 수집하면 `기존 확인 1`로 표시됩니다.
4. 수집한 버전은 설정 폴더의 상위 폴더에 있는 `runtime\archive`, 이력 DB는 `runtime\history.db`에 저장됩니다. `config/collector.local-example.json`의 경로는 **그 설정 파일이 있는 폴더 기준**으로 해석됩니다.

`찾아보기`는 앱 내부의 JSON 파일 선택 창입니다. 취소하면 기존 설정 경로를 유지합니다. 자동 반복을 켜면 **1·5·10·30·60분** 중 선택한 간격으로 오늘과 이전 2일을 다시 검사합니다. 기본 주기는 5분이고, 앱을 닫으면 반복이 멈춥니다. 주기 시간은 직전 검사 **종료 후 대기 시간**입니다. 특정 과거 날짜를 확인하려면 자동 반복을 끄고 기간을 선택하세요.

## 다른 설비 PC의 로그 경로 등록

예제 설정을 `config\collector.local.json`으로 복사하고 PC별 `sourceRoot`를 서버에서 접근할 수 있는 SMB 공유 경로로 바꿉니다. 예를 들어 설비 PC의 `D:\Log\Standard`를 `StandardLog`로 공유했다면 JSON에는 다음처럼 입력합니다. UI에서 이 새 설정 파일을 선택하세요.

```json
{
  "archiveRoot": "E:\\DatasetHub\\Raw",
  "databasePath": "C:\\DatasetHub\\Db\\history.db",
  "completionSuffix": "",
  "equipments": [
    {
      "machineId": "TRIM001",
      "enabled": true,
      "pcs": [
        { "pcId": "CTRL", "sourceRoot": "\\\\192.168.10.101\\StandardLog" }
      ]
    }
  ]
}
```

`sourceRoot` 아래에서 `yyyyMMdd\<시간폴더>\*.log`를 찾습니다. 설비 대수는 설정 목록으로 늘릴 수 있습니다. 서버 PC 탐색기에서 공유 경로를 먼저 열어 읽기 권한을 확인하세요. UI는 현재 설정 파일을 **선택**하지만 설비 목록을 화면에서 편집·저장하지는 않습니다.

기본 `completionSuffix`는 빈 문자열이며 `.done` 파일이 없어도 현재 보이는 로그를 복사합니다. 같은 경로의 로그가 나중에 바뀌면 이전 복사본을 보존하고 새 버전을 만듭니다. 복사본은 **그 시점의 관측 결과**이며 최종 완료를 뜻하지 않습니다. 1분 주기는 로그 전체를 자주 읽으므로 실제 설비 부하를 측정해야 합니다. 오래된 스냅샷 자동 정리, Windows 서비스 실행, 실제 설비 SMB/로그 인코딩 검증은 아직 완료되지 않았습니다. 자세한 동작은 [갱신 로그 수집 규칙](docs/rolling-log.md)에 있습니다.

## 개발 문서

- [시스템 전체 설계](docs/system-design.md)
- [표준 로그 입력 계약](docs/standard-log-input.md)
- [수집기 구현 및 검증 범위](docs/collector-prototype.md)
- [기본 파서 범위](docs/parser-prototype.md)
- [Windows ZIP 사용법](docs/windows-bundle.md)
- [솔루션의 Git 수정 표시 처리](docs/solution-git-status.md)

실제 생산 로그·이미지·운영 DB·접속 자격증명은 이 공개 Git 저장소에 올리지 않습니다.
