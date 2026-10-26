# 입력 텍스트 표준 로그 계약

상태: 표준 기본 형식 확정, 실제 파일 샘플 검증 전.

## 기본 형식

입력 텍스트는 SEM_LOG_Version=2.0의 표준 로그를 따릅니다. 확장자는 .log, 필드 구분자는 탭입니다. 화면 설명용 파이프 구분을 실제 파일 구분자로 사용하지 않습니다.

| 순서 | 필드 |
|---:|---|
| 1 | Datetime |
| 2 | MachineID |
| 3 | LogType |
| 4 | LotID |
| 5 | Recipe |
| 6 | ProductID |
| 7 | Status |
| 8 | Scope |
| 9 | OperationName |
| 10 | OperationStatus |
| 11 | Data |

Datetime 형식은 yyyy-MM-dd HH:mm:ss.fff, OperationStatus는 Start / End / Event입니다.
빈 필드를 보존합니다. Level=L1/L2/L3는 실제 로그 컬럼이나 Data에 추가하지 않습니다.
분할 전 ProductID는 ParentProductId, 분할 후에는 개별 ProductId 의미입니다.

## 1차 파서 구현

- StreamReader 등으로 서버의 확정 원본을 스트리밍합니다.
- 헤더·버전행은 명시적 규칙으로 구분하며 일반 데이터 행으로 등록하지 않습니다.
- 11개 필드, 밀리초 시각, OperationStatus를 검사합니다.
- 원본 Data, Source FileId, LineNumber, 원본 시각을 보존합니다.
- 같은 FileId+LineNumber의 중복 등록을 막습니다.
- 비정상 행은 원본 위치·이유를 기록하고 정상 행과 구분합니다.
- ExecId·ParentExecId·InspectionId 등 Data 키 해석은 합의된 규약만 적용합니다.
- 로그만으로 검사 이미지와 연결했다고 추측하지 않습니다. 명시적 제품·검사 ID를 사용합니다.
- 파일 등록과 마지막 처리 위치는 짧은 트랜잭션으로 확정합니다.

## 샘플 확인이 필요한 세부

UTF-8/BOM 등 인코딩, 헤더·버전행의 정확한 위치·표현, Data의 구분·Escape 규칙, 탭 포함 처리, LogType/Status Enum, 시간대, 로그 롤링·닫힘 표시가 필요합니다.
Data에 탭이 허용되는지 확정 전에는 초과 필드를 임의로 결합하거나 조용히 삭제하지 않습니다.
지금은 합성 표준 로그로 기본 파서를 검증할 수 있지만 실제 파일 호환은 샘플 시험 후 확정합니다.
