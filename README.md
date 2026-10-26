# DatasetHub

트리밍 설비의 이미지·로그·검사 Metadata를 수집하고 제품별로 연결하여 AI 학습·분석용 Dataset을 구성하는 프로젝트입니다.

## 현재 상태

**표준 로그 수집기의 첫 콘솔 프로토타입을 작성했습니다. Windows CI 빌드·모의 테스트를 통과했으며 실제 설비 SMB 검증은 아직입니다.**

입력 텍스트 로그는 SEM_LOG 2.0 표준 11필드의 탭 구분 .log를 따릅니다. 검사 JSON 양식은 미정입니다. 현재 개발은 표준 로그 파일 수집·SQLite 이력·재실행 복구에 한정합니다. 내용 파싱은 후속 단계입니다.

## 기본 원칙

- 설비 동작 영향 최소화와 누락 복구를 실시간성보다 우선합니다.
- 설비의 C(OS·프로그램)와 D(데이터)는 서로 다른 물리 저장장치입니다.
- 서버가 D의 공유폴더에서 저장 완료 자료를 PULL합니다.
- 날짜·시간별 폴더를 대조하며 원본과 수집 이력을 보존합니다.
- 설비 수는 설정 목록으로 관리합니다. 초기 9대/27PC는 예시이며 고정 상한이 아닙니다.
- 이미지는 파일로 저장하고 SQLite에는 이력·경로·제품 관계·측정값을 기록합니다.
- 생산 PC 원본 삭제는 초기 구현 범위에서 제외합니다.

## 구성

| 경로 | 내용 |
|---|---|
| [docs/system-design.md](docs/system-design.md) | 전체 설계 v0.3 |
| [docs/phase1-scope.md](docs/phase1-scope.md) | 1차 구현 범위·검수 기준 |
| [docs/standard-log-input.md](docs/standard-log-input.md) | 입력 표준 로그 계약·파서 범위 |
| [docs/decisions.md](docs/decisions.md) | 확정 사항·미정 항목 |
| [config/collector.example.json](config/collector.example.json) | 비밀정보 없는 제안 설정 |
| src/ | C# 수집기 프로토타입 |
| tests/ | 모의 폴더 통합 검증 코드 |
| scripts/ | 모의 로그 생성 스크립트 |

## 시작 방법

[첫 수집기 실행·검증 안내](docs/collector-prototype.md)를 참고하세요.
.NET 10 SDK에서 실행하며, 모의 완료 마커가 있는 로컬 로그로 먼저 검증합니다.
`config/collector.local-example.json`이 실행 설정이며 기존 `collector.example.json`은 전체 시스템 설계안입니다.

실제 원본 이미지·생산 로그·운영 DB·접속 자격증명은 Git에 넣지 않습니다.
