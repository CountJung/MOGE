# Copilot Instructions (MOGE)

## TL;DR
- 이 레포는 **SharedUI(Razor Class Library)** 를 중심으로 UI/로직을 공유하고, **HybridApp(MAUI Blazor Hybrid)** / **WebApp(Blazor WASM)** 이 이를 호스트합니다.
- 이미지 처리 핵심은 `SharedUI.Services.ImageProcessorService` 입니다.
- **브라우저(WASM)에서는 OpenCV 코덱/인코더/런타임 제약**이 있으므로, 가능한 한 **Raw RGBA 캐시 + “RawToken” 반환** 경로를 사용합니다.

## 프로젝트 구조
- `SharedUI/`: 공용 UI/페이지/서비스
  - 캔버스 렌더링: `SharedUI/Components/ImageCanvas.razor`
  - 이미지 처리: `SharedUI/Services/ImageProcessorService.cs`
  - Raw 토큰/시그니처/캐시 인터페이스: `SharedUI/Services/Raw/*`
  - JS 정적 자산: `SharedUI/wwwroot/moge-canvas.js` (window.mogeCanvas)
  - 레이아웃/테마: `SharedUI/Layout/MainLayout.razor` (MudBlazor)
- `WebApp/`: Blazor WebAssembly 호스트
  - DI 구성: `WebApp/Program.cs`
  - 브라우저 파일피커: `WebApp/Services/BrowserImageFilePicker.cs`
  - Raw RGBA LRU 캐시: `WebApp/Services/Raw/BrowserRawImageProvider.cs`
  - 부트스트랩: `WebApp/wwwroot/index.html`
- `HybridApp/`: MAUI Blazor Hybrid 호스트
  - DI 구성: `HybridApp/MauiProgram.cs`
  - 네이티브 파일피커: `HybridApp/Services/MauiImageFilePicker.cs`

## 런타임별 이미지 처리 원칙 (매우 중요)
### 1) 브라우저(WASM)
- 목표: OpenCV의 디코드/인코드 의존을 최소화하고, **Raw RGBA 캐시/토큰 기반으로 파이프라인을 유지**합니다.
- `WebApp/Services/BrowserImageFilePicker.cs`는 JS(`mogeFilePicker.pickImage`)로부터
  - 원본 파일 bytes(base64)
  - 브라우저가 디코드한 RGBA bytes(rgbaBase64)
  - width/height
  를 받아서 **`BrowserRawImageProvider`에 (signature -> RawRgbaImage)로 캐시**합니다.
- `SharedUI/Components/ImageCanvas.razor`는 `ImageBytes`가 RawToken이면
  `mogeCanvas.setRawRgba(canvas, width, height, rgbaBytes)`로 바로 렌더링합니다.
- `SharedUI/Services/ImageProcessorService.cs`는 브라우저에서:
  - 필터/변환은 `SharedUI.Services.Raw.RgbaImageOps`로 수행
  - 결과는 **인코딩 파일(PNG 등) 대신 RawToken으로 반환**할 수 있습니다.

### 2) 네이티브(MAUI)
- 목표: OpenCvSharp 경로 유지(디코드/인코드/Mat 기반 처리).
- `ImageProcessorService`의 기본 경로(OperatingSystem.IsBrowser()가 false)를 사용합니다.

## JS Interop / Canvas
- `SharedUI/wwwroot/moge-canvas.js`는 `window.mogeCanvas`를 제공하며 `setImage`, `setRawRgba`, `draw`, `clear` 등을 담당합니다.
- Web/Hybrid 호스트에는 `moge-canvas-shim.js`가 존재하며, SharedUI 정적 자산이 오래된 캐시로 로드되어 `setRawRgba`가 없을 때를 대비한 **호환성 shim**입니다.
- 새 JS 기능을 추가할 때는:
  1) `SharedUI/wwwroot/moge-canvas.js`에 구현
  2) 필요 시 `WebApp/wwwroot/moge-canvas-shim.js`, `HybridApp/wwwroot/moge-canvas-shim.js`도 동일 시그니처로 보완
  3) Blazor에서는 `IJSRuntime` 호출부를 `SharedUI` 컴포넌트 쪽에 두는 것을 우선합니다.

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
- 브라우저에서 이미지 처리 코드를 수정할 때:
  - “파일 포맷 bytes” 대신 “RawToken/Raw RGBA 캐시”가 흘러갈 수 있음을 항상 고려합니다.
  - Raw 캐시 miss 시 동작(예외/메시지)을 깨지지 않게 유지합니다.
- 성능:
  - 무거운 변환은 UI thread를 막지 않도록 `Task.Run` 패턴을 유지합니다(예: `SharedUI/Pages/Editor.razor`).

## 코딩 스타일
- 불필요한 리팩터링/포맷 변경은 피하고, 기존 패턴(서비스 주입, MudBlazor 컴포넌트 사용)을 따릅니다.
- `Mat` 등 `IDisposable` 객체는 반드시 `using`으로 정리합니다(네이티브 경로).
