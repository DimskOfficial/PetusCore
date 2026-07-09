Imports Microsoft.AspNetCore.Builder
Imports System.Text
Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>Leaderboards, per-level scores, user search, likes, star rating.</summary>
    Public Module ScoreEndpoints

        Public Sub Map(app As Object, db As Database, pw As PasswordService, hash As HashService)
            Dim a = DirectCast(app, Microsoft.AspNetCore.Builder.WebApplication)

            ' --- Global leaderboard (getGJScores20) -----------------------
            For Each path In {"/getGJScores20.php", "/getGJScores19.php", "/getGJScores.php"}
                a.MapPost(path, Function(ctx As HttpContext)
                    Dim type_ = GdHelpers.Form(ctx, "type")
                    Dim all = db.Users.All()
                    Dim ranked As List(Of GdUser)
                    Select Case type_
                        Case "creators"
                            ranked = all.OrderByDescending(Function(x) x.CreatorPoints).Take(100).ToList()
                        Case Else ' "top" / stars
                            ranked = all.Where(Function(x) x.Stars > 0).OrderByDescending(Function(x) x.Stars).Take(100).ToList()
                    End Select
                    Dim sb As New StringBuilder()
                    Dim rank = 1
                    For Each u In ranked
                        sb.Append(ProfileStringForLeaderboard(u, db, rank)).Append("|")
                        rank += 1
                    Next
                    Return GdHelpers.Text(sb.ToString())
                End Function)
            Next

            ' --- Per-level scores (getGJLevelScores211) -------------------
            For Each path In {"/getGJLevelScores211.php", "/getGJLevelScores.php"}
                a.MapPost(path, Function(ctx As HttpContext)
                    Dim levelID = GdHelpers.FormInt(ctx, "levelID")
                    Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                    Dim percent = GdHelpers.FormInt(ctx, "percent")

                    ' Submit the requester's score if provided.
                    If accountID > 0 AndAlso percent > 0 Then
                        Dim user = db.FindUserByExt(accountID.ToString())
                        If user IsNot Nothing Then
                            db.Scores.Write(Sub(r)
                                                Dim ex = r.Find(Function(s) s.LevelID = levelID AndAlso s.AccountID = accountID)
                                                If ex Is Nothing Then
                                                    r.Add(New LevelScore With {.LevelID = levelID, .AccountID = accountID, .UserID = user.UserID, .Percent = percent, .Coins = GdHelpers.FormInt(ctx, "coins"), .Attempts = GdHelpers.FormInt(ctx, "attempts"), .Timestamp = GdHelpers.Now()})
                                                ElseIf percent > ex.Percent Then
                                                    ex.Percent = percent : ex.Timestamp = GdHelpers.Now()
                                                End If
                                            End Sub)
                        End If
                    End If

                    Dim scores = db.Scores.All().Where(Function(s) s.LevelID = levelID).
                                    OrderByDescending(Function(s) s.Percent).ThenBy(Function(s) s.Timestamp).Take(100).ToList()
                    Dim sb As New StringBuilder()
                    Dim rank = 1
                    For Each s In scores
                        Dim u = db.FindUserById(s.UserID)
                        If u Is Nothing Then Continue For
                        sb.Append(BuildLevelScore(u, s, rank)).Append("|")
                        rank += 1
                    Next
                    Return GdHelpers.Text(sb.ToString())
                End Function)
            Next

            ' --- User search (getGJUsers20) -------------------------------
            a.MapPost("/getGJUsers20.php", Function(ctx As HttpContext)
                Dim str = GdHelpers.Clean(GdHelpers.Form(ctx, "str"))
                Dim page = GdHelpers.FormInt(ctx, "page")
                Dim perPage = 10
                Dim matches As List(Of GdUser)
                Dim byId As Integer
                If Integer.TryParse(str, byId) Then
                    matches = db.Users.All().Where(Function(u) u.ExtID = str OrElse u.UserID = byId).ToList()
                Else
                    matches = db.Users.All().Where(Function(u) u.UserName.IndexOf(str, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
                End If
                matches = matches.Where(Function(u) u.IsRegistered = 1).OrderByDescending(Function(u) u.Stars).ToList()
                Dim total = matches.Count
                Dim items = matches.Skip(page * perPage).Take(perPage).ToList()
                If items.Count = 0 Then Return GdHelpers.Text("-1")
                Dim sb As New StringBuilder()
                For Each u In items
                    Dim acc = db.FindAccount(CInt(If(IsNumeric(u.ExtID), u.ExtID, "0")))
                    If sb.Length > 0 Then sb.Append("|")
                    sb.Append(ProfileString.Build(u, acc, db))
                Next
                sb.Append("#").Append($"{total}:{page * perPage}:{perPage}")
                Return GdHelpers.Text(sb.ToString())
            End Function)

            ' --- Like an item (level/comment) -----------------------------
            For Each path In {"/likeGJItem211.php", "/likeGJItem21.php", "/likeGJItem20.php", "/likeGJItem.php"}
                a.MapPost(path, Function(ctx As HttpContext)
                    Dim itemID = GdHelpers.FormInt(ctx, "itemID")
                    Dim type_ = GdHelpers.FormInt(ctx, "type")
                    Dim like_ = GdHelpers.FormInt(ctx, "like", 1)
                    Dim delta = If(like_ = 1, 1, -1)
                    Select Case type_
                        Case 1 ' level
                            db.Levels.Write(Sub(r)
                                                Dim l = r.Find(Function(x) x.LevelID = itemID)
                                                If l IsNot Nothing Then l.Likes += delta
                                            End Sub)
                        Case 2 ' level comment
                            db.Comments.Write(Sub(r)
                                                  Dim c = r.Find(Function(x) x.CommentID = itemID)
                                                  If c IsNot Nothing Then c.Likes += delta
                                              End Sub)
                        Case 3 ' account comment
                            db.AccountComments.Write(Sub(r)
                                                         Dim c = r.Find(Function(x) x.CommentID = itemID)
                                                         If c IsNot Nothing Then c.Likes += delta
                                                     End Sub)
                    End Select
                    Return GdHelpers.Text("1")
                End Function)
            Next

            ' --- Mod: rate stars (rateGJStars) ----------------------------
            For Each path In {"/rateGJStars211.php", "/rateGJStars20.php"}
                a.MapPost(path, Function(ctx As HttpContext)
                    Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                    If accountID <= 0 Then Return GdHelpers.Text("-1")
                    Dim acc = db.FindAccount(accountID)
                    If acc Is Nothing OrElse acc.ModLevel < 1 Then Return GdHelpers.Text("-1")
                    Dim levelID = GdHelpers.FormInt(ctx, "levelID")
                    Dim stars = GdHelpers.FormInt(ctx, "stars")
                    db.Levels.Write(Sub(r)
                                        Dim l = r.Find(Function(x) x.LevelID = levelID)
                                        If l IsNot Nothing Then
                                            l.Stars = stars
                                            l.Difficulty = DifficultyFromStars(stars)
                                            l.RatedAt = GdHelpers.Now()
                                            l.RatedBy = accountID
                                        End If
                                    End Sub)
                    db.Log(accountID, "rateStars", levelID.ToString(), stars.ToString())
                    Return GdHelpers.Text("1")
                End Function)
            Next

            ' --- Mod: rate demon difficulty -------------------------------
            a.MapPost("/rateGJDemon21.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim acc = db.FindAccount(accountID)
                If acc Is Nothing OrElse acc.ModLevel < 1 Then Return GdHelpers.Text("-1")
                Dim levelID = GdHelpers.FormInt(ctx, "levelID")
                Dim rating = GdHelpers.FormInt(ctx, "rating")
                db.Levels.Write(Sub(r)
                                    Dim l = r.Find(Function(x) x.LevelID = levelID)
                                    If l IsNot Nothing Then l.Demon = 1 : l.DemonDiff = rating
                                End Sub)
                Return GdHelpers.Text(rating.ToString())
            End Function)
        End Sub

        Private Function DifficultyFromStars(stars As Integer) As Integer
            Select Case stars
                Case 2 : Return 1  ' easy
                Case 3 : Return 2  ' normal
                Case 4, 5 : Return 3 ' hard
                Case 6, 7 : Return 4 ' harder
                Case 8, 9 : Return 5 ' insane
                Case Is >= 10 : Return 5 ' demon handled separately
                Case Else : Return 0 ' auto/na
            End Select
        End Function

        Private Function ProfileStringForLeaderboard(u As GdUser, db As Database, rank As Integer) As String
            Dim acc = db.FindAccount(CInt(If(IsNumeric(u.ExtID), u.ExtID, "0")))
            Return ProfileString.Build(u, acc, db) & ":6:" & rank
        End Function

        Private Function BuildLevelScore(u As GdUser, s As LevelScore, rank As Integer) As String
            Dim sb As New StringBuilder()
            sb.Append("1:").Append(u.UserName)
            sb.Append(":2:").Append(u.UserID)
            sb.Append(":3:").Append(s.Percent)
            sb.Append(":6:").Append(rank)
            sb.Append(":9:").Append(u.Icon)
            sb.Append(":10:").Append(u.Color1)
            sb.Append(":11:").Append(u.Color2)
            sb.Append(":13:").Append(s.Coins)
            sb.Append(":14:").Append(u.IconType)
            sb.Append(":15:").Append(u.Special)
            sb.Append(":16:").Append(u.ExtID)
            sb.Append(":42:").Append(CommentEndpoints.AgoString(s.Timestamp))
            Return sb.ToString()
        End Function

    End Module

End Namespace
