# BeeCoding — Deploy dengan Container: Podman vs Docker Swarm (jembatan ke AKS)

Dokumen ini duduk **di antara** [INSTALL.md](INSTALL.md) (deploy langsung di satu VM, tanpa
container — cara yang sekarang dipakai) dan [DEPLOY.md](DEPLOY.md) (arsitektur penuh untuk
Azure Web App / Kubernetes-AKS / scale-out). Tujuannya: mengemas BeeCoding jadi container
image, lalu menjalankannya dengan **Podman** dan **Docker Swarm** — dua cara paling umum
untuk belajar orkestrasi container sebelum lompat ke Kubernetes/AKS yang jauh lebih rumit.

> **Untuk skala kamu sekarang** (1 VM, ~1 kelas), containerizing itu **opsional** — VM
> langsung (INSTALL.md) sudah cukup dan lebih sederhana (lihat DEPLOY.md §4). Dokumen ini
> lebih untuk **latihan konsep**: struktur image, cara jalanin multi-container, dan gimana
> tiap tool "menerjemahkan" ke istilah Kubernetes nanti.

---

## 0. Peta konsep — Podman vs Docker Swarm vs Kubernetes/AKS

| Konsep | Podman | Docker Swarm | Kubernetes / AKS |
|---|---|---|---|
| Unit deploy terkecil | Container, atau **pod** (grup container satu network namespace) | **Service** di dalam sebuah **stack** | **Pod** (mirip pod Podman) di dalam **Deployment** |
| File definisi | `podman-compose.yml` (opsional) atau perintah langsung | Compose file v3 + bagian `deploy:` | Manifest YAML terpisah (Deployment, Service, ConfigMap, Secret, ...) |
| Scaling | Manual per container, atau multi-pod | `docker service scale` — bawaan | `kubectl scale` / HPA — bawaan |
| Load balancing antar-replika | Tidak bawaan (perlu reverse proxy sendiri) | **Routing mesh** bawaan | **Service** (ClusterIP/LoadBalancer) bawaan |
| Rootless by default | **Ya** — jalan tanpa daemon root | Tidak — Docker daemon jalan sebagai root | Tergantung `securityContext` pod |
| Multi-node native | **Tidak** — Podman itu tool single-host; multi-node butuh k3s/kubeadm terpisah | **Ya** — `docker swarm join` dari node lain | Ya, itu memang tujuannya |
| Jembatan ke manifest K8s | **Langsung** — `podman generate kube` menghasilkan YAML yang strukturnya dekat dengan manifest K8s asli | **Tidak ada** — konsep service/stack Swarm tidak auto-convert; nanti ditulis ulang dari nol pakai format K8s | — |

**Kesimpulan cepat:** kalau tujuan akhirnya memang AKS, waktu belajar paling worth-it
dihabiskan di **Podman** (konsepnya paling dekat) — atau bahkan langsung coba Kubernetes
beneran secara lokal pakai **minikube**/**kind**. Docker Swarm tetap berguna untuk dipahami
(gampang, cepat scale multi-node tanpa alat tambahan), tapi skill/config-nya **tidak
portable** ke Kubernetes.

---

## 1. Build image (dipakai sama-sama oleh Podman & Swarm)

Reuse Dockerfile multi-stage dari [DEPLOY.md §2.12](DEPLOY.md#212-image-dockerfile-multi-stage):

```dockerfile
# --- build SPA + publish ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && apt-get install -y nodejs
WORKDIR /src
COPY . .
RUN dotnet publish BeeCoding/BeeCoding.csproj -c Release -o /app   # menjalankan npm ci && npm run build

# --- runtime web ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
RUN apt-get update && apt-get install -y --no-install-recommends gcc g++ bubblewrap \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "BeeCoding.dll"]
```

Simpan sebagai `Dockerfile` di root repo, lalu build dengan salah satu tool — **format
image-nya (OCI) sama, jadi bisa dipakai bolak-balik** antara Podman dan Docker:

```bash
# Podman
podman build -t beecoding:latest .

# Docker (dipakai juga untuk Swarm)
docker build -t beecoding:latest .
```

### Catatan penting: `bubblewrap` di dalam container

INSTALL.md §6 sudah mencatat ini untuk dev container biasa, dan **makin relevan** begitu
BeeCoding sendiri dibungkus container — sekarang ada **dua lapis** isolasi (container
runtime membungkus app, lalu app mau bikin isolasi lagi lewat `bwrap` untuk tiap kode
murid):

- **Docker**: coba jalankan dengan `--security-opt seccomp=unconfined --security-opt
  apparmor=unconfined` supaya `bwrap` bisa bikin *user namespace* di dalam container.
- **Podman rootless**: ini yang genuinely lebih rumit — Podman sendiri *sudah* memakai user
  namespace untuk mode rootless-nya, jadi `bwrap` di dalamnya berarti **nested user
  namespace**. Saya belum pernah verifikasi resep yang pasti berhasil untuk kombinasi ini
  tanpa uji coba langsung di server kamu — coba mulai dengan `--cap-add=SYS_ADMIN`, dan
  kalau tetap gagal, jalankan Podman sebagai **root** (`sudo podman run ...`, bukan
  rootless) untuk sesi ini.
- **Kalau dua-duanya tetap tidak jalan**: itu bukan kegagalan — set `Judge__RequireSandbox=
  false` dan terima mode **rlimits-only** (limit waktu & memori tetap dipaksakan, tapi
  tanpa isolasi filesystem/jaringan). Ini persis mode yang sudah dipakai dev container repo
  ini selama ini, jadi bukan sesuatu yang asing.

---

## 2. Deploy dengan Podman (single-node)

### 2.1 Jalan langsung

```bash
podman run -d --name beecoding \
  -p 8080:8080 \
  -v beecoding-data:/app/data \
  -e ConnectionStrings__Default="Data Source=/app/data/beecoding.db" \
  -e Ai__ApiKey="..." \
  -e Auth__TeacherSignupCode="..." \
  --security-opt seccomp=unconfined \
  beecoding:latest
```

`-v beecoding-data:/app/data` — volume terpisah supaya file SQLite **selamat** kalau
container di-`rm`/rebuild (sama prinsipnya dengan systemd setup di INSTALL.md §4A yang naruh
`beecoding.db` di path persisten).

### 2.2 Podman **pod** (kalau mau tambah container lain, mis. Redis)

Konsep *pod* Podman = beberapa container berbagi satu network namespace (localhost antar
mereka) — persis konsep pod Kubernetes:

```bash
podman pod create --name beecoding-pod -p 8080:8080

podman run -d --pod beecoding-pod --name redis docker.io/library/redis:7-alpine

podman run -d --pod beecoding-pod --name beecoding \
  -v beecoding-data:/app/data \
  -e Realtime__Backend=redis \
  -e Realtime__RedisConnectionString=localhost:6379 \
  -e Ai__ApiKey="..." \
  beecoding:latest
```

(`Realtime__Backend=redis` mengaktifkan implementasi Redis untuk draft/lecture/presence
+ job AI — lihat [DEPLOY.md §2.4](DEPLOY.md#24-state-memori--redis). Judge tetap `inproc`
di sini, cukup untuk satu node.)

### 2.3 Bikin auto-restart (systemd) — pengganti §4A INSTALL.md untuk versi container

```bash
podman generate systemd --new --files --name beecoding
mv container-beecoding.service ~/.config/systemd/user/
systemctl --user enable --now container-beecoding.service
loginctl enable-linger $USER   # supaya tetap jalan setelah logout SSH
```

### 2.4 Bonus: lihat "bentuk" manifest Kubernetes-nya

```bash
podman generate kube beecoding-pod > beecoding-pod.yaml
cat beecoding-pod.yaml
```

Isinya **bukan** manifest AKS yang siap pakai (belum ada Secret, Ingress, resource
limits, dll — lihat DEPLOY.md §2 untuk yang lengkap), tapi strukturnya (`apiVersion`,
`kind: Pod`, `spec.containers[]`) **sama persis format yang nanti kamu tulis manual** untuk
AKS. Ini cara paling cepat untuk mulai terbiasa dengan bentuk YAML Kubernetes dari sesuatu
yang sudah familiar.

---

## 3. Deploy dengan Docker Swarm

### 3.1 Aktifkan Swarm mode (sekali saja, di node yang jadi manager)

```bash
docker swarm init
```

### 3.2 Compose file dengan bagian `deploy:`

```yaml
# docker-compose.yml
version: "3.9"
services:
  beecoding:
    image: beecoding:latest
    ports:
      - "8080:8080"
    volumes:
      - beecoding-data:/app/data
    environment:
      ConnectionStrings__Default: "Data Source=/app/data/beecoding.db"
      Ai__ApiKey: "..."
    deploy:
      replicas: 1
      restart_policy:
        condition: on-failure
      resources:
        limits:
          cpus: "2"
          memory: 4G

volumes:
  beecoding-data:
```

```bash
docker stack deploy -c docker-compose.yml beecoding
docker service ls
docker service logs beecoding_beecoding -f
```

### 3.3 Kalau naikkan `replicas` > 1

**Berhenti dulu** — ini persis masalah yang dipetakan [DEPLOY.md §0](DEPLOY.md#0-peta-state--komponen-kenapa-scale-out-butuh-kerja):
SQLite akan rusak/lock, SignalR di replika A tidak nyampai ke klien di replika B, dan
seterusnya. Swarm **tidak otomatis menyelesaikan** itu — routing mesh cuma menyebar
*request baru*, bukan menyinkronkan state di dalam app.

Yang **sudah bisa** dilakukan di Swarm tanpa perlu Kubernetes: tambahkan container Redis ke
stack yang sama dan set `Realtime__Backend=redis` (persis §2.2 di atas, tapi dalam bentuk
compose):

```yaml
  redis:
    image: redis:7-alpine
  beecoding:
    # ...
    environment:
      Realtime__Backend: redis
      Realtime__RedisConnectionString: redis:6379
```

Ini menghilangkan masalah draft/lecture/presence + job AI. **SQLite dan SignalR
in-memory tetap jadi penghalang** untuk replika > 1 — itu baru selesai kalau ganti ke
PostgreSQL + SignalR backplane, seperti didokumentasikan penuh di DEPLOY.md §2.2–2.3.

### 3.4 Secrets

```bash
echo "sk-xxxxx" | docker secret create ai_api_key -
```

lalu di compose:
```yaml
  beecoding:
    secrets:
      - ai_api_key
secrets:
  ai_api_key:
    external: true
```
Secret muncul sebagai file di `/run/secrets/ai_api_key` di dalam container (bukan env var
langsung) — beda mekanisme dari K8s Secret (DEPLOY.md §2.10), tapi **konsepnya sama**:
kredensial tidak pernah masuk ke image atau `docker-compose.yml`.

---

## 4. Pisahkan `BeeCoding.Judge` jadi image sendiri (opsional)

Ini persis pola yang dijelaskan [DEPLOY.md §2.1](DEPLOY.md#21-tier--sudah-dipecah-jadi-3-project-di-repo):
`BeeCoding.Judge` itu host console minimal — cuma broker Redis + toolchain native +
`JudgeWorker`, **tanpa** `AppDbContext`, SignalR, cookie auth, atau `Ai__ApiKey`. Manfaat
langsungnya begitu dipisah: **image web jadi lebih kecil** (tidak perlu `gcc`/`g++`/
`bubblewrap` lagi — itu semua pindah ke image judge), dan judge bisa di-scale independen
dari web.

Dicek langsung dari `BeeCoding.Judge/Program.cs` dan `appsettings.json`-nya — dua hal wajib
yang beda dari web:

- **`Judge__Queue__Backend=redis` wajib** — proyek ini langsung `throw` saat start kalau
  backend-nya bukan redis (tidak ada mode `inproc` di sini sama sekali).
- **`Judge__Queue__RedisConnectionString` wajib diisi eksplisit** — beda dari web yang boleh
  `null` (nanti numpang multiplexer punya `Realtime`), judge berdiri sendiri jadi tidak ada
  yang dinumpangi.
- Default `Judge__RequireSandbox=true` (beda dari web yang defaultnya `false`) — artinya
  kalau `bwrap` tidak bisa jalan di container (lihat catatan §1), **image judge akan
  menolak start** kecuali kamu set `Judge__RequireSandbox=false` secara eksplisit.

### 4.1 Dockerfile — dua target dari satu file

Perluas Dockerfile di §1: publish **dua** project dari stage `build` yang sama, lalu pisah
jadi dua stage runtime:

```dockerfile
# --- build: publish DUA project ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && apt-get install -y nodejs
WORKDIR /src
COPY . .
RUN dotnet publish BeeCoding/BeeCoding.csproj -c Release -o /app/web
RUN dotnet publish BeeCoding.Judge/BeeCoding.Judge.csproj -c Release -o /app/judge

# --- runtime: web (tidak lagi butuh gcc/g++/bubblewrap!) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
WORKDIR /app
COPY --from=build /app/web .
ENV ASPNETCORE_URLS=http://+:8080
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "BeeCoding.dll"]

# --- runtime: judge (toolchain-nya pindah ke sini) ---
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS judge
RUN apt-get update && apt-get install -y --no-install-recommends gcc g++ bubblewrap \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/judge .
ENTRYPOINT ["dotnet", "BeeCoding.Judge.dll"]
```

```bash
podman build --target web -t beecoding-web:latest .
podman build --target judge -t beecoding-judge:latest .
# docker: ganti "podman" -> "docker", sama persis
```

### 4.2 Podman — tambah ke pod yang sudah ada (§2.2)

```bash
podman run -d --pod beecoding-pod --name beecoding-web \
  -v beecoding-data:/app/data \
  -e ConnectionStrings__Default="Data Source=/app/data/beecoding.db" \
  -e Judge__Queue__Backend=redis \
  -e Judge__Queue__RedisConnectionString=localhost:6379 \
  -e Ai__ApiKey="..." \
  beecoding-web:latest

podman run -d --pod beecoding-pod --name beecoding-judge \
  -e Judge__Queue__RedisConnectionString=localhost:6379 \
  -e Judge__RequireSandbox=false \
  --security-opt seccomp=unconfined \
  beecoding-judge:latest
```

(`Judge__RequireSandbox=false` di sini mengikuti asumsi pesimis dari §1 — kalau `bwrap`
ternyata jalan di server kamu, hapus baris ini dan biarkan default `true` yang lebih aman.)

### 4.3 Docker Swarm — tambah service ke stack yang sudah ada (§3.2)

```yaml
services:
  redis:
    image: redis:7-alpine
  beecoding:
    image: beecoding-web:latest
    ports: ["8080:8080"]
    volumes: ["beecoding-data:/app/data"]
    environment:
      ConnectionStrings__Default: "Data Source=/app/data/beecoding.db"
      Judge__Queue__Backend: redis
      Judge__Queue__RedisConnectionString: redis:6379
      Ai__ApiKey: "..."
  beecoding-judge:
    image: beecoding-judge:latest
    environment:
      Judge__Queue__RedisConnectionString: redis:6379
      Judge__RequireSandbox: "false"
    deploy:
      replicas: 2   # aman di-scale — judge tidak nyimpan state, beda dari web!

volumes:
  beecoding-data:
```

Ini justru contoh **paling sehat** untuk `docker service scale` (atau naikkan `replicas` di
atas): service `beecoding-judge` **tidak menyentuh** SQLite atau SignalR sama sekali —
murni proses compute yang ambil job dari Redis, jalankan, kirim balik hasilnya. Beda dari
menaikkan replika `beecoding` (web) yang langsung kena masalah state di §3.3.

### 4.4 Cara memastikan sudah nyambung

```bash
podman logs -f beecoding-judge     # atau: docker service logs beecoding_beecoding-judge -f
```
Submit kode dari UI BeeCoding seperti biasa — kalau judge sudah kekonek ke broker yang
benar, log ini akan menunjukkan job masuk & verdict keluar. Kalau macet di status
`Queued` selamanya, cek `Judge__Queue__RedisConnectionString` di **kedua** container
sama-sama menunjuk Redis yang sama.

---

## 5. Ringkasan — pilih yang mana?

| Situasi | Pilihan |
|---|---|
| Latihan konsep sebelum AKS, single-host, mau paling dekat ke K8s | **Podman** (+ `podman generate kube`) |
| Sudah familiar Docker Compose, mau cepat multi-node tanpa install tambahan | **Docker Swarm** |
| Sudah yakin tujuan akhirnya AKS, siap belajar langsung | Lewati keduanya, coba **minikube**/**kind** di laptop |
| Produksi sekarang, 1 kelas, VM yang sudah jalan | **Tidak perlu container sama sekali** — tetap pakai INSTALL.md, lihat DEPLOY.md §4 |

Baik Podman maupun Swarm **tidak menghilangkan** pekerjaan di DEPLOY.md kalau tujuannya
betulan scale-out multi-replika (DB relasional, SignalR backplane, Data Protection keys
bersama, dst) — keduanya cuma cara berbeda untuk membungkus & menjalankan container-nya.
Yang benar-benar portable ke AKS dari usaha di dokumen ini adalah: **Dockerfile-nya** (dipakai
apa adanya) dan **kebiasaan mikir dalam "container + env var config"** yang sama-sama
dipraktikkan Podman maupun Swarm.
