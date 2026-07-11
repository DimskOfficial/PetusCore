Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Endpoints
Imports PetusCore.Services

Namespace Api

    ''' <summary>
    ''' Petus ID bridge. The PetusGDPS website authenticates users through Petus
    ''' ID (OAuth2) and then talks to these server-to-server endpoints to bind a
    ''' Petus ID identity (`sub`) to an in-game GDPS account and to log that user
    ''' back in on later visits.
    '''
    ''' Because /login issues a session token WITHOUT a password, every route here
    ''' requires the service secret (header `X-Service-Secret` == PETUS_API_SECRET).
    ''' Only the website — never a browser — should ever call these.
    ''' </summary>
    Public Module PetusIdApi

        Public Sub Map(app As WebApplication)
            Dim db = app.Services.GetRequiredService(Of Database)()
            Dim pw = app.Services.GetRequiredService(Of PasswordService)()
            Dim tokens = app.Services.GetRequiredService(Of TokenService)()
            Dim cfg = app.Services.GetRequiredService(Of ServerConfig)()

            ' Log in by Petus ID subject — only if this identity is already linked.
            app.MapPost("/api/petusid/login", Function(ctx As HttpContext)
                If Not ServiceAuthOk(ctx, cfg) Then Return RestApi.[Error](ctx, 401, "service_unauthorized")
                Dim req = RestApi.ReadJson(ctx)
                Dim petusId = RestApi.Str(req, "petusId")
                If petusId = "" Then Return RestApi.[Error](ctx, 400, "petusid_required")

                Dim acc = db.FindAccountByPetusId(petusId)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 404, "not_linked")
                If acc.IsBanned Then Return RestApi.[Error](ctx, 403, "banned")

                MaybeBootstrapAdmin(acc, cfg, db)
                Dim tok = tokens.Issue(acc.AccountID)
                Return RestApi.Ok(New With {.token = tok.Token, .expiresAt = tok.ExpiresAt, .account = RestApi.PublicAccount(acc, db)})
            End Function)

            ' Resolve a Petus ID to a GDPS account, creating one on first sight.
            ' This is the only way accounts are made now — there is no in-game or
            ' on-site register/password. The account gets a random unusable
            ' password so nobody can log into the game by password; the launcher
            ' authenticates the client with the returned token instead.
            app.MapPost("/api/petusid/resolve", Function(ctx As HttpContext)
                If Not ServiceAuthOk(ctx, cfg) Then Return RestApi.[Error](ctx, 401, "service_unauthorized")
                Dim req = RestApi.ReadJson(ctx)
                Dim petusId = RestApi.Str(req, "petusId")
                If petusId = "" Then Return RestApi.[Error](ctx, 400, "petusid_required")

                Dim acc = db.FindAccountByPetusId(petusId)
                If acc Is Nothing Then
                    Dim preferred = RestApi.Str(req, "username")
                    Dim email = RestApi.Str(req, "email")
                    Dim userName = UniqueUserName(db, preferred, petusId)
                    Dim randomPass = Guid.NewGuid().ToString("N") & Guid.NewGuid().ToString("N")
                    acc = New Account With {
                        .AccountID = db.NextId("accountID"),
                        .UserName = userName,
                        .Password = pw.HashPassword(randomPass),
                        .Gjp2 = pw.HashGjp2(randomPass),
                        .Email = email,
                        .PetusId = petusId,
                        .IsActive = True,
                        .RegisterDate = GdHelpers.Now()
                    }
                    db.Accounts.Write(Sub(r) r.Add(acc))
                    db.ResolveUser(acc.AccountID.ToString(), userName)
                    db.Log(acc.AccountID, "provisionPetusId", petusId, userName)
                End If
                If acc.IsBanned Then Return RestApi.[Error](ctx, 403, "banned")

                MaybeBootstrapAdmin(acc, cfg, db)
                Dim tok = tokens.Issue(acc.AccountID)
                Return RestApi.Ok(New With {.token = tok.Token, .expiresAt = tok.ExpiresAt, .account = RestApi.PublicAccount(acc, db)})
            End Function)
            app.MapPost("/api/petusid/link", Function(ctx As HttpContext)
                If Not ServiceAuthOk(ctx, cfg) Then Return RestApi.[Error](ctx, 401, "service_unauthorized")
                Dim req = RestApi.ReadJson(ctx)
                Dim petusId = RestApi.Str(req, "petusId")
                Dim userName = RestApi.Str(req, "username")
                Dim password = RestApi.Str(req, "password")
                If petusId = "" OrElse userName = "" OrElse password = "" Then Return RestApi.[Error](ctx, 400, "missing_fields")

                Dim acc = db.FindAccountByName(userName)
                If acc Is Nothing OrElse Not pw.VerifyRawPassword(password, acc) Then Return RestApi.[Error](ctx, 401, "invalid_credentials")
                If acc.IsBanned Then Return RestApi.[Error](ctx, 403, "banned")

                ' This Petus ID must not already be bound to a different account.
                Dim other = db.FindAccountByPetusId(petusId)
                If other IsNot Nothing AndAlso other.AccountID <> acc.AccountID Then Return RestApi.[Error](ctx, 409, "petusid_taken")
                ' And this account must not already be bound to a different Petus ID.
                If acc.PetusId <> "" AndAlso acc.PetusId <> petusId Then Return RestApi.[Error](ctx, 409, "account_already_linked")

                acc.PetusId = petusId
                db.SaveAccount(acc)
                MaybeBootstrapAdmin(acc, cfg, db)
                db.Log(acc.AccountID, "linkPetusId", petusId, acc.UserName)

                Dim tok = tokens.Issue(acc.AccountID)
                Return RestApi.Ok(New With {.token = tok.Token, .expiresAt = tok.ExpiresAt, .account = RestApi.PublicAccount(acc, db)})
            End Function)

            ' Unlink (authenticated with a normal bearer token).
            app.MapPost("/api/petusid/unlink", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                acc.PetusId = ""
                db.SaveAccount(acc)
                Return RestApi.Ok(New With {.ok = True})
            End Function)
        End Sub

        ' Auto-promote the configured admin username, same as REST /auth/login.
        Private Sub MaybeBootstrapAdmin(acc As Data.Models.Account, cfg As ServerConfig, db As Database)
            If cfg.AdminUser <> "" AndAlso String.Equals(acc.UserName, cfg.AdminUser, StringComparison.OrdinalIgnoreCase) AndAlso acc.IsAdmin <= 0 Then
                acc.IsAdmin = 1
                acc.ModLevel = 2
                db.SaveAccount(acc)
            End If
        End Sub

        Private Function ServiceAuthOk(ctx As HttpContext, cfg As ServerConfig) As Boolean
            Dim provided = ctx.Request.Headers("X-Service-Secret").ToString()
            Return provided <> "" AndAlso provided = cfg.ApiSecret
        End Function

        ' Build a unique in-game username from the Petus ID nick (fallback to sub).
        Private Function UniqueUserName(db As Database, preferred As String, petusId As String) As String
            Dim base As String = New String((If(preferred, "")).Where(Function(c) Char.IsLetterOrDigit(c) OrElse c = "_"c).ToArray())
            If base.Length < 3 Then base = "petus"
            If base.Length > 16 Then base = base.Substring(0, 16)
            Dim candidate = base
            Dim n = 0
            While db.FindAccountByName(candidate) IsNot Nothing
                n += 1
                candidate = base & n.ToString()
                If n > 9999 Then
                    candidate = base & petusId.Substring(0, Math.Min(6, petusId.Length))
                    Exit While
                End If
            End While
            Return candidate
        End Function

    End Module

End Namespace
