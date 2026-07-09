Imports Microsoft.AspNetCore.Builder
Imports System.Text
Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>
    ''' Map packs, gauntlets and quests/challenges — the curated content
    ''' systems. Data lives in mappacks/gauntlets/quests JSON tables and is
    ''' managed from the website admin panel.
    ''' </summary>
    Public Module ContentEndpoints

        Public Sub Map(app As Object, db As Database, pw As PasswordService, hash As HashService)
            Dim a = DirectCast(app, WebApplication)

            ' ---- Map packs ----------------------------------------------
            For Each path In {"/getGJMapPacks21.php", "/getGJMapPacks20.php", "/getGJMapPacks.php"}
                a.MapPost(path, Function(ctx As HttpContext)
                    Dim page = GdHelpers.FormInt(ctx, "page")
                    Dim perPage = 10
                    Dim all = db.MapPacks.All().OrderBy(Function(m) m.ID).ToList()
                    Dim total = all.Count
                    Dim items = all.Skip(page * perPage).Take(perPage).ToList()
                    If items.Count = 0 Then Return GdHelpers.Text("#" & total & ":" & (page * perPage) & ":" & perPage & "#")

                    Dim sb As New StringBuilder()
                    Dim hashStr As New StringBuilder()
                    For Each m In items
                        If sb.Length > 0 Then sb.Append("|")
                        sb.Append(BuildMapPack(m))
                        Dim idStr = m.ID.ToString()
                        hashStr.Append(idStr(0)).Append(idStr(idStr.Length - 1)).Append(m.Stars).Append(m.Coins)
                    Next
                    Dim result As New StringBuilder()
                    result.Append(sb.ToString())
                    result.Append("#").Append(total).Append(":").Append(page * perPage).Append(":").Append(perPage)
                    result.Append("#").Append(HashService.Sha1Hex(hashStr.ToString() & HashService.SaltLevel))
                    Return GdHelpers.Text(result.ToString())
                End Function)
            Next

            ' ---- Gauntlets ----------------------------------------------
            For Each path In {"/getGJGauntlets21.php", "/getGJGauntlets.php"}
                a.MapPost(path, Function(ctx As HttpContext)
                    Dim all = db.Gauntlets.All().Where(Function(g) g.Levels <> "").OrderBy(Function(g) g.ID).ToList()
                    If all.Count = 0 Then Return GdHelpers.Text("#")
                    Dim sb As New StringBuilder()
                    Dim hashStr As New StringBuilder()
                    For Each g In all
                        If sb.Length > 0 Then sb.Append("|")
                        sb.Append("1:").Append(g.ID).Append(":3:").Append(g.Levels)
                        hashStr.Append(g.ID).Append(g.Levels)
                    Next
                    Dim result As New StringBuilder()
                    result.Append(sb.ToString())
                    result.Append("#").Append(HashService.Sha1Hex(hashStr.ToString() & HashService.SaltLevel))
                    result.Append("#") ' page-info separator
                    Return GdHelpers.Text(result.ToString())
                End Function)
            Next

            ' ---- Quests / challenges ------------------------------------
            a.MapPost("/getGJChallenges.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                Dim extID = If(accountID > 0, accountID.ToString(), GdHelpers.Clean(GdHelpers.Form(ctx, "udid")))
                If extID = "" Then Return GdHelpers.Text("-1")
                Dim user = db.ResolveUser(extID, "")
                Dim chk = GdHelpers.Form(ctx, "chk")

                Dim quests = db.Quests.All().OrderBy(Function(q) q.ID).Take(3).ToList()
                ' Seconds until the quests reset (daily).
                Dim timeLeft = 86400 - (GdHelpers.Now() Mod 86400)

                Dim inner As New StringBuilder()
                inner.Append(user.UserID).Append(":")
                Dim decodedChk = ""
                If chk.Length > 5 Then
                    Try
                        Dim d = Encoding.[Default].GetString(Convert.FromBase64String(chk.Substring(5)))
                        decodedChk = PasswordService.XorCipher(d, "19847")
                    Catch
                    End Try
                End If
                inner.Append(decodedChk)
                Dim qid = 1
                For Each q In quests
                    inner.Append(":").Append(q.ID).Append(",").Append(q.Type).Append(",").Append(q.Amount).Append(",").Append(q.Reward).Append(",").Append(timeLeft).Append(",").Append(q.Name)
                    qid += 1
                Next

                Dim body = HashService.Base64Url(PasswordService.XorCipher(inner.ToString(), "19847"))
                Dim h = HashService.Sha1Hex(body & "oC36fpYaPtdg") ' genSolo3 salt
                Return GdHelpers.Text("SaKuJ" & body & "|" & h)
            End Function)
        End Sub

        Private Function BuildMapPack(m As MapPack) As String
            Dim sb As New StringBuilder()
            sb.Append("1:").Append(m.ID)
            sb.Append(":2:").Append(m.Name)
            sb.Append(":3:").Append(m.Levels)
            sb.Append(":4:").Append(m.Stars)
            sb.Append(":5:").Append(m.Coins)
            sb.Append(":6:").Append(m.Difficulty)
            sb.Append(":7:").Append(m.Color)
            sb.Append(":8:").Append(m.Color2)
            Return sb.ToString()
        End Function

    End Module

End Namespace
