# Desktop 모듈 개발

실행 파일은 기존 `DatasetHub.Desktop.exe`입니다. VS2026에서 Desktop을 시작 프로젝트로 설정합니다.
.NET 10 WPF, Prism.DryIoc 8.1.97을 고정 사용합니다. 최신 Prism으로 올릴 때는 API와 라이선스 조건을 별도 검토합니다.

## 프로젝트 역할

- Desktop: PrismApplication 시작, DI 구현 연결, 정적 ModuleCatalog 구성.
- Shell: 공통 메뉴, MainRegion, 상태 표시, Theme. 업무 모듈 참조 없음.
- Core: 메뉴/설정/로그/대화상자/화면 전환 계약, StatusEvent 및 ShutdownEvent.
- Infrastructure: JSON 읽기, 파일 로깅, 오류 대화상자, Region navigation 구현.
- Modules.Collector: 기존 수집 화면·ViewModel·파일 선택 창, ICollectorService와 기존 엔진 어댑터.
- Modules.Example: 독립 화면/메뉴 등록 및 EventAggregator 예제.
- Collector: 기존 CLI 및 텍스트/이미지 수집·SQLite 처리. 이번 UI 변경에서 수정하지 않음.

## 모듈 추가

1. WPF class library를 만들고 Core와 Prism.Wpf 8.1.97을 참조합니다.
2. IModule.RegisterTypes에서 `registry.RegisterForNavigation<MyView, MyViewModel>("MyView")`로 화면을 등록합니다.
3. OnInitialized에서 `provider.Resolve<IMenuRegistry>().Register(new("새 기능", "MyView"))`를 호출합니다. 화면 키는 전체 앱에서 유일해야 합니다.
4. View에 `prism:ViewModelLocator.AutoWireViewModel="True"`를 지정합니다.
5. Desktop에 프로젝트 참조를 추가하고 App.ConfigureModuleCatalog에서 `catalog.AddModule<MyModule>()` 한 줄을 추가합니다. Shell과 기존 업무 모듈 변경은 없습니다.
6. ViewModel에 공통 서비스를 생성자 주입합니다. 상태 전달은 `IEventAggregator.GetEvent<StatusEvent>().Publish(...)`를 사용합니다.

하위 화면도 고유한 navigation 키로 등록할 수 있습니다. 초기 메뉴는 단일 계층이며 동적 DLL 검색은 하지 않습니다.

## 기존 호환성과 수명

설정 JSON, archive 및 SQLite 경로/스키마는 그대로입니다. VS 빌드는 저장소 config, 배포 ZIP은 EXE 옆 config를 사용합니다.
수집 VM은 singleton으로 화면 이동 중 반복 수집과 표시 상태를 유지합니다. 종료 시 ShutdownEvent가 취소를 요청합니다.
ICollectorService는 CancellationToken 및 진행 callback을 받아 백그라운드에서 기존 엔진을 실행합니다. 향후 Worker로 분리할 때 이 계약/어댑터를 비-WPF 라이브러리로 옮길 수 있습니다. 이번에는 Worker를 구현하지 않습니다.

공통 진단 로그: `%LOCALAPPDATA%\DatasetHub\logs\desktop-yyyyMMdd.log`. 수집 현황은 기존 수집 화면에 표시됩니다.
예상 가능한 수집 오류는 화면과 로그에 기록하고, 처리되지 않은 UI 예외는 기록/안내 후 앱을 종료합니다.

## 검증

Windows CI에서 솔루션 빌드, 기존 수집 SmokeTests, 실제 WPF 파일 선택 취소, Prism 앱 시작/두 모듈 등록/화면 왕복/VM 유지/EventAggregator 전달을 검사합니다.
실제 설비 SMB 및 장시간 실행, DPI/화면 배치는 현장 확인이 필요합니다.
