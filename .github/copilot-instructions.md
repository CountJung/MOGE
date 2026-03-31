# Copilot Instructions (MOGE)

## TL;DR
- 이 레포는 **SharedUI(Razor Class Library)** 를 중심으로 UI/로직을 공유하고, **HybridApp(MAUI Blazor Hybrid)** / **WebApp(Blazor WASM)** 이 이를 호스트합니다.
- 이미지 처리 핵심은 `SharedUI.Services.ImageProcessorService` 입니다.
- **브라우저(WASM)에서는 OpenCV 코덱/인코더/런타임 제약**이 있으므로, 가능한 한 **Raw RGBA 캐시 + "RawToken" 반환** 경로를 사용합니다.

## 프로젝트 구조
- `SharedUI/`: 공용 UI/페이지/서비스
  - 캔버스 렌더링: `SharedUI/Components/ImageCanvas.razor`
  - 이미지 처리: `SharedUI/Services/ImageProcessorService.cs`
  - Raw 토큰/시그니처/캐시 인터페이스: `SharedUI/Services/Raw/*`
  - JS 정적 자산: `SharedUI/wwwroot/moge-canvas.js` (window.mogeCanvas)
  - 레이아웃/테마: `SharedUI/Layout/MainLayout.razor` (MudBlazor)
  - 전역 설정: `SharedUI/Pages/Settings.razor` + `SharedUI/Services/Settings/*`
- `WebApp/`: Blazor WebAssembly 호스트
  - DI 구성: `WebApp/Program.cs`
  - 브라우저 파일피커: `WebApp/Services/BrowserImageFilePicker.cs`
  - Raw RGBA LRU 캐시: `WebApp/Services/Raw/BrowserRawImageProvider.cs`
  - 부트스트랩: `WebApp/wwwroot/index.html`
  - 설정 저장소(Web): `WebApp/Services/Settings/BrowserAppSettingsStore.cs` (localStorage via JS)
- `HybridApp/`: MAUI Blazor Hybrid 호스트
  - DI 구성: `HybridApp/MauiProgram.cs`
  - 네이티브 파일피커: `HybridApp/Services/MauiImageFilePicker.cs`
  - 설정 저장소(App): `HybridApp/Services/Settings/MauiAppSettingsStore.cs` (Preferences)

## 전역 설정 (Settings)
- "테마, 패널 기본 동작, 기타 전역 UI 옵션"처럼 **앱 전체에 영향을 주는 설정은 AppBar/개별 페이지에 하드코딩하지 말고** `SharedUI/Pages/Settings.razor`에서 관리합니다.
- 공용 도메인 모델/서비스:
  - `SharedUI/Services/Settings/AppSettings.cs`
  - `SharedUI/Services/Settings/AppSettingsService.cs` (변경 이벤트 `Changed` 제공)
  - `SharedUI/Services/Settings/IAppSettingsStore.cs` (플랫폼별 저장소 추상화)
- 플랫폼별 저장:
  - Web: `SharedUI/wwwroot/moge-settings.js` + `WebApp/wwwroot/index.html`에서 스크립트 로드
  - Hybrid: `HybridApp/wwwroot/index.html`에서 스크립트 로드(웹 호스트와 동일한 정적 자산 규칙 유지)
- 앞으로 "설정이 필요한 항목"을 추가할 때는:
  1) `AppSettings`에 필드 추가
  2) `Settings.razor`에 UI 추가
  3) `MainLayout.razor` 또는 관련 서비스에서 `AppSettingsService.Current`/`Changed`로 반영

## 정적 자산/부트스트랩 (WebApp)
- WebApp은 표준 `<script src="_framework/blazor.webassembly.js"></script>` 방식으로 부팅합니다.
- `WebApp/WebApp.csproj`는 현재 MudBlazor + SharedUI 참조만 유지합니다.
  - 브라우저 빌드 경고/런타임 이슈를 피하려고 **WASM용 OpenCvSharp 런타임 패키지 참조를 추가하지 않는 방향**을 선호합니다.

## 로깅
- 공용 로깅 옵션: `SharedUI.Logging.MogeLogOptions`
- Web: `WebApp/Services/Logging/*` + `Program.cs`에 `PlatformSubfolder: "web"`
- App: `HybridApp/Services/Logging/*` + `MauiProgram.cs`에 `PlatformSubfolder: "app"`

## 개발/디버깅 워크플로
- VS Code Tasks/Launch:
  - `.vscode/tasks.json`에 `webapp: run` (http://localhost:5049)
  - `.vscode/launch.json`에 `WebApp: Debug (Chrome)` / `HybridApp: Debug (Windows)`
- publish 후 Web 정적 실행(참고 메모):
  - `python -m http.server 8080`

## 변경 작업 시 가이드 (우선순위)
- 공용 UI/기능은 **먼저 SharedUI에 구현**하고, 플랫폼별 차이는 Host(WebApp/HybridApp)에서 DI/서비스로 분기합니다.
- 페이지가 복잡해지면 `.razor`의 `@code` 블록 안에서 큰 `RenderFragment`를 직접 구성하지 말고, **별도 컴포넌트(.razor) 파일로 분리**한 뒤 파라미터/콜백으로 연결합니다(예: `SharedUI/Pages/EditorRightPanel.razor`).
- Blazor UI 바인딩 패턴, 컴포넌트 분리 원칙 → `instructions/moge-blazor.instructions.md` 참조
- 브라우저/네이티브 이미지 처리 파이프라인, JS Interop 규칙 → `instructions/moge-image-processing.instructions.md` 참조

## 코딩 스타일
- 불필요한 리팩터링/포맷 변경은 피하고, 기존 패턴(서비스 주입, MudBlazor 컴포넌트 사용)을 따릅니다.
- `Mat` 등 `IDisposable` 객체는 반드시 `using`으로 정리합니다(네이티브 경로).
- C# 성능 최적화 규칙(async/await, 메모리 관리, 컬렉션 등) → `instructions/csharp-best-practices.instructions.md` 참조
