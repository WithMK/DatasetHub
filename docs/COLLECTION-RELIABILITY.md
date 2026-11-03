# Collection Reliability

공통 기준: [Equipment AI Platform 아키텍처](https://github.com/WithMK/EquipmentAIPlatform/blob/main/docs/ECOSYSTEM_ARCHITECTURE.md).
SMB Pull, 로그 증분, 이미지 안정화/SHA-256, 저장 경로, JSON과 기존 SQLite 이력을 유지합니다.

## 작업 전 분석

| 요구 | 작업 전 상태 | 이번 적용 |
|---|---|---|
| 증분/이미지 안정화/partial/중복 방지/실행 잠금 | Already Implemented | 재사용, 복사/해시 진행 신호 추가 |
| PC 접근 상태/마지막 시도·성공·실패 | Partially Implemented | 실제 루트 목록 접근 결과·실패 시각·연속 실패 표시 |
| 지연 | Partially Implemented | 기존 예정 시각 기반 판정 유지, UI와 실행 루프 분리 |
| Hang | Not Implemented | 무진행/전체 시간 제한, 취소 요청, 실제 종료 전 재실행 금지 |
| 자동 복구 | Partially Implemented | 제한된 지수 Backoff+Jitter 재시도, 한도 후 다음 주기 대기 |
| 과거 누락 보정 | Partially Implemented | 장애/미완료 후 전체 날짜 확인, 알려진 미완료 날짜 포함, 주기적 전체 대조 |
| 장애 묶음/복구 시점 | Not Implemented | 열린 장애 단위 UPSERT, 최초/최근/시도수/복구 시각 |
| 프로세스/UI 장애 | Not Implemented | 독립 Heartbeat, PowerShell watchdog, 선택적 비정상 종료 재시작 |

## 운영 설정

기존 JSON에 `reliability`가 없으면 아래 기본값을 사용합니다.

```json
"reliability": {
  "enabled": true,
  "maxRetries": 2,
  "retryBaseSeconds": 10,
  "retryMaxSeconds": 120,
  "noProgressTimeoutSeconds": 300,
  "jobTimeoutSeconds": 7200,
  "fullScanIntervalHours": 24
}
```

첫 시도 외 최대 2회 추가 재시도합니다. 대기 시간은 지수 증가하며 0.8~1.2 jitter를 적용합니다. 재시도는 실패 배치 전체를 재확인합니다. 정상 파일은 메타데이터로 건너뛰며, PC별 별도 동시 작업을 도입하지 않아 기존 텍스트 경로 충돌 검사와 writer 잠금을 유지합니다. 여러 PC가 동시에 장애여도 무제한 재시도하지 않습니다.
원본 루트의 실제 목록 접근 성공과 파일별 성공은 별개입니다. Ping은 하지 않으며 네트워크 도달성은 SMB 접근 결과로 판단합니다. IP 연결만 정상인 것을 수집 정상으로 취급하지 않습니다.

## 감시·복구

ReliableCollector가 기존 Collector를 감쌉니다. Timeout은 취소를 요청할 뿐 작업을 버리지 않습니다. 취소 불가능한 SMB 호출은 계속 기다리고 잠금을 유지합니다. 그 동안 UI에 시간 초과/종료 대기 상태를 표시하며 **새 writer를 실행하지 않습니다**. 이 경우 운영자가 OS/공유 상태를 해결해야 하고 다음 PC 처리도 지연될 수 있습니다. 프로세스 격리는 후속 검토입니다.
복사/증분/해시 읽기의 진행 신호로 대용량 파일의 정상 진행과 무진행을 구분합니다. 설정값은 실제 파일 크기/회선 속도에 맞춰 조정하세요.
자동 반복은 WPF 컨텍스트 밖에서 실행하고 UI에는 결과만 게시합니다. UI 대기 메시지는 최대 1,000개로 제한하며 정지 중 초과한 임시 화면 메시지는 생략합니다(영구 실행/장애 이력은 유지). 실행 중 설정/기간/주기 UI 변경은 다음 시작부터 적용합니다. 최종 회차 실패도 자동 반복을 끝내지 않고 다음 정기 주기를 기다립니다.

자동 전체 모드에서 DB의 NeedsRecovery를 작업 전에 설정합니다. 최초 실행/비정상 종료/실패 후에는 전체 날짜 원본 목록과 기존 Files 메타데이터를 대조합니다. 정상 회차는 최근 날짜와 알려진 Failed/Collecting/Pending/Deferred 날짜를 포함하고, 기본 24시간마다 전체 날짜를 확인합니다. 파일 전체 해시는 변경 후보에만 기존 정책대로 적용합니다.
수동 기간 지정, CLI, 선택 PC/설비 재수집은 지정 기간을 임의 확대하지 않습니다. 전체 장애 보정은 기간 지정을 끈 자동/전체 수집으로 수행하세요.
알려진 미완료 파일이 삭제되거나 읽을 수 없으면 Missing 장애가 남습니다. 원본이 복원되고 Collected 확인 후 해제합니다. 장애 기간 중 생성됐다가 **한 번도 관측되기 전에 삭제된 파일은 알 수 없습니다**. 원본 보존 기간과 원본 측 목록/manifest 계약이 필요합니다.
이미지 안정화 대기는 성공 탐색과 별도로 남으며 다음 회차에 다시 확인합니다. 파일 수집 완료는 파싱/데이터 처리 완료가 아닙니다.

## SQLite / UI

추가 테이블: ReliabilityIncidents(열린 장애별 중복 억제), ReliabilityState(복구 필요·진행·Heartbeat), SourceHealth(루트 접근 상태/성공·실패 시각). 기존 테이블은 삭제하지 않습니다. 진단 저장 실패는 stderr/Trace로 알리고 수집을 계속합니다. 기존 필수 수집 이력 DB 자체가 쓰기 불가능하면 안전을 위해 수집은 실패합니다.
모니터링에 Success/Warning/Error, 공유 접근, 마지막 실패, 다음 예정, 연속 실패, 작업 시간, 복구 결과를 표시합니다. 상세 오류에는 최초/최근/누적 횟수/복구 시각을 표시합니다. 과거 오류가 있어도 열린 장애가 해소되면 현재 건강 상태를 정상으로 계산합니다.
연속 실패 수는 실제 SourceCollectionRuns의 연속 Failed 회차 수입니다. 장애 Attempts는 관측 횟수이며 재시도 설정 횟수와 동일하지 않습니다. 데이터 최신성은 로그 최종 수정 시각으로 별도 표시합니다. 이미지 최신성 지표는 후속 범위입니다.

## 프로세스/Windows 운영

Heartbeat: `%LOCALAPPDATA%\DatasetHub\health\<PID>.json`. 프로세스 시작 식별자, 백그라운드 heartbeat, UI heartbeat, 정상 종료 표시가 기록됩니다.

```powershell
.\DatasetHub.Desktop.exe --collect "D:\DataCollect\config\collector.local.json"
.\scripts\Watch-DatasetHub.ps1 -CollectorProcessId 1234
# 비정상 종료에만 최대 3회, 60초 대기 후 재시작 (명시적으로 선택)
.\scripts\Watch-DatasetHub.ps1 -CollectorProcessId 1234 -RestartOnCrash -Executable "D:\DataCollect\DatasetHub.Desktop.exe" -ConfigPath "D:\DataCollect\config\collector.local.json"
```

Watchdog는 멈춘 프로세스를 강제 종료하지 않고 콘솔 및 `health\watchdog.log`에 상태 변화를 기록합니다. PID 재사용/Heartbeat 미확인 시 자동 재시작하지 않습니다. 정상 종료는 재시작하지 않습니다. 동일 앱을 다른 PID로 수동 시작하지 않도록 운영하세요; 실행 잠금은 중복 writer를 차단합니다.
Windows 작업 스케줄러에 사용자 로그온 시 위 수집 시작 명령을 등록할 수 있습니다. 이번 작업은 운영 PC에 작업을 자동 설치하지 않습니다. WPF는 대화형 사용자 세션이 필요합니다. 로그아웃 후 지속 운전은 Windows Worker 분리 후속 범위입니다.
같은 PC의 watchdog은 **PC 전원 OFF/OS 전체 정지 자체를 감지하거나 알림 전송할 수 없습니다**. 원격 감시가 필요합니다. 내부 감시는 자기 프로세스 완전 종료를 감지할 수 없습니다.

## 검증 범위와 제한

Windows 자동화: 기존 수집/Equipment/Monitoring 회귀, 빈 원본, 접근 실패→연결 복구→과거 파일 수집, 반복 탐색, 삭제 원본/복원, 무진행 취소·종료 후 재시도, 다중 PC 장애·한도, 별도 프로세스 강제 종료 및 불완전 복사본 복구, 진단 실패 격리.
강제 종료 테스트는 엔진 작업 시작에서 자식 프로세스를 멈춘 후 kill하고 미리 구성한 partial/미커밋 복사본을 검증합니다. 실제 SMB 복사 도중 케이블 분리와 OS가 반환하지 않는 장기 블로킹은 현장 검증 대상입니다. Watchdog 자동 재시작은 운영 환경 도입 전에 계정·경로·권한으로 점검하세요.
독립 exe watchdog, 서비스 전환, 외부 알림, 원본 manifest, 장애별 보존 기간/DB 정리는 후속 권장 사항입니다.
