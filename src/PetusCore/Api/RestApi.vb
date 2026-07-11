Imports System.Text.Json
Imports System.IO
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Endpoints
Imports PetusCore.Services

Namespace Api

    ''' <summary>
    ''' Clean JSON REST API consumed by the PetusGDPS website. Covers auth,
    ''' server stats, account self-service (change password, recover, socials),
    ''' level browsing, music upload and a full admin surface (rate levels,
    ''' grant/revoke mod, ban users, manage songs).
    '''
    ''' Auth model: POST /api/auth/login returns a bearer token; protected
    ''' routes expect "Authorization: Bearer &lt;token&gt;".
    ''' </summary>
    Public Module RestApi

        Public Sub Map(app As WebApplication)
            Dim db = app.Services.GetRequiredService(Of Database)()
            Dim pw = app.Services.GetRequiredService(Of PasswordService)()
            Dim tokens = app.Services.GetRequiredService(Of TokenService)()
            Dim cfg = app.Services.GetRequiredService(Of ServerConfig)()

            ' ---- Public: server info & stats -----------------------------
            app.MapGet("/api/info", Function() Ok(New With {
                .name = cfg.ServerName,
                .core = "PetusCore",
                .version = ServerConfig.Version,
                .gdVersion = "2.2",
                .download = cfg.GameDownloadUrl
            }))

            ' Game update manifest for the launcher. Served straight from the core
            ' (Kestrel, no CDN cache) so a new build is picked up immediately; the
            ' .zip itself is a versioned, cache-proof CDN URL.
            app.MapGet("/api/game/manifest", Function() Ok(New With {
                .version = cfg.GameVersion,
                .url = cfg.GameZipUrl
            }))

            app.MapGet("/api/stats", Function()
                Return Ok(New With {
                    .accounts = db.Accounts.Count(),
                    .users = db.Users.Count(),
                    .levels = db.Levels.Count(),
                    .ratedLevels = db.Levels.Read(Function(r) r.Where(Function(x) x.Stars > 0).Count()),
                    .comments = db.Comments.Count() + db.AccountComments.Count(),
                    .songs = db.Songs.Count()
                })
            End Function)

            app.MapGet("/api/leaderboard", Function()
                ' Registered (Petus) accounts only — no green/UDID players.
                Dim top = db.Users.All().Where(Function(u) u.Stars > 0 AndAlso u.IsRegistered = 1 AndAlso IsNumeric(u.ExtID)).
                             OrderByDescending(Function(u) u.Stars).Take(100).
                             Select(Function(u) New With {u.UserID, u.UserName, u.Stars, u.Demons, u.Diamonds, u.Coins, .creatorPoints = u.CreatorPoints})
                Return Ok(top)
            End Function)

            ' ---- Auth ----------------------------------------------------
            app.MapPost("/api/auth/login", Function(ctx As HttpContext)
                Dim req = ReadJson(ctx)
                Dim userName = Str(req, "username")
                Dim password = Str(req, "password")
                Dim acc = db.FindAccountByName(userName)
                If acc Is Nothing OrElse Not pw.VerifyRawPassword(password, acc) Then Return [Error](ctx, 401, "invalid_credentials")
                If acc.IsBanned Then Return [Error](ctx, 403, "banned")
                ' Bootstrap: auto-promote the configured admin username on first login.
                If cfg.AdminUser <> "" AndAlso String.Equals(acc.UserName, cfg.AdminUser, StringComparison.OrdinalIgnoreCase) AndAlso acc.IsAdmin <= 0 Then
                    acc.IsAdmin = 1
                    acc.ModLevel = 2
                    db.SaveAccount(acc)
                End If
                Dim tok = tokens.Issue(acc.AccountID)
                Return Ok(New With {
                    .token = tok.Token,
                    .expiresAt = tok.ExpiresAt,
                    .account = PublicAccount(acc, db)
                })
            End Function)

            app.MapPost("/api/auth/logout", Function(ctx As HttpContext)
                Dim tk = BearerToken(ctx)
                If tk <> "" Then tokens.Revoke(tk)
                Return Ok(New With {.ok = True})
            End Function)

            app.MapGet("/api/auth/me", Function(ctx As HttpContext)
                Dim acc = RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return [Error](ctx, 401, "unauthorized")
                Return Ok(PublicAccount(acc, db))
            End Function)

            ' ---- Account self-service ------------------------------------
            app.MapPost("/api/account/change-password", Function(ctx As HttpContext)
                Dim acc = RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return [Error](ctx, 401, "unauthorized")
                Dim req = ReadJson(ctx)
                Dim oldPass = Str(req, "oldPassword")
                Dim newPass = Str(req, "newPassword")
                If Not pw.VerifyRawPassword(oldPass, acc) Then Return [Error](ctx, 400, "wrong_old_password")
                If newPass.Length < 6 Then Return [Error](ctx, 400, "password_too_short")
                acc.Password = pw.HashPassword(newPass)
                acc.Gjp2 = pw.HashGjp2(newPass)
                db.SaveAccount(acc)
                Return Ok(New With {.ok = True})
            End Function)

            app.MapPost("/api/account/socials", Function(ctx As HttpContext)
                Dim acc = RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return [Error](ctx, 401, "unauthorized")
                Dim req = ReadJson(ctx)
                acc.Youtube = Str(req, "youtube", acc.Youtube)
                acc.Twitter = Str(req, "twitter", acc.Twitter)
                acc.Twitch = Str(req, "twitch", acc.Twitch)
                acc.Discord = Str(req, "discord", acc.Discord)
                acc.Instagram = Str(req, "instagram", acc.Instagram)
                acc.Tiktok = Str(req, "tiktok", acc.Tiktok)
                acc.PetusPage = Str(req, "petusPage", acc.PetusPage)
                db.SaveAccount(acc)
                Return Ok(New With {.ok = True})
            End Function)

            ' Request a recovery code (in a real deployment you'd email it).
            app.MapPost("/api/account/recover-request", Function(ctx As HttpContext)
                Dim req = ReadJson(ctx)
                Dim userName = Str(req, "username")
                Dim acc = db.FindAccountByName(userName)
                If acc Is Nothing Then Return Ok(New With {.ok = True}) ' don't leak existence
                acc.RecoveryCode = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()
                acc.RecoveryExpires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600
                db.SaveAccount(acc)
                ' NOTE: exposed here for self-hosted convenience; wire to email/Discord in prod.
                Return Ok(New With {.ok = True, .code = acc.RecoveryCode, .expiresAt = acc.RecoveryExpires})
            End Function)

            app.MapPost("/api/account/recover-confirm", Function(ctx As HttpContext)
                Dim req = ReadJson(ctx)
                Dim userName = Str(req, "username")
                Dim code = Str(req, "code")
                Dim newPass = Str(req, "newPassword")
                Dim acc = db.FindAccountByName(userName)
                If acc Is Nothing OrElse acc.RecoveryCode = "" OrElse
                   Not String.Equals(acc.RecoveryCode, code, StringComparison.OrdinalIgnoreCase) OrElse
                   acc.RecoveryExpires < DateTimeOffset.UtcNow.ToUnixTimeSeconds() Then
                    Return [Error](ctx, 400, "invalid_code")
                End If
                If newPass.Length < 6 Then Return [Error](ctx, 400, "password_too_short")
                acc.Password = pw.HashPassword(newPass)
                acc.Gjp2 = pw.HashGjp2(newPass)
                acc.RecoveryCode = ""
                acc.RecoveryExpires = 0
                db.SaveAccount(acc)
                Return Ok(New With {.ok = True})
            End Function)

            ' ---- Music upload --------------------------------------------
            app.MapPost("/api/songs", Function(ctx As HttpContext)
                Dim acc = RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return [Error](ctx, 401, "unauthorized")
                Dim req = ReadJson(ctx)
                Dim song As New Song With {
                    .ID = db.NextId("songID"),
                    .Name = Str(req, "name"),
                    .ArtistName = Str(req, "artist"),
                    .ArtistID = acc.AccountID,
                    .Download = Str(req, "url"),
                    .Size = Num(req, "size"),
                    .UploadedBy = acc.AccountID
                }
                If song.Name = "" OrElse song.Download = "" Then Return [Error](ctx, 400, "name_and_url_required")
                db.Songs.Write(Sub(r) r.Add(song))
                Return Ok(New With {.ok = True, .id = song.ID})
            End Function)

            app.MapGet("/api/songs", Function()
                Return Ok(db.Songs.All().Select(Function(s) New With {s.ID, s.Name, .artist = s.ArtistName, .size = s.Size, .url = s.Download, .uploadedBy = s.UploadedBy}))
            End Function)

            ' ---- Music file upload (drag & drop) -------------------------
            ' Accepts a multipart .mp3, stores it under <db>/music, and serves it
            ' back at /music/<id>.mp3 so the GD client can stream it directly.
            app.MapPost("/api/songs/upload", Function(ctx As HttpContext)
                Dim acc = RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return [Error](ctx, 401, "unauthorized")
                If Not ctx.Request.HasFormContentType Then Return [Error](ctx, 400, "expected_multipart")

                Dim form = ctx.Request.ReadFormAsync().Result
                Dim upload = If(form.Files.Count > 0, form.Files(0), Nothing)
                If upload Is Nothing OrElse upload.Length = 0 Then Return [Error](ctx, 400, "no_file")
                If upload.Length > 25 * 1024 * 1024 Then Return [Error](ctx, 413, "file_too_large")

                Dim ext = Path.GetExtension(upload.FileName).ToLowerInvariant()
                If ext <> ".mp3" Then Return [Error](ctx, 415, "only_mp3")

                Dim id = db.NextId("songID")
                Using ms As New MemoryStream()
                    upload.CopyTo(ms)
                    Dim b64 = Convert.ToBase64String(ms.ToArray())
                    db.Music.Write(Sub(r) r.Add(New MusicFile With {.ID = id, .Data = b64}))
                End Using

                Dim sizeMb = Math.Round(upload.Length / 1024.0 / 1024.0, 2)
                Dim baseUrl = If(cfg.PublicUrl <> "", cfg.PublicUrl, $"{ctx.Request.Scheme}://{ctx.Request.Host}")
                Dim name = If(form("name").ToString(), "")
                If name = "" Then name = Path.GetFileNameWithoutExtension(upload.FileName)
                Dim artist = If(form("artist").ToString(), "")
                If artist = "" Then artist = acc.UserName

                Dim song As New Song With {
                    .ID = id,
                    .Name = name,
                    .ArtistName = artist,
                    .ArtistID = acc.AccountID,
                    .Download = $"{baseUrl}/music/{id}.mp3",
                    .Size = sizeMb,
                    .UploadedBy = acc.AccountID
                }
                db.Songs.Write(Sub(r) r.Add(song))
                Return Ok(New With {.ok = True, .id = song.ID, .url = song.Download, .size = song.Size, .name = song.Name})
            End Function)

            ' Serve uploaded mp3s to the GD client (stored in the DB).
            app.MapGet("/music/{name}", Function(ctx As HttpContext, name As String)
                Dim baseName = Path.GetFileNameWithoutExtension(name)
                Dim id As Integer
                If Not name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) OrElse Not Integer.TryParse(baseName, id) Then
                    Return Results.NotFound()
                End If
                Dim mf = db.Music.Read(Function(r) r.Find(Function(x) x.ID = id))
                If mf Is Nothing OrElse mf.Data = "" Then Return Results.NotFound()
                Dim bytes = Convert.FromBase64String(mf.Data)
                Return Results.File(bytes, "audio/mpeg", enableRangeProcessing:=True)
            End Function)

            ' ---- Levels (browse) -----------------------------------------
            app.MapGet("/api/levels", Function(ctx As HttpContext)
                Dim q = ctx.Request.Query
                Dim page = ParseInt(q("page"), 0)
                Dim perPage = Math.Min(ParseInt(q("perPage"), 20), 100)
                Dim sort = If(q("sort").ToString(), "recent")
                Dim all = db.Levels.All().Where(Function(x) x.Unlisted = 0)
                Select Case sort
                    Case "downloads" : all = all.OrderByDescending(Function(x) x.Downloads)
                    Case "likes" : all = all.OrderByDescending(Function(x) x.Likes)
                    Case "rated" : all = all.Where(Function(x) x.Stars > 0).OrderByDescending(Function(x) x.RatedAt)
                    Case Else : all = all.OrderByDescending(Function(x) x.UploadDate)
                End Select
                Dim list = all.ToList()
                Return Ok(New With {
                    .total = list.Count,
                    .page = page,
                    .items = list.Skip(page * perPage).Take(perPage).Select(AddressOf PublicLevel)
                })
            End Function)

            app.MapGet("/api/levels/{id:int}", Function(id As Integer)
                Dim l = db.Levels.Read(Function(r) r.Find(Function(x) x.LevelID = id))
                If l Is Nothing Then Return Results.NotFound(New With {.error = "not_found"})
                Return Ok(PublicLevel(l))
            End Function)

            ' ---- Admin ---------------------------------------------------
            AdminApi.Map(app, db, pw, tokens)
        End Sub

        ' ================= helpers ====================================

        ''' <summary>
        ''' Total experience for a user, using the Better Progression formula
        ''' (stars*5 + moons*5 + diamonds*2 + secretCoins*100 + userCoins*20 +
        ''' demons*75 + creatorPoints*5000). Computed from stats we already store.
        ''' </summary>
        Public Function ExperienceFor(user As Data.Models.GdUser) As Long
            If user Is Nothing Then Return 0
            Dim xp As Long = 0
            xp += CLng(user.Stars) * 5
            xp += CLng(user.Moons) * 5
            xp += CLng(user.Diamonds) * 2
            xp += CLng(user.Coins) * 100      ' secret coins
            xp += CLng(user.UserCoins) * 20
            xp += CLng(user.Demons) * 75
            xp += CLng(user.CreatorPoints) * 5000
            Return xp
        End Function

        ''' <summary>Player level from total XP. Each level costs 1000 more XP than
        ''' the previous (level 1: 1000, level 2: +2000, ...), a smooth curve.</summary>
        Public Function LevelFor(xp As Long) As Integer
            Dim lvl = 0
            Dim need As Long = 1000
            Dim acc As Long = 0
            While acc + need <= xp AndAlso lvl < 999
                acc += need
                lvl += 1
                need += 1000
            End While
            Return lvl
        End Function

        Public Function PublicAccount(acc As Account, db As Database) As Object
            Dim user = db.FindUserByExt(acc.AccountID.ToString())
            Dim xp = ExperienceFor(user)
            Return New With {
                acc.AccountID, acc.UserName, acc.Email, acc.IsActive, acc.IsBanned,
                .banReason = acc.BanReason, .banUntil = acc.BanUntil,
                .isAdmin = acc.IsAdmin > 0, .modLevel = acc.ModLevel,
                .isLeaderboardMod = acc.IsLeaderboardMod > 0, acc.RegisterDate,
                .petusLinked = acc.PetusId <> "",
                .youtube = acc.Youtube, .twitter = acc.Twitter, .twitch = acc.Twitch,
                .discord = acc.Discord, .instagram = acc.Instagram, .tiktok = acc.Tiktok,
                .petusPage = acc.PetusPage,
                .icon = If(user Is Nothing, Nothing, New With {
                    .cube = user.AccIcon, .color1 = user.Color1, .color2 = user.Color2,
                    .color3 = user.Color3, .glow = user.AccGlow, .iconType = user.IconType,
                    .displayIcon = user.Icon
                }),
                .experience = xp, .level = LevelFor(xp),
                .stats = If(user Is Nothing, Nothing, New With {user.Stars, user.Demons, user.Diamonds, .moons = user.Moons, user.Coins, user.UserCoins, .creatorPoints = user.CreatorPoints})
            }
        End Function

        Public Function PublicLevel(l As Level) As Object
            Return New With {
                l.LevelID, l.LevelName, l.LevelDesc, .author = l.UserName, l.UserID,
                l.Downloads, l.Likes, l.Stars, l.Difficulty, l.Demon, l.Featured, l.Epic,
                l.Coins, l.SongID, .length = l.Length, .uploadDate = l.UploadDate, .unlisted = l.Unlisted,
                .previewUrl = l.PreviewUrl
            }
        End Function

        Public Function RequireAuth(ctx As HttpContext, tokens As TokenService) As Account
            Return tokens.Validate(BearerToken(ctx))
        End Function

        Public Function BearerToken(ctx As HttpContext) As String
            Dim h = ctx.Request.Headers("Authorization").ToString()
            If h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) Then Return h.Substring(7).Trim()
            Return ""
        End Function

        Public Function Ok(value As Object) As IResult
            Return Results.Json(value, Json.Options)
        End Function

        Public Function [Error](ctx As HttpContext, status As Integer, code As String) As IResult
            Return Results.Json(New With {.error = code}, Json.Options, statusCode:=status)
        End Function

        ''' <summary>Read and parse the request body as JSON (blocks on async read).</summary>
        Public Function ReadJson(ctx As HttpContext) As JsonElement
            Try
                Dim body = New IO.StreamReader(ctx.Request.Body).ReadToEndAsync().GetAwaiter().GetResult()
                If String.IsNullOrWhiteSpace(body) Then Return New JsonElement()
                Using doc = JsonDocument.Parse(body)
                    Return doc.RootElement.Clone()
                End Using
            Catch
                Return New JsonElement()
            End Try
        End Function

        Public Function Str(el As JsonElement, name As String, Optional dflt As String = "") As String
            Dim p As JsonElement
            If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(name, p) Then
                If p.ValueKind = JsonValueKind.String Then Return p.GetString()
                If p.ValueKind = JsonValueKind.Number Then Return p.ToString()
            End If
            Return dflt
        End Function

        Public Function Num(el As JsonElement, name As String, Optional dflt As Double = 0) As Double
            Dim p As JsonElement
            If el.ValueKind = JsonValueKind.Object AndAlso el.TryGetProperty(name, p) Then
                If p.ValueKind = JsonValueKind.Number Then Return p.GetDouble()
                Dim d As Double
                If p.ValueKind = JsonValueKind.String AndAlso Double.TryParse(p.GetString(), d) Then Return d
            End If
            Return dflt
        End Function

        Public Function IntOf(el As JsonElement, name As String, Optional dflt As Integer = 0) As Integer
            Return CInt(Num(el, name, dflt))
        End Function

        Public Function ParseInt(s As String, dflt As Integer) As Integer
            Dim n As Integer
            If Integer.TryParse(s, n) Then Return n
            Return dflt
        End Function

    End Module

End Namespace
