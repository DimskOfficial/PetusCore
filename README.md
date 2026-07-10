# PetusCore

**PetusCore** is the server core for **PetusGDPS** — a Geometry Dash 2.2 private
server ("GDPS") emulator written from scratch in **Visual Basic .NET**
(ASP.NET Core, .NET 8).

It speaks the Geometry Dash game protocol the client expects *and* exposes a
clean JSON **REST API** for the [PetusGDPS website](https://github.com/DimskOfficial/PetusGDPS).
There is **no SQL database** — everything is stored as plain, human-readable
**JSON files** in a `database/` folder, so it's trivial to host, back up and
inspect.

> Inspired by / compatible with the account & hashing conventions of
> [GMDprivateServer](https://github.com/Cvolton/GMDprivateServer), but a
> completely independent VB.NET implementation with JSON storage.

---

## ✨ Features

**Game protocol (Boomlings-style `*.php` endpoints the GD client talks to)**
- Accounts: register, login (GJP **and** GJP2), user score/stats sync
- Levels: upload, update, download (with the correct integrity hashes),
  search (recent / most downloaded / most liked / trending / featured /
  awarded / epic / by user / by name or ID), daily & weekly
- Comments: level comments and profile comments (post / list / delete / like)
- Leaderboards: global stars, creator points, and per-level percent boards
- Users: profile info, user search
- Songs: custom/Newgrounds song info, top artists
- Moderation from in-game: rate stars, rate demon, suggest stars

**REST API (for the website & tools)** — see [`docs/API.md`](docs/API.md)
- Public: server info, live stats, leaderboard, level browsing, song list
- Auth: bearer-token login / logout / me
- Account self-service: change password, edit socials, account recovery
- Music upload
- **Admin panel API**: accounts (grant mod / grant admin / ban), levels
  (rate / set daily / delete), song moderation, audit log

**Ops**
- Single JSON-file storage, atomic writes, thread-safe
- BCrypt password hashing — interoperable with PHP `password_hash` (`$2y$`)
- Docker image + Compose, health check, 100% env-var configurable
- Built and end-to-end tested on **.NET 8 SDK** (see the test flow below)

---

## 🚀 Deploy on Dokploy (recommended)

PetusCore ships as an **Application** you can deploy straight from this repo.

1. In Dokploy, **Create → Application → GitHub** and pick
   `DimskOfficial/PetusCore`.
2. **Build type: Dockerfile.** Exact fields:
   - **Dockerfile Path:** `Dockerfile`
   - **Docker Context Path:** `.` (repo root — the Dockerfile COPYs from `src/PetusCore/`)
   - **Docker Build Stage:** *(leave empty — the final stage is the runtime image)*
3. Add a **Volume**: mount path **`/data`** (this is where the JSON database
   lives — it must persist across redeploys).
4. Set **Environment variables** (see the table below). At minimum change
   `PETUS_API_SECRET`, set `PETUS_ADMIN_USER` to your in-game username, and set
   `PETUS_PUBLIC_URL` to your domain (e.g. `https://gdps.petus.ru`).
5. Expose the app and attach your domain. The container listens on **8080**.
6. Deploy. Check `https://your-domain/health` → `{"status":"ok",...}`.

Point your modified Geometry Dash client's server URL at your PetusCore domain
and you're live.

### Environment variables

| Variable                  | Default            | Purpose                                            |
|---------------------------|--------------------|----------------------------------------------------|
| `PORT`                    | `8080`             | Port to listen on (Dokploy sets this)              |
| `PETUS_DB_PATH`           | `/data`            | Folder for the JSON database (mount a volume here) |
| `PETUS_SERVER_NAME`       | `PetusGDPS`        | Display name on the API/site                       |
| `PETUS_API_SECRET`        | `change-me...`     | Secret for signing API tokens — **change it**      |
| `PETUS_ADMIN_USER`        | *(empty)*          | Username auto-promoted to admin on first login     |
| `PETUS_PUBLIC_URL`        | *(empty)*          | Public base URL (e.g. `https://gdps.petus.ru`). Used for account/content URLs and uploaded-song links behind a proxy — **set this in prod** |
| `PETUS_PREACTIVATE`       | `true`             | Activate new accounts immediately                  |
| `PETUS_GAME_DOWNLOAD_URL` | *(empty)*          | Direct link to the modded GD client (site button)  |
| `PETUS_GAME_ZIP`          | *(empty)*          | Path to a packaged client `.zip` served by the launcher API |
| `PETUS_GAME_EXE`          | `GeometryDash.exe` | Executable the launcher runs after install         |

> **Reverse proxy note.** Dokploy/Traefik terminates TLS and forwards to the
> container over HTTP. Always set `PETUS_PUBLIC_URL` so the GD client saves
> accounts and streams uploaded music from your real `https://` domain instead
> of the internal container address.

---

## 🐳 Run with Docker locally

```bash
docker compose up --build
# server on http://localhost:8080  — health: /health
```

Or plain Docker:

```bash
docker build -t petuscore .
docker run -p 8080:8080 -v petus-data:/data \
  -e PETUS_ADMIN_USER=Dimsk -e PETUS_API_SECRET=please-change \
  petuscore
```

---

## 🛠 Run from source (.NET 8 SDK)

```bash
cd src/PetusCore
dotnet run -c Release
# reads ./database by default; override with PETUS_DB_PATH
```

---

## ✅ Verified end-to-end

The build is compiled and smoke-tested with the .NET 8 SDK. The tested flow:

```
register (GD)          -> 1
duplicate register     -> -2
login raw password     -> "<accountID>,<userID>"
login wrong password   -> -11
REST login             -> bearer token + account (auto-admin applied)
upload level (GD)      -> new levelID
search recent (GD)     -> level list + hash
download level (GD)    -> full level string + integrity hashes
admin rate level       -> ok
ban account            -> banned; in-game login now returns -12
change password        -> ok
grant moderator        -> ok
```

---

## 📂 Project layout

```
PetusCore/
├─ Dockerfile, docker-compose.yml, .env.example
├─ database/                 # JSON DB (created/seeded at runtime)
├─ docs/API.md               # full REST API reference
└─ src/PetusCore/
   ├─ Program.vb             # bootstrap: DI, CORS, endpoints
   ├─ Data/                  # JsonTable, Database, models
   ├─ Services/              # PasswordService, HashService, TokenService, config
   ├─ Endpoints/             # Geometry Dash game protocol
   └─ Api/                   # REST API + admin API
```

---

## 🔒 Notes & scope

- This is a fan project / server emulator for private use. Geometry Dash is a
  trademark of RobTop Games. You are responsible for how you host and use it.
- JSON storage is great for small/medium servers and easy hosting. For very
  large player counts you'd want to shard or move hot tables to a database —
  the storage layer (`Data/Json.vb`) is isolated to make that swap easy.
- Some in-game systems (map packs, gauntlets, quests, rewards, messaging) are
  present as valid stub responses so the client never hangs; they can be
  fleshed out incrementally.

## 📜 License

[MIT](LICENSE) © 2026 DimskOfficial
