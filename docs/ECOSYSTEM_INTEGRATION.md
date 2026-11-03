# DatasetHub ↔ Equipment AI Platform 연계 기준

- 공통 아키텍처 기준: [EquipmentAIPlatform / ECOSYSTEM_ARCHITECTURE.md](https://github.com/WithMK/EquipmentAIPlatform/blob/main/docs/ECOSYSTEM_ARCHITECTURE.md)
- 전체 Markdown 상세 사본: 개인 Library `/설비제어SW/Equipment_AI_Platform_Integrated_Architecture_v0.1.md`
- 문서 버전: v0.1 (2026-11-03)

## DatasetHub 소유 기능
- 데이터 수집 PC에서 상시 동작: 타 PC의 표준 로그·검사 이미지 SMB Pull, 증분/중복 수집, 무결성 및 재시도, 수집 이력
- 데이터 Processing: 로그 파싱 및 DB 등록, 이미지 인덱싱 (후속)
- 데이터 Matching: LOT/ProductID/검사 실행 단위 로그·이미지 매칭 및 미매칭 검증 (후속)
- Equipment Management와 Collection Monitoring은 수집 운영을 지원하는 Desktop 모듈
- Processing/Matching은 SAIGE에 종속되지 않으며, WPF ViewModel 밖의 서비스 계층으로 개발

## 외부에 제공할 결과
- 원본 파일 보관소 + 제품/검사/이미지 메타데이터 DB
- Trimming AI Web이 매칭된 데이터 조회 및 Dataset 선택에 활용할 **읽기 API**(후속)
- 웹이 DB 파일(SQLite)을 공유 폴더로 직접 열거나 수집/매칭 엔진을 재구현하지 않도록 함
- 모델 학습/SAIGE 실행과 모델 목록 관리 UI는 DatasetHub 소유 아님

## 개발 및 배포 원칙
- 기존 Collector 코드·JSON 설정·수집 이력 호환성 보존
- 당장은 WPF Desktop 상시 실행; Windows Worker Service 분리는 상시 운영 안정성을 위한 별도 단계
- AI Platform Web과 Trimming AI Web은 별도 저장소/프로세스로 개발한 후 Reverse Proxy로 단일 외부 주소/포트에 통합 가능
- 새로운 cross-project 결정 시 EquipmentAIPlatform 공통 문서를 먼저 개정
