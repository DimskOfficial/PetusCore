Imports System.Text
Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>Level endpoints: upload, download, search, daily.</summary>
    Public Module LevelEndpoints

        Public Sub Map(app As Object, db As Database, pw As PasswordService, hash As HashService)
            Dim a = DirectCast(app, Microsoft.AspNetCore.Builder.WebApplication)

            ' --- Upload ---------------------------------------------------
            Dim uploadHandler As Func(Of HttpContext, IResult) =
                Function(ctx As HttpContext)
                    Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                    Dim extID = If(accountID > 0, accountID.ToString(), GdHelpers.Clean(GdHelpers.Form(ctx, "udid")))
                    If extID = "" Then Return GdHelpers.Text("-1")
                    Dim userName = GdHelpers.Clean(GdHelpers.Form(ctx, "userName"))
                    Dim user = db.ResolveUser(extID, userName)

                    Dim levelID = GdHelpers.FormInt(ctx, "levelID")
                    Dim existing As Level = Nothing
                    If levelID > 0 Then existing = db.Levels.Read(Function(r) r.Find(Function(x) x.LevelID = levelID))

                    Dim l = If(existing, New Level With {.LevelID = db.NextId("levelID"), .UploadDate = GdHelpers.Now()})
                    ' Only the owner may update an existing level.
                    If existing IsNot Nothing AndAlso existing.UserID <> user.UserID Then Return GdHelpers.Text("-1")

                    l.UserID = user.UserID
                    l.ExtID = extID
                    l.UserName = user.UserName
                    l.LevelName = GdHelpers.Clean(GdHelpers.Form(ctx, "levelName"))
                    l.LevelDesc = GdHelpers.Form(ctx, "levelDesc")
                    l.LevelString = GdHelpers.Form(ctx, "levelString")
                    l.Version = GdHelpers.FormInt(ctx, "levelVersion", l.Version)
                    l.GameVersion = GdHelpers.FormInt(ctx, "gameVersion", l.GameVersion)
                    l.Length = GdHelpers.FormInt(ctx, "levelLength", l.Length)
                    l.Audiotrack = GdHelpers.FormInt(ctx, "audioTrack", l.Audiotrack)
                    l.SongID = GdHelpers.FormInt(ctx, "songID", l.SongID)
                    l.Objects = GdHelpers.FormInt(ctx, "objects", l.Objects)
                    l.Coins = GdHelpers.FormInt(ctx, "coins", l.Coins)
                    l.RequestedStars = GdHelpers.FormInt(ctx, "requestedStars", l.RequestedStars)
                    l.TwoPlayer = GdHelpers.FormInt(ctx, "twoPlayer", l.TwoPlayer)
                    l.Password = GdHelpers.CleanNumber(GdHelpers.Form(ctx, "password", l.Password))
                    l.Unlisted = GdHelpers.FormInt(ctx, "unlisted", l.Unlisted)
                    l.SongIDs = GdHelpers.Form(ctx, "songIDs", l.SongIDs)
                    l.SfxIDs = GdHelpers.Form(ctx, "sfxIDs", l.SfxIDs)
                    l.Ts = GdHelpers.FormInt(ctx, "ts", l.Ts)
                    l.Ldm = GdHelpers.FormInt(ctx, "ldm", l.Ldm)
                    l.ExtraString = GdHelpers.Form(ctx, "extraString", l.ExtraString)
                    l.Original = GdHelpers.FormInt(ctx, "original", l.Original)
                    l.UpdateDate = GdHelpers.Now()

                    If existing Is Nothing Then
                        db.Levels.Write(Sub(r) r.Add(l))
                    Else
                        db.Levels.Write(Sub(r)
                                            Dim i = r.FindIndex(Function(x) x.LevelID = l.LevelID)
                                            If i >= 0 Then r(i) = l
                                        End Sub)
                    End If
                    Return GdHelpers.Text(l.LevelID.ToString())
                End Function
            For Each path In {"/uploadGJLevel21.php", "/uploadGJLevel20.php", "/uploadGJLevel19.php", "/uploadGJLevel.php"}
                a.MapPost(path, uploadHandler)
            Next

            ' --- Download -------------------------------------------------
            Dim downloadHandler As Func(Of HttpContext, IResult) =
                Function(ctx As HttpContext)
                    Dim levelID = GdHelpers.FormInt(ctx, "levelID")
                    Dim l As Level
                    If levelID = -1 Then
                        ' Daily level.
                        l = db.Levels.Read(Function(r) r.FindAll(Function(x) x.IsDaily = 1).OrderByDescending(Function(x) x.RatedAt).FirstOrDefault())
                    ElseIf levelID = -2 Then
                        l = db.Levels.Read(Function(r) r.FindAll(Function(x) x.IsDaily = 2).OrderByDescending(Function(x) x.RatedAt).FirstOrDefault())
                    Else
                        l = db.Levels.Read(Function(r) r.Find(Function(x) x.LevelID = levelID))
                    End If
                    If l Is Nothing Then Return GdHelpers.Text("-1")

                    ' Count the download.
                    db.Levels.Write(Sub(r)
                                        Dim i = r.FindIndex(Function(x) x.LevelID = l.LevelID)
                                        If i >= 0 Then r(i).Downloads += 1
                                    End Sub)

                    Dim body As New StringBuilder()
                    body.Append(LevelStringBuilder.BuildDownload(l))
                    body.Append("#").Append(hash.GenSolo(l.LevelString))
                    body.Append("#").Append(hash.GenWithSalt(
                        $"{l.UserID},{l.Stars},{If(l.Demon > 0, 1, 0)},{l.LevelID},{If(l.RateCoins > 0, 1, 0)},{l.Featured},{l.Password},0",
                        HashService.SaltLevel))
                    Return GdHelpers.Text(body.ToString())
                End Function
            For Each path In {"/downloadGJLevel22.php", "/downloadGJLevel21.php", "/downloadGJLevel20.php", "/downloadGJLevel19.php", "/downloadGJLevel.php"}
                a.MapPost(path, downloadHandler)
            Next

            ' --- Search ---------------------------------------------------
            Dim searchHandler As Func(Of HttpContext, IResult) =
                Function(ctx As HttpContext)
                    Dim type_ = GdHelpers.FormInt(ctx, "type")
                    Dim str = GdHelpers.Form(ctx, "str")
                    Dim page = GdHelpers.FormInt(ctx, "page")
                    Dim perPage = 10

                    Dim all = db.Levels.All().Where(Function(x) x.Unlisted = 0).ToList()
                    Dim results As List(Of Level)

                    Select Case type_
                        Case 0 ' search by name or ID
                            Dim byId As Integer
                            If Integer.TryParse(str, byId) Then
                                results = all.Where(Function(x) x.LevelID = byId).ToList()
                            Else
                                results = all.Where(Function(x) x.LevelName.IndexOf(str, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
                            End If
                        Case 1 ' most downloaded
                            results = all.OrderByDescending(Function(x) x.Downloads).ToList()
                        Case 2 ' most liked
                            results = all.OrderByDescending(Function(x) x.Likes).ToList()
                        Case 3 ' trending
                            results = all.OrderByDescending(Function(x) x.Likes + x.Downloads).ToList()
                        Case 4 ' recent
                            results = all.OrderByDescending(Function(x) x.UploadDate).ToList()
                        Case 5 ' by user
                            Dim uid = GdHelpers.FormInt(ctx, "str")
                            results = all.Where(Function(x) x.UserID = uid).OrderByDescending(Function(x) x.UploadDate).ToList()
                        Case 6, 17 ' featured
                            results = all.Where(Function(x) x.Featured > 0).OrderByDescending(Function(x) x.Featured).ToList()
                        Case 11 ' awarded / rated
                            results = all.Where(Function(x) x.Stars > 0).OrderByDescending(Function(x) x.RatedAt).ToList()
                        Case 16 ' epic / hall of fame
                            results = all.Where(Function(x) x.Epic > 0).OrderByDescending(Function(x) x.RatedAt).ToList()
                        Case Else
                            results = all.OrderByDescending(Function(x) x.UploadDate).ToList()
                    End Select

                    Dim total = results.Count
                    Dim pageItems = results.Skip(page * perPage).Take(perPage).ToList()

                    Dim levelsStr As New StringBuilder()
                    Dim usersStr As New StringBuilder()
                    Dim songsStr As New StringBuilder()
                    Dim seenSongs As New HashSet(Of Integer)()
                    For Each l In pageItems
                        If levelsStr.Length > 0 Then levelsStr.Append("|")
                        levelsStr.Append(LevelStringBuilder.BuildList(l))
                        usersStr.Append($"{l.UserID}:{l.UserName}:{l.ExtID}|")
                        If l.SongID > 0 AndAlso Not seenSongs.Contains(l.SongID) Then
                            seenSongs.Add(l.SongID)
                            Dim song = db.Songs.Read(Function(r) r.Find(Function(s) s.ID = l.SongID))
                            If song IsNot Nothing Then songsStr.Append(SongEndpoints.BuildSongString(song)).Append("~:~")
                        End If
                    Next

                    Dim body As New StringBuilder()
                    body.Append(levelsStr.ToString())
                    body.Append("#").Append(usersStr.ToString().TrimEnd("|"c))
                    body.Append("#").Append(songsStr.ToString())
                    body.Append("#").Append($"{total}:{page * perPage}:{perPage}")
                    body.Append("#").Append(hash.GenMulti(pageItems))
                    Return GdHelpers.Text(body.ToString())
                End Function
            For Each path In {"/getGJLevels21.php", "/getGJLevels20.php", "/getGJLevels19.php", "/getGJLevels.php"}
                a.MapPost(path, searchHandler)
            Next

            ' --- Daily / weekly level id ----------------------------------
            a.MapPost("/getGJDailyLevel.php", Function(ctx As HttpContext)
                Dim weekly = GdHelpers.FormInt(ctx, "weekly")
                Dim kind = If(weekly = 1, 2, 1)
                Dim l = db.Levels.Read(Function(r) r.FindAll(Function(x) x.IsDaily = kind).OrderByDescending(Function(x) x.RatedAt).FirstOrDefault())
                If l Is Nothing Then Return GdHelpers.Text("-1")
                Dim secondsLeft = 86400 - (GdHelpers.Now() Mod 86400)
                Return GdHelpers.Text($"{l.DailyID}|{secondsLeft}")
            End Function)
        End Sub

    End Module

End Namespace
