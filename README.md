# Taskbar Music Widget

Windows 11 작업표시줄 근처에서 동작하는 미니 음악 컨트롤 위젯입니다.
<br>
<img width="352" height="45" alt="image" src="https://github.com/user-attachments/assets/cea9f2c9-0281-4ca4-a6ca-ca8390ffedcd" />

이 프로젝트는 다음 기능을 목표로 합니다.

- 이전곡, 재생/일시정지, 다음곡 버튼 제공
- 시스템 트레이 아이콘 상주
- 작업표시줄 우측 근처 자동 위치 보정
- 전체화면 앱 실행 시 자동 숨김 또는 Z-order 보정

## Tech Stack

- .NET 8
- WPF
- Windows Media Control API

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

## Publish

배포 폴더 생성:

dotnet publish .\TaskbarMusicWidget\TaskbarMusicWidget.csproj -c Release -r win-x64 --self-contained false

출력 위치:

TaskbarMusicWidget\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish
