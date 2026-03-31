---
applyTo: "**/*.razor,**/*.razor.cs"
---

# MOGE Blazor UI 패턴 지침

## Blazor UI 바인딩 구조

Blazor UI는 **바인딩 구조를 선호**합니다.

- `.razor`는 **마크업 중심**으로 유지하고, 로직은 우선 `.razor.cs`(code-behind)로 분리합니다.
- Blazor 표준 바인딩(`@bind`, `EventCallback`, 파라미터 단방향/양방향 바인딩)을 우선 적용합니다.
- 상태와 로직이 복잡한 페이지(예: Editor, Settings)는 `SharedUI/ViewModels/*`에 ViewModel을 두고 `ViewModelComponentBase<T>`로 바인딩할 수 있습니다. 단, **모든 컴포넌트에 ViewModel을 강제하지 않습니다**.
- 리프 컴포넌트(예: EditorHeader, EditorToolBar, EditorRightPanel)는 `[Parameter]` + `EventCallback` 패턴으로 충분하며, ViewModel 없이 코드-비하인드에서 직접 처리해도 됩니다.
- 큰 기능 추가 시 **Razor 파일 길이를 늘리지 말고** 코드-비하인드/서비스/컴포넌트로 분리합니다.

## 컴포넌트 분리 원칙

- 페이지가 복잡해지면 `.razor`의 `@code` 블록 안에서 큰 `RenderFragment`를 직접 구성하지 말고, **별도 컴포넌트(.razor) 파일로 분리**한 뒤 파라미터/콜백으로 연결합니다(예: `SharedUI/Pages/EditorRightPanel.razor`).
- 공용 UI/기능은 **먼저 SharedUI에 구현**하고, 플랫폼별 차이는 Host(WebApp/HybridApp)에서 DI/서비스로 분기합니다.

## 코딩 스타일

- 불필요한 리팩터링/포맷 변경은 피하고, 기존 패턴(서비스 주입, MudBlazor 컴포넌트 사용)을 따릅니다.
