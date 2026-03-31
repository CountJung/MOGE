---
applyTo: "**/*.cs,**/*.razor,**/*.razor.cs,**/*.js"
---

# MOGE 이미지 처리 & JS Interop 지침

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

### 이미지 처리 코드 수정 시 주의사항
- "파일 포맷 bytes" 대신 "RawToken/Raw RGBA 캐시"가 흘러갈 수 있음을 항상 고려합니다.
- Raw 캐시 miss 시 동작(예외/메시지)을 깨지지 않게 유지합니다.

### 성능: CPU-bound 이미지 연산
- 무거운 변환/필터/마스크 생성/썸네일 생성 등 **CPU-bound 이미지 연산은 UI thread를 막지 않도록 `Task.Run`(또는 동등한 백그라운드 실행)으로 감싸서 처리**합니다.
- 처리 중에는 사용자가 "멈춘 것처럼" 느끼지 않도록 **중앙 Progress ring**을 표시합니다.
  - 기준 구현: `SharedUI/ViewModels/EditorViewModel.IsProcessing` (카운터 기반) + `SharedUI/Pages/Editor.razor`의 `MudOverlay + MudProgressCircular`.
  - 새 이미지 연산을 추가할 때는 ViewModel에서 공통 헬퍼(예: `RunImageCpuAsync`)를 통해 실행하고, UI는 `IsProcessing`에 바인딩합니다.

---

## JS Interop / Canvas

- `SharedUI/wwwroot/moge-canvas.js`는 `window.mogeCanvas`를 제공하며 `setImage`, `setRawRgba`, `draw`, `clear` 등을 담당합니다.
- Web/Hybrid 호스트에는 `moge-canvas-shim.js`가 존재하며, SharedUI 정적 자산이 오래된 캐시로 로드되어 `setRawRgba`가 없을 때를 대비한 **호환성 shim**입니다.
- 새 JS 기능을 추가할 때는:
  1) `SharedUI/wwwroot/moge-canvas.js`에 구현
  2) 필요 시 `WebApp/wwwroot/moge-canvas-shim.js`, `HybridApp/wwwroot/moge-canvas-shim.js`도 동일 시그니처로 보완
  3) Blazor에서는 `IJSRuntime` 호출부를 `SharedUI` 컴포넌트 쪽에 두는 것을 우선합니다.
