Imports System.Text
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services

Namespace Api

    ''' <summary>
    ''' Website-facing REST beyond the basic browse: single-level detail with a
    ''' preview image, comments + likes that are shared with the in-game data and
    ''' gated behind 20% progress, the mod's 50%-preview upload, and the editable
    ''' built-in "Play" levels.
    ''' </summary>
    Public Module SiteApi

        Public Sub Map(app As WebApplication)
            Dim db = app.Services.GetRequiredService(Of Database)()
            Dim tokens = app.Services.GetRequiredService(Of TokenService)()
            Dim cfg = app.Services.GetRequiredService(Of ServerConfig)()

            ' ---- Comments (shared with in-game) --------------------------
            ' (single-level detail is served by RestApi's /api/levels/{id})
            app.MapGet("/api/levels/{id:int}/comments", Function(id As Integer)
                Dim rows = db.Comments.Read(Function(r) r.Where(Function(c) c.LevelID = id).OrderByDescending(Function(c) c.Timestamp).Take(100).ToList())
                Dim outp = rows.Select(Function(c)
                                           Dim u = db.FindUserById(c.UserID)
                                           Return New With {
                                               c.CommentID,
                                               .userID = c.UserID,
                                               .userName = If(u IsNot Nothing, u.UserName, "?"),
                                               .content = DecodeB64(c.Content),
                                               c.Likes, c.Percent, c.Timestamp
                                           }
                                       End Function).ToList()
                Return RestApi.Ok(outp)
            End Function)

            app.MapPost("/api/levels/{id:int}/comments", Function(ctx As HttpContext, id As Integer)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim l = db.Levels.Read(Function(r) r.Find(Function(x) x.LevelID = id))
                If l Is Nothing Then Return RestApi.[Error](ctx, 404, "not_found")

                Dim pct = BestPercent(db, acc.AccountID, id)
                If pct < 20 Then Return RestApi.[Error](ctx, 403, "need_20_percent")

                Dim req = RestApi.ReadJson(ctx)
                Dim text = RestApi.Str(req, "content").Trim()
                If text = "" OrElse text.Length > 500 Then Return RestApi.[Error](ctx, 400, "bad_content")

                Dim user = db.ResolveUser(acc.AccountID.ToString(), acc.UserName)
                Dim c As New Comment With {
                    .CommentID = db.NextId("commentID"),
                    .UserID = user.UserID,
                    .LevelID = id,
                    .Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
                    .Percent = pct,
                    .Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
                db.Comments.Write(Sub(r) r.Add(c))
                Return RestApi.Ok(New With {.ok = True, .id = c.CommentID})
            End Function)

            ' ---- Like a level (shared with in-game count, anti-double) ----
            app.MapPost("/api/levels/{id:int}/like", Function(ctx As HttpContext, id As Integer)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim l = db.Levels.Read(Function(r) r.Find(Function(x) x.LevelID = id))
                If l Is Nothing Then Return RestApi.[Error](ctx, 404, "not_found")

                Dim pct = BestPercent(db, acc.AccountID, id)
                If pct < 20 Then Return RestApi.[Error](ctx, 403, "need_20_percent")

                Dim already = db.LevelLikes.Read(Function(r) r.Any(Function(x) x.LevelID = id AndAlso x.AccountID = acc.AccountID))
                If already Then Return RestApi.[Error](ctx, 409, "already_liked")

                db.LevelLikes.Write(Sub(r) r.Add(New LevelLike With {.ID = db.NextId("levelLike"), .LevelID = id, .AccountID = acc.AccountID, .Value = 1}))
                db.Levels.Write(Sub(r)
                                    Dim lv = r.Find(Function(x) x.LevelID = id)
                                    If lv IsNot Nothing Then lv.Likes += 1
                                End Sub)
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' ---- Post a comment on a user's profile ----------------------
            app.MapPost("/api/users/{name}/comments", Function(ctx As HttpContext, name As String)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim target = db.FindAccountByName(name)
                If target Is Nothing Then Return RestApi.[Error](ctx, 404, "not_found")

                Dim req = RestApi.ReadJson(ctx)
                Dim text = RestApi.Str(req, "content").Trim()
                If text = "" OrElse text.Length > 500 Then Return RestApi.[Error](ctx, 400, "bad_content")

                Dim user = db.ResolveUser(acc.AccountID.ToString(), acc.UserName)
                Dim c As New AccountComment With {
                    .CommentID = db.NextId("commentID"),
                    .AccountID = target.AccountID,
                    .UserID = user.UserID,
                    .Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
                    .Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
                db.AccountComments.Write(Sub(r) r.Add(c))
                Return RestApi.Ok(New With {.ok = True, .id = c.CommentID})
            End Function)

            ' ---- Submit a moderator application --------------------------
            app.MapPost("/api/mod-application", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim req = RestApi.ReadJson(ctx)
                Dim role = RestApi.Str(req, "role", "mod").ToLower()
                If role <> "mod" AndAlso role <> "elder" AndAlso role <> "leaderboard" Then role = "mod"
                Dim msg = RestApi.Str(req, "message").Trim()
                If msg = "" OrElse msg.Length > 2000 Then Return RestApi.[Error](ctx, 400, "bad_content")

                ' One pending application per account.
                Dim hasPending = db.ModApplications.Read(Function(r) r.Any(Function(a) a.AccountID = acc.AccountID AndAlso a.Status = 0))
                If hasPending Then Return RestApi.[Error](ctx, 409, "already_pending")

                Dim app2 As New ModApplication With {
                    .ID = db.NextId("modApplication"),
                    .AccountID = acc.AccountID,
                    .UserName = acc.UserName,
                    .Role = role,
                    .Message = Convert.ToBase64String(Encoding.UTF8.GetBytes(msg)),
                    .Status = 0,
                    .Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
                db.ModApplications.Write(Sub(r) r.Add(app2))
                Return RestApi.Ok(New With {.ok = True, .id = app2.ID})
            End Function)

            ' ---- Mod uploads the 50%-progress preview image URL ----------
            app.MapPost("/api/levels/{id:int}/preview", Function(ctx As HttpContext, id As Integer)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                If acc.IsAdmin <= 0 AndAlso acc.ModLevel <= 0 Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim url = RestApi.Str(req, "url").Trim()
                If Not url.StartsWith("http") Then Return RestApi.[Error](ctx, 400, "bad_url")
                Dim found = False
                db.Levels.Write(Sub(r)
                                    Dim lv = r.Find(Function(x) x.LevelID = id)
                                    If lv IsNot Nothing Then lv.PreviewUrl = url : found = True
                                End Sub)
                If Not found Then Return RestApi.[Error](ctx, 404, "not_found")
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' ---- Default "Play" levels: public config (for the mod) ------
            app.MapGet("/api/defaultlevels", Function()
                Dim rows = db.DefaultLevels.Read(Function(r) r.Where(Function(d) d.Enabled = 1).OrderBy(Function(d) d.Slot).ToList())
                Return RestApi.Ok(rows.Select(Function(d)
                                                  Dim lv = db.Levels.Read(Function(r) r.Find(Function(x) x.LevelID = d.LevelID))
                                                  Return New With {
                                                      d.Slot, d.LevelID,
                                                      .name = If(d.Name <> "", d.Name, If(lv IsNot Nothing, lv.LevelName, "")),
                                                      .levelString = If(lv IsNot Nothing, lv.LevelString, "")
                                                  }
                                              End Function).ToList())
            End Function)

            ' ---- Default levels: admin management ------------------------
            app.MapGet("/api/admin/defaultlevels", Function(ctx As HttpContext)
                If Not IsMod(ctx, tokens) Then Return RestApi.[Error](ctx, 403, "forbidden")
                Return RestApi.Ok(db.DefaultLevels.Read(Function(r) r.OrderBy(Function(d) d.Slot).ToList()))
            End Function)

            app.MapPost("/api/admin/defaultlevels", Function(ctx As HttpContext)
                If Not IsMod(ctx, tokens) Then Return RestApi.[Error](ctx, 403, "forbidden")
                Dim req = RestApi.ReadJson(ctx)
                Dim slot = RestApi.IntOf(req, "slot")
                If slot <= 0 Then Return RestApi.[Error](ctx, 400, "bad_slot")
                db.DefaultLevels.Write(Sub(r)
                                           Dim d = r.Find(Function(x) x.Slot = slot)
                                           If d Is Nothing Then
                                               d = New DefaultLevel With {.Slot = slot}
                                               r.Add(d)
                                           End If
                                           d.LevelID = RestApi.IntOf(req, "levelID")
                                           d.Name = RestApi.Str(req, "name")
                                           d.Enabled = If(RestApi.IntOf(req, "enabled", 1) > 0, 1, 0)
                                       End Sub)
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            app.MapDelete("/api/admin/defaultlevels/{slot:int}", Function(ctx As HttpContext, slot As Integer)
                If Not IsMod(ctx, tokens) Then Return RestApi.[Error](ctx, 403, "forbidden")
                db.DefaultLevels.Write(Sub(r) r.RemoveAll(Function(x) x.Slot = slot))
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' ---- Public player profile (for /u/[name]) -------------------
            app.MapGet("/api/users/{name}", Function(ctx As HttpContext, name As String)
                Dim acc = db.FindAccountByName(name)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 404, "not_found")
                Dim user = db.FindUserByExt(acc.AccountID.ToString())

                ' Derive badges from role + in-game stats.
                Dim badges = New List(Of String)()
                If acc.IsAdmin > 0 Then badges.Add("admin")
                If acc.ModLevel >= 2 Then badges.Add("elder")
                If acc.ModLevel = 1 Then badges.Add("mod")
                If acc.IsLeaderboardMod > 0 Then badges.Add("lbmod")
                If user IsNot Nothing Then
                    If user.Stars >= 1000 Then badges.Add("stars1000")
                    If user.Demons >= 10 Then badges.Add("demonslayer")
                    If user.CreatorPoints >= 1 Then badges.Add("creator")
                    If user.Diamonds >= 500 Then badges.Add("diamonds500")
                End If

                ' Levels created by this account (listed only).
                Dim myLevels = db.Levels.Read(Function(r) r.Where(Function(l) l.ExtID = acc.AccountID.ToString() AndAlso l.Unlisted = 0).OrderByDescending(Function(l) l.UploadDate).Take(50).ToList())

                ' Profile comments (base64 in AccountComments).
                Dim pComments = db.AccountComments.Read(Function(r) r.Where(Function(c) c.AccountID = acc.AccountID).OrderByDescending(Function(c) c.Timestamp).Take(50).ToList())

                Return RestApi.Ok(New With {
                    acc.AccountID, acc.UserName,
                    .isAdmin = acc.IsAdmin > 0, .modLevel = acc.ModLevel,
                    .isLeaderboardMod = acc.IsLeaderboardMod > 0,
                    acc.RegisterDate,
                    .youtube = acc.Youtube, .twitter = acc.Twitter, .twitch = acc.Twitch,
                    .discord = acc.Discord, .instagram = acc.Instagram, .tiktok = acc.Tiktok,
                    .icon = If(user Is Nothing, Nothing, New With {
                        .cube = user.AccIcon, .color1 = user.Color1, .color2 = user.Color2,
                        .color3 = user.Color3, .glow = user.AccGlow
                    }),
                    .stats = If(user Is Nothing, Nothing, New With {user.Stars, user.Demons, user.Diamonds, user.Coins, user.UserCoins, .creatorPoints = user.CreatorPoints, user.Moons, user.CompletedLvls}),
                    .badges = badges,
                    .levels = myLevels.Select(Function(l) New With {l.LevelID, l.LevelName, l.Downloads, l.Likes, l.Stars, l.Difficulty, .previewUrl = l.PreviewUrl}).ToList(),
                    .comments = pComments.Select(Function(c) New With {c.CommentID, .content = DecodeB64(c.Content), c.Likes, c.Timestamp, .author = AuthorName(db, c.UserID)}).ToList()
                })
            End Function)
        End Sub

        ' Best percent this account reached on a level (0 if never played).
        Private Function BestPercent(db As Database, accountID As Integer, levelID As Integer) As Integer
            Dim scores = db.Scores.Read(Function(r) r.Where(Function(s) s.LevelID = levelID AndAlso s.AccountID = accountID).ToList())
            If scores.Count = 0 Then Return 0
            Return scores.Max(Function(s) s.Percent)
        End Function

        Private Function IsMod(ctx As HttpContext, tokens As TokenService) As Boolean
            Dim acc = RestApi.RequireAuth(ctx, tokens)
            Return acc IsNot Nothing AndAlso (acc.IsAdmin > 0 OrElse acc.ModLevel > 0)
        End Function

        Private Function DecodeB64(s As String) As String
            If String.IsNullOrEmpty(s) Then Return ""
            Try
                Return Encoding.UTF8.GetString(Convert.FromBase64String(s))
            Catch
                Return s
            End Try
        End Function

        ' Resolve a commenter's display name from their in-game user id.
        Private Function AuthorName(db As Database, userID As Integer) As String
            Dim u = db.FindUserById(userID)
            Return If(u Is Nothing, "Player", u.UserName)
        End Function

    End Module

End Namespace
