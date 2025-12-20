# GitHub Pages 배포 가이드

이 문서는 MOGE WebApp을 GitHub Pages에 배포하는 방법을 설명합니다.

## 자동 배포

이 저장소는 GitHub Actions를 통해 자동으로 배포됩니다.

### 배포 트리거

- `main` 브랜치에 푸시할 때 자동으로 배포됩니다
- 또는 GitHub Actions 탭에서 "Deploy WebApp to GitHub Pages" 워크플로를 수동으로 실행할 수 있습니다

### 배포된 사이트 확인

배포가 완료되면 다음 URL에서 WebApp을 확인할 수 있습니다:
- **https://countjung.github.io/MOGE/**

## 배포 구성 상세 내용

### 1. GitHub Actions 워크플로 (`.github/workflows/deploy-webapp.yml`)

워크플로는 다음 단계를 수행합니다:

1. **체크아웃**: 저장소 코드를 가져옴
2. **.NET 설정**: .NET 8.0 SDK 설치
3. **워크로드 설치**: Blazor WebAssembly에 필요한 워크로드 설치
4. **의존성 복원**: NuGet 패키지 복원
5. **빌드 및 퍼블리시**: Release 모드로 WebApp 빌드
6. **.nojekyll 추가**: GitHub Pages의 Jekyll 처리 방지
7. **Base path 수정**: `/MOGE/` 경로로 설정
8. **아티팩트 업로드**: 빌드 결과물 업로드
9. **배포**: GitHub Pages에 배포

### 2. SPA 라우팅 지원

GitHub Pages는 정적 파일 호스팅이므로 SPA(Single Page Application) 라우팅을 위한 설정이 필요합니다:

- **404.html**: 존재하지 않는 경로 접근 시 실행되는 리다이렉트 스크립트
- **index.html**: 404.html에서 전달받은 경로 정보를 복원하는 스크립트

이를 통해 사용자가 직접 URL을 입력하거나 새로고침해도 정상적으로 동작합니다.

### 3. Base Path 설정

GitHub Pages의 프로젝트 페이지는 `username.github.io/repository-name/` 형식의 URL을 사용합니다.
따라서 `<base href="/MOGE/" />`로 설정하여 모든 리소스가 올바른 경로에서 로드되도록 합니다.

## GitHub Pages 설정

저장소에서 GitHub Pages를 처음 설정하는 경우:

1. 저장소의 **Settings** 탭으로 이동
2. 왼쪽 메뉴에서 **Pages** 선택
3. **Source**를 "GitHub Actions"로 선택
4. 설정 저장

## 워크플로 상태 확인

1. 저장소의 **Actions** 탭으로 이동
2. "Deploy WebApp to GitHub Pages" 워크플로 선택
3. 최근 실행 내역에서 빌드 및 배포 상태 확인

## 로컬에서 배포 테스트

로컬에서 배포 결과를 테스트하려면:

```bash
# 프로젝트 빌드 및 publish
dotnet publish WebApp/WebApp.csproj -c Release -o publish

# .nojekyll 파일 추가 (GitHub Pages의 Jekyll 처리 방지)
touch publish/wwwroot/.nojekyll

# base path 업데이트
sed -i 's|<base href="/" />|<base href="/MOGE/" />|g' publish/wwwroot/index.html

# 로컬 서버로 테스트 (Python 3 사용)
cd publish/wwwroot
python -m http.server 8080
```

그런 다음 브라우저에서 `http://localhost:8080/MOGE/`으로 접속합니다.

**참고**: 로컬 테스트 시 base path를 `/MOGE/`로 설정했으므로 반드시 `http://localhost:8080/MOGE/`로 접속해야 합니다.

## 문제 해결

### 404 오류 발생

- GitHub Pages 설정이 올바른지 확인 (Source가 "GitHub Actions"로 설정되어 있는지)
- 워크플로가 성공적으로 완료되었는지 확인
- Actions 탭에서 빌드 로그 확인

### 리소스 로딩 실패 (CSS, JS 파일이 로드되지 않음)

- `index.html`의 base href가 `/MOGE/`로 설정되어 있는지 확인
- `.nojekyll` 파일이 wwwroot에 포함되어 있는지 확인
- 브라우저 개발자 도구(F12)의 Network 탭에서 실패한 리소스의 경로 확인

### 빌드 실패

- Actions 탭에서 워크플로 로그를 확인
- 필요한 .NET 워크로드가 설치되었는지 확인
- `dotnet workload restore` 단계가 성공했는지 확인

### 페이지 새로고침 시 404 오류

- 404.html과 index.html의 SPA 리다이렉트 스크립트가 올바르게 포함되어 있는지 확인
- 워크플로에서 404.html이 wwwroot에 포함되어 배포되었는지 확인

## 배포 프로세스 플로우

```
코드 푸시 (main 브랜치)
    ↓
GitHub Actions 트리거
    ↓
.NET 환경 설정
    ↓
워크로드 설치
    ↓
의존성 복원
    ↓
Release 빌드 및 퍼블리시
    ↓
.nojekyll 추가
    ↓
Base path 수정 (/MOGE/)
    ↓
아티팩트 업로드
    ↓
GitHub Pages 배포
    ↓
배포 완료 (https://countjung.github.io/MOGE/)
```

## 추가 정보

- GitHub Actions 워크플로: `.github/workflows/deploy-webapp.yml`
- 404 리다이렉트: `WebApp/wwwroot/404.html`
- SPA 복원 스크립트: `WebApp/wwwroot/index.html` (head 섹션)
- Base path 설정: 워크플로의 sed 명령으로 자동 수정
