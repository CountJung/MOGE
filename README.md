# MOGE - MAUI (Blazor) OpenCV GIMP like Editor

MAUI Blazor Hybrid + Blazor WebAssembly로 동작하는 간단한 이미지 편집기(에디터) 프로젝트입니다.

- **SharedUI (Razor Class Library)**: 페이지/UI/이미지 처리 로직의 중심
- **HybridApp (MAUI Blazor Hybrid)**: 네이티브(Windows 등) 호스트
- **WebApp (Blazor WASM)**: 브라우저 호스트

## 🌐 온라인 데모

WebApp은 GitHub Pages에 자동으로 배포됩니다:
- **https://countjung.github.io/MOGE/**

배포 설정 및 상세 정보는 [DEPLOYMENT.md](DEPLOYMENT.md)를 참고하세요.

## 핵심 아이디어

### 브라우저(WASM)에서의 이미지 처리 제약과 우회

브라우저(WASM) 런타임에서는 OpenCV의 디코드/인코드(코덱/엔코더) 경로가 제약을 받기 쉬워, 이 레포는 가능한 한 **브라우저가 디코드한 Raw RGBA를 캐시**하고 **RawToken을 반환/전달**하는 방식으로 파이프라인을 유지합니다.

- Web에서 파일 선택 시, JS가 이미지 파일을 디코드해 RGBA 바이트를 만들고
- C#에서 시그니처(signature) 키로 RGBA를 LRU 캐시에 보관하고
- 이후 변환/필터 결과도 가능하면 인코딩된 PNG/JPEG 대신 **RawToken(byte[])** 으로 돌려서 캔버스에 바로 렌더링합니다.

### 네이티브(MAUI) 경로

네이티브에서는 OpenCvSharp의 Mat 기반 처리/인코딩 경로를 사용합니다.

## 폴더 구조

- `SharedUI/`
  - `Components/`: Canvas 렌더링 컴포넌트
  - `Services/`: 이미지 처리(`ImageProcessorService`) 등
  - `Services/Raw/`: RawToken/Signature/Raw RGBA 연산
  - `wwwroot/`: JS interop(캔버스 등) 정적 자산
  - `Layout/`: MudBlazor 기반 레이아웃/테마
- `WebApp/`
  - `Program.cs`: DI 구성(브라우저 Raw 캐시 제공자 주입)
  - `Services/`: 브라우저 파일피커/로깅 등
  - `wwwroot/`: index.html 및 JS
- `HybridApp/`
  - `MauiProgram.cs`: DI 구성
  - `Services/`: 네이티브 파일피커/로깅 등

## 로컬 실행

### WebApp 실행

```bash
# Debug (기본 포트: http://localhost:5049)
dotnet run --project WebApp/WebApp.csproj -c Debug
```

VS Code에서는 `.vscode/tasks.json`의 **webapp: run** / `.vscode/launch.json`의 **WebApp: Debug (Chrome)** 설정을 사용합니다.

### HybridApp(Windows) 빌드

```bash
dotnet build HybridApp/HybridApp.csproj -c Debug -f net10.0-windows10.0.19041.0
```

## publish 후 정적 호스팅(Web)

```bash
# publish 산출물의 wwwroot 폴더에서 실행 (예: WebApp/bin/Release/net8.0/publish/wwwroot)
python -m http.server 8080
```

## 코드 규칙 / 작업 규칙

### 공통

- 공용 UI/기능은 **SharedUI 먼저** 구현하고, 플랫폼별 차이는 Host(WebApp/HybridApp)의 DI/서비스로 분기합니다.
- 불필요한 리팩터링/포맷 변경은 피하고, 기존 패턴(서비스 주입, MudBlazor 컴포넌트 사용)을 유지합니다.

### 브라우저(WASM)

- OpenCV 코덱/인코더 의존을 늘리지 않습니다.
- 결과를 파일 포맷 바이트(PNG/JPEG)로 내기보다 **RawToken + 캐시** 경로를 우선합니다.
- Raw 캐시 miss 상황(토큰인데 캐시 없음)은 예외/메시지 처리로 UX가 깨지지 않게 유지합니다.

### 네이티브(MAUI)

- `Mat` 등 `IDisposable` 객체는 반드시 `using`으로 정리합니다.

### JS interop

- `SharedUI/wwwroot/moge-canvas.js`의 `window.mogeCanvas` API를 기준으로 합니다.
- Web/Hybrid 호스트의 `moge-canvas-shim.js`는 캐시/버전 꼬임 대응을 위한 호환 계층이므로, 새 함수 추가 시 동일 시그니처를 유지합니다.

### 접근성

- 아이콘 버튼 등은 `aria-label`을 포함해 MudBlazor 경고가 뜨지 않게 유지합니다.

## 로드맵 (TODO)

- [x] 브러시/지우개 MVP: Tools(좌측 툴바) 선택 + 스트로크 적용(브러시=흰색, 지우개=투명) + 히스토리 기록
- [x] 좌측 툴 바 + 선택(사각형) + 선택영역 블러/샤프닝 도구(MVP, 레이어/마스크 제외)
- [x] 간단한 크롭(Crop) UI + 적용
- [x] 히스토리(Undo/Redo) UX 개선: 현재 스텝 강조/키보드 단축키
- [x] 이미지 저장/내보내기(Web: 다운로드 / App: 파일 저장)
- [ ] 모바일 터치 UX 개선(핀치 줌, 관성 스크롤 등)
- [ ] 성능 개선: 큰 이미지에서 변환 파이프라인 최적화(스케일 프리뷰/타일링 등)
- [ ] 오류/로그 UX: 사용자에게 친절한 오류 메시지 + 로그 파일 내보내기
- [ ] 테스트 추가: Raw RGBA 연산(RgbaImageOps) 단위 테스트

---

AI 에이전트용 상세 규칙은 `.github/copilot-instructions.md`를 참고합니다.
