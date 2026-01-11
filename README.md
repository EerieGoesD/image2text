# Image 2 Text - Windows Desktop App (WPF)

Native Windows application for converting images to text using OCR.

## 🚀 Build & Run

### Requirements:
- Visual Studio 2022 (Community edition is free)
- .NET 8.0 SDK

### Steps:

1. **Open in Visual Studio:**
   - Double-click `Image2Text.csproj`
   - Or: File → Open → Project/Solution → Select `Image2Text.csproj`

2. **Download Tesseract Language Data:**
   - Create folder: `tessdata` in project root
   - Download `eng.traineddata` from: https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata
   - Place it in `tessdata/` folder

3. **Build:**
   - Press F5 (or click "Start")
   - App will build and run

4. **Publish for Microsoft Store:**
   ```
   Right-click project → Publish → Create App Packages
   → Microsoft Store → Follow wizard
   ```

## 📦 Alternative: Command Line Build

```bash
# Restore packages
dotnet restore

# Build
dotnet build --configuration Release

# Run
dotnet run

# Publish standalone exe
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## ✨ Features

- **Screen Area Capture** - Click and drag ANY area of your screen (works outside app)
- **Upload Images** - Supports JPG, PNG, BMP, GIF, TIFF
- **Extract Text** - Uses Tesseract OCR
- **Copy to Clipboard**
- **Save as TXT file**
- Fully native Windows app

## 📁 Project Structure

```
Image2Text/
├── Image2Text.csproj       - Project file
├── App.xaml                - App definition
├── App.xaml.cs             - App code-behind
├── MainWindow.xaml         - Main UI
├── MainWindow.xaml.cs      - Main logic
├── ScreenCaptureWindow.xaml - Fullscreen capture UI
├── ScreenCaptureWindow.xaml.cs - Capture logic
└── tessdata/
    └── eng.traineddata     - OCR language data (download this)
```

## 🔧 How Screen Capture Works

1. Click "Capture Screen Area"
2. App minimizes, captures entire screen
3. Fullscreen overlay appears
4. Click and drag to select area
5. Selected area is cropped and ready for OCR

**This works ANYWHERE on screen** - not limited to app window!

## 🏪 Microsoft Store Submission

### Option 1: Visual Studio (Easiest)
1. Right-click project → Publish
2. Create App Packages
3. Microsoft Store
4. Follow the wizard
5. Upload to Partner Center

### Option 2: MSIX Packaging Tool
1. Build Release version
2. Use MSIX Packaging Tool
3. Package the .exe
4. Submit to Store

## 📝 Notes

- First run needs internet to download Tesseract language data
- After that, works completely offline
- Requires Windows 10 1809 or higher

## 👤 Author

Made by EERIE - https://eeriegoesd.com/
Icon designed by Freepik (www.freepik.com)
