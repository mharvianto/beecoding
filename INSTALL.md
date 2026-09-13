# BeeCoding — Panduan Instalasi

Papan gaya Padlet + online judge C/C++. Backend ASP.NET Core 10, frontend Vue 3
(di-build ke `wwwroot/`), database SQLite, realtime SignalR.

---

## 1. Prasyarat

| Komponen | Versi | Catatan |
|---|---|---|
| **.NET SDK** | **10.0** | `dotnet --version` → `10.0.x`. Belum di repo semua distro — lihat cara pasang di bawah. |
| **Node.js + npm** | **20+** (diuji dengan 24) | untuk membangun SPA Vue |
| **gcc & g++** | 11+ (diuji 13) | **wajib di PATH** — judge meng-compile kode C/C++ murid |
| **OS** | **Linux** | Judge memakai `fork` + `setrlimit` (helper C khusus POSIX). Di Windows/macOS backend tetap jalan, tapi *Run/Submit* tidak. |
| bubblewrap (`bwrap`) | opsional | isolasi filesystem/jaringan. Tanpa ini → mode *rlimits-only* (limit waktu & memori tetap dipaksakan). |
| **clangd** | opsional (14+, diuji 18) | IntelliSense C/C++ di editor (autocomplete, hover, diagnostik). Non-aktif secara default (`Lsp:Enabled=false`); tanpa clangd editor jatuh ke completion berbasis kata. |

Yang **tidak perlu** dipasang:

- Font — DejaVu TTF sudah disertakan di `BeeCoding/Assets/fonts/` (dipakai untuk render soal-terenkripsi).
- Native SkiaSharp — paket `SkiaSharp.NativeAssets.Linux.NoDependencies` sudah membundel binari (jalan di Ubuntu 24.04 tanpa dependensi tambahan).

### Pasang prasyarat di Ubuntu/Debian

**.NET 10 SDK** — paket `dotnet-sdk-10.0` belum tentu ada di repo distro
(mis. Debian 13/trixie belum). Cara paling andal adalah script resmi Microsoft:

```bash
sudo apt-get update && sudo apt-get install -y libicu-dev ca-certificates
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0            # → $HOME/.dotnet

# tambahkan ke ~/.bashrc agar permanen:
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"
dotnet --version                                 # → 10.0.x
```

Alternatif via apt (feed Microsoft, hanya kalau config distro-mu tersedia):

```bash
wget https://packages.microsoft.com/config/debian/13/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0
# untuk Ubuntu ganti "debian/13" → mis. "ubuntu/24.04"
```

> Jika `dotnet` mengeluh soal ICU/globalization: `export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`.

**Sisanya:**

```bash
# Node.js 20+ (contoh via nodesource)
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash - && sudo apt-get install -y nodejs

# toolchain judge
sudo apt-get install -y build-essential

# opsional: sandbox filesystem
sudo apt-get install -y bubblewrap

# opsional: IntelliSense C/C++ di editor (lihat §5 untuk mengaktifkan)
sudo apt-get install -y clangd
```

---

## 2. Ambil kode

```bash
git clone <URL-repo> beecoding
cd beecoding
```

---

## 3. Menjalankan untuk pengembangan (dua terminal)

### Terminal 1 — backend

```bash
cd BeeCoding
dotnet run
```

- Mendengarkan di **http://localhost:5048** (profil `http` di `Properties/launchSettings.json`).
- Saat pertama kali: `beecoding.db` dibuat, migrasi dijalankan, lalu data demo + **bank 81 soal** di-seed otomatis.
- Startup akan mencetak mode sandbox, mis.
  `Sandbox mode: rlimits only (bwrap ...)` atau `bubblewrap + rlimits`.

### Terminal 2 — frontend (Vite dev server)

```bash
cd BeeCoding/ClientApp
npm install
npm run dev
```

- Mendengarkan di **http://localhost:5173** dan mem-proxy `/api` + `/hubs` ke `http://localhost:5048`.
- Kalau backend memakai port lain: `BACKEND_URL=http://localhost:5001 npm run dev`.

Buka **http://localhost:5173**.

### Akun demo

| Peran | Kredensial |
|---|---|
| Guru | `teacher@demo.test` / `password` |
| Board demo | `/boards/demo-board` — kode gabung **`DEMO01`** |
| Murid | daftar sendiri dari halaman **Register** |

---

## 4. Build satu proses (produksi)

```bash
cd BeeCoding
dotnet publish -c Release -o out
```

- Target MSBuild `BuildClientApp` otomatis menjalankan `npm ci && npm run build` ke `wwwroot/`,
  lalu menyalinnya ke `out/wwwroot/`. Lewati build SPA dengan `-p:BuildClient=false` bila
  sudah kamu build sendiri.

Verifikasi setelah publish — folder ini **harus ada**:

```bash
ls out/wwwroot/index.html          # kalau tidak ada → SPA tidak ikut ter-publish
```

> **Jika `/` memberi HTTP 404** dan log menampilkan `The WebRootPath was not found: .../out/wwwroot`,
> berarti `out/wwwroot` kosong. Perbaiki cepat: `cp -r wwwroot out/` lalu jalankan ulang,
> atau bangun SPA lebih dulu sebagai langkah terpisah:
> ```bash
> (cd ClientApp && npm ci && npm run build)      # menulis ../wwwroot
> dotnet publish -c Release -o out -p:BuildClient=false
> ```

Jalankan:

```bash
cd out
ASPNETCORE_URLS="http://0.0.0.0:8080" \
ASPNETCORE_ENVIRONMENT=Production \
./BeeCoding
```

- Satu port melayani SPA + REST API + SignalR (`app.MapFallbackToFile("index.html")` menangani route klien).
- Taruh **reverse proxy** (nginx/Caddy) di depan untuk TLS dan **upgrade WebSocket** (dibutuhkan SignalR).

Contoh lokasi nginx:

```nginx
location / {
    proxy_pass         http://127.0.0.1:8080;
    proxy_http_version 1.1;
    proxy_set_header   Upgrade $http_upgrade;
    proxy_set_header   Connection "upgrade";
    proxy_set_header   Host $host;
    proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header   X-Forwarded-Proto $scheme;
}
```

---

## 4A. Jalankan sebagai service (systemd)

Agar tetap hidup setelah SSH ditutup, otomatis mulai saat boot, dan restart bila crash.
Asumsi: sudah di-publish ke `/srv/beecoding/BeeCoding/out`, dan .NET dipasang di
`~/.dotnet` (lewat `dotnet-install.sh`) untuk user `harvianto`.

**1) Siapkan folder data (di luar folder `out` supaya aman saat re-publish):**

```bash
sudo mkdir -p /srv/beecoding/data /srv/beecoding/.judge
sudo chown -R harvianto:harvianto /srv/beecoding
```

**2) Buat unit file:**

```bash
sudo tee /etc/systemd/system/beecoding.service > /dev/null <<'EOF'
[Unit]
Description=BeeCoding (Padlet + C/C++ online judge)
After=network-online.target
Wants=network-online.target

[Service]
Type=notify            # butuh build yang sudah memakai UseSystemd(); kalau belum, ganti: Type=simple
User=harvianto
Group=harvianto
WorkingDirectory=/srv/beecoding/BeeCoding/out
ExecStart=/srv/beecoding/BeeCoding/out/BeeCoding

Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:8080
Environment=DOTNET_ROOT=/home/harvianto/.dotnet
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
# nilai mengandung spasi -> WAJIB dikutip penuh, kalau tidak systemd memecahnya
Environment="ConnectionStrings__Default=Data Source=/srv/beecoding/data/beecoding.db"
Environment=Judge__WorkRoot=/srv/beecoding/.judge

Restart=on-failure
RestartSec=5
TimeoutStartSec=60

[Install]
WantedBy=multi-user.target
EOF
```

> `DOTNET_ROOT` wajib karena .NET dipasang di home, bukan sistem. Kalau kamu memasang
> **ASP.NET Core Runtime** sistem-wide (`sudo .../dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/lib/dotnet`),
> baris `DOTNET_ROOT` bisa dihapus dan `User=` boleh diganti user khusus berprivilege rendah.

**3) Aktifkan & jalankan:**

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now beecoding
systemctl status beecoding --no-pager
journalctl -u beecoding -f            # ikuti log (Ctrl+C untuk keluar)
```

Buka `http://<ip-server>:8080`.

**Perintah harian:**

```bash
sudo systemctl restart beecoding
sudo systemctl stop beecoding
sudo systemctl disable --now beecoding      # matikan permanen
journalctl -u beecoding --since "10 min ago"
```

**Update ke versi baru:**

```bash
cd /srv/beecoding && git pull
cd BeeCoding && ~/.dotnet/dotnet publish -c Release -o out
sudo systemctl restart beecoding
```

**Catatan:**

- DB pindah ke `/srv/beecoding/data/beecoding.db` (path absolut). Saat pertama start via
  service, DB baru dibuat & di-seed ulang. Kalau mau bawa data lama:
  `mv /srv/beecoding/BeeCoding/out/beecoding.db* /srv/beecoding/data/` sebelum `enable`.
- `g++` ada di `/usr/bin` sehingga terjangkau PATH default systemd — tidak perlu setting tambahan.
- Judge menjalankan kode C++ murid sebagai user service. Untuk produksi sungguhan,
  pakai user khusus + pertimbangkan isolasi lebih kuat (lihat *Security note* di `README.md`).

---

## 4B. nginx (reverse proxy) + SSL/HTTPS

BeeCoding dijalankan di `127.0.0.1:8080`, nginx di depan menangani TLS + WebSocket.

### 1) Kunci app hanya ke localhost

Di `/etc/systemd/system/beecoding.service` ubah:

```
Environment=ASPNETCORE_URLS=http://127.0.0.1:8080
```

lalu `sudo systemctl daemon-reload && sudo systemctl restart beecoding`.
(Dukungan `X-Forwarded-Proto` sudah ada di aplikasi — `UseForwardedHeaders`, jadi cookie
dan `Request.Scheme` mengikuti HTTPS.)

### 2) Pasang nginx

```bash
sudo apt-get install -y nginx
```

### 3) Config situs — mulai **HTTP-only**

Blok `map`/`upstream`/`proxy` di bawah dipakai oleh kedua opsi TLS. Jangan tulis blok
`listen 443` dulu — certbot yang menambahkannya (Opsi A), atau kamu tambah manual setelah
sertifikat dibuat (Opsi B).

```bash
sudo tee /etc/nginx/sites-available/beecoding > /dev/null <<'EOF'
# WebSocket upgrade (dipakai SignalR di /hubs)
map $http_upgrade $connection_upgrade { default upgrade; '' close; }

upstream beecoding { server 127.0.0.1:8080; keepalive 32; }

server {
    listen 80;
    listen [::]:80;
    server_name beecoding.example.com;     # <-- DOMAIN ASLI (Opsi A) atau IP LAN (Opsi B)

    client_max_body_size 4m;              # kiriman kode murid (<=200 KB) + margin

    location / {
        proxy_pass http://beecoding;
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade           $http_upgrade;
        proxy_set_header Connection        $connection_upgrade;
    }

    # SignalR (/hubs) + clangd LSP (/lsp): koneksi persisten -> timeout panjang
    location ~ ^/(hubs|lsp)/ {
        proxy_pass http://beecoding;
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade           $http_upgrade;
        proxy_set_header Connection        $connection_upgrade;
        proxy_read_timeout  3600s;
        proxy_send_timeout  3600s;
        proxy_buffering     off;
    }
}
EOF

sudo ln -sf /etc/nginx/sites-available/beecoding /etc/nginx/sites-enabled/beecoding
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t && sudo systemctl reload nginx
```

### 4) Sertifikat — pilih salah satu

**Opsi A — Let's Encrypt** — hanya jika ada **domain publik** yang resolve ke **IP publik**
server ini dan **port 80 terbuka dari internet**:

```bash
sudo apt-get install -y certbot python3-certbot-nginx
sudo certbot --nginx -d beecoding.example.com
```

certbot mengubah `server` HTTP tadi menjadi HTTPS (menambah `listen 443 ssl`, path
sertifikat `/etc/letsencrypt/live/...`, dan redirect 80→443), lalu memasang timer
perpanjangan (`systemctl list-timers 'certbot*'`). Selesai.

> Kalau server hanya di LAN / tanpa domain publik, certbot **tidak akan berhasil**
> (validasi HTTP-01 gagal). Pakai Opsi B.

**Opsi B — self-signed (LAN / tanpa domain):**

```bash
# 1) buat sertifikat DULU
sudo openssl req -x509 -nodes -newkey rsa:2048 -days 825 \
  -keyout /etc/ssl/private/beecoding.key \
  -out /etc/ssl/certs/beecoding.crt \
  -subj "/CN=192.168.50.7" \
  -addext "subjectAltName=IP:192.168.50.7"        # samakan dengan alamat yang dipakai klien
```

```bash
# 2) baru ganti config: 80 -> redirect, tambah server 443
sudo tee /etc/nginx/sites-available/beecoding > /dev/null <<'EOF'
map $http_upgrade $connection_upgrade { default upgrade; '' close; }
upstream beecoding { server 127.0.0.1:8080; keepalive 32; }

server {
    listen 80;
    listen [::]:80;
    server_name 192.168.50.7;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    http2 on;
    server_name 192.168.50.7;

    ssl_certificate     /etc/ssl/certs/beecoding.crt;
    ssl_certificate_key /etc/ssl/private/beecoding.key;
    ssl_protocols       TLSv1.2 TLSv1.3;

    client_max_body_size 4m;

    location / {
        proxy_pass http://beecoding;
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade           $http_upgrade;
        proxy_set_header Connection        $connection_upgrade;
    }
    # SignalR (/hubs) + clangd LSP (/lsp)
    location ~ ^/(hubs|lsp)/ {
        proxy_pass http://beecoding;
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade           $http_upgrade;
        proxy_set_header Connection        $connection_upgrade;
        proxy_read_timeout  3600s;
        proxy_send_timeout  3600s;
        proxy_buffering     off;
    }
}
EOF
sudo nginx -t && sudo systemctl reload nginx
```

Browser akan menandai "not trusted" (wajar untuk sertifikat sendiri) — lanjutkan saja,
atau impor `beecoding.crt` ke *trust store* perangkat klien. Alternatif yang otomatis
dipercaya di jaringan lokal: pakai **mkcert**.

**Opsi C — di belakang CGNAT / ISP blokir port 80-443 (Cloudflare Tunnel):**

Sertifikat valid tanpa membuka port apa pun. Butuh **domain sendiri** yang dikelola
Cloudflare (`*.synology.me` / DDNS gratis tidak bisa).

```bash
curl -L https://pkg.cloudflare.com/cloudflare-main.gpg | sudo tee /usr/share/keyrings/cloudflare-main.gpg >/dev/null
echo "deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared any main" | sudo tee /etc/apt/sources.list.d/cloudflared.list
sudo apt-get update && sudo apt-get install -y cloudflared

cloudflared tunnel login
cloudflared tunnel create beecoding
cloudflared tunnel route dns beecoding beecoding.domainmu.com

mkdir -p ~/.cloudflared && cat > ~/.cloudflared/config.yml <<EOF
tunnel: beecoding
credentials-file: $HOME/.cloudflared/<TUNNEL-ID>.json
ingress:
  - hostname: beecoding.domainmu.com
    service: http://127.0.0.1:8080
  - service: http_status:404
EOF

sudo cloudflared service install
sudo systemctl enable --now cloudflared
```

nginx tidak wajib di jalur ini (tunnel langsung ke `127.0.0.1:8080`); WebSocket SignalR
didukung Cloudflare. TLS ditangani di edge Cloudflare.

### 5) Firewall & uji

```bash
sudo apt-get install -y ufw && sudo ufw allow 'Nginx Full' && sudo ufw allow OpenSSH && sudo ufw enable
```

Buka `https://<domain-atau-IP>/`. Uji API: `curl -k https://<host>/api/auth/me` → `401`.

### Catatan

| Masalah | Solusi |
|---|---|
| SignalR putus-nyambung / "WebSocket closed" | Pastikan blok `map $http_upgrade` ada dan header `Upgrade`/`Connection` diteruskan. |
| Login berhasil tapi langsung ter-logout | app harus di belakang HTTPS **dan** menerima `X-Forwarded-Proto` (sudah default). Jangan campur akses `http://` dan `https://`. |
| 413 Request Entity Too Large saat submit | naikkan `client_max_body_size`. |
| 502 Bad Gateway | `beecoding.service` mati / bukan di `127.0.0.1:8080`. Cek `systemctl status beecoding`. |
| certbot: `cannot load certificate ".../beecoding.crt"` saat `nginx -t` | Config sudah punya blok `listen 443 ssl` menunjuk file yang belum ada. Mulai dari config **HTTP-only** (langkah 3), baru jalankan `certbot --nginx`. |
| certbot: `Timeout during connect (likely firewall problem)` | DNS benar, tapi port 80 dari internet tidak sampai ke server (ISP blokir / NAT ganda / CGNAT). Buka port 80+443 di router, atau pakai **Opsi C (Cloudflare Tunnel)**, atau **Opsi B (self-signed)** untuk LAN. |
| certbot: challenge gagal / `NXDOMAIN` | Domain tidak resolve ke IP publik server ini. Perbaiki DNS/DDNS, atau Opsi B/C. |
| certbot: `Could not automatically find a matching server block` | `server_name` di config nginx masih placeholder. Set `server_name <domain-asli>;`, `reload`, lalu `sudo certbot install --cert-name <domain>`. |

---

## 4C. Jalan di subpath (mis. `/beecoding`) alih-alih root domain

Berguna kalau domain/nginx yang sama juga melayani aplikasi lain. Dua sisi harus sinkron:
build SPA dengan base path itu, dan backend diberi tahu `PathBase` yang sama.

### 1) Build SPA dengan base path

```bash
cd BeeCoding/ClientApp
VITE_BASE_PATH=/beecoding/ npm ci && VITE_BASE_PATH=/beecoding/ npm run build   # tulis ../wwwroot
```

(harus diakhiri `/`). Kalau pakai `dotnet publish` satu-proses (§4), set env var yang sama
sebelum menjalankannya supaya target `BuildClientApp` memakainya:

```bash
cd BeeCoding
VITE_BASE_PATH=/beecoding/ dotnet publish -c Release -o out
```

### 2) Backend: `PathBase`

Tambahkan `PathBase=/beecoding` (tanpa `/` di akhir) sebagai environment variable service —
di `/etc/systemd/system/beecoding.service` (lihat §4A):

```
Environment=PathBase=/beecoding
```

lalu `sudo systemctl daemon-reload && sudo systemctl restart beecoding`. Ini membuat
Kestrel mengenali prefix untuk static files/routing, dan cookie login otomatis dibatasi ke
path itu (`CookieBuilder` mem-default `Path` ke `PathBase`).

### 3) nginx: **jangan** strip prefix-nya

Beda dari §4B — di sini prefix **diteruskan apa adanya** ke backend (bukan dipangkas),
karena SPA dan backend sama-sama sudah tahu mereka hidup di `/beecoding`:

```nginx
map $http_upgrade $connection_upgrade { default upgrade; '' close; }
upstream beecoding { server 127.0.0.1:8080; keepalive 32; }

server {
    listen 80;
    server_name example.com;   # domain yang sama dipakai aplikasi lain juga

    client_max_body_size 4m;

    location /beecoding/ {
        proxy_pass http://beecoding;            # TANPA trailing slash — path diteruskan utuh
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade           $http_upgrade;
        proxy_set_header Connection        $connection_upgrade;
    }

    location ~ ^/beecoding/(hubs|lsp)/ {
        proxy_pass http://beecoding;
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade           $http_upgrade;
        proxy_set_header Connection        $connection_upgrade;
        proxy_read_timeout  3600s;
        proxy_send_timeout  3600s;
        proxy_buffering     off;
    }

    # ... location blok aplikasi lain di domain yang sama ada di sini ...
}
```

`sudo nginx -t && sudo systemctl reload nginx`, lalu buka `https://example.com/beecoding/`.

### Catatan

- `VITE_BASE_PATH` dan `PathBase` **harus** sama persis (modulo trailing slash: yang satu
  butuh `/`, yang lain tidak boleh punya). Beda salah satunya → asset 404 atau cookie tidak
  terkirim.
- Balik ke root domain kapan saja: hapus `PathBase` dari service, build ulang SPA tanpa
  `VITE_BASE_PATH` (default `/`), pulihkan `location /` biasa (§4B). Tidak ada perubahan
  kode lain yang diperlukan — semua path di frontend sudah relatif terhadap base ini.
- `curl -k https://example.com/beecoding/api/auth/me` harus balas `401` (bukan 404) kalau
  semuanya tersambung benar.

---

## 4D. Auto-deploy dengan GitHub Actions self-hosted runner

Melengkapi prosedur update manual di §4A ("Update ke versi baru"). Alih-alih SSH manual
tiap ada perubahan, runner GitHub Actions dipasang **langsung di VM produksi** dan
menjalankan 3 perintah update yang sama itu setiap ada push ke `master`.

**Kenapa self-hosted (bukan runner GitHub biasa yang SSH masuk):** runner ini
long-polling **keluar** ke `github.com` lewat HTTPS, jadi **tidak perlu buka port SSH
inbound** dari internet ke VM. Log deploy tetap muncul di tab **Actions** repo.

> Repo ini **private** — jangan pernah nyalakan opsi "Allow fork pull requests to run
> workflows" untuk runner ini. Siapa pun yang bisa push ke `master` efektif bisa
> menjalankan perintah apa pun di VM lewat isi file workflow.

### 1) Siapkan folder & user (kalau belum ikuti §4A)

Runner dijalankan sebagai user yang sama dengan yang menjalankan `beecoding.service`
(mis. `harvianto`), supaya bisa `git pull` di `/srv/beecoding` tanpa masalah izin.

### 2) Daftarkan runner dari GitHub

Di halaman repo: **Settings → Actions → Runners → New self-hosted runner**, pilih
**Linux / x64**. GitHub menampilkan 4 blok perintah unik untuk repo ini (token
registrasinya kadaluarsa ~1 jam, jadi ambil langsung dari halaman itu, jangan disalin
dari dokumen ini). Pola umumnya:

```bash
# di VM produksi, sebagai user harvianto
mkdir -p ~/actions-runner && cd ~/actions-runner
curl -o actions-runner-linux-x64.tar.gz -L https://github.com/actions/runner/releases/download/vX.Y.Z/actions-runner-linux-x64-X.Y.Z.tar.gz
tar xzf ./actions-runner-linux-x64.tar.gz

# token & URL persis dari halaman "New self-hosted runner" tadi
./config.sh --url https://github.com/<owner>/<repo> --token <TOKEN_DARI_GITHUB>
```

Nama runner dan label boleh default (`self-hosted`), atau tambah label kustom (mis.
`beecoding-prod`) kalau nanti mau lebih dari satu runner.

### 3) Jalankan sebagai service (biar hidup setelah SSH ditutup, auto-start saat boot)

```bash
cd ~/actions-runner
sudo ./svc.sh install
sudo ./svc.sh start
sudo ./svc.sh status
```

### 4) Izinkan runner me-restart service TANPA password

Runner butuh `sudo systemctl restart beecoding`, tapi **jangan** beri `sudo` penuh —
scope ke satu perintah itu saja:

```bash
sudo visudo -f /etc/sudoers.d/beecoding-deploy
```

Isi dengan (ganti `harvianto` kalau user service-mu beda):

```
harvianto ALL=(root) NOPASSWD: /bin/systemctl restart beecoding
```

```bash
sudo chmod 0440 /etc/sudoers.d/beecoding-deploy
sudo visudo -c   # validasi syntax sebelum keluar
```

### 5) Workflow file

Sudah ada di repo ini: [`.github/workflows/deploy.yml`](.github/workflows/deploy.yml).
Isinya persis prosedur §4A — job ini **tidak** `actions/checkout` (supaya tidak menimpa
`appsettings.json`/`out/` lokal di server), cukup `git pull` di clone yang sudah ada:

```yaml
on:
  push:
    branches: [master]

jobs:
  deploy:
    runs-on: [self-hosted]
    steps:
      - run: cd /srv/beecoding && git pull --ff-only origin master
      - run: cd /srv/beecoding/BeeCoding && "$HOME/.dotnet/dotnet" publish -c Release -o out
      - run: sudo systemctl restart beecoding
      - run: sleep 3 && curl -fsS http://127.0.0.1:8080/health
```

Sesuaikan path (`/srv/beecoding`), nama service (`beecoding`), dan port health check
kalau setup-mu beda dari §4A/§4B.

### 6) Uji coba

Push apa saja ke `master`, lalu buka tab **Actions** di GitHub — job `deploy` harus
muncul, jalan di runner-mu (bukan runner GitHub-hosted), dan hijau kalau `/health`
membalas 200. Kalau merah di step `git pull`, biasanya ada perubahan lokal tak
ter-commit di `/srv/beecoding` (mis. `beecoding.db` ikut ke-track tanpa sengaja) —
bereskan dulu working tree di server sebelum push berikutnya.

### Menghapus runner

```bash
cd ~/actions-runner
sudo ./svc.sh stop
sudo ./svc.sh uninstall
./config.sh remove --token <TOKEN_REMOVAL_DARI_GITHUB>   # Settings → Actions → Runners
```

---

## 5. Konfigurasi

Ubah lewat `BeeCoding/appsettings.json`, `appsettings.Production.json`, atau environment
variable (nesting pakai `__`).

| Kunci | Default | Keterangan |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=beecoding.db` | Path file SQLite. Produksi: `Data Source=/var/lib/beecoding/beecoding.db`. |
| `Judge:WorkRoot` | `/tmp/beecoding-judge` | Direktori kerja compile/run. |
| `Judge:MaxConcurrent` | `2` | Jumlah compile/run paralel. |
| `Judge:CompileTimeoutMs` | `10000` | Batas waktu kompilasi. |
| `Judge:QueueCapacity` | `200` | Kapasitas antrean judge. |
| `Judge:HardWallBufferMs` | `800` | Selisih wall-clock di atas batas soal sebelum di-*kill* paksa. |
| `Judge:RunTimeLimitMs` | `1000` | Batas waktu default tombol **Run** ad-hoc. |
| `Judge:RunMemoryLimitKb` | `32768` | Batas memori default tombol **Run**. |
| `Judge:RateLimitMs` | `1500` | Jarak minimum antar Run/Submit per user. |
| `Judge:RequireSandbox` | `false` | **Set `true` di produksi.** Bila `true` dan bwrap tak bisa bikin namespace, app **gagal start** (daripada diam-diam jalan tanpa isolasi filesystem/jaringan). |
| `Auth:TeacherSignupCode` | `""` (kosong) | Kosong ⇒ pendaftaran mandiri **hanya bisa jadi Student**. Diisi ⇒ user boleh memilih peran Teacher jika memasukkan kode ini. Buat guru pertama lewat seeder / DB. |
| `Admin:Token` | `""` (kosong) | Token endpoint **skrip** `/api/admin/*` (ingest bank soal). Kosong ⇒ semua respons `404`. Token salah juga `404` (tak bisa dibedakan dari "mati"); brute force di-throttle per-IP (8 gagal / 10 menit). Pakai `openssl rand -hex 32`, set via env, jangan commit. |
| `Admin:Emails` | `""` (kosong) | Daftar email (pisah koma) yang mendapat **halaman Admin** (`/admin`): lihat penggunaan AI semua user, daftar user, export/import soal. Cookie-authed via role `Admin`; endpoint `/api/admin-ui/*`. Login ulang setelah mengubahnya. |
| `Security:ContentSecurityPolicy` | *(bawaan)* | Override CSP dengan string sendiri, atau `"off"` untuk tidak mengirim header CSP (mis. jika Monaco bermasalah). |
| `Lsp:Enabled` | `false` | Aktifkan IntelliSense C/C++ (butuh `clangd` di PATH). |
| `Lsp:ClangdPath` | `clangd` | Path biner clangd. |
| `Lsp:MaxConcurrent` | `4` | Maksimum sesi clangd bersamaan (1 per editor yang terbuka). |
| `Lsp:IdleTimeoutSeconds` | `300` | Sesi clangd dimatikan setelah sekian detik tanpa lalu-lintas. |
| `Lsp:MemoryLimitMb` | `0` | *Runaway guard* opsional (RLIMIT_DATA via `prlimit`). `0` = nonaktif. Isi longgar (≥2048); nilai terlalu kecil membuat clangd *abort* saat meng-index header standar. |
| `Admin:Token` | `""` (kosong) | Token untuk endpoint `/api/admin/*` (mengisi bank soal via skrip). Kosong ⇒ endpoint mati (404). |
| `Ai:Enabled` | `false` | Aktifkan tutor AI (butuh `Ai:ApiKey` juga). |
| `Ai:ApiKey` | `""` | Bearer token endpoint chat-completions (kompatibel OpenAI). Set via **env**, jangan commit. |
| `Ai:BaseUrl` | NVIDIA NIM | URL chat-completions. |
| `Ai:Model` | `deepseek-ai/deepseek-v4-flash-0731` | Nama model. |
| `Ai:Thinking` | `false` | Kirim `chat_template_kwargs.thinking` (lebih teliti, lebih lambat). |
| `Ai:DefaultReplyLanguage` | `id` | Bahasa balasan default (`id`/`en`); murid bisa memilih sendiri per pertanyaan. |
| `Ai:TimeoutSeconds` | `60` | Batas tunggu 1 permintaan; lewat ⇒ `502` ramah. |
| `Ai:MaxTokens` / `Ai:Temperature` / `Ai:RateLimitSeconds` | `700` / `0.3` / `8` | Batas panjang jawaban, kreativitas, dan jarak antar-permintaan per user. |
| `ASPNETCORE_URLS` | — | mis. `http://0.0.0.0:8080`. |

Contoh override via env:

```bash
export ConnectionStrings__Default="Data Source=/var/lib/beecoding/beecoding.db"
export Judge__MaxConcurrent=4
export Judge__WorkRoot=/var/tmp/beecoding-judge
export Lsp__Enabled=true          # setelah `apt install clangd`
export Admin__Token="$(openssl rand -hex 32)"   # aktifkan endpoint admin bank soal
export Ai__Enabled=true; export Ai__ApiKey="nvapi-…"   # tutor AI
```

### Tutor AI (hint, bukan jawaban)

Dengan `Ai:Enabled=true` + `Ai:ApiKey` terisi, muncul panel **🤖 AI tutor** di halaman
Solve & Practice. Endpoint: `GET /api/ai/enabled`, `POST /api/ai/hint` (sekali balas) dan
`POST /api/ai/hint/stream` (**SSE**, token-per-token — inilah yang dipakai UI). Body:
`{ problemId | bankProblemId, language, code, verdict?, compilerOutput?, stderr?, question?, lang }`
— `lang` = `id`/`en` (dipilih murid, disimpan di localStorage).

Respons stream memakai header `X-Accel-Buffering: no`, jadi nginx tidak perlu blok
`location` khusus — cukup jangan meng-`proxy_buffering on` paksa untuk `/api/`.

Server mengirim statement + sample test + kode & error murid ke model dengan *system prompt*
yang **melarang** memberi solusi lengkap / badan fungsi / algoritma sebagai kode; balasannya
juga dipangkas kalau ada blok kode > 12 baris. Kompatibel dengan endpoint chat-completions
gaya OpenAI mana pun — ganti `Ai:BaseUrl` + `Ai:Model` (mis. OpenAI, Groq, vLLM lokal).
Kalau model default lambat, naikkan `Ai:TimeoutSeconds` atau pakai model lain; `Ai:Thinking=true`
lebih teliti tapi jauh lebih lambat.

`Ai:ApiKey`/`Ai:BaseUrl`/`Ai:Model`/`Ai:GenerateModel` di `appsettings.json` cuma **fallback
paling bawah** sekarang — bisa di-override tanpa restart lewat tab **AI** di `/admin`
("AI provider"), dan tiap organisasi bisa punya API key/model sendiri lewat `/org-admin`
(lihat §5B). Urutan prioritas: override organisasi → override platform (di `/admin`) →
`appsettings.json`, per field (key/BaseUrl/model boleh di-override sendiri-sendiri).

### Checklist keamanan produksi

- `Environment=Judge__RequireSandbox=true` — pastikan log startup berbunyi `Sandbox mode: bubblewrap + rlimits` (kalau `rlimits only`, aktifkan *unprivileged user namespaces*, lihat §6).
- Ikat Kestrel ke localhost: `Environment=ASPNETCORE_URLS=http://127.0.0.1:8080` (nginx yang menghadap publik).
- `Auth__TeacherSignupCode` diisi (atau biarkan kosong → tak ada guru baru dari form). Peran Teacher = bisa menulis soal → jangan biarkan siapa pun mengambilnya.
- `Admin__Token` panjang & acak, hanya via `Environment=` di unit systemd — **jangan** taruh di `appsettings.json` yang ter-commit.
- Di nginx, batasi body untuk route judge: `location /api/run { client_max_body_size 1m; proxy_pass http://beecoding; ... }` — biarkan `100m` hanya untuk `/api/admin/`.
- Header keamanan (CSP, `X-Frame-Options`, `X-Content-Type-Options`, HSTS saat HTTPS) sudah dikirim aplikasi otomatis.
- Statement soal disanitasi (DOMPurify) sebelum dirender — aman dari HTML/script sisipan.
- Login sudah di-throttle otomatis (8 gagal / 15 menit per akun, 25 per IP → `429`). Untuk membendung spam pendaftaran, tambahkan `limit_req` nginx pada `/api/auth/`:
  ```nginx
  limit_req_zone $binary_remote_addr zone=auth:10m rate=10r/m;   # di http {}
  location /api/auth/ { limit_req zone=auth burst=20 nodelay; proxy_pass http://beecoding; ... }
  ```
- Scratch dir judge yang tertinggal (proses ke-kill) dibersihkan otomatis tiap 15 menit (usia > 1 jam).

### Endpoint admin — mengisi bank soal via skrip

Aktif hanya kalau `Admin:Token` di-set. Kirim token sebagai header `X-Admin-Token`
(atau query `?token=`). Semua di bawah `/api/admin` — **bukan** cookie login.

| Method & path | Fungsi |
|---|---|
| `GET /api/admin/ping` | cek token; balas owner default + jumlah soal bank |
| `GET /api/admin/bank-problems` | daftar semua soal bank (semua owner) |
| `POST /api/admin/bank-problems` | buat/update batch soal (upsert per *(owner, title)*) |
| `DELETE /api/admin/bank-problems/{id}` | hapus satu soal |

Soal yang dibuat masuk ke **bank publik** (muncul di **Practice**, dan guru bisa
meng-copy-nya ke board lewat `POST /api/bank/{id}/copy-to/{slug}`). Body `POST`:

```jsonc
{
  "ownerEmail": "teacher@demo.test",   // opsional; default = guru pertama
  "replaceExisting": true,             // opsional; true = timpa yang judul-nya sama
  "problems": [
    {
      "title": "Sum of Array",
      "statementMarkdown": "Baca n lalu n bilangan, cetak jumlahnya.",
      "language": "cpp",               // "c" | "cpp" (default cpp)
      "level": "Easy",                 // Easy | Medium | Hard (default Medium)
      "tags": "array, math",
      "starterCode": "#include <bits/stdc++.h>\n...",
      "timeLimitMs": 1000,             // opsional
      "memoryLimitKb": 32768,          // opsional
      "isPublic": true,                // opsional (default true)
      "tests": [
        { "stdin": "3\n1 2 3\n", "expectedStdout": "6\n", "isSample": true },
        { "stdin": "1\n-5\n",    "expectedStdout": "-5\n" }
      ]
    }
  ]
}
```

Aturan validasi per soal: `title` wajib, minimal 1 test, dan minimal 1 test **penilaian**
(`isSample:false`, `points`>0). Soal yang gagal dilewati dan dilaporkan di `errors`; sisanya
tetap tersimpan. Contoh:

```bash
curl -sS -X POST http://localhost:5048/api/admin/bank-problems \
  -H "X-Admin-Token: $Admin__Token" -H 'Content-Type: application/json' \
  -d @soal.json
# -> {"created":[...],"updated":[...],"errors":[...]}
```

**IntelliSense C/C++ (clangd).** Bila `Lsp:Enabled=true` dan `clangd` ada di PATH, editor
Monaco di halaman **Solve** dan **Practice** memakai clangd sebagai *language server* lewat
WebSocket `/lsp/cpp`: autocomplete, hover, *signature help*, dan diagnostik sebaris. Setiap
editor yang terbuka memakai satu proses clangd berumur pendek dengan *workspace* satu file
sementara; proses dimatikan saat editor ditutup atau `Lsp:IdleTimeoutSeconds` terlewati.
Di belakang nginx, blok `location ~ ^/(hubs|lsp)/` di §4B sudah menangani *upgrade* WebSocket-nya.
Tanpa clangd atau dengan `Lsp:Enabled=false`, editor tetap jalan memakai completion berbasis kata.

---

## 5A. Integrasi LTI 1.3 (BeeCoding sebagai Tool di LMS)

BeeCoding bisa dibuka langsung dari dalam LMS (Moodle, Canvas, dll.) lewat **LTI 1.3
Advantage**: mahasiswa/dosen klik link di course-nya, langsung masuk ke board BeeCoding
tanpa daftar/login manual. Tidak perlu konfigurasi apapun di `appsettings.json` — semua
diatur dari tab **LTI** di [/admin](/admin/lti).

**Cara daftarkan LMS baru:**

1. Buka tab **LTI** di admin panel — bagian "Tool configuration" menampilkan 4 URL milik
   BeeCoding: OIDC login initiation, launch/redirect, JWKS publik, dan Deep Linking (URL
   yang sama dengan launch). Salin ke form registrasi *external tool*/*LTI Advantage* di
   LMS-nya.
2. LMS akan menerbitkan: **issuer**, **client ID**, **deployment ID**, dan tiga URL
   miliknya sendiri (auth login, auth token, key set/JWKS). Isi ke form "Add platform"
   di tab yang sama.
3. Selesai — dosen tinggal tambahkan BeeCoding sebagai *activity*/*external tool* di
   course-nya. Peluncuran pertama oleh dosen otomatis membuat board baru (atau, kalau
   ditambahkan lewat **Deep Linking**, dosen memilih board yang sudah ada); peluncuran
   berikutnya — oleh siapa pun di course itu — otomatis masuk ke board yang sama.

**Yang didukung:** launch dasar (SSO + auto-provision akun + auto-join board), Deep
Linking (dosen memilih board dari dalam LMS saat menambah activity), dan grade passback
(AGS) — skor "soal terpecahkan / total soal" di board terkirim ke gradebook LMS setiap
kali mahasiswa menyelesaikan soal baru, asal LMS memberi izin *grading* pada activity itu.

**Catatan keamanan:** akun yang dibuat via LTI memakai email asli dari LMS kalau
platform-nya membagikannya; kalau tidak (mode privasi), dibuatkan email sintetis
`lti-<platformId>-<sub>@lti.invalid` yang tidak bisa dipakai login manual. Grade
passback bersifat *best-effort* — kalau LMS-nya tidak bisa dihubungi atau izin
*grading*-nya dicabut, itu cuma tercatat di log server, tidak pernah menggagalkan
proses penilaian soal itu sendiri.

**Troubleshooting: 401 terus-menerus di dalam iframe LMS (Deep Linking atau launch
biasa), padahal login/launch-nya sendiri sukses.** Ini cookie session yang tidak
terkirim — browser tidak pernah mengirim cookie `SameSite=Lax` untuk request AJAX
lintas-situs (yaitu semua panggilan API dari dalam iframe LMS). BeeCoding otomatis
pakai `SameSite=None; Secure` kalau request-nya terdeteksi HTTPS, tapi deteksi itu
bergantung pada reverse proxy meneruskan header `X-Forwarded-Proto: https` dengan
benar. Kalau proxy-mu (misal reverse proxy bawaan Synology) tidak meneruskan header
itu, set:

```
Environment=Security__CookieAlwaysSecure=true
```

di unit systemd (§4A), lalu `sudo systemctl restart beecoding`. Ini paksa cookie
selalu `Secure`, terlepas dari apa yang terdeteksi aplikasi — **hanya aman dipakai
kalau aplikasi memang cuma bisa diakses lewat HTTPS dari luar** (kalau masih ada
akses HTTP polos, browser akan menolak cookie yang di-mark Secure lewat koneksi HTTP).

**Masih 401 walau cookie sudah `SameSite=None; Secure` dengan benar (cek DevTools →
Application → Cookies)?** Browser modern (Chrome dkk.) sekarang **memblokir cookie
pihak-ketiga secara default** sekalipun atribut `SameSite`/`Secure`-nya sudah benar —
fitur "Tracking Protection"/deprecation cookie pihak-ketiga. BeeCoding otomatis
menambahkan atribut `Partitioned` (CHIPS — cara resmi browser mengizinkan cookie tetap
jalan di dalam iframe pihak-ketiga tanpa perlu izin manual user, karena storage-nya
terisolasi per situs induk sehingga tidak bisa dipakai melacak lintas situs). Kalau versi
BeeCoding-mu sudah termasuk ini tapi masih gagal, pastikan **hapus cookie
`beecoding.auth` yang lama** dulu (DevTools → Application → Cookies → hapus semua baris
itu) sebelum tes ulang — cookie lama yang ke-cache dari percobaan sebelum fix ini
ditambahkan tidak akan otomatis ter-upgrade atributnya.

---

## 5B. Multi-tenant: beberapa organisasi/institusi dalam satu deployment

Berguna kalau satu instance BeeCoding dipakai lebih dari satu universitas/institusi
sekaligus — tiap organisasi punya kuota AI sendiri dan **Org Admin**-nya sendiri, yang
hanya melihat data organisasinya sendiri (tidak pernah bisa melihat organisasi lain).
Satu user boleh jadi anggota beberapa organisasi, atau tidak sama sekali (normal, bukan
kasus khusus).

**Setup:**

1. Super admin (Admin:Emails) bikin organisasi di tab **Organizations** (`/admin`) — cukup
   nama + slug.
2. Cara board masuk ke suatu organisasi:
   - **Via LTI (paling otomatis):** di tab **LTI**, set field "Organization" saat
     daftarkan platform sebuah universitas. Setiap launch lewat platform itu otomatis
     meng-enroll user ke organisasi tsb, dan board yang otomatis dibuat ikut organisasi
     itu — tidak perlu setting manual apa-apa lagi setelahnya.
   - **Manual:** dosen yang sudah jadi anggota suatu organisasi bisa memilihnya saat bikin
     board baru; kalau dia cuma anggota satu organisasi, otomatis terpilih.
3. Super admin promosikan salah satu anggota organisasi jadi **Org Admin** lewat
   `/org-admin` (pilih organisasi → tab Members → ubah role jadi Admin). Org Admin itu
   sendiri lalu bisa kelola member/board/AI settings organisasinya dari halaman yang sama,
   tanpa butuh akses super admin.

**Yang bisa diatur Org Admin (di `/org-admin`):** anggota (tambah/hapus/ubah role),
lihat daftar board organisasinya, kuota + pause AI khusus organisasinya (pause di
level organisasi tidak pernah mengalahkan kill-switch platform di `/admin` — kalau
platform di-pause, semua organisasi ikut ter-pause), dan **API key/model AI sendiri**
("bring your own key") — kalau organisasinya punya langganan API AI sendiri, isi di sini;
kosongkan untuk ikut key/model default platform. Kuncinya tidak pernah ditampilkan lagi
setelah disimpan, cuma preview 4 karakter terakhir.

**Yang TIDAK ada di Org Admin** (sengaja dibatasi ke super admin/`/admin`, karena
lintas-organisasi): trash/restore, audit log, dan export laporan CSV/Excel.

---

## 6. Catatan sandbox / keamanan judge

- **Limit waktu & memori selalu dipaksakan** lewat `setrlimit` (CPU, address space, stack,
  jumlah proses, ukuran file) + backstop wall-clock.
- **Isolasi filesystem/jaringan hanya aktif bila `bwrap` bisa membuat namespace.** Itu perlu
  *unprivileged user namespaces* diizinkan host:
  - Debian/lama: `sudo sysctl -w kernel.unprivileged_userns_clone=1`
  - Ubuntu 24.04 (dibatasi AppArmor): `sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0`
  - Kontainer Docker: jalankan dengan `--security-opt seccomp=unconfined --security-opt apparmor=unconfined`
    (dev container bawaan repo **memblokir** userns → judge berjalan *rlimits-only*).
  - Bila bwrap aktif, **proses kompilasi `g++` juga dijalankan di dalam jail yang sama** —
    jadi kode seperti `#include "/etc/passwd"` tidak bisa membaca file host. Tanpa bwrap
    (*rlimits-only*), kompilasi tidak terisolasi → pakai `Judge:RequireSandbox=true` untuk
    menolak jalan dalam mode itu.
- Untuk produksi: jalankan aplikasi sebagai **user berprivilege rendah** khusus, dan idealnya
  pisahkan eksekusi kode ke VM/host sekali-pakai. Lihat bagian *Security note* di `README.md`.

---

## 7. Migrasi database (opsional)

Aplikasi **otomatis menerapkan migrasi** saat startup. Untuk mengelolanya manual:

```bash
dotnet tool install --global dotnet-ef      # sekali saja
export PATH="$PATH:$HOME/.dotnet/tools"

cd BeeCoding
dotnet ef migrations add NamaMigrasi
dotnet ef database update
```

**Reset data pengembangan:** hentikan aplikasi, `rm BeeCoding/beecoding.db*`, jalankan lagi →
seed ulang (demo + 81 soal).

---

## 8. Ringkasan port

| Port | Dipakai |
|---|---|
| 5048 | Backend Kestrel (dev, profil `http`) |
| 7101 | Backend Kestrel (dev, profil `https`) |
| 5173 | Vite dev server (frontend) |
| 8080 | Contoh port produksi (`ASPNETCORE_URLS`) |

---

## 9. Dev Container (VS Code)

Repo menyertakan `.devcontainer/devcontainer.json` (image .NET 10 + fitur Node).

1. Buka folder di VS Code → **Reopen in Container**.
2. Ikuti langkah **Bagian 3** (dua terminal).
3. Catatan: base image ini memblokir user namespaces → judge memakai mode *rlimits-only*.

---

## 10. Troubleshooting

| Gejala | Solusi |
|---|---|
| Startup gagal: `gcc not found on PATH` / `g++ not found` | `sudo apt-get install -y build-essential` |
| Buka `:5048` langsung → halaman kosong / 404 | Jalankan `npm run build` di `ClientApp` (mengisi `wwwroot/`), atau pakai Vite di `:5173`. |
| Publish → `/` HTTP 404, log: `WebRootPath was not found: .../out/wwwroot` | `out/wwwroot` kosong. `cp -r wwwroot out/` lalu jalankan ulang, atau build SPA lebih dulu lalu `dotnet publish ... -p:BuildClient=false`. |
| SignalR tidak connect di belakang proxy | Teruskan header `Upgrade`/`Connection` untuk WebSocket; pastikan path `/hubs` ikut ter-proxy. |
| Verdict selalu `MemoryLimit` untuk program sederhana | Naikkan `MemoryLimitKb` pada soal (mis. 65536) — `RLIMIT_AS` membatasi *virtual address space*, bukan RSS. |
| Gambar soal terlindungi kosong / error | Pada image minimal, `sudo apt-get install -y libfontconfig1`. Di Ubuntu 24.04 biasanya sudah cukup. |
| `Address already in use` | Ubah `applicationUrl` di `launchSettings.json` atau set `ASPNETCORE_URLS`, dan sesuaikan `BACKEND_URL` untuk Vite. |
| Service: `Format of the initialization string does not conform...` | Baris `Environment=` dengan nilai berspasi tidak dikutip. Bungkus penuh: `Environment="ConnectionStrings__Default=Data Source=/path/db"`. |
| Judge lambat / antre | Naikkan `Judge__MaxConcurrent` sesuai jumlah core. |
