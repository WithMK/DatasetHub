# 임시 Windows 화면

.NET 10 Desktop Runtime이 설치된 Windows PC에서 실행합니다. 개발 시 .NET 10 SDK를 사용하세요.

```powershell
dotnet run --project src/DatasetHub.Desktop/DatasetHub.Desktop.csproj --configuration Release
```

화면에서 `config/collector.local-example.json` 또는 본인 설정 파일을 고르고 시작일·종료일을 선택한 뒤 **수집 시작**을 누릅니다. 시험 자료는 저장소 루트에서 `./scripts/New-MockLogs.ps1`로 생성할 수 있습니다. 상태 영역에서 신규/기존/완료 대기/실패/정상 파싱/형식 오류 수를 보고, 아래 목록에서 PC와 파일별 진행 상황을 확인합니다. 중지 요청은 현재 진행 중인 파일 작업 또는 폴더 열거가 끝난 뒤 적용됩니다. 화면의 최근 메시지는 1,000개만 표시하며 운영 감사 기록 대신 쓰지 않습니다.

[자동 반복 수집 규칙](rolling-log.md)에 따라 주기적으로 오늘과 이전 2일을 다시 확인할 수 있습니다. 이 화면을 닫으면 자동 반복도 중단됩니다. 설정 편집, 일정 예약, 전송 속도 제한, 수집 진행률(바이트), 실제 설비 로그인, DB 조회 화면은 아직 없습니다. 기존 콘솔 수집기와 동일한 설정·수집·파서 코드를 호출하며 실행 중 설정 변경은 적용되지 않습니다. 실제 SMB 및 완료 표시 규약 검증 전에는 모의 폴더로 시험하세요.
