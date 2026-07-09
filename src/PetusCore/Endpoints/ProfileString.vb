Imports System.Text
Imports PetusCore.Data
Imports PetusCore.Data.Models

Namespace Endpoints

    ''' <summary>Builds the ":" separated user profile string for getGJUserInfo20.</summary>
    Public Module ProfileString

        Public Function Build(user As GdUser, acc As Account, db As Database) As String
            Dim rank = ComputeRank(user, db)
            Dim sb As New StringBuilder()
            Append(sb, 1, user.UserName)
            Append(sb, 2, If(acc IsNot Nothing, acc.AccountID, user.UserID))
            Append(sb, 3, user.Stars)
            Append(sb, 4, user.Demons)
            Append(sb, 6, rank)
            Append(sb, 7, user.UserID)
            Append(sb, 8, CInt(user.CreatorPoints))
            Append(sb, 9, user.Icon)
            Append(sb, 10, user.Color1)
            Append(sb, 11, user.Color2)
            Append(sb, 13, user.Coins)
            Append(sb, 14, user.IconType)
            Append(sb, 15, user.Special)
            Append(sb, 16, If(acc IsNot Nothing, acc.AccountID, user.UserID))
            Append(sb, 17, user.UserCoins)
            Append(sb, 18, If(acc IsNot Nothing, acc.MS, 0))
            Append(sb, 19, If(acc IsNot Nothing, acc.FrS, 0))
            Append(sb, 20, If(acc IsNot Nothing, acc.Youtube, ""))
            Append(sb, 21, user.AccIcon)
            Append(sb, 22, user.AccShip)
            Append(sb, 23, user.AccBall)
            Append(sb, 24, user.AccBird)
            Append(sb, 25, user.AccDart)
            Append(sb, 26, user.AccRobot)
            Append(sb, 28, user.AccGlow)
            Append(sb, 29, 1) ' isRegistered
            Append(sb, 30, rank)
            Append(sb, 43, user.AccSpider)
            Append(sb, 44, If(acc IsNot Nothing, acc.Twitter, ""))
            Append(sb, 45, If(acc IsNot Nothing, acc.Twitch, ""))
            Append(sb, 46, user.Diamonds)
            Append(sb, 48, user.AccExplosion)
            Append(sb, 49, If(acc IsNot Nothing, acc.ModLevel, 0))
            Append(sb, 50, user.Moons)
            Append(sb, 51, user.Color3)
            Append(sb, 52, user.Moons)
            Append(sb, 53, user.AccSwing)
            Append(sb, 54, user.AccJetpack)
            Return sb.ToString().TrimEnd(":"c)
        End Function

        Private Function ComputeRank(user As GdUser, db As Database) As Integer
            If user.Stars <= 0 Then Return 0
            Return db.Users.Read(Function(r) r.Where(Function(u) u.Stars > user.Stars).Count() + 1)
        End Function

        Private Sub Append(sb As StringBuilder, key As Integer, value As Object)
            sb.Append(key).Append(":").Append(value).Append(":")
        End Sub

    End Module

End Namespace
