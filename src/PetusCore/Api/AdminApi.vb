Imports System.Text.Json
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Endpoints
Imports PetusCore.Services

Namespace Api

    ''' <summary>
    ''' Admin / moderation REST surface. Every route requires a valid token
    ''' belonging to an account with IsAdmin &gt; 0 (full admin) — moderation
    ''' actions additionally accept ModLevel where noted.
    ''' </summary>
    Public Module AdminApi

        Public Sub Map(app As WebApplication, db As Database, pw As PasswordService, tokens As TokenService)

            ' ---- Overview ------------------------------------------------
            app.MapGet("/api/admin/overview", Function(ctx As HttpContext)
                Dim admin = RequireAdmin(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Return RestApi.Ok(New With {
                    .accounts = db.Accounts.Count(),
                    .users = db.Users.Count(),
                    .levels = db.Levels.Count(),
                    .rated = db.Levels.Read(Function(r) r.Where(Function(x) x.Stars > 0).Count()),
                    .featured = db.Levels.Read(Function(r) r.Where(Function(x) x.Featured > 0).Count()),
                    .banned = db.Accounts.Read(Function(r) r.Where(Function(x) x.IsBanned).Count()),
                    .mods = db.Accounts.Read(Function(r) r.Where(Function(x) x.ModLevel > 0).Count()),
                    .songs = db.Songs.Count(),
                    .recentActions = db.ModActions.All().OrderByDescending(Function(x) x.Timestamp).Take(20)
                })
            End Function)

            ' ---- Accounts management -------------------------------------
            app.MapGet("/api/admin/accounts", Function(ctx As HttpContext)
                Dim admin = RequireAdmin(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim search = ctx.Request.Query("q").ToString().ToLower()
                Dim list = db.Accounts.All().
                    Where(Function(a) search = "" OrElse a.UserName.ToLower().Contains(search)).
                    OrderBy(Function(a) a.AccountID).
                    Select(Function(a) New With {a.AccountID, a.UserName, a.Email, a.IsActive, a.IsBanned, .isAdmin = a.IsAdmin > 0, a.ModLevel, a.RegisterDate})
                Return RestApi.Ok(list)
            End Function)

            ' Grant / revoke moderator (ModLevel 0..2).
            app.MapPost("/api/admin/accounts/{id:int}/mod", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireAdmin(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim level = RestApi.IntOf(req, "modLevel")
                Dim acc = db.FindAccount(id)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 404, "not_found")
                acc.ModLevel = Math.Max(0, Math.Min(2, level))
                db.SaveAccount(acc)
                db.Log(admin.AccountID, "grantMod", id.ToString(), acc.ModLevel.ToString())
                Return RestApi.Ok(New With {.ok = True, .modLevel = acc.ModLevel})
            End Function)

            ' Grant / revoke admin.
            app.MapPost("/api/admin/accounts/{id:int}/admin", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireAdmin(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim acc = db.FindAccount(id)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 404, "not_found")
                acc.IsAdmin = If(RestApi.IntOf(req, "isAdmin") > 0, 1, 0)
                db.SaveAccount(acc)
                db.Log(admin.AccountID, "grantAdmin", id.ToString(), acc.IsAdmin.ToString())
                Return RestApi.Ok(New With {.ok = True, .isAdmin = acc.IsAdmin > 0})
            End Function)

            ' Ban / unban account (also flags the linked user).
            ' Body: { banned: 1|0, reason: "English text", days: 0 } — days=0 => permanent.
            app.MapPost("/api/admin/accounts/{id:int}/ban", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireAdmin(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim banned = RestApi.IntOf(req, "banned") > 0
                Dim reason = RestApi.Str(req, "reason")
                Dim days = RestApi.IntOf(req, "days")
                Dim acc = db.FindAccount(id)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 404, "not_found")
                acc.IsBanned = banned
                If banned Then
                    acc.BanReason = reason
                    acc.BanUntil = If(days > 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + CLng(days) * 86400L, 0L)
                Else
                    acc.BanReason = ""
                    acc.BanUntil = 0
                End If
                db.SaveAccount(acc)
                Dim user = db.FindUserByExt(id.ToString())
                If user IsNot Nothing Then user.IsBanned = If(banned, 1, 0) : db.SaveUser(user)
                db.Log(admin.AccountID, If(banned, "ban", "unban"), id.ToString(), reason)
                Return RestApi.Ok(New With {.ok = True, .banned = acc.IsBanned, .until = acc.BanUntil, .reason = acc.BanReason})
            End Function)

            ' Grant / revoke leaderboard moderator (new in GD 2.2).
            app.MapPost("/api/admin/accounts/{id:int}/lbmod", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireAdmin(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim acc = db.FindAccount(id)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 404, "not_found")
                acc.IsLeaderboardMod = If(RestApi.IntOf(req, "enabled") > 0, 1, 0)
                db.SaveAccount(acc)
                db.Log(admin.AccountID, "grantLbMod", id.ToString(), acc.IsLeaderboardMod.ToString())
                Return RestApi.Ok(New With {.ok = True, .isLeaderboardMod = acc.IsLeaderboardMod > 0})
            End Function)

            ' ---- Level moderation ----------------------------------------
            app.MapGet("/api/admin/levels", Function(ctx As HttpContext)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim q = ctx.Request.Query
                Dim page = RestApi.ParseInt(q("page"), 0)
                Dim perPage = Math.Min(RestApi.ParseInt(q("perPage"), 30), 100)
                Dim search = q("q").ToString().ToLower()
                Dim list = db.Levels.All().
                    Where(Function(l) search = "" OrElse l.LevelName.ToLower().Contains(search) OrElse l.LevelID.ToString() = search).
                    OrderByDescending(Function(l) l.UploadDate).ToList()
                Return RestApi.Ok(New With {
                    .total = list.Count,
                    .items = list.Skip(page * perPage).Take(perPage).Select(AddressOf RestApi.PublicLevel)
                })
            End Function)

            ' Rate a level (stars, difficulty face, feature, epic, coins).
            app.MapPost("/api/admin/levels/{id:int}/rate", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim l = db.Levels.Read(Function(r) r.Find(Function(x) x.LevelID = id))
                If l Is Nothing Then Return RestApi.[Error](ctx, 404, "not_found")
                db.Levels.Write(Sub(r)
                                    Dim lv = r.Find(Function(x) x.LevelID = id)
                                    lv.Stars = RestApi.IntOf(req, "stars", lv.Stars)
                                    lv.Difficulty = RestApi.IntOf(req, "difficulty", lv.Difficulty)
                                    lv.Demon = RestApi.IntOf(req, "demon", lv.Demon)
                                    lv.DemonDiff = RestApi.IntOf(req, "demonDiff", lv.DemonDiff)
                                    lv.Featured = RestApi.IntOf(req, "featured", lv.Featured)
                                    lv.Epic = RestApi.IntOf(req, "epic", lv.Epic)
                                    lv.RateCoins = RestApi.IntOf(req, "coinsVerified", lv.RateCoins)
                                    lv.RatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                    lv.RatedBy = admin.AccountID
                                End Sub)
                db.Log(admin.AccountID, "rateLevel", id.ToString(), RestApi.IntOf(req, "stars").ToString())
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' Set daily / weekly.
            app.MapPost("/api/admin/levels/{id:int}/daily", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim kind = RestApi.IntOf(req, "kind") ' 1 daily, 2 weekly, 0 none
                db.Levels.Write(Sub(r)
                                    Dim lv = r.Find(Function(x) x.LevelID = id)
                                    If lv IsNot Nothing Then
                                        lv.IsDaily = kind
                                        If kind > 0 Then lv.DailyID = db.NextId("dailyID") : lv.RatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                    End If
                                End Sub)
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' Delete a level.
            app.MapDelete("/api/admin/levels/{id:int}", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                db.Levels.Write(Sub(r) r.RemoveAll(Function(x) x.LevelID = id))
                db.Log(admin.AccountID, "deleteLevel", id.ToString(), "")
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' ---- Song moderation -----------------------------------------
            app.MapDelete("/api/admin/songs/{id:int}", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireAdmin(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                db.Songs.Write(Sub(r) r.RemoveAll(Function(x) x.ID = id))
                db.Log(admin.AccountID, "deleteSong", id.ToString(), "")
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' ---- Map packs -----------------------------------------------
            app.MapGet("/api/admin/mappacks", Function(ctx As HttpContext)
                If RequireMod(ctx, tokens, db) Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Return RestApi.Ok(db.MapPacks.All().OrderBy(Function(m) m.ID))
            End Function)

            app.MapPost("/api/admin/mappacks", Function(ctx As HttpContext)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim mp As New MapPack With {
                    .ID = db.NextId("mapPackID"),
                    .Name = RestApi.Str(req, "name"),
                    .Levels = RestApi.Str(req, "levels"),
                    .Stars = RestApi.IntOf(req, "stars"),
                    .Coins = RestApi.IntOf(req, "coins"),
                    .Difficulty = RestApi.IntOf(req, "difficulty"),
                    .Color = RestApi.Str(req, "color", "255,255,255"),
                    .Color2 = RestApi.Str(req, "color2", "255,255,255")
                }
                db.MapPacks.Write(Sub(r) r.Add(mp))
                db.Log(admin.AccountID, "createMapPack", mp.ID.ToString(), mp.Name)
                Return RestApi.Ok(New With {.ok = True, .id = mp.ID})
            End Function)

            app.MapPost("/api/admin/mappacks/{id:int}", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                db.MapPacks.Write(Sub(r)
                                      Dim mp = r.Find(Function(x) x.ID = id)
                                      If mp IsNot Nothing Then
                                          mp.Name = RestApi.Str(req, "name", mp.Name)
                                          mp.Levels = RestApi.Str(req, "levels", mp.Levels)
                                          mp.Stars = RestApi.IntOf(req, "stars", mp.Stars)
                                          mp.Coins = RestApi.IntOf(req, "coins", mp.Coins)
                                          mp.Difficulty = RestApi.IntOf(req, "difficulty", mp.Difficulty)
                                          mp.Color = RestApi.Str(req, "color", mp.Color)
                                          mp.Color2 = RestApi.Str(req, "color2", mp.Color2)
                                      End If
                                  End Sub)
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            app.MapDelete("/api/admin/mappacks/{id:int}", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                db.MapPacks.Write(Sub(r) r.RemoveAll(Function(x) x.ID = id))
                db.Log(admin.AccountID, "deleteMapPack", id.ToString(), "")
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' ---- Gauntlets -----------------------------------------------
            app.MapGet("/api/admin/gauntlets", Function(ctx As HttpContext)
                If RequireMod(ctx, tokens, db) Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Return RestApi.Ok(db.Gauntlets.All().OrderBy(Function(g) g.ID))
            End Function)

            ' Create or update a gauntlet by its type id (1..15).
            app.MapPost("/api/admin/gauntlets", Function(ctx As HttpContext)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim gid = RestApi.IntOf(req, "id")
                Dim levels = RestApi.Str(req, "levels")
                If gid <= 0 Then Return RestApi.[Error](ctx, 400, "id_required")
                db.Gauntlets.Write(Sub(r)
                                       Dim g = r.Find(Function(x) x.ID = gid)
                                       If g Is Nothing Then r.Add(New Gauntlet With {.ID = gid, .Levels = levels}) Else g.Levels = levels
                                   End Sub)
                db.Log(admin.AccountID, "setGauntlet", gid.ToString(), levels)
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            app.MapDelete("/api/admin/gauntlets/{id:int}", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                db.Gauntlets.Write(Sub(r) r.RemoveAll(Function(x) x.ID = id))
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' ---- Quests --------------------------------------------------
            app.MapGet("/api/admin/quests", Function(ctx As HttpContext)
                If RequireMod(ctx, tokens, db) Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Return RestApi.Ok(db.Quests.All().OrderBy(Function(q) q.ID))
            End Function)

            app.MapPost("/api/admin/quests", Function(ctx As HttpContext)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim q As New Quest With {
                    .ID = db.NextId("questID"),
                    .Type = RestApi.IntOf(req, "type", 1),
                    .Amount = RestApi.IntOf(req, "amount"),
                    .Reward = RestApi.IntOf(req, "reward"),
                    .Name = RestApi.Str(req, "name")
                }
                db.Quests.Write(Sub(r) r.Add(q))
                db.Log(admin.AccountID, "createQuest", q.ID.ToString(), q.Name)
                Return RestApi.Ok(New With {.ok = True, .id = q.ID})
            End Function)

            app.MapDelete("/api/admin/quests/{id:int}", Function(ctx As HttpContext, id As Integer)
                Dim admin = RequireMod(ctx, tokens, db)
                If admin Is Nothing Then Return RestApi.[Error](ctx, 403, "forbidden")
                db.Quests.Write(Sub(r) r.RemoveAll(Function(x) x.ID = id))
                Return RestApi.Ok(New With {.ok = True})
            End Function)
        End Sub

        ' ---------------- guards --------------------------------------
        Private Function RequireAdmin(ctx As HttpContext, tokens As TokenService, db As Database) As Account
            Dim acc = RestApi.RequireAuth(ctx, tokens)
            If acc Is Nothing OrElse acc.IsAdmin <= 0 Then Return Nothing
            Return acc
        End Function

        Private Function RequireMod(ctx As HttpContext, tokens As TokenService, db As Database) As Account
            Dim acc = RestApi.RequireAuth(ctx, tokens)
            If acc Is Nothing Then Return Nothing
            If acc.IsAdmin > 0 OrElse acc.ModLevel > 0 Then Return acc
            Return Nothing
        End Function

    End Module

End Namespace
