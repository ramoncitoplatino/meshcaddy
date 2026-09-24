# MeshCaddy

MeshCaddy is a lightweight 3D model browser for 64-bit Windows. It lets you open a folder, move quickly through its model files, inspect geometry and saved material colors, and organize the files without opening a slicer or CAD application.

## Features

- Native binary and ASCII STL loading
- Native 3MF loading, including build transforms and supported material/color resources
- Assimp-based loading for more than 40 additional mesh and interchange formats
- Folder-wide background preview caching with a RAM-aware limit
- Model dimensions and triangle count
- Orbit, zoom, and automatic camera fitting
- Search and sorting by name, modified date, created date, size, or file type
- Modified date descending as the default sort order
- Light, dark, and Windows system themes
- Previous/next browsing with the keyboard
- Multi-selection checkboxes for moving or recycling several files
- Rename, copy, move, recycle, create-folder, refresh, and Show in Explorer commands
- Folder and model drag-and-drop
- Command-line and Windows file-association opening

Common supported formats include 3MF, STL, OBJ, PLY, FBX, glTF/GLB, DAE, 3DS, DXF, OFF, X, X3D, IFC, and AMF. The complete extension list is maintained in `Services\ModelLoader.cs`.

Native CAD documents such as STEP, IGES, SolidWorks, and Fusion 360 project files contain CAD geometry rather than display-ready meshes. Export or convert them to 3MF, STL, OBJ, or glTF before opening them in MeshCaddy.

## System requirements

- Windows 10 or Windows 11, 64-bit
- A graphics adapter supported by WPF

The release installer contains the .NET runtime. People installing a release do **not** need the .NET SDK or a separate runtime.

## Install a release

1. Open the `Releases` folder.
2. Run the newest `MeshCaddy-Setup-<version>-win-x64.exe` file.
3. Follow the setup wizard.
4. Optionally enable the desktop shortcut and STL/3MF file associations.
5. Launch MeshCaddy from the Start menu or desktop shortcut.

The installer installs MeshCaddy for the current Windows user under `%LocalAppData%\Programs\MeshCaddy`. Administrator access is not required.

To uninstall it, open **Settings > Apps > Installed apps**, find **MeshCaddy**, and choose **Uninstall**.

## Use MeshCaddy

1. Select **Open folder** or press `Ctrl+O`.
2. Choose a folder containing 3D model files.
3. Select a file in the left pane to display it.
4. Drag over the preview with the left mouse button to orbit and use the mouse wheel to zoom.

MeshCaddy starts loading previews in the background after opening a folder. The cache uses 25% of currently available physical memory, with a minimum budget of 256 MB and a maximum of 4 GB. If the folder is larger than that budget, uncached files are loaded when selected and older cached previews are removed as needed.

### Keyboard and mouse controls

| Control | Action |
| --- | --- |
| `Ctrl+O` | Open a folder |
| `Left` / `Right` | Show the previous or next model |
| `F5` | Refresh the current folder |
| `Delete` | Move the selected model to the Recycle Bin |
| Left-button drag | Orbit the model |
| Mouse wheel | Zoom |
| Double-click preview | Fit the model in the view |

You can also drop a supported file or a folder anywhere on the MeshCaddy window.

## Build and run from source

### Prerequisites

Install:

- [Git for Windows](https://git-scm.com/download/win), if obtaining the source from Git
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Clone from GitHub

Using Git over HTTPS:

```powershell
git clone https://github.com/ramoncitoplatino/meshcaddy.git
cd meshcaddy
```

Using the [GitHub CLI](https://cli.github.com/):

```powershell
gh repo clone ramoncitoplatino/meshcaddy
cd meshcaddy
```

To download the source without Git:

1. Open the [MeshCaddy GitHub repository](https://github.com/ramoncitoplatino/meshcaddy).
2. Select **Code** and then **Download ZIP**.
3. Extract the downloaded ZIP file.
4. Open PowerShell in the extracted `meshcaddy` folder.

After cloning or extracting the source, continue with the build commands below.

Open PowerShell in the repository root and restore and build the project:

```powershell
dotnet restore .\MeshCaddy.csproj
dotnet build .\MeshCaddy.csproj
```

Run the development build:

```powershell
dotnet run --project .\MeshCaddy.csproj
```

## Publish a standalone application from source

The following command produces a self-contained 64-bit application that does not require .NET on the destination computer:

```powershell
dotnet publish .\MeshCaddy.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

The resulting executable is located at:

```text
bin\Release\net8.0-windows\win-x64\publish\MeshCaddy.exe
```

Copy the published folder to another 64-bit Windows computer or run `MeshCaddy.exe` directly.

## Build and install the Windows installer from source

Install these build tools first:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php)

From PowerShell in the repository root, choose a version number and run:

```powershell
.\build-release.ps1 -Version "1.0.0"
```

The script publishes a compressed, self-contained `win-x64` application into `artifacts\publish` and compiles its installer into `Releases`.

Install the package through the setup wizard:

```powershell
.\Releases\MeshCaddy-Setup-1.0.0-win-x64.exe
```

For a quiet current-user installation:

```powershell
Start-Process `
  -FilePath ".\Releases\MeshCaddy-Setup-1.0.0-win-x64.exe" `
  -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" `
  -Wait
```

Replace `1.0.0` in these examples with the version passed to `build-release.ps1`.

## Project layout

| Path | Purpose |
| --- | --- |
| `MainWindow.xaml` / `.cs` | Main browser interface and folder/file commands |
| `Services\StlParser.cs` | Binary and ASCII STL parser |
| `Services\ThreeMfParser.cs` | 3MF geometry, transform, and color parser |
| `Services\AssimpModelLoader.cs` | Additional model-format importer |
| `Services\ModelLoader.cs` | Import routing and supported extensions |
| `Assets\Brand` | MeshCaddy icons and brand assets |
| `installer\MeshCaddy.iss` | Inno Setup installer definition |
| `build-release.ps1` | Self-contained publish and installer build script |
| `Releases` | Generated Windows installers |

## Troubleshooting

- If a model opens without color, confirm that the source format actually stores material or vertex colors. STL normally contains geometry only, and nonstandard STL color extensions are not portable.
- If a model cannot be displayed, try exporting it as 3MF, STL, OBJ, or glTF from its original application.
- If MeshCaddy cannot start, review `%LocalAppData%\MeshCaddy\crash.log` for the recorded exception.
- If package restore reports that NuGet cannot be reached, verify internet access to `https://api.nuget.org` and run `dotnet restore` again.

## Third-party software

MeshCaddy uses StirlingLabs.Assimp.Net and its native Assimp components for additional file formats. See `THIRD_PARTY_NOTICES.txt` for attribution and license information.
