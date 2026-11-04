<script setup>
import { computed } from 'vue';
import { useAuth } from '../stores/auth';

const auth = useAuth();
const student = computed(() => !auth.isTeacher);

const sections = [
  ['alur', 'Alur singkat'],
  ['statement', 'Menulis statement'],
  ['gambar', 'Gambar dan rumus'],
  ['test', 'Test case'],
  ['lanjutan', 'Pengaturan lanjutan'],
  ['validasi', 'Yang diperiksa saat menyimpan'],
  ['keliru', 'Kesalahan yang sering terjadi'],
  ['contoh', 'Contoh lengkap'],
];
</script>

<template>
  <div class="max-w-3xl mx-auto px-4 py-6 text-slate-700 dark:text-slate-200">
    <h1 class="text-2xl font-bold">📖 Panduan membuat soal</h1>
    <p class="text-sm text-slate-500 dark:text-slate-400 mt-1">
      Dari ide sampai soal yang bisa dikerjakan: apa yang ditulis, bagaimana test case bekerja, dan apa yang diperiksa sistem.
    </p>

    <nav class="mt-4 flex flex-wrap gap-2 text-xs">
      <a v-for="[id, label] in sections" :key="id" :href="`#${id}`"
         class="px-2.5 py-1 rounded-full border border-slate-300 dark:border-slate-700 hover:border-amber-400 hover:text-amber-600 dark:hover:text-amber-400">{{ label }}</a>
    </nav>

    <article class="mt-6 space-y-4 text-sm leading-relaxed
                    [&_h2]:text-lg [&_h2]:font-bold [&_h2]:mt-8 [&_h2]:text-slate-900 dark:[&_h2]:text-slate-100 [&_h2]:scroll-mt-16
                    [&_h3]:font-semibold [&_h3]:mt-4 [&_h3]:text-slate-900 dark:[&_h3]:text-slate-100
                    [&_ul]:list-disc [&_ul]:pl-5 [&_ul]:space-y-1 [&_ol]:list-decimal [&_ol]:pl-5 [&_ol]:space-y-1
                    [&_a]:text-amber-600 dark:[&_a]:text-amber-400 [&_a]:underline
                    [&_code]:bg-slate-100 dark:[&_code]:bg-slate-800 [&_code]:px-1 [&_code]:rounded [&_code]:text-[0.9em] [&_code]:font-mono
                    [&_pre]:bg-slate-900 [&_pre]:text-slate-100 [&_pre]:p-3 [&_pre]:rounded-lg [&_pre]:overflow-x-auto [&_pre]:text-[13px] [&_pre]:leading-snug
                    [&_pre_code]:!bg-transparent [&_pre_code]:!p-0
                    [&_table]:w-full [&_th]:text-left [&_th]:font-semibold [&_td]:align-top [&_td]:py-1 [&_td]:pr-3 [&_th]:pr-3">

      <section>
        <h2 id="alur">1. Alur singkat</h2>
        <template v-if="student">
          <ol>
            <li>Buka <RouterLink to="/bank">My problems</RouterLink> lalu <strong>+ New problem</strong>. Soal itu <strong>pribadi</strong>: hanya kamu yang melihatnya.</li>
            <li>Isi judul, statement, dan minimal satu test case (lihat bagian di bawah), lalu <strong>Save</strong>. Kamu boleh menyimpan, mengedit, dan menjalankannya sebanyak yang kamu mau.</li>
            <li>Kalau sudah siap, pilih salah satu:
              <ul>
                <li><strong>Submit to Practice</strong> — admin memeriksa. Kalau disetujui, soalmu tampil di Practice untuk semua orang.</li>
                <li><strong>Propose to a board</strong> — pemilik board memeriksa. Kalau disetujui, soal disalin ke board itu dalam keadaan tersembunyi sampai pemiliknya menampilkannya.</li>
              </ul>
            </li>
            <li>Kamu mendapat notifikasi (🔔) saat jawabannya keluar. Kalau ditolak, alasannya ada di notifikasi dan soalnya bisa diperbaiki lalu dikirim lagi.</li>
          </ol>
          <ul>
            <li>Selama menunggu jawaban, atau setelah dipublikasikan, soal <strong>tidak bisa diedit</strong>.</li>
            <li>Mengerjakan soal buatanmu sendiri <strong>tidak memberi XP</strong> dan tidak menambah streak.</li>
            <li>Jumlah soal yang boleh kamu simpan dibatasi admin (awalnya 50), dengan maksimal 30 test per soal dan 10 soal yang menunggu review sekaligus.</li>
          </ul>
        </template>
        <template v-else>
          <ul>
            <li><strong>Bank soal pribadi</strong> (<RouterLink to="/bank">My problems</RouterLink>): tulis sekali, lalu tarik ke board mana pun lewat <em>copy to</em>. Kalau soalnya dibuat <em>public</em>, soal itu muncul di Practice.</li>
            <li><strong>Langsung di board</strong>: Boards → pilih board → Problems → New. Soal baru langsung ada di board itu.</li>
            <li><strong>AI Generator</strong> bisa membuat draf soal beserta test-nya. Selalu baca hasilnya: periksa statement dan test sebelum dipakai. Soal hasil AI yang dibagikan melewati review admin.</li>
            <li>Siswa bisa mengusulkan soal ke boardmu. Usulan muncul di halaman Problems board (dan di 🔔). Saat disetujui, soal disalin dalam keadaan <strong>hidden</strong>.</li>
          </ul>
        </template>
      </section>

      <section>
        <h2 id="statement">2. Menulis statement</h2>
        <p>Statement ditulis dengan Markdown. Soal baru langsung terisi kerangka ini; ganti isinya:</p>
        <pre v-pre><code>Tulis ceritanya atau masalahnya di sini, singkat dan jelas.

**Format Input:** jelaskan baris demi baris apa yang dibaca program.

**Format Output:** jelaskan apa yang harus dicetak, termasuk format angka atau teksnya.

**Batasan:** 1 &lt;= N &lt;= 1000.</code></pre>
        <h3>Isi yang baik</h3>
        <ul>
          <li><strong>Ceritanya pendek, tugasnya jelas.</strong> Satu kalimat di akhir yang menyebut apa yang dihitung atau dicetak sering lebih berguna daripada paragraf cerita.</li>
          <li><strong>Format Input</strong>: urutan data per baris (<em>"baris pertama berisi N, N baris berikutnya berisi dua bilangan"</em>). Sebut tipe dan pemisahnya.</li>
          <li><strong>Format Output</strong>: apa yang dicetak, berapa angka di belakang koma, huruf besar atau kecil, satu atau banyak baris. Soal sering salah di sini.</li>
          <li><strong>Batasan</strong>: rentang semua nilai (untuk menentukan <code>int</code> atau <code>long long</code>), dan batas waktu yang masuk akal.</li>
          <li><strong>Contoh Input/Output tidak perlu ditulis lagi.</strong> Test case bertanda <em>sample</em> ditampilkan otomatis di bawah statement.</li>
        </ul>
        <h3>Cuplikan Markdown yang sering dipakai</h3>
        <table>
          <thead><tr><th>Tulis</th><th>Hasil</th></tr></thead>
          <tbody>
            <tr><td><code v-pre>**tebal**</code>, <code v-pre>*miring*</code></td><td><strong>tebal</strong>, <em>miring</em></td></tr>
            <tr><td><code v-pre>`N`</code></td><td>nama variabel atau nilai dalam huruf kode</td></tr>
            <tr><td><code v-pre>- butir</code> / <code v-pre>1. butir</code></td><td>daftar berpoin / bernomor</td></tr>
            <tr><td><code v-pre>```</code> di awal dan akhir baris</td><td>blok kode atau contoh data</td></tr>
            <tr><td><code v-pre>| a | b |</code> dengan baris <code v-pre>|---|---|</code></td><td>tabel</td></tr>
            <tr><td><code v-pre>## Judul kecil</code></td><td>sub-judul</td></tr>
          </tbody>
        </table>
        <p>Siswa membaca statement sebagai gambar yang dilindungi (ada watermark), jadi tidak bisa disalin. Tampilan di pratinjau editor sedikit berbeda, tetapi isinya sama.</p>
      </section>

      <section>
        <h2 id="gambar">3. Gambar dan rumus</h2>
        <h3>Gambar</h3>
        <ol>
          <li>Di editor, klik <strong>🖼 Add image</strong> di atas kotak statement dan pilih file.</li>
          <li>Kode seperti <code v-pre>![](/api/images/abc123…)</code> disisipkan di kursor. Kamu bisa mengisi keterangan di dalam kurung siku: <code v-pre>![Graf contoh](/api/images/…)</code>.</li>
          <li>Beri baris kosong di atas dan di bawahnya supaya gambar tampil sebagai blok sendiri. Satu soal boleh punya beberapa gambar.</li>
        </ol>
        <ul>
          <li>Format: PNG, JPEG, GIF, atau WebP. <strong>SVG tidak diterima</strong> karena bisa memuat kode.</li>
          <li>Ukuran file dibatasi admin (awalnya 1 MB). File yang lolos dikecilkan otomatis (lebar maksimal 1200 px).</li>
          <li>Hanya gambar yang diunggah lewat tombol itu yang tampil. Gambar dari situs lain diblokir.</li>
          <li>Gambar yang kamu unggah tetap ada walau kodenya dihapus dari statement, dan jumlahnya dibatasi per orang.</li>
        </ul>
        <h3>Rumus</h3>
        <p>Belum ada mesin rumus (LaTeX). Tulis rumus sebagai teks seperti di kode C:</p>
        <ul>
          <li><code v-pre>x^2</code>, <code v-pre>sqrt(x)</code>, <code v-pre>a / b</code>, <code v-pre>a &lt;= b</code>, <code v-pre>a != b</code>, <code v-pre>|x|</code>, <code v-pre>10^9</code>.</li>
          <li>LaTeX sederhana (<code v-pre>\frac{a}{b}</code>, <code v-pre>\sqrt{x}</code>, <code v-pre>\leq</code>) otomatis diubah menjadi teks seperti di atas. Rumus yang rumit (sigma, integral, matriks) lebih baik digambar lalu diunggah sebagai gambar.</li>
        </ul>
      </section>

      <section>
        <h2 id="test">4. Test case</h2>
        <p>Test case adalah pasangan <strong>input</strong> dan <strong>output yang diharapkan</strong>. Program peserta dijalankan pada tiap input, lalu keluarannya dibandingkan dengan output yang diharapkan.</p>
        <table>
          <thead><tr><th>Jenis</th><th>Dilihat peserta?</th><th>Fungsi</th></tr></thead>
          <tbody>
            <tr><td><strong>Sample</strong></td><td>Ya, tampil sebagai Contoh di bawah statement</td><td>Memperjelas format. Cukup 1–2.</td></tr>
            <tr><td><strong>Hidden</strong></td><td>Tidak</td><td>Yang benar-benar menilai: kasus biasa, kasus tepi, dan kasus besar.</td></tr>
          </tbody>
        </table>
        <ul>
          <li><strong>Akan diterima (Accepted) hanya jika semua test lulus.</strong> Kalau ada yang gagal, skor = poin test yang lulus dibagi total poin, sehingga <em>Points</em> menentukan bobot tiap test.</li>
          <li><strong>Perbandingan output:</strong> spasi di akhir setiap baris dan baris kosong di akhir output diabaikan. Selebihnya harus persis sama, termasuk huruf besar atau kecil, spasi di awal, dan baris kosong di tengah.</li>
          <li><strong>Wajib ada minimal satu test hidden</strong> sebelum soal dikirim ke Practice atau diusulkan ke board. Kalau semuanya sample, peserta bisa "menyelesaikan" soal hanya dengan mencetak contoh.</li>
        </ul>
        <h3>Test yang sebaiknya ada</h3>
        <ul>
          <li>Kasus paling kecil (N = 1, daftar kosong kalau diizinkan, nilai 0).</li>
          <li>Kasus yang membedakan jawaban benar dari yang hampir benar: nilai negatif, angka kembar, urutan terbalik.</li>
          <li>Kasus besar yang menguji ketepatan tipe data dan kecepatan (mis. nilai mendekati 10^9 untuk menguji <code>int</code> vs <code>long long</code>).</li>
          <li><strong>Output yang diharapkan harus benar-benar dihitung dari solusi yang sudah kamu uji</strong>, bukan ditebak. Jalankan solusimu sendiri (tombol <em>Run</em> di soal) pada tiap input untuk memastikannya.</li>
        </ul>
      </section>

      <section>
        <h2 id="lanjutan">5. Pengaturan lanjutan</h2>
        <table>
          <tbody>
            <tr><td><strong>Allowed languages</strong></td><td>C, C++, atau keduanya. Kosong berarti semua bahasa boleh.</td></tr>
            <tr><td><strong>Level</strong> dan <strong>Tags</strong></td><td>Easy / Medium / Hard. Tag dipisah koma (<code>array,sorting</code>) dan dipakai untuk pencarian serta rekomendasi di Practice.</td></tr>
            <tr><td><strong>Time (ms)</strong></td><td>Batas waktu per test, 100–10000, awalnya 1000. Naikkan hanya kalau solusi yang benar memang butuh lebih lama.</td></tr>
            <tr><td><strong>Memory (KB)</strong></td><td>Batas memori, 4096–512000, awalnya 32768.</td></tr>
            <tr><td><strong>Banned headers</strong></td><td>Header yang tidak boleh di-<code>#include</code>, mis. <code>algorithm</code>. Mengisi satu saja juga memblokir <code>&lt;bits/stdc++.h&gt;</code>.</td></tr>
            <tr><td><strong>Banned functions</strong></td><td>Nama yang tidak boleh dipakai, mis. <code>qsort</code>. Menulis <code>sort</code> juga mengenai <code>std::sort</code>; tulis <code>std::sort</code> kalau hanya yang berkualifikasi itu yang dilarang. Pelanggaran dilaporkan sebagai Compile Error.</td></tr>
            <tr><td><strong>Input file</strong></td><td>Isi nama file (mis. <code>input.txt</code>) untuk soal yang membaca dengan <code>fopen()</code>. Input tiap test ditulis ke file itu, bukan dikirim lewat stdin. Siswa melihat catatan tentang file itu di bagian atas soal. Kosongkan untuk soal biasa (<code>scanf</code>/<code>cin</code>).</td></tr>
          </tbody>
        </table>
      </section>

      <section>
        <h2 id="validasi">6. Yang diperiksa saat menyimpan</h2>
        <p>Penyimpanan ditolak, dengan pesan di bawah tombol Save, kalau:</p>
        <ul>
          <li>judul kosong atau lebih dari 200 karakter;</li>
          <li>statement kosong atau lebih dari 50.000 karakter;</li>
          <li>tidak ada test case sama sekali;</li>
          <li>ada baris test yang kosong total (input dan output sama-sama kosong), biasanya baris "add test" yang lupa diisi. Output kosong dengan input terisi tetap boleh, untuk soal yang memang tidak mencetak apa pun.</li>
        </ul>
        <p>Pengiriman ke Practice atau board juga butuh <strong>minimal satu test hidden</strong> (lihat bagian 4).</p>
      </section>

      <section>
        <h2 id="keliru">7. Kesalahan yang sering terjadi</h2>
        <ul>
          <li><strong>Output yang diharapkan salah.</strong> Penyebab terbanyak soal dilaporkan peserta. Hitung dari solusi yang sudah kamu jalankan, jangan dari kepala.</li>
          <li><strong>Input melanggar batasan sendiri.</strong> Contoh: batasan <code>N &lt;= 100</code> tapi ada test dengan N = 1000. Periksa tiap test terhadap statement.</li>
          <li><strong>Format output tidak jelas.</strong> "Cetak rata-rata" tanpa menyebut jumlah angka di belakang koma membuat jawaban yang benar bisa ditolak. Tulis persis, misalnya "tepat 2 angka di belakang koma".</li>
          <li><strong>Satu jawaban benar tapi banyak bentuk.</strong> Kalau soal punya lebih dari satu jawaban sah (mis. urutan bebas), perbandingan teks tidak bisa menilainya. Ubah soalnya supaya jawabannya tunggal, misalnya "cetak yang terkecil secara urutan abjad".</li>
          <li><strong>Hanya ada test sample.</strong> Tambahkan test hidden, termasuk kasus tepi.</li>
          <li><strong>Statement menyuruh baca file, tetapi pengaturan Input file kosong</strong> (atau sebaliknya). Pastikan keduanya cocok.</li>
          <li><strong>Mengubah test pada soal yang sudah dikerjakan orang lain.</strong> Hasil submission lama dinilai dengan test yang lama, jadi tidak lagi sebanding. Kalau perubahannya besar, buat soal baru.</li>
        </ul>
      </section>

      <section>
        <h2 id="contoh">8. Contoh lengkap: "Jumlah Dua Bilangan"</h2>
        <table>
          <tbody>
            <tr><td><strong>Title</strong></td><td>Jumlah Dua Bilangan</td></tr>
            <tr><td><strong>Level / Tags</strong></td><td>Easy / <code>math</code></td></tr>
            <tr><td><strong>Time / Memory</strong></td><td>1000 ms / 32768 KB (bawaan)</td></tr>
          </tbody>
        </table>
        <p><strong>Statement:</strong></p>
        <pre v-pre><code>Diberikan dua bilangan bulat `a` dan `b`. Cetak hasil penjumlahannya.

**Format Input:** satu baris berisi dua bilangan bulat `a` dan `b` dipisah spasi.

**Format Output:** satu bilangan bulat, yaitu `a + b`.

**Batasan:** -10^9 &lt;= a, b &lt;= 10^9.</code></pre>
        <p><strong>Test case:</strong></p>
        <table>
          <thead><tr><th>#</th><th>Jenis</th><th>Input</th><th>Output</th><th>Mengapa ada</th></tr></thead>
          <tbody>
            <tr><td>1</td><td>sample</td><td><code>2 3</code></td><td><code>5</code></td><td>Menunjukkan format.</td></tr>
            <tr><td>2</td><td>hidden</td><td><code>100 -40</code></td><td><code>60</code></td><td>Ada bilangan negatif.</td></tr>
            <tr><td>3</td><td>hidden</td><td><code>0 0</code></td><td><code>0</code></td><td>Kasus tepi: nol.</td></tr>
            <tr><td>4</td><td>hidden</td><td><code>1000000000 1000000000</code></td><td><code>2000000000</code></td><td>Menjebak peserta yang memakai <code>int</code> (hasilnya meluap).</td></tr>
          </tbody>
        </table>
        <p>Tiga test hidden membedakan solusi yang benar dari yang hampir benar, sedangkan sample hanya menjelaskan formatnya.</p>
      </section>

      <p class="pt-4 text-xs text-slate-400 dark:text-slate-500">
        Ada yang kurang jelas? Tanyakan ke pengajar atau admin instansimu.
        <RouterLink :to="student ? '/bank' : '/bank/new'" class="ml-1">Mulai membuat soal →</RouterLink>
      </p>
    </article>
  </div>
</template>
