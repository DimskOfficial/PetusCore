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

    End Module

End Namespace
