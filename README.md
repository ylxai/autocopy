# 🎨 IN Studio Files - Professional File Manager

Aplikasi Windows WPF (.NET 6) professional untuk creative studio yang membutuhkan file management solution yang powerful dan efisien.

## ✨ Fitur Utama

### 📁 **Folder Management**
- **Source Folder**: Pilih folder sumber yang akan di-scan (termasuk semua subfolder)
- **Destination Folder**: Pilih folder tujuan untuk menyalin file
- **File List**: Upload file .txt berisi daftar nama file yang ingin disalin

### ⚙️ **Opsi Pencarian**
- **Ignore File Extension**: Cocokkan file hanya berdasarkan nama, abaikan ekstensi
  - Contoh: `photo.jpg` akan cocok dengan `photo.png`, `photo.bmp`, dll.
- **Case-Insensitive Matching**: Abaikan huruf besar/kecil
  - Contoh: `Photo.jpg` akan cocok dengan `PHOTO.JPG`, `photo.jpg`, dll.
- **Duplicate Handling**:
  - **Skip**: Lewati file yang sudah ada di destination
  - **Rename**: Tambahkan nomor urut (file_1.jpg, file_2.jpg, dst)
  - **Overwrite**: Timpa file yang sudah ada

### 📊 **Progress & Statistik Real-time**
- Progress bar dengan persentase
- Timer untuk melacak durasi proses
- Statistik lengkap:
  - ✅ **Berhasil**: Jumlah file yang berhasil disalin
  - 📝 **Total**: Total file dalam list
  - ⏭️ **Dilewati**: File yang dilewati (sudah ada)
  - ❌ **Tidak Ditemukan**: File yang tidak ditemukan di source

### 📋 **Log & Export**
- Log aktivitas real-time dengan timestamp
- Export daftar file yang tidak ditemukan ke `not_found.txt`
- Save log ke file untuk dokumentasi

### 💾 **Configuration Management**
- Save konfigurasi folder dan setting terakhir
- Load konfigurasi secara otomatis saat startup
- Konfigurasi disimpan dalam format JSON

### 🎯 **Fitur Advanced**
- **Drag & Drop**: Drop file .txt langsung ke aplikasi
- **Preview Mode**: Preview file yang akan disalin tanpa melakukan copy
- **Async Operations**: UI tidak freeze saat proses berjalan
- **Scan Rekursif**: Scan semua subfolder di source folder
- **Cancel Operation**: Stop proses kapan saja

## 🚀 Cara Menggunakan

### 1. **Persiapan**
```
1. Siapkan Source Folder yang berisi file-file sumber
2. Siapkan Destination Folder untuk menyimpan hasil copy
3. Siapkan File List (.txt) berisi daftar nama file, satu nama per baris
```

### 2. **Pilih Folder**
- Klik **Browse** untuk Source Folder
- Klik **Browse** untuk Destination Folder
- Klik **Browse** atau **Drag & Drop** file .txt untuk File List

### 3. **Atur Opsi**
- Centang **Ignore File Extension** jika ingin mengabaikan ekstensi
- Centang **Case-Insensitive** untuk matching tanpa memperhatikan huruf besar/kecil
- Pilih **Duplicate Handling** sesuai kebutuhan

### 4. **Preview (Opsional)**
- Klik **🔍 Preview Matches** untuk melihat file mana saja yang akan disalin
- Lihat hasil di log area

### 5. **Start Copy**
- Klik **▶️ Start Copy**
- Monitor progress dan statistik real-time
- Klik **⏹️ Stop** jika ingin membatalkan

### 6. **Export Not Found**
- Setelah selesai, klik **📄 Export Not Found** untuk export daftar file yang tidak ditemukan
- File akan disimpan sebagai `not_found.txt`

## 📝 Format File List

File list harus berformat `.txt` dengan satu nama file per baris:

```txt
photo1.jpg
photo2.jpg
document.pdf
image.png
video.mp4
```

### Catatan:
- Baris kosong akan diabaikan
- Whitespace di awal/akhir akan dihapus otomatis
- Tidak perlu path lengkap, cukup nama file

## 🛠️ Build & Run

### Requirements
- .NET 6.0 SDK atau lebih tinggi
- Visual Studio 2022 atau VS Code
- Windows 10/11

### Build dari Source
```bash
# Clone atau download project
cd AutoCopy

# Restore dependencies
dotnet restore

# Build project
dotnet build

# Run aplikasi
dotnet run
```

### Build Release
```bash
dotnet publish -c Release -r win-x64 --self-contained
```

Output akan ada di: `bin/Release/net6.0-windows/win-x64/publish/`

## 📦 Dependencies

- **.NET 6.0**: Framework utama
- **Newtonsoft.Json**: Untuk save/load konfigurasi
- **Windows Forms**: Untuk FolderBrowserDialog

## 🎨 Screenshot Features

### Main Window
- Modern UI dengan desain clean
- Color-coded statistics
- Real-time progress tracking

### Log Area
- Timestamp untuk setiap aktivitas
- Color emoji untuk status
- Auto-scroll ke bawah

## ⚡ Performance Tips

1. **Large File Lists**: Aplikasi dapat handle ribuan file dengan baik
2. **Network Drives**: Hindari scan network drives yang lambat
3. **SSD**: Untuk performa terbaik, gunakan SSD untuk source dan destination
4. **Preview First**: Gunakan preview mode untuk memastikan matching sudah benar

## 🔧 Troubleshooting

### File tidak ditemukan padahal ada?
- Pastikan opsi **Case-Insensitive** dicentang jika ada perbedaan huruf besar/kecil
- Cek apakah nama file di list sama persis dengan file di source (termasuk spasi)
- Gunakan **Preview Mode** untuk debug

### UI Freeze?
- Seharusnya tidak terjadi karena menggunakan async operations
- Jika terjadi, restart aplikasi

### Access Denied error?
- Jalankan aplikasi sebagai Administrator
- Pastikan folder destination memiliki write permission

## 📄 License

MIT License - Bebas digunakan untuk keperluan pribadi maupun komersial.

## 👨‍💻 Developer Notes

### Struktur Project
```
AutoCopy/
├── AutoCopy.csproj          # Project file
├── App.xaml                 # Application entry point
├── App.xaml.cs              # Application code-behind
├── MainWindow.xaml          # Main UI
├── MainWindow.xaml.cs       # Main logic
├── Models/
│   ├── AppConfig.cs         # Configuration model
│   └── FileOperationResult.cs  # Result model
└── Services/
    └── FileService.cs       # File operations service
```

### Key Technologies
- **WPF**: Modern Windows UI
- **MVVM Pattern**: Separation of concerns
- **Async/Await**: Non-blocking operations
- **Task Parallel Library**: Background processing

## 🎯 Future Enhancements

Fitur yang bisa ditambahkan:
- [ ] Multi-threading untuk copy file lebih cepat
- [ ] Support untuk file patterns (*.jpg, *.png)
- [ ] History panel untuk melihat operasi sebelumnya
- [ ] Dark mode theme
- [ ] Localization (multi-language)
- [ ] Auto-watch folder dan copy otomatis

---

**Dibuat dengan ❤️ oleh IN Studio menggunakan WPF .NET 6**
