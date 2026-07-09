Imports System.Text
Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>Account + profile endpoints: register, login, user info, score, settings.</summary>
    Public Module AccountEndpoints

        Public Sub Map(app As Object, db As Database, pw As PasswordService, cfg As ServerConfig)
            Dim a = DirectCast(app, Microsoft.AspNetCore.Builder.WebApplication)

            ' --- Register -------------------------------------------------
            a.MapPost("/accounts/registerGJAccount.php", Function(ctx As HttpContext)
                Dim userName = GdHelpers.Clean(GdHelpers.Form(ctx, "userName"))
                Dim password = GdHelpers.Form(ctx, "password")
                Dim email = GdHelpers.Clean(GdHelpers.Form(ctx, "email"))

                If userName.Length < 3 OrElse userName.Length > 20 Then Return GdHelpers.Text("-4")   ' name too short/long
                If password.Length < 6 Then Return GdHelpers.Text("-8")                                ' password too short
                If db.FindAccountByName(userName) IsNot Nothing Then Return GdHelpers.Text("-2")       ' taken

                Dim acc As New Account With {
                    .AccountID = db.NextId("accountID"),
                    .UserName = userName,
                    .Password = pw.HashPassword(password),
                    .Gjp2 = pw.HashGjp2(password),
                    .Email = email,
                    .IsActive = cfg.PreactivateAccounts,
                    .RegisterDate = GdHelpers.Now()
                }
                db.Accounts.Write(Sub(r) r.Add(acc))
                db.ResolveUser(acc.AccountID.ToString(), userName)
                Return GdHelpers.Text("1")
            End Function)

            ' --- Login ----------------------------------------------------
            a.MapPost("/accounts/loginGJAccount.php", Function(ctx As HttpContext)
                Dim userName = GdHelpers.Clean(GdHelpers.Form(ctx, "userName"))
                Dim acc = db.FindAccountByName(userName)
                If acc Is Nothing Then Return GdHelpers.Text("-1")
                If acc.IsBanned Then Return GdHelpers.Text("-12") ' banned
                If Not acc.IsActive Then Return GdHelpers.Text("-1")

                Dim ok = False
                Dim gjp2 = GdHelpers.Form(ctx, "gjp2")
                Dim password = GdHelpers.Form(ctx, "password")
                If gjp2 <> "" Then ok = pw.VerifyGjp2(gjp2, acc) Else ok = pw.VerifyRawPassword(password, acc)
                If Not ok Then Return GdHelpers.Text("-11") ' wrong credentials

                ' Ensure a linked in-game user exists.
                Dim user = db.ResolveUser(acc.AccountID.ToString(), acc.UserName)
                Return GdHelpers.Text($"{acc.AccountID},{user.UserID}")
            End Function)

            ' --- Update user score / stats (updateGJUserScore22) ----------
            Dim scoreHandler As Func(Of HttpContext, IResult) =
                Function(ctx As HttpContext)
                    Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                    Dim extID = If(accountID > 0, accountID.ToString(), GdHelpers.Clean(GdHelpers.Form(ctx, "udid")))
                    If extID = "" Then Return GdHelpers.Text("-1")

                    Dim userName = GdHelpers.Clean(GdHelpers.Form(ctx, "userName"))
                    Dim user = db.ResolveUser(extID, userName)

                    user.UserName = If(userName <> "", userName, user.UserName)
                    user.Stars = GdHelpers.FormInt(ctx, "stars", user.Stars)
                    user.Demons = GdHelpers.FormInt(ctx, "demons", user.Demons)
                    user.Diamonds = GdHelpers.FormInt(ctx, "diamonds", user.Diamonds)
                    user.Moons = GdHelpers.FormInt(ctx, "moons", user.Moons)
                    user.Coins = GdHelpers.FormInt(ctx, "coins", user.Coins)
                    user.UserCoins = GdHelpers.FormInt(ctx, "userCoins", user.UserCoins)
                    user.Orbs = GdHelpers.FormInt(ctx, "orbs", user.Orbs)
                    user.CompletedLvls = GdHelpers.FormInt(ctx, "completedLevels", user.CompletedLvls)
                    user.Icon = GdHelpers.FormInt(ctx, "accIcon", user.Icon)
                    user.Color1 = GdHelpers.FormInt(ctx, "color1", user.Color1)
                    user.Color2 = GdHelpers.FormInt(ctx, "color2", user.Color2)
                    user.Color3 = GdHelpers.FormInt(ctx, "color3", user.Color3)
                    user.IconType = GdHelpers.FormInt(ctx, "iconType", user.IconType)
                    user.AccIcon = GdHelpers.FormInt(ctx, "accIcon", user.AccIcon)
                    user.AccShip = GdHelpers.FormInt(ctx, "accShip", user.AccShip)
                    user.AccBall = GdHelpers.FormInt(ctx, "accBall", user.AccBall)
                    user.AccBird = GdHelpers.FormInt(ctx, "accBird", user.AccBird)
                    user.AccDart = GdHelpers.FormInt(ctx, "accDart", user.AccDart)
                    user.AccRobot = GdHelpers.FormInt(ctx, "accRobot", user.AccRobot)
                    user.AccGlow = GdHelpers.FormInt(ctx, "accGlow", user.AccGlow)
                    user.AccSpider = GdHelpers.FormInt(ctx, "accSpider", user.AccSpider)
                    user.AccSwing = GdHelpers.FormInt(ctx, "accSwing", user.AccSwing)
                    user.AccJetpack = GdHelpers.FormInt(ctx, "accJetpack", user.AccJetpack)
                    user.AccExplosion = GdHelpers.FormInt(ctx, "accExplosion", user.AccExplosion)
                    user.Special = GdHelpers.FormInt(ctx, "special", user.Special)
                    user.LastPlayed = GdHelpers.Now()
                    user.IP = GdHelpers.ClientIp(ctx)
                    db.SaveUser(user)
                    Return GdHelpers.Text(user.UserID.ToString())
                End Function
            For Each path In {"/updateGJUserScore22.php", "/updateGJUserScore21.php", "/updateGJUserScore20.php", "/updateGJUserScore.php"}
                a.MapPost(path, scoreHandler)
            Next

            ' --- Get user info (getGJUserInfo20) --------------------------
            a.MapPost("/getGJUserInfo20.php", Function(ctx As HttpContext)
                Dim targetAcc = GdHelpers.FormInt(ctx, "targetAccountID")
                Dim user = db.FindUserByExt(targetAcc.ToString())
                If user Is Nothing Then Return GdHelpers.Text("-1")
                Dim acc = db.FindAccount(targetAcc)
                Return GdHelpers.Text(ProfileString.Build(user, acc, db))
            End Function)

            ' --- Update account settings (privacy, socials) ---------------
            a.MapPost("/updateGJAccSettings20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim acc = db.FindAccount(accountID)
                acc.MS = GdHelpers.FormInt(ctx, "mS", acc.MS)
                acc.FrS = GdHelpers.FormInt(ctx, "frS", acc.FrS)
                acc.CS = GdHelpers.FormInt(ctx, "cS", acc.CS)
                acc.Youtube = GdHelpers.Clean(GdHelpers.Form(ctx, "yt", acc.Youtube))
                acc.Twitter = GdHelpers.Clean(GdHelpers.Form(ctx, "twitter", acc.Twitter))
                acc.Twitch = GdHelpers.Clean(GdHelpers.Form(ctx, "twitch", acc.Twitch))
                db.SaveAccount(acc)
                Return GdHelpers.Text("1")
            End Function)

            ' --- Update profile description (twitter/yt via updateGJDesc) --
            a.MapPost("/updateGJDesc20.php", Function(ctx As HttpContext)
                Return GdHelpers.Text("1")
            End Function)
        End Sub

    End Module

End Namespace
