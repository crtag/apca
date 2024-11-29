# Audio Capture App

A Windows application that captures system audio output and microphone input simultaneously into a stereo WAV file.

## Features

- Captures system audio output (left channel)
- Captures microphone input (right channel)
- Device selection for both input and output
- Real-time monitoring of audio devices
- Saves to a single stereo WAV file
- Self-contained executable (no installation required)

## Download

Download the latest release from the [Releases](../../releases) page.

## Building from Source

### Prerequisites

- GitHub account (for Codespaces)
- Or local installation of:
  - Visual Studio 2022 or
  - .NET 6.0 SDK

### Building with GitHub Codespaces

1. Click the "Code" button above
2. Select "Open with Codespaces"
3. Wait for the environment to set up
4. Open terminal and run:
   ```bash
   dotnet publish src/apca/apca.csproj -c Release
   ```
5. Find your executable in `src/apca/bin/Release/net6.0-windows/win-x64/publish/apca.exe`

### Building Locally

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/apca.git
   ```
2. Open `apca.sln` in Visual Studio 2022 or run:
   ```bash
   dotnet publish src/apca/apca.csproj -c Release
   ```

## Usage

1. Run the executable
2. Select desired output device (speakers/headphones)
3. Select desired input device (microphone)
4. Click "Start Recording"
5. Both system audio and microphone will be recorded
6. Click "Stop Recording" when done
7. Find the output file in the same directory as the executable

## Output Format

- 44.1kHz, 16-bit stereo WAV
- Left channel: System audio output
- Right channel: Microphone input

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.