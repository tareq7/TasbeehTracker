# Modern Tasbeeh Tracker 📿

**Modern Tasbeeh Tracker** is a sleek, minimalist, and lightweight Windows desktop utility designed for tracking daily *dhikr* (remembrance) and *tasbeeh* effortlessly while working.

Built as a frameless, semi-transparent floating orb overlay, the app stays topmost on your screen, integrates with Windows accent colors and Dark/Light theme settings, and responds to global hotkeys without taking focus away from your active work.

---

## ✨ Features

- **🌐 Global Hotkeys:** Increment count from any application or full-screen window (default: `NumPad +`).
- **🎨 Windows Theme Synchronization:** Dynamic background tinting and accent color matching with real-time Windows Light/Dark mode detection.
- **🛸 Floating Orb Interface:** Semi-transparent, frameless circular window with smooth scale bounce animations and glowing interactive feedback.
- **👁️ Hover Controls:** Subtle close (`✕`) and reset (`↺`) actions fade in only when hovered to keep the UI clean.
- **⚙️ Hotkey Customization:** Easily rebind global shortcuts using the interactive settings overlay.
- **🔒 100% Offline & Private:** Zero network requests, zero data collection, and local registry persistence.
- **⚡ Lightweight:** Extremely low RAM and CPU footprint with zero background idle load.

---

## 🏗️ Architecture & Component Overview

The repository is organized into a clean .NET 8 WPF architecture accompanied by a Desktop Bridge packaging project:

```
TasbeehTracker/
├── TasbeehTracker/                 # Core WPF Desktop Application
│   ├── MainWindow.xaml             # XAML layout, circular clip, glow animations, controls overlay
│   ├── MainWindow.xaml.cs          # Win32 P/Invoke interop, hotkeys, theme & registry logic
│   ├── App.xaml / App.xaml.cs      # WPF Application entry point & resource dictionaries
│   ├── AssemblyInfo.cs             # Versioning and assembly attributes
│   └── Design 4.ico                # Application icon
├── PackagingProject/               # Windows Application Packaging Project (WAP / MSIX)
│   ├── Package.appxmanifest        # MSIX package capabilities (runFullTrust), entry point, visual assets
│   ├── Package.StoreAssociation.xml# Microsoft Store publisher & identity mapping
│   └── Images/                     # Store tiles, badges, splash screens, and logos
├── Modern-Tasbeeh-Tracker-listing/ # Store listing metadata & localized descriptions
├── LICENSE                         # MIT License
└── TasbeehTracker.sln              # Visual Studio Solution File
```

### Key Technical Implementations

1. **Global Hotkeys via Win32 Interop:**
   Uses `user32.dll` `RegisterHotKey` and `UnregisterHotKey` alongside `HwndSource` message hooks (`WM_HOTKEY`) to intercept user keystrokes globally across Windows.
2. **Adaptive Windows Themeing:**
   Queries the Windows Registry (`HKCU\Software\Microsoft\Windows\DWM` and `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`) for system accent color and dark mode preferences. Listens to `WM_SETTINGCHANGE` for real-time theme updates.
3. **Local Preference Storage:**
   Persists custom key combinations and preferences to `HKCU\Software\TasbeehTracker`.

---

## 📋 Prerequisites & Dependencies

To build or run Modern Tasbeeh Tracker locally, ensure you have:

- **Operating System:** Windows 10 (Version 1809 / Build 17763 or higher) or Windows 11.
- **SDK / Runtime:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (with Desktop Development workload).
- **IDE (Recommended):** [Visual Studio 2022](https://visualstudio.microsoft.com/) with **.NET desktop development** and **Desktop development with C++** (for Windows Application Packaging).

---

## 🚀 Quick Start & Local Execution

### Option A: Using the .NET CLI

1. **Clone the repository:**
   ```bash
   git clone https://github.com/tareq7/TasbeehTracker.git
   cd TasbeehTracker
   ```

2. **Build and run the WPF application:**
   ```bash
   dotnet run --project TasbeehTracker/TasbeehTracker.csproj
   ```

### Option B: Using Visual Studio 2022

1. Open `TasbeehTracker.sln` in Visual Studio 2022.
2. Set `TasbeehTracker` (or `PackagingProject`) as the Startup Project.
3. Select `Debug` or `Release` and press **F5** or click **Start**.

---

## 🛠️ Important Commands & Packaging

### Building the Core WPF App
```bash
dotnet build TasbeehTracker/TasbeehTracker.csproj -c Release
```

### Publishing Standalone Executables
```bash
dotnet publish TasbeehTracker/TasbeehTracker.csproj -c Release -r win-x64 --self-contained false
```

### Packaging for Microsoft Store (MSIX Bundle)
To build multi-architecture app packages (`x86`, `x64`, `ARM64`) via MSBuild:
```bash
msbuild PackagingProject/PackagingProject.wapproj /p:Configuration=Release /p:Platform=x64 /p:AppxBundlePlatforms="x86|x64|ARM64" /p:AppxBundle=Always
```

---

## 🔧 Configuration & Customization

All settings are managed seamlessly within the application UI and saved to the local Windows Registry:

- **Hotkey Rebinding:** Click **Change Hotkey** in the app overlay or press key combinations while in configuration mode.
- **Registry Key Location:** `HKEY_CURRENT_USER\Software\TasbeehTracker`
  - `HotkeyKey`: Integer representation of the virtual key code.
  - `HotkeyModifiers`: Flags for `Ctrl`, `Alt`, `Shift`, or `Win`.

---

## ❓ Troubleshooting

- **Global Hotkey fails to register:** Another running application (e.g. gaming overlay or utility) may already be using `NumPad +`. Use the settings overlay to assign a different key combination (e.g., `Ctrl+Alt+T`).
- **MSIX Architecture Warning (MSB3270):** If building the packaging project, set `Platform` to `x64` or `x86` instead of `AnyCPU`.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE) - feel free to use, modify, and distribute.
