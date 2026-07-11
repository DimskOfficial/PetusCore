# PetusCore

Игровое ядро приватного сервера **Geometry Dash 2.2** (PetusGDPS).
Написано на **VB.NET / ASP.NET Core (.NET 8)**, хранилище — **PostgreSQL**.

Один процесс обслуживает две поверхности:
- **Игровые эндпоинты** для клиента GD (`/accounts/*`, `/getGJLevels21.php`, …).
- **REST API** (`/api/*`) для сайта и лаунчера (аккаунты, уровни, рейтинги,
  профили, модерация, манифест игры).

Ядро самодостаточно: с чистой БД оно поднимает схему само и готово к работе.

## Архитектура

```
            ┌──────────────┐      REST /api/*        ┌───────────────┐
  Игрок ───▶│  GD 2.2      │──── /accounts, /get… ──▶│               │
            │  (клиент)    │                          │   PetusCore   │──▶ PostgreSQL
  Сайт  ───▶│  gdps.site   │──── /api/* ─────────────▶│  (VB.NET /    │
            └──────────────┘                          │  ASP.NET Core)│
  Лаунчер ─▶ /api/game/manifest ─────────────────────▶└───────────────┘
```

- **`Program.vb`** — старт, DI, middleware (срез URL-префиксов), регистрация
  всех групп эндпоинтов.
- **`Data/PgTable(Of T)`** — тонкий реляционный маппер: каждое публичное
  свойство модели = типизированная колонка. Схема создаётся и мигрируется
  (`CREATE TABLE IF NOT EXISTS` + `ADD COLUMN IF NOT EXISTS`) на старте.
- **`Data/Database.vb`** — таблицы (accounts, users, levels, comments, songs,
  scores, map_packs, gauntlets, quests, mod_applications, …) + счётчики ID.
- **`Api/*`** — REST: `RestApi`, `SiteApi`, `AdminApi`, `PetusIdApi`, `LauncherApi`.
- **`Endpoints/*`** — Boomlings-совместимые игровые эндпоинты.
- **`Services/*`** — конфиг, пароли (BCrypt + GJP2), токены, хеши.

## Аутентификация

- Аккаунты создаются **только** через провайдер идентичности (сервис-секрет,
  `POST /api/petusid/resolve`) — на сервере нет открытой регистрации/пароля.
- Игровые запросы авторизуются либо классическим GJP2, либо **токеном лаунчера**:
  клиент шлёт `gjp2 = "petus:<token>"`, ядро валидирует токен (`GdAuth`). Так
  статистика/рекорды/профиль сохраняются на реальный аккаунт.
- Незалогиненные (green/UDID) игроки не могут писать данные — рейтинги и
  профили только для зарегистрированных аккаунтов.

## Запуск (Docker Compose — всё в комплекте)

```bash
docker compose up --build
```

`docker-compose.yml` поднимает PostgreSQL + ядро. Дефолтные секреты в compose —
поменяй перед публичным запуском.

## Запуск локально (без Docker)

Нужен .NET 8 SDK и запущенный PostgreSQL.

```bash
export PETUS_DB_URL="Host=localhost;Port=5432;Username=petus;Password=secret;Database=petusgdps"
export PETUS_API_SECRET="change-me"
export PETUS_ADMIN_USER="YourName"
dotnet run --project src/PetusCore/PetusCore.vbproj
```

Ядро слушает `http://0.0.0.0:8080`. Проверка: `GET /health`.

## Переменные окружения

| Переменная | Назначение |
| --- | --- |
| `PETUS_DB_URL` | **обязательно** — строка подключения PostgreSQL (Npgsql). |
| `PETUS_API_SECRET` | сервис-секрет для server-to-server вызовов (`/api/petusid/*`). |
| `PETUS_ADMIN_USER` | ник, который автоматически становится админом при входе. |
| `PETUS_SERVER_NAME` | отображаемое имя сервера. |
| `PETUS_PUBLIC_URL` | публичный URL за реверс-прокси (для getAccountURL и т.п.). |
| `PETUS_PATH_PREFIX` | доп. префикс пути, который срезается перед роутингом. |
| `PETUS_PREACTIVATE` | `true` — новые аккаунты активны сразу. |
| `PETUS_GAME_VERSION` / `PETUS_GAME_ZIP_URL` | версия и URL сборки игры для лаунчера (`/api/game/manifest`). |
| `PORT` | порт (по умолчанию 8080). |

## Как указать игру на своё ядро

Клиент GD ходит на игровые эндпоинты по своему хосту. Ядро умеет срезать
префиксы пути (`/gd`, `/database`), поэтому клиент, направленный на
`https://<ваш-хост>/gd`, попадает в `/gd/database/<endpoint>` → ядро срежет
`/gd` и обработает запрос. Настраивается через `PETUS_PATH_PREFIX`.

## REST API (основное)

- `GET  /health`, `GET /api/info`, `GET /api/stats`, `GET /api/leaderboard`
- `GET  /api/levels`, `GET /api/levels/{id}`, `GET /api/levels/{id}/comments`
- `GET  /api/users/{name}` — профиль (статы, опыт, уровень, бейджи, комментарии)
- `GET  /api/game/manifest` — версия + URL сборки для лаунчера
- `POST /api/petusid/resolve|login` — провижининг/вход (сервис-секрет)
- `POST /api/game/state` — приветствие/бан для мода (bearer)
- `/api/admin/*` — модерация (рейты, баны, роли, map packs, гаунтлеты, заявки)

## Опыт и уровни

Опыт считается из статистики:
`stars*5 + moons*5 + diamonds*2 + secretCoins*100 + userCoins*20 + demons*75 +
creatorPoints*5000`. Уровень — по нарастающей кривой (ур. N стоит N*1000 XP).

## Сборка

```bash
dotnet build src/PetusCore/PetusCore.vbproj -c Release
dotnet publish src/PetusCore/PetusCore.vbproj -c Release -o out
```

Готовый Docker-образ собирается из `Dockerfile` (multi-stage).
