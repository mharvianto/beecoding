<script setup>
import LegalPage from './LegalPage.vue';
import { docLang } from '../lib/docLang';
</script>

<template>
  <LegalPage :title="docLang === 'id' ? 'Syarat & Ketentuan' : 'Terms & Agreement'" updated="7 September 2026">
    <template v-if="docLang === 'en'">
    <p>
      By creating an account or using this BeeCoding instance you agree to these terms. The
      instance is run by your school, teacher or institution (the <strong>“Operator”</strong>);
      the Operator may add its own classroom rules on top of these.
    </p>

    <h2>1. Your account</h2>
    <ul>
      <li>You are responsible for activity under your account and for keeping your password
        confidential. Tell the Operator immediately if you suspect misuse.</li>
      <li>Provide accurate registration details. The <em>teacher</em> role is granted only
        with an invite code; do not attempt to obtain it otherwise.</li>
      <li>One person per account. Do not share logins.</li>
    </ul>

    <h2>2. Acceptable use</h2>
    <p>You agree not to:</p>
    <ul>
      <li>Attempt to break out of, overload, or bypass the code sandbox; access files,
        networks, processes or data that are not yours; or attack the server or other users.</li>
      <li>Probe the platform for vulnerabilities without the Operator's written permission.</li>
      <li>Submit work that violates your class's academic-integrity rules, or pass off
        someone else's work as your own where your teacher prohibits it.</li>
      <li>Post harassing, hateful, illegal or off-topic content in notes, comments or code.</li>
      <li>Use automated tools to scrape content or hammer the run/submit endpoints.</li>
    </ul>
    <p>The Operator may suspend or delete accounts that break these rules.</p>

    <h2>3. Your content</h2>
    <p>
      You keep ownership of the code and text you create. You grant the Operator the
      permission needed to store, compile, run, display and grade that content for the
      purpose of operating the class. Teachers can view submissions from students in their
      classes.
    </p>

    <h2>4. The AI tutor</h2>
    <p>
      If enabled, the AI tutor gives <strong>hints and guidance, not solutions</strong>. Its
      output can be wrong or incomplete — verify it and use your own judgement. Using the
      tutor is still subject to your class's rules on outside help.
    </p>

    <h2>5. Availability and data</h2>
    <p>
      The service is provided “as is”, without warranty of any kind. It may be unavailable,
      reset between terms, or lose data. Keep your own copy of work that matters to you. The
      code judge is classroom-grade isolation, not hardened multi-tenant security.
    </p>

    <h2>6. Limitation of liability</h2>
    <p>
      To the extent permitted by law, the Operator and the BeeCoding authors are not liable
      for indirect or consequential damages, or for loss of data or coursework, arising from
      use of the service.
    </p>

    <h2>7. Termination</h2>
    <p>
      You may stop using the service and delete your account at any time from Account
      settings. The Operator may end your access at the end of a course or for a breach of
      these terms.
    </p>

    <h2>8. Changes</h2>
    <p>
      These terms may be updated with new versions of the software. Continued use after a
      change means you accept the updated terms.
    </p>

    <h2>9. Contact</h2>
    <p>Questions about these terms go to the Operator of this instance.</p>
    </template>
    <template v-else>
    <p>
      Dengan membuat akun atau menggunakan instans BeeCoding ini, Anda menyetujui syarat-syarat ini.
      Instans ini dijalankan oleh sekolah, guru, atau institusi Anda (<strong>“Operator”</strong>);
      Operator dapat menambahkan aturan kelasnya sendiri di atas syarat ini.
    </p>

    <h2>1. Akun Anda</h2>
    <ul>
      <li>Anda bertanggung jawab atas aktivitas di akun Anda dan atas kerahasiaan kata sandi.
        Segera beri tahu Operator jika Anda curiga akun disalahgunakan.</li>
      <li>Berikan data pendaftaran yang benar. Peran <em>guru</em> hanya diberikan dengan kode
        undangan; jangan mencoba mendapatkannya dengan cara lain.</li>
      <li>Satu orang satu akun. Jangan berbagi login.</li>
    </ul>

    <h2>2. Penggunaan yang dapat diterima</h2>
    <p>Anda setuju untuk tidak:</p>
    <ul>
      <li>Mencoba keluar dari, membebani, atau menghindari sandbox kode; mengakses berkas, jaringan,
        proses, atau data yang bukan milik Anda; atau menyerang server maupun pengguna lain.</li>
      <li>Mencari celah keamanan platform tanpa izin tertulis dari Operator.</li>
      <li>Mengumpulkan pekerjaan yang melanggar aturan kejujuran akademik kelas Anda, atau mengaku
        pekerjaan orang lain sebagai milik Anda di tempat guru Anda melarangnya.</li>
      <li>Memposting konten yang melecehkan, penuh kebencian, ilegal, atau di luar topik pada catatan,
        komentar, atau kode.</li>
      <li>Memakai alat otomatis untuk mengambil konten secara massal atau membanjiri endpoint
        jalankan/kirim.</li>
    </ul>
    <p>Operator dapat menangguhkan atau menghapus akun yang melanggar aturan ini.</p>

    <h2>3. Konten Anda</h2>
    <p>
      Anda tetap memegang hak atas kode dan teks yang Anda buat. Anda memberi Operator izin yang
      diperlukan untuk menyimpan, mengompilasi, menjalankan, menampilkan, dan menilai konten itu demi
      menjalankan kelas. Guru dapat melihat pengumpulan siswa di kelas mereka.
    </p>

    <h2>4. Tutor AI</h2>
    <p>
      Jika diaktifkan, tutor AI memberikan <strong>petunjuk dan arahan, bukan solusi</strong>.
      Keluarannya bisa salah atau tidak lengkap — periksa kembali dan gunakan penilaian Anda sendiri.
      Memakai tutor tetap tunduk pada aturan kelas Anda tentang bantuan dari luar.
    </p>

    <h2>5. Ketersediaan dan data</h2>
    <p>
      Layanan disediakan “apa adanya”, tanpa jaminan apa pun. Layanan bisa tidak tersedia, direset
      antar-semester, atau kehilangan data. Simpan salinan sendiri untuk pekerjaan yang penting bagi
      Anda. Juri kode hanya berisolasi tingkat ruang kelas, bukan keamanan multi-penyewa yang diperkuat.
    </p>

    <h2>6. Batasan tanggung jawab</h2>
    <p>
      Sejauh diizinkan hukum, Operator dan para pembuat BeeCoding tidak bertanggung jawab atas
      kerugian tidak langsung atau lanjutan, maupun atas hilangnya data atau tugas kuliah, yang timbul
      dari penggunaan layanan ini.
    </p>

    <h2>7. Pengakhiran</h2>
    <p>
      Anda dapat berhenti memakai layanan dan menghapus akun kapan saja dari pengaturan Akun. Operator
      dapat mengakhiri akses Anda di akhir mata kuliah atau jika syarat ini dilanggar.
    </p>

    <h2>8. Perubahan</h2>
    <p>
      Syarat ini dapat diperbarui bersama versi perangkat lunak yang baru. Tetap memakai layanan
      setelah perubahan berarti Anda menerima syarat yang diperbarui.
    </p>

    <h2>9. Kontak</h2>
    <p>Pertanyaan tentang syarat ini ditujukan kepada Operator instans ini.</p>
    </template>
  </LegalPage>
</template>
