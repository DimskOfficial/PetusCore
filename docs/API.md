# PetusCore REST API

Base URL: `https://your-petuscore-domain`
All responses are JSON. Protected routes require a header:

```
Authorization: Bearer <token>
```

Get a token from `POST /api/auth/login`.

---

## Public

### `GET /api/info`
Server identity.
```json
{ "name": "PetusGDPS", "core": "PetusCore", "version": "1.0.0", "gdVersion": "2.2", "download": "" }
```

### `GET /api/stats`
Live counters: `accounts, users, levels, ratedLevels, comments, songs`.

### `GET /api/leaderboard`
Top 100 players by stars.

### `GET /api/levels?page=0&perPage=20&sort=recent`
`sort` ∈ `recent | downloads | likes | rated`. Returns `{ total, page, items[] }`.

### `GET /api/levels/{id}`
Single level (404 if missing).

### `GET /api/songs`
All custom songs.

---

## Auth

### `POST /api/auth/login`
```json
{ "username": "Dimsk", "password": "secret123" }
```
→ `{ "token", "expiresAt", "account": { ... } }`
Errors: `401 invalid_credentials`, `403 banned`.

### `POST /api/auth/logout`  *(auth)*
Revokes the current token.

### `GET /api/auth/me`  *(auth)*
Returns the current account.

---

## Account self-service *(auth)*

### `POST /api/account/change-password`
```json
{ "oldPassword": "secret123", "newPassword": "newsecret9" }
```
Errors: `400 wrong_old_password`, `400 password_too_short`.

### `POST /api/account/socials`
Any of: `youtube, twitter, twitch, discord, instagram, tiktok`.

### `POST /api/account/recover-request`
```json
{ "username": "Dimsk" }
```
→ `{ "ok": true, "code": "AB12CD34", "expiresAt": 1712345678 }`
(In production wire the code to email/Discord instead of returning it.)

### `POST /api/account/recover-confirm`
```json
{ "username": "Dimsk", "code": "AB12CD34", "newPassword": "newsecret9" }
```

---

## Music *(auth)*

### `POST /api/songs`
```json
{ "name": "My Track", "artist": "Me", "url": "https://.../song.mp3", "size": 3.2 }
```
→ `{ "ok": true, "id": 42 }`

---

## Admin *(auth, `isAdmin` — moderation routes also accept `modLevel`)*

### `GET /api/admin/overview`
Dashboard counters + last 20 mod actions.

### `GET /api/admin/accounts?q=<search>`
List accounts.

### `POST /api/admin/accounts/{id}/mod`
```json
{ "modLevel": 1 }
```
`modLevel` 0..2 (0 none, 1 mod, 2 elder).

### `POST /api/admin/accounts/{id}/admin`
```json
{ "isAdmin": 1 }
```

### `POST /api/admin/accounts/{id}/ban`
```json
{ "banned": 1 }
```

### `GET /api/admin/levels?q=&page=0&perPage=30`  *(mod)*
List/search levels.

### `POST /api/admin/levels/{id}/rate`  *(mod)*
```json
{ "stars": 5, "difficulty": 3, "demon": 0, "demonDiff": 0, "featured": 1, "epic": 0, "coinsVerified": 0 }
```

### `POST /api/admin/levels/{id}/daily`  *(mod)*
```json
{ "kind": 1 }
```
`kind`: 0 none, 1 daily, 2 weekly.

### `DELETE /api/admin/levels/{id}`  *(mod)*
Delete a level.

### `DELETE /api/admin/songs/{id}`  *(admin)*
Delete a song.

---

## Error format
```json
{ "error": "code_string" }
```
with the matching HTTP status (400/401/403/404).
