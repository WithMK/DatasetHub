# DatasetHub Windows x64 시험용 ZIP

이 ZIP에는 .NET 런타임을 포함한 임시 WPF 화면과 콘솔 수집기가 들어 있습니다. Windows x64에서 ZIP을 **전체 압축 해제**한 뒤 사용하세요. .NET SDK나 Visual Studio가 없어도 모의 수집을 실행할 수 있습니다. 사내 PC의 실행 정책과 백신 허용 여부는 별도로 확인하세요.

1. PowerShell을 열고 압축을 해제한 `DatasetHub-Windows-x64` 폴더로 이동합니다.
2. `./scripts/New-MockLogs.ps1`을 실행합니다. 합성 로그 1개와 `.done` 완료 표시가 `runtime/mock-source/20261026/`에 생깁니다. PowerShell 정책으로 막히면 스크립트를 텍스트로 확인하고 Windows PowerShell의 실행 정책을 관리자와 확인하세요.
3. `DatasetHub.Desktop.exe`를 실행합니다. 설정 파일은 패키지 안의 예제 경로가 자동으로 입력됩니다. 시작일과 종료일을 모두 **2026-10-26**으로 선택하고 **수집 시작**을 누릅니다.
4. 첫 실행의 신규 파일 1건/발견한 .log 1개, 두 번째 실행의 기존 파일 확인 1건을 확인합니다. 결과는 `runtime/archive`와 `runtime/history.db`에 보관됩니다.

실제 장비에는 아직 연결하지 마세요. 완료 표시 규칙과 로그 인코딩을 실제 샘플로 확인해야 합니다. 장비 연결을 시험할 때에는 `config/collector.local-example.json`을 다른 이름으로 복사해 공유 경로와 서버 보관 경로를 수정합니다. 설정 파일의 상대 경로는 해당 설정 파일 위치를 기준으로 해석합니다. 배포 패키지는 이력 DB와 원본 파일을 포함하지 않습니다.

콘솔 점검: `./console/DatasetHub.Collector.exe ./config/collector.local-example.json 20261026 20261026`
