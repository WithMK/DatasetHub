# 표준 로그 수집기: 첫 구현

> 갱신 로그 수집 정책은 [rolling-log.md](rolling-log.md)가 최신입니다. 아래의 `.done` 필수/기존 파일 충돌 설명은 초기 구현의 기록입니다.

## 범위와 상태

C# / .NET 10 / Microsoft.Data.Sqlite 10.0.0 + SQLitePCLRaw.bundle_e_sqlite3 3.0.5 기반 수동 콘솔 프로토타입입니다.
설비 수·PC 수는 설정 목록으로 결정합니다. 이번 버전은 모든 PC를 순차 처리합니다.
Worker Service, 예약, UI, 속도 제한, 표준 로그 기본 파싱을 추가했습니다. 이미지·JSON, Dataset 생성은 아직 구현하지 않았습니다.
Windows CI에서 Release 빌드(경고/오류 0), 모의 테스트 11개, PowerShell 모의 로그 생성 및 CLI 2회 실행을 통과했습니다.
[검증 실행 기록](https://github.com/WithMK/DatasetHub/actions/runs/40284942891), 코드 커밋 `167f65a`.
작성 환경에는 .NET SDK가 없어 GitHub Windows 실행 환경을 사용했습니다. 실제 SMB 시험은 아직입니다.

## 시작

.NET 10 SDK가 있는 Windows에서 저장소 루트를 기준으로 실행합니다.

```powershell
./scripts/New-MockLogs.ps1
dotnet build src/DatasetHub.Collector/DatasetHub.Collector.csproj --configuration Release
dotnet run --project src/DatasetHub.Collector -- config/collector.local-example.json 20261026 20261026
dotnet run --project tests/DatasetHub.SmokeTests --configuration Release
```

실행 인자는 설정 파일, 시작일, 종료일이며 양 끝 날짜를 포함합니다.
상대 경로는 **설정 파일이 있는 폴더 기준**입니다. 로컬 설정을 만들 때 config/*.local.json으로 저장합니다.
기존 collector.example.json은 전체 시스템 설계용이며 실행 설정이 아닙니다. 실행 예제는 collector.local-example.json입니다.
알 수 없는 설정 필드는 오류로 거부하므로 두 설정을 혼용하지 않습니다.

## 수집 계약

- 원본: SourceRoot/yyyyMMdd/*.log. 날짜 폴더 바로 아래 파일만 탐색합니다. 예: D:\Log\20261026\2026102610_TRIM001.log.
- 모의 완료 규약: `demo.log`를 완전히 닫은 후 `demo.log.done`을 생성하고 이후 원본을 변경하지 않습니다.
- 마커 없는 .log는 Deferred, .partial은 탐색 대상에서 제외합니다. 오래됐다는 이유만으로 완료 처리하지 않습니다.
- 실제 생산 프로그램에 이 완료 규약이 있다고 가정하지 않습니다. 실제 완료 신호 확정 후 교체해야 합니다.
- 원본은 읽기 전용 접근하며 삭제하거나 마커를 생성하지 않습니다.
- 대상: ArchiveRoot/MachineId/PcId/.snapshots/yyyyMMdd/파일명/<SHA256>.log. 식별자는 고유하며 운영 중 재사용하지 않습니다.
- SHA-256 원본 계산 → .partial 복사 → 서버 SHA-256·크기 검사 → 원본 크기·수정시각 재검사 → 최종 rename → SQLite 기록.
- 재실행 때 완료 이력만 믿지 않고 원본과 수집본 해시를 다시 확인합니다. DB 기록 전 종료된 파일도 이 방식으로 복구합니다.
- 실패·누락은 같은 날짜 범위를 다시 실행해 처리합니다. 과거 폴더에 늦게 추가된 파일도 발견합니다.
- 동일 경로의 해시가 달라지면 Conflict, 기존 수집본은 유지합니다. 원본 변경인지 수집본 손상인지는 자동 판단하지 않습니다.
- DB의 Attempts는 해당 파일 상태를 기록한 횟수이며, 전송 시도 횟수와 동일하지 않습니다.
- 파일/폴더 접근 오류는 기록하고 다음 항목으로 넘어갑니다. DB 자체 오류는 실행을 중단합니다.
- 원본 바이트는 그대로 보존하고, [기본 11필드 파서](parser-prototype.md)가 별도 테이블에 해석 결과를 등록합니다.

## 운영 제약

- 실제 Windows SMB 연결·권한·파일 잠금·공유 끊김은 아직 미검증입니다.
- Windows의 파일 공유 모드로 복사 중 쓰기/삭제를 차단하지만, 올바른 완료 마커 계약이 우선입니다.
- 네트워크 폴더 열거는 동기 API입니다. 응답 없는 SMB의 강제 타임아웃과 신속한 취소는 보장하지 않습니다.
- 원본/대상 링크·junction은 지원하지 않습니다. 경로와 상위 디렉터리는 관리자가 통제해야 합니다.
- DB는 서버 로컬 디스크에 둡니다. archive와 DB 각각의 배타적 파일 핸들로 같은 저장소의 중복 실행을 막습니다.
- 전송 속도 제한, 저장 공간 사전 검사, 스케줄링이 없으므로 현 단계는 로컬 모의 시험용입니다.
- 선택한 날짜 범위 전체를 스캔합니다. 설비에 이미 삭제된 파일이나 유효하지 않은 일자 폴더는 복구할 수 없습니다.
- 충돌은 수동 확인 대상입니다. 해시가 다른 파일을 자동 덮어쓰기하는 옵션은 없습니다.

## 결과 확인

종료 코드: 0=발견한 대상 모두 확인, 1=실패/충돌/완료 대기 존재, 2=설정/시작 오류, 130=취소.
파일이 하나도 없을 때의 0은 생산 데이터 완전성을 증명하지 않습니다.
콘솔 요약: Collected / Skipped / Deferred / Failed.
SQLite Files는 최신 파일 상태, ScanErrors는 폴더 탐색 실패를 보관합니다.
전체 실행 감사 이력과 제품별 기대 파일 수 검증은 후속 단계입니다.

테스트 코드는 원본 바이트 일치, 재실행, DB 이력 누락, 부분 복사 잔재,
늦은 완료 마커, 내용 변경 충돌, 접속 실패 후 다른 설비 진행, 실행 중복 및 취소를 검사합니다.
실제 프로세스 강제 종료·디스크 부족·복사 중 SMB 끊김은 별도 통합 시험이 필요합니다.
