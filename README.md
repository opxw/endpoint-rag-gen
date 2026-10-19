# Text RAG Generator

Tool Windows .NET 10 untuk mengubah plain text, Markdown, atau HTML menjadi paket knowledge RAG yang siap diindeks oleh vector database dan diimpor ke AI Workflow Bot.

## Prinsip

- Konten khusus teks; tidak membaca OpenAPI dan tidak membuat endpoint.
- Tidak memakai AI sehingga tidak mengarang intent, kebijakan, parameter, atau relasi.
- Heading Markdown menjadi judul chunk; paragraf panjang dipecah secara deterministik tanpa pemotongan diam-diam.
- Setiap chunk memiliki source, ID stabil, dan SHA-256.
- Paket terlalu besar ditolak dan harus dipecah menjadi beberapa paket.

## Output

`rag-package.zip` berisi:

- `rag-chunks.jsonl` — sumber utama embedding/vector database.
- `rag-knowledge.md` — versi baca manusia.
- `rag-system-prompt.md` — aturan grounding text-only.
- `provenance.json` — asal sumber, chunk ID, dan hash.
- `manifest.json` — hash integritas paket.
- `api-rag-knowledge.json` — marker kompatibilitas dengan `documents: []`; tidak berisi endpoint.
- `README-RAG.md` dan `PROMPT-PENGGUNAAN.md` — panduan opsional.

## Penggunaan

1. Jalankan `TextRagGen.exe`.
2. Pilih file `.txt`/`.md`/`.html`/`.htm` atau tempel teks dan beri nama sumber.
3. Periksa preview chunk.
4. Klik **Ekspor paket RAG**.
5. Import `rag-package.zip` pada **Sumber pengetahuan** di AI Workflow Bot.

## HTML menjadi teks biasa

- **Pilih file teks/HTML**: file `.html`/`.htm` otomatis dikonversi ketika diimpor, lalu muncul sebagai teks biasa dalam daftar sumber dan preview. Nama file asal tetap disimpan sebagai nama sumber.
- Untuk HTML yang ditempel, gunakan menu atas **Format → HTML ke teks biasa**, periksa hasil di editor, lalu klik **Tambahkan teks/Perbarui teks** agar hasil masuk ke paket RAG.
- Konversi mempertahankan teks heading/paragraf, baris baru, daftar, sel tabel (dipisahkan tab), indentasi `pre`, label tautan, dan teks alternatif gambar. Entity HTML didekode sekali. Script, style, komentar, metadata head, template, iframe, object/embed, serta elemen dengan atribut `hidden` dikeluarkan.
- Pemrosesan lokal memakai [AngleSharp](https://github.com/AngleSharp/AngleSharp) 1.8.5 tanpa menjalankan JavaScript atau mengambil resource/URL. HTML yang membutuhkan rendering JavaScript atau CSS eksternal tidak direkonstruksi; konversi ini mengambil teks struktur HTML, bukan tampilan browser. File dibaca sebagai UTF-8 dengan deteksi BOM.
- Batas impor tetap 10 MB per file; konversi editor maksimal 10 juta karakter. Paket RAG tetap mengikuti batas teks chunker. HTML tanpa teks ditolak sebagai sumber.
- HTML dikonversi hanya pada impor HTML atau melalui menu eksplisit; teks/Markdown dan proyek yang dimuat tidak otomatis diparse ulang, sehingga contoh kode dan entity literal tidak berubah diam-diam.

### Keputusan parser

Parser HTML5 menangani entity dan markup tidak lengkap dengan lebih konsisten dibanding penghapusan tag berbasis regex. Konsekuensinya ada dependency NuGet tambahan yang perlu dipelihara. Versi dipatok; pengujian konversi, simpan/muat, dan ekspor RAG menjadi pemeriksaan regresi saat upgrade. Format proyek tetap versi 1 karena yang disimpan adalah hasil teks biasa.

## Simpan dan muat proyek

- Perintah proyek tersedia pada menu atas **File**: **Muat proyek** (`Ctrl+O`), **Simpan proyek** (`Ctrl+S`), dan **Simpan sebagai…** (`Ctrl+Shift+S`).
- **Simpan proyek** menyimpan nama asisten, domain, semua sumber teks, serta nama dan isi draft editor ke file `.textrag.json`. File sumber asli tidak diperlukan lagi untuk memuat proyek.
- **Muat proyek** memulihkan data dan menghitung ulang preview chunk. **Simpan sebagai…** membuat salinan ke lokasi lain.
- Saat memuat proyek lain atau menutup aplikasi, perubahan yang belum disimpan memunculkan pilihan simpan, abaikan, atau batal.
- File proyek memakai format JSON versi 1 dengan batas 32 MB. File rusak atau versi yang tidak didukung ditolak sebelum mengganti data aktif. Penyimpanan memakai file sementara sebelum mengganti tujuan.
- Draft editor tetap terpisah dari daftar sumber; klik **Tambahkan teks/Perbarui teks** agar draft ikut diekspor ke paket RAG.
- Proyek berisi teks lengkap tanpa enkripsi; simpan di lokasi dengan akses yang sesuai. File proyek digunakan untuk melanjutkan penyuntingan; hasil impor bot tetap `rag-package.zip`.

## Build

Menu atas **Build package** membuat paket RAG dari sumber yang sudah ditambahkan, tanpa dialog pemilihan folder. Hasil berada di `<folder TextRagGen.exe>/package/rag-export-<waktu>-<id>/rag-package.zip` beserta file pendukung. Folder `package` dibuat otomatis; setiap build memakai subfolder baru agar hasil sebelumnya tetap tersedia. Menu aktif setelah ada sumber yang valid. Tombol **Ekspor paket RAG** tetap menyediakan pemilihan folder manual.

```powershell
dotnet run --project tests/TextRag.Verification -c Release
dotnet build EndpointRagGen.slnx -c Release
```

Portable self-contained:

```powershell
.\build-release.ps1
```

ZIP aplikasi hanya membawa EXE dan pemberitahuan lisensi dependency; folder `package` dan file proyek pengguna yang berada di samping EXE tidak ikut dikemas.

Build/test lokal tidak membuktikan kualitas embedding, retrieval vector database, deployment, atau jawaban model live.
