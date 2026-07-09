Imports Microsoft.AspNetCore.Builder
Imports System.Text
Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>
    ''' Daily / weekly reward chests (getGJRewards.php). Returns the standard
    ''' XOR+base64 encoded reward string the GD client decodes, using the
    ''' rewards XOR key (59182) and the genSolo4 salt (pC26fpYaQCtg).
    ''' </summary>
    Public Module RewardEndpoints

        Private Const RewardKey As String = "59182"
        Private Const RewardSalt As String = "pC26fpYaQCtg"
        Private Const Chest1Cooldown As Long = 3600      ' small chest: 1h
        Private Const Chest2Cooldown As Long = 14400     ' big chest: 4h

        Public Sub Map(app As Object, db As Database, pw As PasswordService)
            Dim a = DirectCast(app, WebApplication)
            Dim rng As New Random()

            a.MapPost("/getGJRewards.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                Dim extID = If(accountID > 0, accountID.ToString(), GdHelpers.Clean(GdHelpers.Form(ctx, "udid")))
                If extID = "" Then Return GdHelpers.Text("-1")
                Dim user = db.ResolveUser(extID, GdHelpers.Clean(GdHelpers.Form(ctx, "userName")))
                Dim chk = GdHelpers.Form(ctx, "chk")
                Dim udid = GdHelpers.Form(ctx, "udid")
                Dim rewardType = GdHelpers.FormInt(ctx, "rewardType") ' 0 = just check, 1 = open small, 2 = open big
                Dim now = GdHelpers.Now()

                ' Time remaining on each chest (0 = ready).
                Dim c1remain = Math.Max(0L, (user.Chest1Time + Chest1Cooldown) - now)
                Dim c2remain = Math.Max(0L, (user.Chest2Time + Chest2Cooldown) - now)

                Dim chest1stuff = "0,0,0,0"
                Dim chest2stuff = "0,0,0,0"

                If rewardType = 1 AndAlso c1remain <= 0 Then
                    chest1stuff = RollChest(rng, 200, 4, 1)
                    db.Users.Write(Sub(r)
                                       Dim u = r.Find(Function(x) x.UserID = user.UserID)
                                       If u IsNot Nothing Then u.Chest1Time = now : u.Chest1Count += 1
                                   End Sub)
                    user.Chest1Count += 1
                    c1remain = Chest1Cooldown
                ElseIf rewardType = 2 AndAlso c2remain <= 0 Then
                    chest2stuff = RollChest(rng, 600, 10, 3)
                    db.Users.Write(Sub(r)
                                       Dim u = r.Find(Function(x) x.UserID = user.UserID)
                                       If u IsNot Nothing Then u.Chest2Time = now : u.Chest2Count += 1
                                   End Sub)
                    user.Chest2Count += 1
                    c2remain = Chest2Cooldown
                End If

                ' Build the reward payload the client expects.
                Dim prep = String.Join(":", New String() {
                    user.UserID.ToString(),
                    chk,
                    udid,
                    extID,
                    c1remain.ToString(),
                    chest1stuff,
                    user.Chest1Count.ToString(),
                    c2remain.ToString(),
                    chest2stuff,
                    user.Chest2Count.ToString(),
                    rewardType.ToString()
                })

                Dim body = HashService.Base64Url(PasswordService.XorCipher(prep, RewardKey))
                Dim secret = RandomString(rng, 5)
                Dim hash = HashService.Sha1Hex(body & RewardSalt)
                Return GdHelpers.Text(secret & body & hash)
            End Function)
        End Sub

        ''' <summary>orbs,diamonds,shardID,keys — a randomized chest payout.</summary>
        Private Function RollChest(rng As Random, maxOrbs As Integer, maxDiamonds As Integer, maxKeys As Integer) As String
            Dim orbs = rng.Next(maxOrbs \ 2, maxOrbs + 1)
            Dim diamonds = rng.Next(1, maxDiamonds + 1)
            Dim shard = rng.Next(1, 7)          ' fire/ice/poison/shadow/lava/earth
            Dim keys = rng.Next(0, maxKeys + 1)
            Return $"{orbs},{diamonds},{shard},{keys}"
        End Function

        Private Function RandomString(rng As Random, len As Integer) As String
            Const chars As String = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
            Dim sb As New StringBuilder(len)
            For i = 1 To len
                sb.Append(chars(rng.Next(chars.Length)))
            Next
            Return sb.ToString()
        End Function

    End Module

End Namespace
