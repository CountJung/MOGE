# 🎨 Project: MOGE - MAUI OpenCV GIMP Editor (GIMP-like Blazor Hybrid Advanced Image Editor)

## 1. 프로젝트 개요 (Project Overview)
이 프로젝트는 **Blazor Hybrid (.NET MAUI)** 기술을 사용하여 단일 C# 코드베이스로 **데스크톱 앱(Windows/macOS), 모바일 앱(iOS/Android), 그리고 웹(WebAssembly)**에서 모두 동작하는 GIMP 유사 이미지 편집기를 개발하는 것입니다.

### 🎯 핵심 목표 (Core Goals)
- **Cross-Platform:** 하나의 프로젝트로 네이티브 앱과 웹사이트를 동시에 구축한다.
- **Client-Side Processing:** 무거운 서버 통신 없이 클라이언트(기기) 리소스를 사용하여 빠르고 끊김 없는 이미지 처리를 수행한다.
- **OpenCV Integration:** **OpenCVSharp**을 활용하여 C# 환경에서 강력한 이미지 필터 및 변형 기능을 구현한다.
- **Responsive UI:** **MudBlazor**를 사용하여 모든 화면 해상도(모바일~데스크톱)에 대응하는 반응형 UI를 구축한다.

---

## 2. 기술 스택 (Tech Stack)

### 🛠️ Framework & Language
- **Framework:** .NET 8.0 (or 9.0 Preview)
- **Platform:** Blazor Hybrid (.NET MAUI) + Blazor WebAssembly (Shared Razor Class Library 활용)
- **Language:** C#
- **IDE:** VS Code (C# Dev Kit extension) or Visual Studio 2022

### 🎨 UI & UX
- **Component Library:** **MudBlazor** (Material Design, 반응형 그리드 시스템)
- **Canvas Control:** `Blazor.Extensions.Canvas` 또는 HTML5 Canvas JS Interop 직접 구현
- **Responsive Design:** MudBlazor Grid (`MudGrid`, `MudItem`) 및 Breakpoints 활용

### 🖼️ Image Processing
- **Library:** **OpenCVSharp4** (OpenCVSharp4.runtime.win, .ubuntu, .wasm 등 플랫폼별 런타임 패키지 관리 중요)
- **Alternatives:** SkiaSharp (OpenCV 보조 또는 경량 작업용)

---

## 3. 아키텍처 설계 (Architecture Design)

### 📂 프로젝트 구조 (Solution Structure)
1.  **SharedUI (Razor Class Library):**
    *   모든 UI 컴포넌트(캔버스, 툴바, 슬라이더), 페이지, 이미지 처리 로직이 포함됨.
    *   앱과 웹 프로젝트가 이 라이브러리를 참조하여 코드를 공유함.
2.  **BlazorHybridApp (.NET MAUI Blazor):**
    *   네이티브 앱 껍데기. `WebView`를 통해 SharedUI를 렌더링.
    *   파일 시스템 접근 등 네이티브 기능 담당.
3.  **BlazorWebApp (Blazor WebAssembly):**
    *   웹 브라우저 배포용.
    *   WASM 상에서 OpenCVSharp 동작 설정 필요.

---

## 4. 기능 명세 (Feature Specifications)

### A. 이미지 입출력 (I/O)
- **File Picker:** `IBrowserFile`(Web)과 MAUI `FilePicker`(App)를 추상화한 서비스 인터페이스 구현.
- **Memory Management:** 대용량 이미지를 효율적으로 처리하기 위해 `RecyclableMemoryStream` 또는 `Span<T>` 활용.

### B. UI 구성 (MudBlazor)
- **Layout:** `MudLayout` 기반.
    - **Drawer:** 좌측 도구 모음 (필터 선택, 도구 아이콘). 모바일에서는 햄버거 메뉴로 숨김.
    - **Main Content:** 중앙 캔버스 영역. 줌/팬(Zoom/Pan) 기능 지원.
    - **Right Panel:** 속성 창 (슬라이더, 세부 설정). 화면이 좁을 경우 하단 시트(Bottom Sheet)나 탭으로 변형.
- **Theme:** System / Light / Dark Mode 기본 지원.

### C. 이미지 처리 (OpenCVSharp)
- **Filters:** Gaussian Blur, Canny Edge, Sepia, Grayscale, Brightness/Contrast 조정.
- **Spatial Transform:** Perspective Transform (4점 원근 변환), Rotate, Resize.
- **Performance:** UI 스레드 차단을 막기 위해 이미지 처리는 `Task.Run` 또는 별도 스레드에서 수행.

---

## 5. 단계별 구현 지시 (Step-by-Step Implementation Guide)
*AI 에이전트는 아래 순서대로 작업을 진행해야 합니다.*

### ✅ Step 1: 솔루션 및 프로젝트 세팅 (Setup)
1.  VS Code 터미널에서 `dotnet new sln`으로 솔루션 생성.
2.  `dotnet new razorclasslib -n SharedUI` (공통 로직).
3.  `dotnet new maui-blazor -n HybridApp` (앱).
4.  `dotnet new blazorwasm -n WebApp` (웹).
5.  프로젝트 참조 연결: HybridApp -> SharedUI, WebApp -> SharedUI.
6.  **MudBlazor** 패키지 설치 및 `_Imports.razor`, `MainLayout.razor` 설정.

### ✅ Step 2: 반응형 레이아웃 구현 (UI Layout)
1.  SharedUI에 `MudThemeProvider`, `MudDialogProvider` 설정.
2.  `MainLayout.razor`에 `MudLayout`, `MudAppBar`, `MudDrawer` 배치.
3.  화면 크기(`Breakpoint`)에 따라 Drawer가 열림(Desktop)/닫힘(Mobile) 상태가 되도록 로직 구현.

### ✅ Step 3: 캔버스 및 이미지 로더 (Canvas & Loader)
1.  HTML5 `<canvas>` 요소를 감싸는 Blazor 컴포넌트 생성.
2.  `InputFile` 컴포넌트와 MudBlazor 버튼을 사용하여 이미지를 업로드하고 `byte[]`로 변환하여 메모리에 로드.
3.  JS Interop을 통해 캔버스에 이미지를 그리는 기능 구현 (Zoom/Pan 기능 포함).

### ✅ Step 4: OpenCVSharp 연동 (Image Engine)
1.  **OpenCVSharp4** 및 **OpenCVSharp4.runtime.wasm**(웹용) 패키지 설치.
2.  이미지 처리 서비스 클래스(`ImageProcessorService.cs`) 생성.
3.  `byte[]` 이미지를 `Mat` 객체로 변환 -> OpenCV 연산 수행 -> 다시 `byte[]` 또는 Base64로 변환하여 리턴하는 메서드 작성.

### ✅ Step 5: 필터 및 변형 기능 구현 (Features)
1.  **기본 필터:** MudBlazor 슬라이더(`MudSlider`) 값 변경 시 OpenCV `Blur`, `Canny` 등을 적용하여 캔버스 갱신.
2.  **공간 변형:** 캔버스 위에 4개의 핸들(원)을 오버레이로 렌더링. 핸들 좌표를 OpenCV `GetPerspectiveTransform` -> `WarpPerspective`에 전달하여 이미지 변형.

### ✅ Step 6: 최적화 및 배포 준비 (Optimization)
1.  **Debounce:** 슬라이더 이동 시 연산이 너무 잦지 않도록 디바운스 처리.
2.  **WASM 최적화:** 웹 어셈블리 로딩 속도 개선 (AOT 컴파일 고려).
3.  **App 권한:** 모바일 앱의 파일 접근/저장 권한 설정 (Android Manifest, Info.plist).

---

## 6. AI 에이전트 작업 지침 (Instructions for AI)
1.  **MudBlazor 활용:** UI 코드를 작성할 때는 항상 MudBlazor 컴포넌트를 최우선으로 사용하라. (CSS 직접 작성 지양)
2.  **플랫폼 호환성:** 코드를 작성할 때 이것이 Web(WASM)과 Native(MAUI) 양쪽에서 돌아가는지 항상 체크하라. (특히 파일 입출력 부분에서 추상화 인터페이스 사용 필수)
3.  **OpenCV 메모리 관리:** `Mat` 객체는 `IDisposable`이므로 `using` 문을 사용하여 메모리 누수를 방지하라.
4.  **반응형 대응:** `MudGrid` 시스템을 적극 활용하여 모바일 뷰와 데스크톱 뷰를 동시에 만족시켜라.

## 7. License - MIT