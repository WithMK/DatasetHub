# Visual Studio 2026에서 열고 실행하기

Windows 10/11 x64의 **Visual Studio 2026**에서 `.NET 데스크톱 개발` 워크로드와 `.NET 10 SDK`를 설치하세요. 솔루션을 열 때 NuGet 패키지 복원이 필요하므로 개발 PC에서 nuget.org 접근 또는 사내 패키지 미러가 필요합니다. 실제 설비 데이터를 Git 저장소에 넣지 않습니다.

1. [PR #1](https://github.com/WithMK/DatasetHub/pull/1)의 `feat/log-collector-prototype` 브랜치를 내려받습니다. `main`에는 아직 병합되지 않았습니다.
2. 저장소 루트의 **`DatasetHub.sln`**을 Visual Studio로 엽니다. `솔루션 빌드`(Ctrl+Shift+B)를 실행합니다. 종속 NuGet 패키지를 처음 복원할 때 시간이 걸릴 수 있습니다.
3. 처음 검증할 때는 솔루션 루트의 `scripts/New-MockLogs.ps1`을 PowerShell에서 한 번 실행해 모의 파일을 만듭니다. 파일이 이미 있다면 이 스크립트는 덮어쓰지 않고 종료합니다.
4. 솔루션 탐색기에서 **DatasetHub.Desktop** 프로젝트를 마우스 오른쪽 클릭해 **시작 프로젝트로 설정**하고 F5를 누릅니다. 화면의 설정 경로에는 `config/collector.local-example.json`이 자동 표시됩니다.
5. 시작일/종료일을 모두 **2026-10-26**으로 선택해 수집을 실행합니다. 첫 실행은 신규 1건, 다음 실행은 기존 확인 1건을 예상합니다.

프로젝트 역할: `DatasetHub.Desktop` = 임시 WPF 화면, `DatasetHub.Collector` = 수집/파싱 및 CLI, `DatasetHub.SmokeTests` = 수집·복구 검증 콘솔. 테스트를 다시 돌리려면 SmokeTests를 시작 프로젝트로 선택해 실행할 수 있습니다. 테스트는 OS 임시 폴더의 합성 데이터를 쓰고 완료 시 삭제합니다.

빌드하면 실행에 필요한 WPF 출력이 저장소 루트의 **`run/Debug/` 또는 `run/Release/`**에 모입니다. `run/Debug/DatasetHub.Desktop.exe`를 탐색기에서 바로 실행할 수 있습니다. 설정은 빌드 출력과 분리된 **솔루션 루트의 `config/`**에서 읽습니다. 솔루션 폴더 이름이 `DataCollect`라면 `DataCollect/config/collector.local-example.json`입니다. 예제 설정의 상대 경로는 이 설정 파일을 기준으로 해석되어 데이터는 솔루션 루트의 `runtime/`에 기록됩니다.

**다른 PC로 옮길 때는 EXE 하나만 복사하지 말고 출력 폴더 전체와 별도의 `config/` 폴더를 복사**하세요. VS 개발 PC 밖에서는 해당 .NET Desktop Runtime이 필요합니다. 런타임 설치 없이 쓰려면 CI의 자체 포함 Windows ZIP을 사용합니다. ZIP은 압축 해제 폴더 아래 `config/`를 사용합니다. 빌드 폴더 `run/`은 Git에 등록되지 않습니다.
