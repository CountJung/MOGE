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

## 전역 설정 (Settings)
- “테마, 패널 기본 동작, 기타 전역 UI 옵션”처럼 **앱 전체에 영향을 주는 설정은 AppBar/개별 페이지에 하드코딩하지 말고** `SharedUI/Pages/Settings.razor`에서 관리합니다.
- 공용 도메인 모델/서비스:
  - `SharedUI/Services/Settings/AppSettings.cs`
  - `SharedUI/Services/Settings/AppSettingsService.cs` (변경 이벤트 `Changed` 제공)
  - `SharedUI/Services/Settings/IAppSettingsStore.cs` (플랫폼별 저장소 추상화)
- 플랫폼별 저장:
  - Web: `SharedUI/wwwroot/moge-settings.js` + `WebApp/wwwroot/index.html`에서 스크립트 로드
  - Hybrid: `HybridApp/wwwroot/index.html`에서 스크립트 로드(웹 호스트와 동일한 정적 자산 규칙 유지)
- 앞으로 “설정이 필요한 항목”을 추가할 때는:
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
- (중요) Blazor UI는 **MVVM 패턴을 사용**합니다.
  - `.razor`는 **마크업 중심**으로 유지하고, 로직은 우선 `.razor.cs`(code-behind)로 이동합니다.
  - 화면/기능 상태는 `SharedUI/ViewModels/*`의 ViewModel로 이동하고(`INotifyPropertyChanged`/`ObservableObject` 기반), 컴포넌트는 ViewModel에 바인딩합니다.
  - ViewModel ↔ 서비스는 DI/생성자 주입으로 연결하고, UI 이벤트는 “ViewModel 메서드/Command”로 위임합니다.
  - 큰 기능 추가 시 **Razor 파일 길이를 늘리지 말고** ViewModel/서비스/컴포넌트로 분리합니다.
- 브라우저에서 이미지 처리 코드를 수정할 때:
  - “파일 포맷 bytes” 대신 “RawToken/Raw RGBA 캐시”가 흘러갈 수 있음을 항상 고려합니다.
  - Raw 캐시 miss 시 동작(예외/메시지)을 깨지지 않게 유지합니다.
- 성능:
  - 무거운 변환/필터/마스크 생성/썸네일 생성 등 **CPU-bound 이미지 연산은 UI thread를 막지 않도록 `Task.Run`(또는 동등한 백그라운드 실행)으로 감싸서 처리**합니다.
  - 처리 중에는 사용자가 “멈춘 것처럼” 느끼지 않도록 **중앙 Progress ring**을 표시합니다.
    - 기준 구현: `SharedUI/ViewModels/EditorViewModel.IsProcessing` (카운터 기반) + `SharedUI/Pages/Editor.razor`의 `MudOverlay + MudProgressCircular`.
    - 새 이미지 연산을 추가할 때는 ViewModel에서 공통 헬퍼(예: `RunImageCpuAsync`)를 통해 실행하고, UI는 `IsProcessing`에 바인딩합니다.

## 코딩 스타일
- 불필요한 리팩터링/포맷 변경은 피하고, 기존 패턴(서비스 주입, MudBlazor 컴포넌트 사용)을 따릅니다.
- `Mat` 등 `IDisposable` 객체는 반드시 `using`으로 정리합니다(네이티브 경로).

## C# Best Practices (AI Agent 가이드)

아래는 AI 에이전트/LLM이 코드를 생성하거나 리팩터링할 때 따라야 할 C# 및 .NET 성능 최적화 규칙입니다.

### 1. Async/Await 패턴 (CRITICAL)

#### 1.1 async void 금지
- 이벤트 핸들러 외에는 `async void` 사용 금지
- 항상 `async Task` 또는 `async ValueTask` 반환

```csharp
// ❌ Bad
private async void LoadDataAsync() { ... }

// ✅ Good
private async Task LoadDataAsync() { ... }
```

#### 1.2 병렬 실행 (Task.WhenAll)
- 독립적인 비동기 작업은 `Task.WhenAll`로 병렬 실행

```csharp
// ❌ Bad - 순차 실행
var user = await GetUserAsync(id);
var orders = await GetOrdersAsync(id);

// ✅ Good - 병렬 실행
var userTask = GetUserAsync(id);
var ordersTask = GetOrdersAsync(id);
await Task.WhenAll(userTask, ordersTask);
```

#### 1.3 .Result / .Wait() 금지
- 데드락 방지를 위해 비동기 코드에서 `.Result` 또는 `.Wait()` 사용 금지
- 항상 `await` 사용

#### 1.4 ValueTask 활용
- 캐시 히트 등 자주 동기적으로 완료되는 메서드는 `ValueTask<T>` 반환
- 단, ValueTask는 한 번만 await 가능

### 2. 메모리 관리 (CRITICAL)

#### 2.1 ArrayPool 사용
- 임시 버퍼는 `ArrayPool<T>.Shared`에서 Rent/Return

```csharp
// ✅ Good
byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
try { /* use buffer */ }
finally { ArrayPool<byte>.Shared.Return(buffer); }
```

#### 2.2 Span<T> / Memory<T> 활용
- 배열 슬라이싱 시 할당 없이 `Span<T>` 사용
- 비동기 메서드에서는 `Memory<T>` 사용

#### 2.3 IDisposable 정리
- **반드시** `using` 선언으로 `IDisposable` 객체 정리
- 특히 `Mat`, `Stream`, `HttpClient` 등

```csharp
// ✅ Good
using var mat = new Mat();
using var stream = new FileStream(path, FileMode.Open);
```

### 3. 컬렉션 성능 (HIGH)

#### 3.1 용량 지정
- 크기를 알 때 컬렉션 생성 시 용량 지정

```csharp
// ✅ Good
var list = new List<int>(1000);
var sb = new StringBuilder(estimatedLength);
```

#### 3.2 적절한 컬렉션 타입
- 빠른 조회: `HashSet<T>`, `Dictionary<TKey, TValue>`
- 순차 접근: `List<T>`
- 정렬 유지: `SortedSet<T>`, `SortedDictionary<K,V>`

#### 3.3 Hot Path에서 LINQ 주의
- 성능 중요 구간에서는 LINQ 대신 for 루프 사용
- `Any()` 대신 `Count > 0` 또는 직접 체크

### 4. 문자열 처리 (MEDIUM)

#### 4.1 StringBuilder 사용
- 루프 내 문자열 연결은 `StringBuilder` 사용

```csharp
// ❌ Bad
string result = "";
for (int i = 0; i < 1000; i++)
    result += i.ToString();

// ✅ Good
var sb = new StringBuilder(4000);
for (int i = 0; i < 1000; i++)
    sb.Append(i);
```

### 5. 동시성 (MEDIUM)

#### 5.1 SemaphoreSlim 사용
- 비동기 동기화에는 `SemaphoreSlim` 사용 (`lock` 대신)

```csharp
private readonly SemaphoreSlim _lock = new(1, 1);

public async Task ProcessAsync()
{
    await _lock.WaitAsync();
    try { /* critical section */ }
    finally { _lock.Release(); }
}
```

#### 5.2 CancellationToken 전파
- 비동기 메서드는 `CancellationToken` 매개변수 수용 및 전파

### 6. 기타 패턴

#### 6.1 record 타입 활용
- DTO, 불변 데이터는 `record` 사용

```csharp
// ✅ Good
public record UserDto(int Id, string Name);
public record struct Point(int X, int Y);
```

#### 6.2 정적 람다
- 캡처 없는 람다는 `static` 키워드 사용

```csharp
// ✅ Good
items.ForEach(static item => Process(item));
```

### 요약 체크리스트

**Critical (필수)**
- ✅ `async void` 금지 (이벤트 핸들러 제외)
- ✅ `Task.WhenAll`로 병렬 실행
- ✅ `.Result` / `.Wait()` 금지
- ✅ `IDisposable` 반드시 `using`으로 정리

**High (권장)**
- ✅ 임시 버퍼에 `ArrayPool` 사용
- ✅ 컬렉션 용량 사전 지정
- ✅ 적절한 컬렉션 타입 선택

**Medium (상황에 따라)**
- ✅ Hot Path에서 LINQ 대신 루프
- ✅ 루프 내 문자열 연결에 `StringBuilder`
- ✅ 비동기 동기화에 `SemaphoreSlim`

