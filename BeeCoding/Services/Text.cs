using System.Text.Json;

namespace BeeCoding.Services;

/// <summary>
/// The few texts the server writes itself (emails, push notifications) in the reader's language. The English text, written as a
/// composite format ({0}, {1}), is the key; <see cref="Id"/> maps it to Indonesian and a text with no entry stays English. The
/// language is the user's saved display preference (<c>beecoding.docLang</c> in <c>User.Preferences</c>), else the <c>X-Lang</c>
/// header the SPA sends. API error messages are NOT translated here: the browser does that (ClientApp/src/lib/i18n).
/// </summary>
public static class Text
{
    private const string PrefKey = "beecoding.docLang";

    public static string T(string lang, string en, params object[] args)
    {
        var f = lang == "id" && Id.TryGetValue(en, out var t) ? t : en;
        return args.Length == 0 ? f : string.Format(f, args);
    }

    /// <summary>"id" when the user's saved preferences say Indonesian, else the request's X-Lang header, else "en".</summary>
    public static string LangOf(string? preferencesJson, HttpRequest? request = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(preferencesJson)
                && JsonSerializer.Deserialize<Dictionary<string, string>>(preferencesJson) is { } p && p.TryGetValue(PrefKey, out var v))
                return v == "id" ? "id" : "en";
        }
        catch (JsonException) { /* unreadable preferences: fall through */ }
        return request?.Headers["X-Lang"].ToString() == "id" ? "id" : "en";
    }

    private static readonly Dictionary<string, string> Id = new()
    {
        // password reset
        ["Reset your BeeCoding password"] = "Atur ulang kata sandi BeeCoding Anda",
        ["Hi {0},\n\nSomeone asked to reset the password for your BeeCoding account. Use this link to choose a new one (it works once and expires in {1} minutes):\n\n{2}\n\nIf that wasn't you, ignore this email — your password stays as it is.\n"] =
            "Halo {0},\n\nSeseorang meminta pengaturan ulang kata sandi untuk akun BeeCoding Anda. Pakai tautan ini untuk memilih yang baru (hanya berlaku sekali dan kedaluwarsa dalam {1} menit):\n\n{2}\n\nJika itu bukan Anda, abaikan email ini — kata sandi Anda tidak berubah.\n",
        // email confirmation
        ["Confirm your BeeCoding email"] = "Konfirmasi email BeeCoding Anda",
        ["Hi {0},\n\nPlease confirm your email address for your BeeCoding account by opening this link (it works once and expires in {1} hours):\n\n{2}\n\nIf you didn't create a BeeCoding account, you can ignore this email.\n"] =
            "Halo {0},\n\nMohon konfirmasi alamat email untuk akun BeeCoding Anda dengan membuka tautan ini (hanya berlaku sekali dan kedaluwarsa dalam {1} jam):\n\n{2}\n\nJika Anda tidak membuat akun BeeCoding, abaikan email ini.\n",
        // sign-in / email-code mails
        ["{0} is your BeeCoding code"] = "{0} adalah kode BeeCoding Anda",
        ["Your BeeCoding sign-in code is {0}\n\nIt works once and expires in {1} minutes.\n\nIf you weren't trying to sign in, someone may know your password. Please change it."] =
            "Kode masuk BeeCoding Anda adalah {0}\n\nKode hanya berlaku sekali dan kedaluwarsa dalam {1} menit.\n\nJika Anda tidak sedang mencoba masuk, mungkin ada yang tahu kata sandi Anda. Mohon ubah kata sandi.",
        ["Your BeeCoding code to turn on email verification is {0}\n\nIt works once and expires in {1} minutes.\n\nIf you didn't ask for this, you can ignore this email."] =
            "Kode BeeCoding Anda untuk mengaktifkan verifikasi email adalah {0}\n\nKode hanya berlaku sekali dan kedaluwarsa dalam {1} menit.\n\nJika Anda tidak memintanya, abaikan email ini.",
        // notification mail + push
        ["BeeCoding: you have a new notification"] = "BeeCoding: Anda punya notifikasi baru",
        ["{0}\n\nOpen it: {1}{2}\n\nChoose which notifications you get by email: {1}/account"] =
            "{0}\n\nBuka: {1}{2}\n\nPilih notifikasi mana yang Anda terima lewat email: {1}/account",
        ["Test notification: push works on this device."] = "Notifikasi uji: push berfungsi di perangkat ini.",
        ["{0} reported your problem \"{1}\"{2}"] = "{0} melaporkan soal Anda \"{1}\"{2}",
        ["Your report on \"{0}\" was resolved by {1}{2}"] = "Laporan Anda pada \"{0}\" diselesaikan oleh {1}{2}",
        ["Your report on \"{0}\" was dismissed by {1}{2}"] = "Laporan Anda pada \"{0}\" ditolak oleh {1}{2}",
        ["{0} submitted \"{1}\" for review"] = "{0} mengirim \"{1}\" untuk ditinjau",
        ["{0} proposed the problem \"{1}\" for your board{2}"] = "{0} mengusulkan soal \"{1}\" untuk board Anda{2}",
        ["Your proposal \"{0}\" was approved by {1}{2}"] = "Usulan Anda \"{0}\" disetujui oleh {1}{2}",
        ["Your proposal \"{0}\" was declined by {1}{2}"] = "Usulan Anda \"{0}\" ditolak oleh {1}{2}",
        ["Your practice submission \"{0}\" was approved by {1}{2}"] = "Kiriman latihan Anda \"{0}\" disetujui oleh {1}{2}",
        ["Your practice submission \"{0}\" was declined by {1}{2}"] = "Kiriman latihan Anda \"{0}\" ditolak oleh {1}{2}",
        ["{0} invited you to the organization \"{1}\". Accept or decline it in your account."] = "{0} mengundang Anda ke organisasi \"{1}\". Terima atau tolak di akun Anda.",
        ["The session \"{0}\" on {1} is now open"] = "Sesi \"{0}\" di {1} sekarang dibuka",
        ["The session \"{0}\" on {1} closes within the hour"] = "Sesi \"{0}\" di {1} ditutup dalam satu jam",
        ["{0} new problems on {1}"] = "{0} soal baru di {1}",
        ["New problem on {0}: \"{1}\""] = "Soal baru di {0}: \"{1}\"",
        ["Sending grades of the board \"{0}\" to the LMS failed. Check the LTI settings."] = "Pengiriman nilai board \"{0}\" ke LMS gagal. Periksa pengaturan LTI.",
        ["Security notice for your account: {0}. If this was not you, reset your password now."] = "Pemberitahuan keamanan untuk akun Anda: {0}. Jika ini bukan Anda, segera atur ulang kata sandi.",
        ["{0} commented on your card{1}"] = "{0} berkomentar pada kartu Anda{1}",
        ["{0} reacted {1} to your card"] = "{0} bereaksi {1} pada kartu Anda",
    };
}
