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

## 문제 해결

### 404 오류 발생

- GitHub Pages 설정이 올바른지 확인 (Source가 "GitHub Actions"로 설정되어 있는지)
- 워크플로가 성공적으로 완료되었는지 확인

### 리소스 로딩 실패

- `index.html`의 base href가 `/MOGE/`로 설정되어 있는지 확인
- `.nojekyll` 파일이 wwwroot에 포함되어 있는지 확인

### 빌드 실패

- Actions 탭에서 워크플로 로그를 확인
- 필요한 .NET 워크로드가 설치되었는지 확인
