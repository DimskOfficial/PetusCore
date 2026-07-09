Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection
Imports PetusCore.Data
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>
    ''' Registers every Geometry Dash game endpoint. These are the Boomlings /
    ''' "*.php" routes the GD client POSTs to. Point your client's server URL at
    ''' this app and it will talk to PetusCore.
    ''' </summary>
    Public Module GdEndpoints

        Public Sub Map(app As WebApplication)
            Dim db = app.Services.GetRequiredService(Of Database)()
            Dim pw = app.Services.GetRequiredService(Of PasswordService)()
            Dim hash = app.Services.GetRequiredService(Of HashService)()

            AccountEndpoints.Map(app, db, pw, app.Services.GetRequiredService(Of ServerConfig)())
            LevelEndpoints.Map(app, db, pw, hash)
            CommentEndpoints.Map(app, db, pw, hash)
            ScoreEndpoints.Map(app, db, pw, hash)
            SongEndpoints.Map(app, db)
            MiscEndpoints.Map(app, db, pw)
        End Sub

    End Module

    ''' <summary>Smaller/stub endpoints so the client never hangs on a missing route.</summary>
    Public Module MiscEndpoints

        Public Sub Map(app As Object, db As Database, pw As PasswordService)
            Dim a = DirectCast(app, WebApplication)

            ' Content URLs the client asks for during boot.
            a.MapPost("/getAccountURL.php", Function(ctx As HttpContext) GdHelpers.Text(BaseUrl(ctx)))
            a.MapPost("/getCustomContentURL.php", Function(ctx As HttpContext) GdHelpers.Text(BaseUrl(ctx)))

            ' Song extras.
            a.MapPost("/getGJTopArtists.php", Function(ctx As HttpContext)
                Dim artists = db.Songs.All().GroupBy(Function(s) s.ArtistName).Take(50)
                Dim sb As New Text.StringBuilder()
                Dim i = 1
                For Each g In artists
                    Dim first = g.First()
                    sb.Append($"4:{first.ArtistName}:7:{first.ArtistID}|")
                    i += 1
                Next
                sb.Append("#" & artists.Count() & ":0:50")
                Return GdHelpers.Text(sb.ToString())
            End Function)

            ' Map packs / gauntlets / challenges — empty but valid responses.
            For Each path In {"/getGJMapPacks21.php", "/getGJMapPacks20.php", "/getGJMapPacks.php"}
                a.MapPost(path, Function(ctx As HttpContext) GdHelpers.Text("#0:0:10#"))
            Next
            For Each path In {"/getGJGauntlets21.php", "/getGJGauntlets.php"}
                a.MapPost(path, Function(ctx As HttpContext) GdHelpers.Text("#"))
            Next
            a.MapPost("/getGJChallenges.php", Function(ctx As HttpContext) GdHelpers.Text("-1"))
            a.MapPost("/getGJRewards.php", Function(ctx As HttpContext) GdHelpers.Text("-1"))

            ' Report a level (accepted, logged).
            a.MapPost("/reportGJLevel.php", Function(ctx As HttpContext)
                db.Log(0, "report", GdHelpers.CleanNumber(GdHelpers.Form(ctx, "levelID")), GdHelpers.ClientIp(ctx))
                Return GdHelpers.Text("1")
            End Function)

            ' Delete own level.
            a.MapPost("/deleteGJLevelUser20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim user = db.FindUserByExt(accountID.ToString())
                Dim levelID = GdHelpers.FormInt(ctx, "levelID")
                db.Levels.Write(Sub(r) r.RemoveAll(Function(x) x.LevelID = levelID AndAlso (user IsNot Nothing AndAlso x.UserID = user.UserID)))
                Return GdHelpers.Text("1")
            End Function)

            ' Suggest stars (mod) — recorded as a suggestion log.
            a.MapPost("/suggestGJStars20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim acc = db.FindAccount(accountID)
                If acc Is Nothing OrElse acc.ModLevel < 1 Then Return GdHelpers.Text("-1")
                db.Log(accountID, "suggest", GdHelpers.CleanNumber(GdHelpers.Form(ctx, "levelID")), GdHelpers.Form(ctx, "stars"))
                Return GdHelpers.Text("1")
            End Function)

            ' Creators list.
            For Each path In {"/getGJCreators.php", "/getGJCreators19.php"}
                a.MapPost(path, Function(ctx As HttpContext)
                    Dim sb As New Text.StringBuilder()
                    For Each u In db.Users.All().Where(Function(x) x.CreatorPoints > 0).OrderByDescending(Function(x) x.CreatorPoints).Take(50)
                        sb.Append($"{u.UserName}:{u.UserID}:{u.ExtID}|")
                    Next
                    Return GdHelpers.Text(sb.ToString())
                End Function)
            Next
        End Sub

        Private Function BaseUrl(ctx As HttpContext) As String
            Return $"{ctx.Request.Scheme}://{ctx.Request.Host}"
        End Function

    End Module

End Namespace
