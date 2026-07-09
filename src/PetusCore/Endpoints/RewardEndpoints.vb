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
                Dim rawChk = GdHelpers.Form(ctx, "chk")
                Dim udid = GdHelpers.Form(ctx, "udid")
                Dim accIdStr = GdHelpers.Form(ctx, "accountID")
                Dim rewardType = GdHelpers.FormInt(ctx, "rewardType") ' 0 = check, 1 = small, 2 = big
                ' Decode chk: XOR(base64_decode(substr(chk,5)), 59182) — echoed back verbatim.
                Dim chk = ""
                If rawChk.Length > 5 Then
                    Try
                        Dim decoded = Encoding.[Default].GetString(Convert.FromBase64String(rawChk.Substring(5)))
                        chk = PasswordService.XorCipher(decoded, RewardKey)
                    Catch
                        chk = ""
                    End Try
                End If
                Dim now = GdHelpers.Now() + 100

                ' Time remaining on each chest (0 = ready).
                Dim c1remain = Math.Max(0L, Chest1Cooldown - (now - user.Chest1Time))
                Dim c2remain = Math.Max(0L, Chest2Cooldown - (now - user.Chest2Time))

                Dim chest1stuff = RollChest(rng, 200, 4, 1)
                Dim chest2stuff = RollChest(rng, 600, 10, 3)

                If rewardType = 1 Then
                    If c1remain <> 0 Then Return GdHelpers.Text("-1")
                    db.Users.Write(Sub(r)
                                       Dim u = r.Find(Function(x) x.UserID = user.UserID)
                                       If u IsNot Nothing Then u.Chest1Time = now : u.Chest1Count += 1
                                   End Sub)
                    c1remain = Chest1Cooldown
                ElseIf rewardType = 2 Then
                    If c2remain <> 0 Then Return GdHelpers.Text("-1")
                    db.Users.Write(Sub(r)
                                       Dim u = r.Find(Function(x) x.UserID = user.UserID)
                                       If u IsNot Nothing Then u.Chest2Time = now : u.Chest2Count += 1
                                   End Sub)
                    c2remain = Chest2Cooldown
                End If

                ' Re-read counts after any mutation.
                Dim freshUser = db.FindUserById(user.UserID)
                Dim c1count = If(freshUser IsNot Nothing, freshUser.Chest1Count, user.Chest1Count)
                Dim c2count = If(freshUser IsNot Nothing, freshUser.Chest2Count, user.Chest2Count)

                ' Build the reward payload exactly as the client expects.
                Dim prep = String.Join(":", New String() {
                    "1",
                    user.UserID.ToString(),
                    chk,
                    udid,
                    accIdStr,
                    c1remain.ToString(),
                    chest1stuff,
                    c1count.ToString(),
                    c2remain.ToString(),
                    chest2stuff,
                    c2count.ToString(),
                    rewardType.ToString()
                })

                Dim body = HashService.Base64Url(PasswordService.XorCipher(prep, RewardKey))
                Dim hash = HashService.Sha1Hex(body & RewardSalt) ' genSolo4 salt
                Return GdHelpers.Text("SaKuJ" & body & "|" & hash)
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
