# Taskbar Music Widget

Windows 11 작업표시줄 근처에서 동작하는 미니 음악 컨트롤 위젯입니다.
<br>
<img width="355" height="44" alt="image" src="https://github.com/user-attachments/assets/d1031917-ecfb-4eaf-89e6-2cb6826e640e" />


이 프로젝트는 다음 기능을 목표로 합니다.

- 이전곡, 재생/일시정지, 다음곡 버튼 제공
- 시스템 트레이 아이콘 상주
- 작업표시줄 우측 근처 자동 위치 보정
- 전체화면 앱 실행 시 자동 숨김 또는 Z-order 보정
- 마우스 휠로 현재 재생 앱 세션의 음량만 조절

현재 구현된 주요 동작:

- MDL2 아이콘 기반 컨트롤 버튼(이전/재생-일시정지/다음)
- 재생 상태에 따른 토글 아이콘 자동 전환
- 위젯 툴팁으로 현재 재생 중인 앱 세션/곡명/아티스트 표시
- 마우스 휠로 현재 재생 중인 앱 세션의 음량만 조절
- 전경 윈도우 이벤트 기반 전체화면 감지 + 타이머 백업 감지

## Tech Stack

- .NET 8
- WPF
- Windows Media Control API
- NAudio

## Project Structure

- TaskbarMusicWidget/: WPF 앱 본체
- TaskbarMusicWidget.sln: 솔루션 파일

## Getting Started

1. 프로젝트 폴더로 이동
2. 빌드
3. 실행

PowerShell 예시:

dotnet build .\TaskbarMusicWidget\TaskbarMusicWidget.csproj
dotnet run --project .\TaskbarMusicWidget\TaskbarMusicWidget.csproj

또는 TaskbarMusicWidget 폴더에서:

dotnet build
dotnet run

## Runtime Smoke Test

아래 순서대로 점검하면 최근 추가 기능을 빠르게 검증할 수 있습니다.

### 1) 기본 실행

1. 앱 실행 후 작업표시줄 근처에 위젯이 표시되는지 확인
2. 트레이 아이콘이 보이는지 확인
3. 트레이 메뉴 Show/Hide/Exit 동작 확인

### 2) 미디어 제어

1. Spotify, 브라우저(YouTube), Discord 중 1개 이상에서 재생 시작
2. 위젯의 이전/재생-일시정지/다음 버튼 동작 확인
3. 재생/일시정지 시 토글 아이콘이 즉시 바뀌는지 확인

### 3) 툴팁 메타데이터

1. 위젯에 마우스를 올려 툴팁 표시
2. 현재 재생 중인 앱 이름, 곡명, 아티스트가 의도대로 표시되는지 확인
3. 곡 전환 후 툴팁이 최신 정보로 갱신되는지 확인

### 4) 음량 조절

1. 음악 앱에서 재생 시작
2. 위젯 위에서 마우스 휠을 위/아래로 스크롤
3. 시스템 전체 음량이 아니라 해당 앱 세션 음량만 변하는지 확인
4. 다른 앱의 소리가 영향을 받지 않는지 확인

### 5) 전체화면 동작

1. 전체화면 게임/영상 진입 시 위젯 자동 숨김 확인
2. 전체화면 해제 시 위젯 자동 복귀 확인
3. 반복 전환(진입/해제 여러 번) 시 깜빡임/미복귀 여부 확인

### 6) 회귀 체크

1. 디스플레이 배율 변경 또는 해상도 변경 후 위치 보정 확인
2. 절전/잠금 해제 후 트레이 아이콘 및 위젯 정상 동작 확인
3. 장시간(10분 이상) 실행 후 입력 반응성과 CPU 사용량 이상 유무 확인

## Publish

배포 폴더 생성:

dotnet publish .\TaskbarMusicWidget\TaskbarMusicWidget.csproj -c Release -r win-x64 --self-contained false

출력 위치:

TaskbarMusicWidget\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish
