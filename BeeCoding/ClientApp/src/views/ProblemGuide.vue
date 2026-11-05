<script setup>
import { computed } from 'vue';
import { useAuth } from '../stores/auth';
import DocLangToggle from '../components/DocLangToggle.vue';
import { docLang as lang } from '../lib/docLang';

const auth = useAuth();
const student = computed(() => !auth.isTeacher);

const sections = [
  ['alur', { en: 'Overview', id: 'Alur singkat' }],
  ['statement', { en: 'Writing the statement', id: 'Menulis statement' }],
  ['gambar', { en: 'Images and formulas', id: 'Gambar dan rumus' }],
  ['test', { en: 'Test cases', id: 'Test case' }],
  ['lanjutan', { en: 'Advanced settings', id: 'Pengaturan lanjutan' }],
  ['validasi', { en: 'What is checked on save', id: 'Yang diperiksa saat menyimpan' }],
  ['keliru', { en: 'Common mistakes', id: 'Kesalahan yang sering terjadi' }],
  ['originality', { en: 'Originality and copyright', id: 'Keaslian dan hak cipta' }],
  ['contoh', { en: 'Complete example', id: 'Contoh lengkap' }],
];
</script>

<template>
  <div class="max-w-3xl mx-auto px-4 py-6 text-slate-700 dark:text-slate-200">
    <div class="flex items-start justify-between gap-3">
      <h1 class="text-2xl font-bold">📖 {{ lang === 'id' ? 'Panduan membuat soal' : 'Guide to writing problems' }}</h1>
      <DocLangToggle class="shrink-0 mt-1" />
    </div>
    <p class="text-sm text-slate-500 dark:text-slate-400 mt-1">
      {{ lang === 'id'
        ? 'Dari ide sampai soal yang bisa dikerjakan: apa yang ditulis, bagaimana test case bekerja, dan apa yang diperiksa sistem.'
        : 'From idea to a problem people can solve: what to write, how test cases work, and what the system checks.' }}
    </p>

    <nav class="mt-4 flex flex-wrap gap-2 text-xs">
      <a v-for="[id, label] in sections" :key="id" :href="`#${id}`"
         class="px-2.5 py-1 rounded-full border border-slate-300 dark:border-slate-700 hover:border-amber-400 hover:text-amber-600 dark:hover:text-amber-400">{{ label[lang] }}</a>
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

      <template v-if="lang === 'id'">
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
          <li>Gambar yang tidak lagi dipakai statement mana pun dihapus otomatis beberapa hari setelah berhenti dipakai (awalnya 3), jadi gambar yang tak sengaja terhapus dari statement masih sempat dikembalikan. Jumlah gambar per orang juga dibatasi.</li>
        </ul>
        <h3>Rumus</h3>
        <p>Rumus ditulis dengan LaTeX di antara tanda dolar, dan tampil sebagai rumus matematika, baik di tampilan guru maupun di gambar soal yang dilihat siswa:</p>
        <ul>
          <li><code v-pre>$1 \le n \le 2 \times 10^5$</code> untuk rumus di tengah kalimat, dan <code v-pre>$$\sum_{i=1}^{n} a_i$$</code> untuk rumus yang berdiri sendiri. <code v-pre>\(...\)</code> dan <code v-pre>\[...\]</code> juga bisa.</li>
          <li>Yang didukung antara lain pecahan (<code v-pre>\frac</code>), akar, pangkat dan indeks, sigma, limit, matriks, <code v-pre>cases</code>, huruf Yunani, <code v-pre>\binom</code>, <code v-pre>\bmod</code> dan <code v-pre>\pmod{p}</code>.</li>
          <li>Bilah alat di atas kotak soal menyisipkannya untuk Anda: tombol <b>$ x $</b>, <b>$$ ∑ $$</b>, dan menu <b>Formula</b> berisi rumus yang umum (batasan, pecahan, modulo, sigma, <code v-pre>cases</code>, matriks). Tombol <b>? Formula help</b> menampilkan contoh-contoh itu lengkap dengan kodenya. Pratinjau di sebelahnya langsung menampilkan hasilnya.</li>
          <li>Tanda dolar yang bukan rumus (misalnya <code v-pre>$5 dan $10</code>) dibiarkan, begitu juga isi <code v-pre>`kode`</code> dan blok kode.</li>
          <li>Rumus yang salah ditulis tampil apa adanya (merah di tampilan guru), dan teks lain tetap terbaca. Satu soal memuat paling banyak 200 rumus, masing-masing paling panjang 500 karakter.</li>
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
          <li><strong>Urutan:</strong> test dijalankan sesuai urutan di editor, dan judge melaporkan yang pertama gagal. Seret ⠿ atau pakai ▲ ▼ untuk mengubah urutan.</li>
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
        <h2 id="originality">8. Keaslian dan hak cipta</h2>
        <p>Soal dari situs lain (LeetCode, Codeforces, UVa, buku, dan sejenisnya) dilindungi hak cipta. Yang dilindungi adalah <strong>teksnya</strong>: statement, cerita, contoh, gambar. Ide dan algoritmanya tidak dilindungi.</p>
        <ul>
          <li><strong>Jangan menyalin atau menerjemahkan statement</strong> dari situs lain, dan jangan sekadar mengganti nama tokoh. Itu tetap salinan.</li>
          <li><strong>Tulis ulang dengan kata-katamu sendiri</strong>: cerita, contoh, dan test case buatan sendiri. Mengambil idenya (mis. "cari indeks yang jumlah kiri dan kanannya sama") boleh.</li>
          <li><strong>Lebih baik lagi, ubah tantangannya</strong>: aturan atau batasan yang berbeda, agar bukan soal yang jawabannya sudah beredar luas.</li>
          <li><strong>Cek dulu apakah soalmu sudah ada</strong> di bank atau di board. Soal yang sama dengan nama berbeda akan ditolak saat ditinjau.</li>
          <li>Bila terinspirasi dari sebuah soal, sebutkan sumbernya di akhir statement, misalnya <em>"Terinspirasi dari ..."</em>.</li>
        </ul>
        <p>Admin dapat menolak atau menghapus soal yang disalin dari sumber lain.</p>
      </section>

      <section>
        <h2 id="contoh">9. Contoh lengkap: "Jumlah Dua Bilangan"</h2>
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

      </template>
      <template v-else>
      <section>
        <h2 id="alur">1. Overview</h2>
        <template v-if="student">
          <ol>
            <li>Open <RouterLink to="/bank">My problems</RouterLink> and click <strong>+ New problem</strong>. The problem is <strong>private</strong>: only you can see it.</li>
            <li>Fill in the title, the statement and at least one test case (see below), then <strong>Save</strong>. You can save, edit and run it as often as you like.</li>
            <li>When it is ready, pick one:
              <ul>
                <li><strong>Submit to Practice</strong> — an admin reviews it. If approved, your problem appears in Practice for everyone.</li>
                <li><strong>Propose to a board</strong> — the board's owner reviews it. If approved, it is copied to that board, hidden until the owner shows it.</li>
              </ul>
            </li>
            <li>You get a notification (🔔) when the answer arrives. If it is declined, the reason is in the notification and you can fix the problem and send it again.</li>
          </ol>
          <ul>
            <li>While it waits for an answer, or once it is published, a problem <strong>cannot be edited</strong>.</li>
            <li>Solving a problem you wrote yourself <strong>earns no XP</strong> and does not add to your streak.</li>
            <li>The number of problems you may keep is limited by the admin (50 to begin with), with at most 30 tests per problem and 10 problems waiting for review at once.</li>
          </ul>
        </template>
        <template v-else>
          <ul>
            <li><strong>Personal problem bank</strong> (<RouterLink to="/bank">My problems</RouterLink>): write once, then pull it into any board with <em>copy to</em>. A problem marked <em>public</em> appears in Practice.</li>
            <li><strong>Straight on a board</strong>: Boards → pick a board → Problems → New. The new problem is on that board right away.</li>
            <li>The <strong>AI Generator</strong> can draft a problem and its tests. Always read the result: check the statement and the tests before using it. AI problems that are shared go through admin review.</li>
            <li>Students can propose problems to your board. Proposals show up on the board's Problems page (and in 🔔). When approved, the problem is copied as <strong>hidden</strong>.</li>
          </ul>
        </template>
      </section>

      <section>
        <h2 id="statement">2. Writing the statement</h2>
        <p>The statement is written in Markdown. A new problem starts with this skeleton; replace its contents:</p>
        <pre v-pre><code>Tell the story or describe the task here, briefly and clearly.

**Input Format:** explain, line by line, what the program reads.

**Output Format:** explain what must be printed, including the number or text format.

**Constraints:** 1 &lt;= N &lt;= 1000.</code></pre>
        <h3>What a good statement contains</h3>
        <ul>
          <li><strong>A short story and a clear task.</strong> One closing sentence saying what to compute or print is often worth more than a paragraph of story.</li>
          <li><strong>Input Format</strong>: the data, line by line (<em>"the first line holds N, the next N lines hold two numbers each"</em>). Give the types and the separators.</li>
          <li><strong>Output Format</strong>: what is printed, how many decimals, upper or lower case, one line or many. This is where problems most often go wrong.</li>
          <li><strong>Constraints</strong>: the range of every value (it decides between <code>int</code> and <code>long long</code>), and a time limit that makes sense.</li>
          <li><strong>You do not need to write the Input/Output examples again.</strong> Test cases marked <em>sample</em> are shown automatically under the statement.</li>
        </ul>
        <h3>Markdown you will use often</h3>
        <table>
          <thead><tr><th>Write</th><th>Result</th></tr></thead>
          <tbody>
            <tr><td><code v-pre>**bold**</code>, <code v-pre>*italic*</code></td><td><strong>bold</strong>, <em>italic</em></td></tr>
            <tr><td><code v-pre>`N`</code></td><td>a variable name or value in code font</td></tr>
            <tr><td><code v-pre>- item</code> / <code v-pre>1. item</code></td><td>bulleted / numbered list</td></tr>
            <tr><td><code v-pre>```</code> on its own line, before and after</td><td>a code block or sample data</td></tr>
            <tr><td><code v-pre>| a | b |</code> with a row <code v-pre>|---|---|</code></td><td>a table</td></tr>
            <tr><td><code v-pre>## Small heading</code></td><td>a sub-heading</td></tr>
          </tbody>
        </table>
        <p>Students read the statement as a protected image (with a watermark), so it cannot be copied. The preview in the editor looks slightly different, but the content is the same.</p>
      </section>

      <section>
        <h2 id="gambar">3. Images and formulas</h2>
        <h3>Images</h3>
        <ol>
          <li>In the editor, click <strong>🖼 Add image</strong> above the statement box and choose a file.</li>
          <li>Code like <code v-pre>![](/api/images/abc123…)</code> is inserted at the cursor. You can write a caption inside the square brackets: <code v-pre>![Example graph](/api/images/…)</code>.</li>
          <li>Leave a blank line above and below it so the image is shown as a block of its own. A problem can have several images.</li>
        </ol>
        <ul>
          <li>Formats: PNG, JPEG, GIF or WebP. <strong>SVG is not accepted</strong> because it can carry code.</li>
          <li>The file size is limited by the admin (1 MB to begin with). An accepted file is shrunk automatically (at most 1200 px wide).</li>
          <li>Only images uploaded with that button are shown. Images from other sites are blocked.</li>
          <li>An image that no statement uses any more is deleted automatically a few days after it stops being used (3 to begin with), so one removed by accident can still be put back. The number of images per person is also limited.</li>
        </ul>
        <h3>Formulas</h3>
        <p>Write formulas in LaTeX between dollar signs. They appear as typeset maths, both in the teacher's view and in the statement picture students see:</p>
        <ul>
          <li><code v-pre>$1 \le n \le 2 \times 10^5$</code> for a formula inside a sentence, and <code v-pre>$$\sum_{i=1}^{n} a_i$$</code> for one on its own line. <code v-pre>\(...\)</code> and <code v-pre>\[...\]</code> work too.</li>
          <li>Supported include fractions (<code v-pre>\frac</code>), roots, powers and subscripts, sums, limits, matrices, <code v-pre>cases</code>, Greek letters, <code v-pre>\binom</code>, <code v-pre>\bmod</code> and <code v-pre>\pmod{p}</code>.</li>
          <li>The toolbar above the statement box inserts them for you: the <b>$ x $</b> and <b>$$ ∑ $$</b> buttons, and the <b>Formula</b> menu of common ones (constraints, fractions, modulo, sums, <code v-pre>cases</code>, matrices). The <b>? Formula help</b> button lists those examples with the code behind each. The preview beside the box shows the result as you type.</li>
          <li>A dollar sign that is not a formula (for example <code v-pre>$5 and $10</code>) is left alone, and so is anything inside <code v-pre>`code`</code> and code blocks.</li>
          <li>A formula that is written wrongly is shown as typed (in red in the teacher's view) and the rest of the text still reads. A statement holds at most 200 formulas, each at most 500 characters long.</li>
        </ul>
      </section>

      <section>
        <h2 id="test">4. Test cases</h2>
        <p>A test case is a pair of <strong>input</strong> and <strong>expected output</strong>. The participant's program is run on each input and its output is compared with the expected output.</p>
        <table>
          <thead><tr><th>Kind</th><th>Seen by participants?</th><th>Purpose</th></tr></thead>
          <tbody>
            <tr><td><strong>Sample</strong></td><td>Yes, shown as an Example under the statement</td><td>Clarifies the format. 1–2 are enough.</td></tr>
            <tr><td><strong>Hidden</strong></td><td>No</td><td>What actually judges: ordinary cases, edge cases and large cases.</td></tr>
          </tbody>
        </table>
        <ul>
          <li><strong>A solution is Accepted only if every test passes.</strong> If some fail, the score is the points of the passed tests divided by the total points, so <em>Points</em> sets the weight of each test.</li>
          <li><strong>Output comparison:</strong> spaces at the end of each line and blank lines at the end of the output are ignored. Everything else must match exactly, including upper/lower case, leading spaces and blank lines in the middle.</li>
          <li><strong>At least one hidden test is required</strong> before a problem can be sent to Practice or proposed to a board. If everything is a sample, a participant can "solve" the problem just by printing the examples.</li>
          <li><strong>Order:</strong> tests run in the order shown in the editor, and the judge reports the first one that fails. Drag a test by its ⠿ handle, or use ▲ ▼, to reorder.</li>
        </ul>
        <h3>Tests worth having</h3>
        <ul>
          <li>The smallest case (N = 1, an empty list if allowed, the value 0).</li>
          <li>Cases that tell a correct answer from an almost-correct one: negative values, duplicates, reversed order.</li>
          <li>Large cases that test the data type and the speed (e.g. values near 10^9 to catch <code>int</code> vs <code>long long</code>).</li>
          <li><strong>The expected output must really be computed from a solution you have tested</strong>, not guessed. Run your own solution (the <em>Run</em> button on the problem) on every input to be sure.</li>
        </ul>
      </section>

      <section>
        <h2 id="lanjutan">5. Advanced settings</h2>
        <table>
          <tbody>
            <tr><td><strong>Allowed languages</strong></td><td>C, C++, or both. Empty means every language is allowed.</td></tr>
            <tr><td><strong>Level</strong> and <strong>Tags</strong></td><td>Easy / Medium / Hard. Tags are comma-separated (<code>array,sorting</code>) and used for search and recommendations in Practice.</td></tr>
            <tr><td><strong>Time (ms)</strong></td><td>Time limit per test, 100–10000, 1000 to begin with. Raise it only if a correct solution really needs longer.</td></tr>
            <tr><td><strong>Memory (KB)</strong></td><td>Memory limit, 4096–512000, 32768 to begin with.</td></tr>
            <tr><td><strong>Banned headers</strong></td><td>Headers that may not be <code>#include</code>d, e.g. <code>algorithm</code>. Setting even one also blocks <code>&lt;bits/stdc++.h&gt;</code>.</td></tr>
            <tr><td><strong>Banned functions</strong></td><td>Names that may not be used, e.g. <code>qsort</code>. Writing <code>sort</code> also catches <code>std::sort</code>; write <code>std::sort</code> if only the qualified form should be banned. A violation is reported as a Compile Error.</td></tr>
            <tr><td><strong>Input file</strong></td><td>Enter a file name (e.g. <code>input.txt</code>) for problems that read with <code>fopen()</code>. Each test's input is written to that file instead of being sent through stdin. Students see a note about the file at the top of the problem. Leave it empty for ordinary problems (<code>scanf</code>/<code>cin</code>).</td></tr>
          </tbody>
        </table>
      </section>

      <section>
        <h2 id="validasi">6. What is checked when you save</h2>
        <p>Saving is refused, with a message under the Save button, if:</p>
        <ul>
          <li>the title is empty or longer than 200 characters;</li>
          <li>the statement is empty or longer than 50,000 characters;</li>
          <li>there are no test cases at all;</li>
          <li>a test row is completely empty (input and output both blank), usually an "add test" row that was never filled in. An empty output with a filled-in input is still allowed, for problems that really print nothing.</li>
        </ul>
        <p>Sending to Practice or a board also needs <strong>at least one hidden test</strong> (see section 4).</p>
      </section>

      <section>
        <h2 id="keliru">7. Common mistakes</h2>
        <ul>
          <li><strong>A wrong expected output.</strong> The most common reason participants report a problem. Compute it from a solution you have run, not from your head.</li>
          <li><strong>An input that breaks your own constraints.</strong> For example the constraint says <code>N &lt;= 100</code> but a test has N = 1000. Check every test against the statement.</li>
          <li><strong>An unclear output format.</strong> "Print the average" without saying how many decimals can make a correct answer get rejected. Be exact, e.g. "exactly 2 digits after the decimal point".</li>
          <li><strong>One answer, many forms.</strong> If a problem has more than one valid answer (e.g. any order), a text comparison cannot judge it. Change the problem so the answer is unique, e.g. "print the alphabetically smallest one".</li>
          <li><strong>Only sample tests.</strong> Add hidden tests, including edge cases.</li>
          <li><strong>The statement says to read a file but the Input file setting is empty</strong> (or the other way round). Make sure both agree.</li>
          <li><strong>Changing the tests of a problem others have already solved.</strong> Old submissions were judged with the old tests, so they are no longer comparable. If the change is big, create a new problem.</li>
        </ul>
      </section>

      <section>
        <h2 id="originality">8. Originality and copyright</h2>
        <p>Problems from other sites (LeetCode, Codeforces, UVa, books and the like) are protected by copyright. What is protected is the <strong>text</strong>: the statement, the story, the examples, the images. The ideas and algorithms are not.</p>
        <ul>
          <li><strong>Do not copy or translate a statement</strong> from another site, and do not just change the character names. That is still a copy.</li>
          <li><strong>Rewrite it in your own words</strong>: your own story, examples and test cases. Taking the idea (e.g. "find the index whose left and right sums are equal") is fine.</li>
          <li><strong>Better still, change the challenge</strong>: different rules or constraints, so it is not a problem whose answer is already everywhere.</li>
          <li><strong>Check that your problem does not already exist</strong> in the bank or on a board. The same problem under a new name will be rejected in review.</li>
          <li>If a problem inspired yours, name the source at the end of the statement, e.g. <em>"Inspired by ..."</em>.</li>
        </ul>
        <p>Admins may reject or remove problems copied from another source.</p>
      </section>

      <section>
        <h2 id="contoh">9. A complete example: "Sum of Two Numbers"</h2>
        <table>
          <tbody>
            <tr><td><strong>Title</strong></td><td>Sum of Two Numbers</td></tr>
            <tr><td><strong>Level / Tags</strong></td><td>Easy / <code>math</code></td></tr>
            <tr><td><strong>Time / Memory</strong></td><td>1000 ms / 32768 KB (defaults)</td></tr>
          </tbody>
        </table>
        <p><strong>Statement:</strong></p>
        <pre v-pre><code>You are given two integers `a` and `b`. Print their sum.

**Input Format:** one line with two integers `a` and `b` separated by a space.

**Output Format:** one integer, `a + b`.

**Constraints:** -10^9 &lt;= a, b &lt;= 10^9.</code></pre>
        <p><strong>Test cases:</strong></p>
        <table>
          <thead><tr><th>#</th><th>Kind</th><th>Input</th><th>Output</th><th>Why it is there</th></tr></thead>
          <tbody>
            <tr><td>1</td><td>sample</td><td><code>2 3</code></td><td><code>5</code></td><td>Shows the format.</td></tr>
            <tr><td>2</td><td>hidden</td><td><code>100 -40</code></td><td><code>60</code></td><td>Has a negative number.</td></tr>
            <tr><td>3</td><td>hidden</td><td><code>0 0</code></td><td><code>0</code></td><td>Edge case: zero.</td></tr>
            <tr><td>4</td><td>hidden</td><td><code>1000000000 1000000000</code></td><td><code>2000000000</code></td><td>Traps participants who use <code>int</code> (the result overflows).</td></tr>
          </tbody>
        </table>
        <p>The three hidden tests tell a correct solution from an almost-correct one, while the sample only explains the format.</p>
      </section>
      </template>

      <p class="pt-4 text-xs text-slate-400 dark:text-slate-500">
        {{ lang === 'id' ? 'Ada yang kurang jelas? Tanyakan ke pengajar atau admin instansimu.' : 'Something unclear? Ask your teacher or your institution\'s admin.' }}
        <RouterLink :to="student ? '/bank' : '/bank/new'" class="ml-1">{{ lang === 'id' ? 'Mulai membuat soal →' : 'Start writing a problem →' }}</RouterLink>
      </p>
    </article>
  </div>
</template>
