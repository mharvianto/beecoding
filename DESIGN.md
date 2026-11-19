# Standar desain BeeCoding

Untuk siapa: pengembang yang menambah atau mengubah layar di `BeeCoding/ClientApp`.
Dokumen ini **mencatat apa yang sudah dipakai di kode** (Tailwind v4, Vue 3). Kalau kode dan dokumen ini berbeda,
perbaiki salah satunya, jangan membuat gaya ketiga.
Contoh yang bisa dilihat dan diklik ada di halaman **`/design`** (`views/Design.vue`): komponen dan kelas aslinya, terang/gelap, termasuk daftar ikon.
Komponen atau kelas baru yang masuk standar ditambahkan juga ke halaman itu. Sebelum membuat komponen atau kelas baru, cari dulu yang sudah ada.

## 1. Prinsip

1. **Satu aksi utama per layar atau baris.** Hanya itu yang berupa tombol terisi (amber). Aksi lain berupa tombol ghost.
2. **Tampilan sama di terang dan gelap.** Setiap warna ditulis berpasangan: `text-slate-600 dark:text-slate-300`.
3. **Pakai yang sudah ada.** Komponen bersama dan kelas di `src/style.css` lebih utama daripada gaya baru.
4. **Aman di layar sentuh dan layar sempit** (lihat bagian 9).
5. **Dua bahasa** untuk halaman dokumen (lihat bagian 10).

## 2. Warna

Tailwind, palet `slate` untuk netral. Satu warna aksen: **amber**.

| Peran | Terang | Gelap |
|---|---|---|
| Latar halaman | `#f8fafc` | `#0b1120` (diatur di `style.css`) |
| Kartu / panel | `bg-white` | `dark:bg-slate-900` |
| Garis kartu | `border-slate-200` | `dark:border-slate-800` |
| Teks utama | `#0f172a` | `#e2e8f0` |
| Teks sekunder | `text-slate-500` | `dark:text-slate-400` |
| Aksen / aksi utama | `bg-amber-500` (teks `text-slate-900`) | sama |
| Aksi sekunder aktif (ghost) | `violet` (`.row-action-btn--accent`) | `violet-400` |
| Sukses | `emerald` | `emerald-400` |
| Bahaya / error | `red-600` untuk teks, `rose` untuk tombol dan menu | `red-400` / `rose-400` |

Aturan:
- **Jangan memakai biru (`sky`/`blue`) untuk tautan atau teks biasa.** Biru hanya muncul di status "running" dan di data grafik.
- Dua warna aksi (amber terisi dan violet ghost) tidak boleh dipakai berdampingan untuk aksi yang berbeda bobotnya: aksi utama amber, sisanya ghost netral.
- Warna selalu dalam kelas Tailwind, jangan heksadesimal di template. Pengecualian: warna data grafik (bagian 7).

## 3. Tipografi

- Font: bawaan sistem (`ui-sans-serif, system-ui, ...`). Tidak ada font web.
- Judul halaman: `text-xl font-bold` (halaman utama boleh `text-2xl`).
- Judul bagian: `font-semibold text-sm`.
- Isi dan kontrol: `text-sm`. Keterangan kecil: `text-xs`; yang paling kecil `text-[11px]` untuk petunjuk, label badge dan waktu. **Jangan lebih kecil dari 11 px.**
- Kode: `font-mono`. Angka yang berubah atau sejajar: `tabular-nums`.
- Label bagian di panel survei: `uppercase tracking-wide text-xs` dengan warna amber.

## 4. Tata letak

- Lebar halaman: `max-w-7xl mx-auto px-4` dengan `py-6` atau `py-8`. Halaman form sempit: `max-w-lg` atau `max-w-2xl`.
- Gutter samping 16 px (`px-4`) di semua lebar. Tidak boleh ada scroll horizontal pada halaman.
- Kartu: `rounded-xl border border-slate-200 dark:border-slate-800 bg-white dark:bg-slate-900 p-4` (`p-5` untuk panel yang lebih lega).
- Sudut: `rounded-lg` untuk tombol, input dan kontrol; `rounded-xl` untuk kartu dan modal; `rounded` untuk badge.
- Daftar dan tabel: pemisah `divide-y divide-slate-100 dark:divide-slate-800`; baris yang bisa diklik atau disorot diberi `hover:bg-slate-50 dark:hover:bg-slate-800/50`.

## 5. Komponen

### Tombol
| Jenis | Kelas |
|---|---|
| **Utama** (satu per layar/baris) | `bg-amber-500 hover:bg-amber-400 text-slate-900 rounded-lg px-4 py-2 text-sm font-medium disabled:opacity-50` (di baris padat `px-3 py-1`, `text-xs`) |
| **Sekunder, di baris/daftar** | `.row-action-btn` (+ `--accent`, `--success`, `--danger`) dari `style.css`. Ikon + label, hit area seragam |
| **Menu "⋮"** | `RowMenu.vue`, isi memakai `.row-menu-item` (+ `--danger`), pemisah `.row-menu-sep` |
| **Bahaya konfirmasi** | `bg-rose-600 hover:bg-rose-700 text-white` (hanya di dialog konfirmasi) |
| **Toolbar kecil berbingkai** | `.tb` |

Tombol yang menonaktifkan dirinya saat bekerja: `disabled:opacity-50` dan teks `…` selama proses.

### Input
`w-full border border-slate-400 dark:border-slate-500 dark:bg-slate-800 rounded-lg px-3 py-1.5` (atau `py-2 text-sm` di form). Setiap input punya `<label>` atau `aria-label`. Dropdown `select` memakai panah dari `style.css`, jangan menggambar sendiri. Input file memakai gaya `style.css`.

### Toggle bersegmen (tab kecil, pilihan 2 sampai 4 opsi)
Wadah: `inline-flex rounded-lg border border-slate-300 dark:border-slate-700 overflow-hidden text-xs`. Opsi aktif: `bg-slate-800 text-white dark:bg-slate-600`, atau `bg-amber-500 text-slate-900` bila itu pilihan utama halaman. Non-aktif: `text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800/60`. Selalu beri `aria-pressed`.

### Badge
- Hasil juri: `VerdictBadge.vue` (AC emerald, WA red, TLE orange, MLE purple, RE rose, CE slate). Jangan membuat badge verdict sendiri.
- Tingkat soal: `LevelBadge.vue` (Easy emerald, Medium amber, Hard rose).
- Badge lain: `text-[11px] font-semibold px-1.5 py-0.5 rounded` dengan pasangan `bg-X-100 text-X-700 dark:bg-X-500/15 dark:text-X-300`.

### Tautan
Tautan di tabel dan daftar tampil sebagai teks biasa, saat disorot: `hover:underline hover:text-amber-600 dark:hover:text-amber-400`. Tautan di kalimat: `text-amber-600` (gelap `dark:text-amber-300`). Tautan "bantuan" kecil: `underline decoration-dotted`.

### Ikon
Hanya `Icon.vue` (garis Lucide, mewarisi warna dan ukuran dari teks). Tambah ikon dengan menaruh path-nya di `PATHS`. **Jangan emoji dan jangan glyph teks** (▾ ▸ ✕ sebagai ikon). Panah buka/tutup: `Chevron.vue`.

### Modal
Dua bentuk, pilih berdasarkan isi:
- **Konfirmasi singkat:** `ConfirmDialog.vue` lewat `useConfirmDialog()`. Jangan memakai `window.confirm`.
- **Isi besar** (kode, diff, detail): acuan `SubmissionView.vue` dan modal di `SubmissionHistory.vue`:
  - lapisan: `fixed inset-0 bg-black/50 flex items-start justify-center p-4 overflow-y-auto z-50`, klik di luar menutup;
  - kotak: `bg-white dark:bg-slate-900 border border-transparent dark:border-slate-800 rounded-xl w-full max-w-3xl my-8 overflow-hidden`, tinggi tetap (`80vh`), editor mengisi penuh tanpa padding;
  - kepala dua baris dengan garis bawah: judul + ✕ di atas, info dan tombol ghost di bawah;
  - `role="dialog" aria-modal="true" aria-label`, `Esc` menutup, tombol tutup menerima fokus saat dibuka;
  - diff lebar: boleh melebar ke `max-w-6xl`.

### Toast
`NotificationToast`, `UndoToast`, `CelebrationToast`. Tindakan yang bisa dibatalkan (hapus) memakai `UndoToast`, bukan dialog.

### Keadaan kosong, memuat, error
- Kosong: satu kalimat `text-sm text-slate-500 dark:text-slate-400`, dengan tautan ke langkah berikut bila ada.
- Memuat: `…` atau `opacity-50` pada daftar yang sudah ada.
- Error: `text-sm text-red-600 dark:text-red-400` dengan `role="alert"`, ditaruh dekat aksi yang gagal.
- Keberhasilan: `role="status"`, teks `emerald`.

## 6. Waktu

Tampilkan waktu relatif ("2 hours ago", `lib/timeAgo.js`) dengan tanggal lengkap pada `title` saat disorot. Elemennya `<time datetime>`. Tanggal tetap lengkap hanya di tempat audit (log, tabel admin).

## 7. Grafik

- Warna data: amber `#f59e0b`, sky `#0ea5e9`, emerald `#10b981`, violet `#8b5cf6`, rose `#f43f5e`, slate `#64748b` (urutan ini).
- Grafik garis/batang: Chart.js (`MiniLineChart.vue`, `TopicBarChart.vue`). Pilihan granularitas waktu disimpan per tampilan di `localStorage` (`beecoding.<view>.engagementGranularity`).
- Donat survei: SVG dengan `pathLength="100"`; segmen terakhir dihitung `100 − sisa` agar tidak ada serpihan.
- Setiap grafik punya `role="img"` + `aria-label`, dan daftar angka di sebelahnya, jangan hanya warna.

## 8. Mode gelap

Mode gelap memakai kelas `.dark` pada `<html>` (`lib/theme.js`), **bukan** `prefers-color-scheme`. Pakai `dark:` seperti biasa. Widget bawaan browser mengikuti `color-scheme` di `style.css`. Pseudo-elemen yang tidak dijangkau `dark:` (mis. tombol pemilih file) ditulis ulang dengan `.dark` di `style.css`.

## 9. Layar sempit dan sentuh

- Uji di 390 px: tidak ada scroll horizontal pada halaman, tombol aksi boleh turun baris (`flex-wrap`).
- Layar sentuh: tinggi tombol minimal 32 px (diatur `@media (pointer: coarse)` di `style.css`). Target yang berupa teks kecil dijadikan tombol dengan padding.
- Teks yang panjang di daftar dipotong (`truncate`) di lebar, dan dibungkus (`line-clamp-2`) di sempit.
- Konten sekunder yang hanya penting di layar lebar disembunyikan dengan `hidden sm:inline`.

## 10. Bahasa

Antarmuka berbahasa Inggris. Halaman dokumen (privasi, syarat, panduan menulis soal, survei) dua bahasa (Inggris dan Indonesia): pilihan di `lib/docLang.js`, tombol `DocLangToggle.vue`, kedua teks ditulis berdampingan dengan `v-if`. **Mengubah satu bahasa berarti mengubah yang lain.**

## 11. Aksesibilitas (minimal)

- Elemen yang bisa diklik adalah `<button>` atau `<a>`/`RouterLink`, bukan `<div @click>` (masih ada beberapa `<div @click>` lama yang belum dibereskan). Baris daftar yang bisa dibuka dibuat `<button>` (contoh: `SubmissionHistory.vue`).
- Tombol hanya berikon: `aria-label` dan `title`.
- Toggle: `aria-pressed`. Bagian yang bisa dilipat: `aria-expanded`.
- Kontras teks 4,5 : 1. Teks sekunder tidak lebih terang dari `slate-500` (terang) / `slate-400` (gelap).
- Status tidak hanya dengan warna: verdict punya singkatan (AC, WA), grafik punya legenda angka.
- Preferensi tampilan yang ikut berpindah perangkat harus didaftarkan di `lib/prefs.js` (`SYNCED_KEYS`) **dan** di `PreferencesController`.

## 12. Penyimpangan yang diketahui (belum dibereskan)

Catatan agar tidak dianggap standar:
- Dua pemanggilan `confirm()` bawaan browser masih ada (`Solve.vue` dan `PracticeSolve.vue`, saat memuat kode dari riwayat ke editor); seharusnya `ConfirmDialog`.
- Tombol urut `▲ ▼` (editor soal, grup), panah urutan di `SortTh.vue` (9 px) dan `⬇` masih berupa glyph teks, bukan `Icon`.
- Donat survei berupa SVG tulis tangan, sedangkan grafik lain memakai Chart.js.
- Teks petunjuk di riwayat submission masih Inggris saja (benar untuk antarmuka, tetapi belum mengikuti `docLang`).
- Baris milik sendiri di "Recent solves" (latar amber) berubah abu-abu saat disorot.

## 13. Sebelum menggabungkan perubahan layar

1. `npm run build` lolos.
2. Dilihat di terang **dan** gelap.
3. Dilihat di lebar desktop dan 390 px.
4. Tidak ada kelas warna baru di luar palet bagian 2.
5. Komponen atau pola baru dicatat di dokumen ini.
