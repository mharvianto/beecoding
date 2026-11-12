<script setup>
import LegalPage from './LegalPage.vue';
import { docLang } from '../lib/docLang';
</script>

<template>
  <LegalPage :title="docLang === 'id' ? 'Kebijakan Privasi' : 'Privacy Policy'" updated="7 September 2026">
    <template v-if="docLang === 'en'">
    <p>
      BeeCoding is a self-hosted classroom platform. This instance is operated by your
      school, teacher, or institution (the <strong>“Operator”</strong>), who is responsible
      for how your data is handled and is your point of contact for any privacy request.
      This page describes what the software collects and does by design.
    </p>

    <h2>1. Information we collect</h2>
    <ul>
      <li><strong>Account data</strong> — your email address, display name, chosen role
        (student or teacher), and a one-way hashed password. Plain passwords are never stored.</li>
      <li><strong>Learning activity</strong> — code you run and submit, judge results
        (verdict, runtime, memory), experience points, board and class memberships, wall
        posts, notes, comments and reactions you create.</li>
      <li><strong>Technical data</strong> — a single authentication cookie to keep you
        signed in, and standard server logs (timestamps, IP address, error traces) kept for
        security and debugging.</li>
      <li><strong>AI tutor prompts</strong> — only if the Operator has enabled the optional
        AI tutor. See section 4.</li>
    </ul>

    <h2>2. How we use it</h2>
    <ul>
      <li>To run the service: authenticate you, compile and judge your code, show class
        progress, award XP and build leaderboards.</li>
      <li>To let teachers see and grade the work of students in their classes.</li>
      <li>To keep the service secure (rate limiting, abuse prevention, debugging).</li>
    </ul>
    <p>We do not sell your data or use it for advertising.</p>

    <h2>3. Cookies</h2>
    <p>
      One first-party cookie (<code>beecoding.auth</code>) is used to keep you logged in. It
      is <code>HttpOnly</code> and, over HTTPS, <code>Secure</code>. Some preferences (theme,
      chosen language, panel sizes) are stored in your browser's local storage and never
      leave your device. No third-party or tracking cookies are used.
    </p>

    <h2>4. Third parties</h2>
    <p>
      The core platform sends your data to no third party. If the Operator enables the
      optional <strong>AI tutor</strong>, then when you request a hint the problem
      statement, sample tests, your current code and any error output are sent to the
      AI provider configured by the Operator (for example NVIDIA NIM or an OpenAI-compatible
      endpoint) to generate that hint. Ask your Operator which provider is in use and review
      that provider's own privacy terms. The platform keeps only how many hints you asked for on
      each problem (not what you asked or the reply), and the teacher of a board can see that
      count for the board's problems to know who may need help.
    </p>

    <h2>5. Data retention and deletion</h2>
    <ul>
      <li>Your data is kept for as long as your account exists on this instance.</li>
      <li>You can delete your account from <strong>Account settings</strong>. This
        permanently removes your profile, submissions and practice progress; if you are a
        teacher it also deletes boards you own and the work submitted to them.</li>
      <li>The Operator may also remove data as part of end-of-term or backup policies.</li>
    </ul>

    <h2>6. Security</h2>
    <p>
      Passwords are hashed with PBKDF2. Submitted code runs in a resource-limited sandbox.
      Traffic should be served over HTTPS. No system is perfectly secure; the code judge is
      classroom-grade isolation, not a guarantee against a determined attacker.
    </p>

    <h2>7. Children and educational use</h2>
    <p>
      BeeCoding is intended for use within a class under the supervision of an educator. Where
      students are minors, the school or Operator acts as the data controller and is
      responsible for obtaining any consent required by local law.
    </p>

    <h2>8. Changes</h2>
    <p>
      This policy may change as the software evolves. The “last updated” date above reflects
      the current version shipped with this instance.
    </p>

    <h2>9. Contact</h2>
    <p>
      Direct privacy questions, access requests or deletion requests to the Operator of this
      instance (usually your teacher or school IT).
    </p>
    </template>
    <template v-else>
    <p>
      BeeCoding adalah platform kelas yang dihosting sendiri. Instans ini dijalankan oleh sekolah,
      guru, atau institusi Anda (<strong>“Operator”</strong>), yang bertanggung jawab atas cara data
      Anda ditangani dan menjadi tempat Anda mengajukan permintaan terkait privasi. Halaman ini
      menjelaskan apa yang dikumpulkan dan dilakukan perangkat lunak ini sesuai rancangannya.
    </p>

    <h2>1. Informasi yang kami kumpulkan</h2>
    <ul>
      <li><strong>Data akun</strong> — alamat email, nama tampilan, peran yang dipilih (siswa atau
        guru), dan kata sandi yang di-hash satu arah. Kata sandi asli tidak pernah disimpan.</li>
      <li><strong>Aktivitas belajar</strong> — kode yang Anda jalankan dan kirim, hasil juri
        (verdict, waktu, memori), poin pengalaman, keanggotaan board dan kelas, serta kiriman di
        dinding, catatan, komentar, dan reaksi yang Anda buat.</li>
      <li><strong>Data teknis</strong> — satu cookie autentikasi agar Anda tetap masuk, dan log
        server standar (waktu, alamat IP, jejak galat) yang disimpan untuk keamanan dan debugging.</li>
      <li><strong>Prompt tutor AI</strong> — hanya jika Operator mengaktifkan tutor AI opsional.
        Lihat bagian 4.</li>
    </ul>

    <h2>2. Bagaimana kami memakainya</h2>
    <ul>
      <li>Untuk menjalankan layanan: mengautentikasi Anda, mengompilasi dan menilai kode Anda,
        menampilkan kemajuan kelas, memberi XP, dan membuat papan peringkat.</li>
      <li>Agar guru dapat melihat dan menilai pekerjaan siswa di kelas mereka.</li>
      <li>Untuk menjaga keamanan layanan (pembatasan laju, pencegahan penyalahgunaan, debugging).</li>
    </ul>
    <p>Kami tidak menjual data Anda dan tidak memakainya untuk iklan.</p>

    <h2>3. Cookie</h2>
    <p>
      Satu cookie pihak pertama (<code>beecoding.auth</code>) dipakai agar Anda tetap masuk. Cookie itu
      <code>HttpOnly</code> dan, lewat HTTPS, <code>Secure</code>. Beberapa preferensi (tema, bahasa
      yang dipilih, ukuran panel) disimpan di penyimpanan lokal browser Anda dan tidak pernah
      meninggalkan perangkat Anda. Tidak ada cookie pihak ketiga atau pelacak yang dipakai.
    </p>

    <h2>4. Pihak ketiga</h2>
    <p>
      Platform inti tidak mengirim data Anda ke pihak ketiga mana pun. Jika Operator mengaktifkan
      <strong>tutor AI</strong> opsional, maka saat Anda meminta petunjuk, soal, contoh test, kode Anda
      saat ini, dan keluaran galat apa pun dikirim ke penyedia AI yang dikonfigurasi Operator (misalnya
      NVIDIA NIM atau endpoint yang kompatibel dengan OpenAI) untuk membuat petunjuk itu. Tanyakan ke
      Operator penyedia mana yang dipakai dan baca ketentuan privasi penyedia tersebut. Platform hanya
      menyimpan berapa kali Anda meminta petunjuk pada tiap soal (bukan isi pertanyaan maupun
      jawabannya), dan guru sebuah papan dapat melihat jumlah itu untuk soal di papannya agar tahu
      siapa yang mungkin butuh bantuan.
    </p>

    <h2>5. Penyimpanan dan penghapusan data</h2>
    <ul>
      <li>Data Anda disimpan selama akun Anda ada di instans ini.</li>
      <li>Anda dapat menghapus akun dari <strong>pengaturan Akun</strong>. Ini menghapus permanen
        profil, kiriman, dan kemajuan latihan Anda; jika Anda guru, board milik Anda beserta
        pekerjaan yang dikumpulkan ke dalamnya juga ikut terhapus.</li>
      <li>Operator juga dapat menghapus data sebagai bagian dari kebijakan akhir semester atau
        pencadangan.</li>
    </ul>

    <h2>6. Keamanan</h2>
    <p>
      Kata sandi di-hash dengan PBKDF2. Kode yang dikirim berjalan di sandbox dengan sumber daya
      terbatas. Lalu lintas sebaiknya dilayani lewat HTTPS. Tidak ada sistem yang sepenuhnya aman;
      juri kode hanya berisolasi tingkat ruang kelas, bukan jaminan terhadap penyerang yang bertekad.
    </p>

    <h2>7. Anak-anak dan penggunaan pendidikan</h2>
    <p>
      BeeCoding dimaksudkan untuk dipakai di dalam kelas di bawah pengawasan pendidik. Jika siswa
      masih di bawah umur, sekolah atau Operator bertindak sebagai pengendali data dan bertanggung
      jawab memperoleh persetujuan yang diwajibkan hukum setempat.
    </p>

    <h2>8. Perubahan</h2>
    <p>
      Kebijakan ini dapat berubah seiring berkembangnya perangkat lunak. Tanggal “terakhir
      diperbarui” di atas menunjukkan versi terkini yang dikirim bersama instans ini.
    </p>

    <h2>9. Kontak</h2>
    <p>
      Arahkan pertanyaan privasi, permintaan akses, atau permintaan penghapusan kepada Operator
      instans ini (biasanya guru atau TI sekolah Anda).
    </p>
    </template>
  </LegalPage>
</template>
