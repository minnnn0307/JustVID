# JustVID

Windows 10/11 x64용 동영상 플레이어. 버전 1.0.0, 이후 Semantic Versioning 적용. 앱 이름과 창 제목에는 버전을 붙이지 않습니다.

## 사용

파일 열기 버튼, Ctrl+O 또는 파일 드롭으로 영상을 엽니다. Space는 재생/일시정지, 좌우 방향키는 5초 이동, M은 음소거, F는 전체 화면, Esc는 전체 화면/영역 선택 해제입니다.

- 재생바 클릭 또는 드래그: 해당 위치로 이동
- 재생바 마우스 오버: 시간과 영상 미리보기
- Ctrl+왼쪽 드래그: 초록 선택 영역 확대 (1픽셀 선택도 허용)
- Ctrl+오른쪽 클릭: 원본 화면 복구
- 오른쪽 아래 모서리 및 창 테두리: 크기 조절. 영상은 종횡비를 유지합니다.
- ◐ 메뉴: 다크/라이트. 볼륨과 테마를 사용자 설정에 저장합니다.

최소 창 크기는 조작을 위한 160×140이며 작은 창에서는 볼륨바, 시간 및 파일명이 순서대로 숨겨집니다. 홍보 문구, 네트워크 요청, 분석 SDK는 없습니다.

## 빌드

Windows 및 .NET 8 SDK:

```powershell
dotnet run --project tests/JustVID.Tests.csproj -c Release
dotnet run --project JustVID/JustVID.csproj
dotnet publish JustVID/JustVID.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
```

설치 프로그램은 Inno Setup 6에서 `installer/JustVID.iss`를 컴파일합니다. GitHub Actions의 Windows build가 기하 계산 테스트, 앱 빌드, 설치 파일 생성을 수행하며 Artifacts에서 결과물을 다운로드할 수 있습니다. 자동 공개 릴리스는 만들지 않습니다.

게시자는 앱 정보 메뉴에서 Windows UI 언어에 따라 `조민혁` / `Minhyeok Cho`로 표시합니다. 설치 프로그램과 Windows 설치 앱 목록의 게시자는 설치 언어에 따라 지정합니다. 실행 파일의 기본 Company 메타데이터는 Minhyeok Cho입니다. 코드 서명 인증서는 포함하지 않습니다.

## 리소스 및 재생 범위

WPF MediaElement와 Windows 기본 미디어 기능을 사용합니다. 별도 웹 런타임이나 코덱 묶음을 포함하지 않습니다. 파일 형식 목록은 선택 필터이며 실제 재생 지원은 Windows에 설치된 코덱에 달려 있습니다. MP4 H.264/AAC를 우선 확인하고, HEVC/MKV/WebM은 시스템에 따라 지원이 다를 수 있습니다. Windows N은 Media Feature Pack이 필요할 수 있습니다.

미리보기는 요청 시에만 보조 디코더를 열고, 180ms 주기로 탐색 요청을 제한하며 마우스가 떠난 뒤 2초 후 해제합니다. 전체 영상의 썸네일을 사전 생성하지 않습니다. 미리보기 표시 속도와 정확도는 코덱/키프레임에 따라 다릅니다. CPU/메모리 사용량은 아직 측정하지 않았습니다.

## Windows 수동 검증

CI의 컴파일 검증 외에 실제 Windows에서 다음을 확인해야 합니다:

1. H.264/AAC 영상의 열기, 재생, 종료 후 재생, 일시정지, 탐색, 음소거 및 파일 교체.
2. 재생/일시정지 중 재생바 미리보기, 빠른 마우스 이동, 미지원 파일 오류, 앱 종료 시 파일 잠금 해제.
3. 레터박스와 세로 영상에서 역방향 및 1픽셀 Ctrl 드래그, 중첩 확대, Ctrl+우클릭 복구, 선택 중 Esc.
4. 작은 창/큰 창, 최대화/전체 화면, 다중 모니터 및 100/150/200% DPI에서 비율과 선택 좌표.
5. 한국어/영어 설치 시 게시자, 앱 아이콘, 테마/볼륨 저장, 설치 및 제거.
6. 작업 관리자에서 1080p/4K 재생 및 미리보기 전후 CPU/GPU/메모리 측정. 하드웨어와 코덱별 결과 기록.
