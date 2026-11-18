// The feedback survey. Answers are saved by question id (SurveyController keeps them as an id → text map), so:
// never reuse an id for a different question; to retire a question just delete it (old answers stay in the data).
// type: 'scale' 1–5 (labels = low/high ends), 'nps' 0–10, 'choice' ([value, en, id] options, value is what is stored), 'text'.
// who: 'all' | 'student' | 'teacher'. Every text is {en, id}.
const S5 = { en: ['Strongly disagree', 'Strongly agree'], id: ['Sangat tidak setuju', 'Sangat setuju'] };
const YN = (a, b, c) => [['yes', ...a], ['no', ...b], ['na', ...c]];

export const SECTIONS = [
  { id: 'about', title: { en: 'About you', id: 'Tentang Anda' } },
  { id: 'use', title: { en: 'Using BeeCoding', id: 'Memakai BeeCoding' } },
  { id: 'student', title: { en: 'Solving problems', id: 'Mengerjakan soal' }, who: 'student' },
  { id: 'teacher', title: { en: 'Teaching with it', id: 'Mengajar dengan BeeCoding' }, who: 'teacher' },
  { id: 'end', title: { en: 'In your words', id: 'Dengan kata Anda' } },
];

export const QUESTIONS = [
  { id: 'freq', section: 'about', type: 'choice', who: 'all',
    q: { en: 'How often do you use BeeCoding?', id: 'Seberapa sering Anda memakai BeeCoding?' },
    options: [['daily', 'Daily', 'Setiap hari'], ['weekly', 'Weekly', 'Mingguan'], ['assigned', 'Only when there is an assignment', 'Hanya saat ada tugas'], ['rare', 'Rarely', 'Jarang']] },
  { id: 'device', section: 'about', type: 'choice', who: 'all',
    q: { en: 'Which device do you mostly use?', id: 'Perangkat apa yang paling sering Anda pakai?' },
    options: [['laptop', 'Laptop / desktop', 'Laptop / desktop'], ['phone', 'Phone', 'HP'], ['tablet', 'Tablet', 'Tablet']] },

  { id: 'ease', section: 'use', type: 'scale', who: 'all', labels: S5,
    q: { en: 'It was easy to get started with BeeCoding.', id: 'Mudah untuk mulai memakai BeeCoding.' } },
  { id: 'nps', section: 'use', type: 'nps', who: 'all',
    q: { en: 'How likely are you to recommend BeeCoding to a friend or colleague?', id: 'Seberapa besar kemungkinan Anda merekomendasikan BeeCoding ke teman atau kolega?' } },

  { id: 'statement', section: 'student', type: 'scale', who: 'student', labels: S5,
    q: { en: 'Problem statements are easy to understand.', id: 'Pernyataan soal mudah dipahami.' } },
  { id: 'verdict', section: 'student', type: 'scale', who: 'student', labels: S5,
    q: { en: 'The judge feedback (verdict, compiler messages) helps me fix my code.', id: 'Umpan balik juri (verdict, pesan compiler) membantu saya memperbaiki kode.' } },
  { id: 'editor', section: 'student', type: 'choice', who: 'student',
    q: { en: 'How often does the editor feel slow, or code suggestions fail to appear?', id: 'Seberapa sering editor terasa lambat atau saran kode tidak muncul?' },
    options: [['never', 'Never', 'Tidak pernah'], ['sometimes', 'Sometimes', 'Kadang'], ['often', 'Often', 'Sering']] },
  { id: 'board_feel', section: 'student', type: 'choice', who: 'student',
    q: { en: 'Seeing classmates’ progress on the board makes me…', id: 'Melihat progres teman di papan membuat saya…' },
    options: [['motivated', 'More motivated', 'Lebih termotivasi'], ['same', 'No different', 'Biasa saja'], ['pressured', 'Pressured', 'Tertekan']] },
  { id: 'visible', section: 'student', type: 'scale', who: 'student', labels: S5,
    q: { en: 'I am comfortable with my code and progress being visible to classmates.', id: 'Saya nyaman jika kode dan progres saya terlihat oleh teman sekelas.' } },
  { id: 'xp', section: 'student', type: 'choice', who: 'student',
    q: { en: 'Do XP, levels, streaks and the leaderboard make you practise more?', id: 'Apakah XP, level, streak, dan leaderboard membuat Anda lebih sering berlatih?' },
    options: [['yes', 'Yes', 'Ya'], ['no', 'No', 'Tidak'], ['ignore', 'I ignore them', 'Saya tidak peduli']] },
  { id: 'level', section: 'student', type: 'choice', who: 'student',
    q: { en: 'The practice problems are…', id: 'Soal di Practice…' },
    options: [['easy', 'Too easy', 'Terlalu mudah'], ['right', 'About right', 'Pas'], ['hard', 'Too hard', 'Terlalu sulit'], ['na', 'I have not used Practice', 'Belum pernah memakai Practice']] },
  { id: 'ai_help', section: 'student', type: 'choice', who: 'student',
    q: { en: 'The AI hint pointed me to the mistake without giving the answer away.', id: 'Petunjuk AI menunjukkan letak kesalahan tanpa memberi jawabannya.' },
    options: [...YN(['Yes', 'Ya'], ['No', 'Tidak'], ['I have not used AI hints', 'Belum pernah memakai petunjuk AI']), ['leak', 'It gave away too much', 'Terlalu banyak membocorkan jawaban']] },

  { id: 't_create', section: 'teacher', type: 'scale', who: 'teacher', labels: S5,
    q: { en: 'Creating a problem (statement, sample and hidden tests) is easy.', id: 'Membuat soal (pernyataan, tes contoh, tes tersembunyi) itu mudah.' } },
  { id: 't_bank', section: 'teacher', type: 'scale', who: 'teacher', labels: S5,
    q: { en: 'The problem bank (searching, copying to a board, sharing) meets my needs.', id: 'Bank soal (mencari, menyalin ke papan, berbagi) memenuhi kebutuhan saya.' } },
  { id: 't_monitor', section: 'teacher', type: 'choice', who: 'teacher',
    q: { en: 'Which view do you use to follow the class?', id: 'Tampilan mana yang Anda pakai untuk memantau kelas?' },
    options: [['wall', 'Wall', 'Wall'], ['grid', 'Grid', 'Grid'], ['both', 'Both', 'Keduanya']] },
  { id: 't_exam', section: 'teacher', type: 'scale', who: 'teacher', labels: S5,
    q: { en: 'Exam mode, group schedules and hiding answers give me enough control.', id: 'Mode ujian, jadwal grup, dan menyembunyikan jawaban memberi saya kendali yang cukup.' } },
  { id: 't_fair', section: 'teacher', type: 'scale', who: 'teacher', labels: S5,
    q: { en: 'I trust the judge’s verdicts to be fair and correct.', id: 'Saya percaya verdict juri adil dan benar.' } },

  { id: 'like', section: 'end', type: 'text', who: 'all',
    q: { en: 'What do you like most about BeeCoding?', id: 'Apa yang paling Anda sukai dari BeeCoding?' } },
  { id: 'annoy', section: 'end', type: 'text', who: 'all',
    q: { en: 'What bothers you most, or what would you change?', id: 'Apa yang paling mengganggu, atau ingin Anda ubah?' } },
  { id: 'want', section: 'end', type: 'text', who: 'all',
    q: { en: 'Which feature do you wish BeeCoding had?', id: 'Fitur apa yang Anda harapkan ada di BeeCoding?' } },
];

export const forWho = (who) => QUESTIONS.filter((q) => q.who === 'all' || q.who === who);
