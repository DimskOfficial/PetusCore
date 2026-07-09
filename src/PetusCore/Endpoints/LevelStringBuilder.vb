Imports System.Text
Imports PetusCore.Data.Models

Namespace Endpoints

    ''' <summary>Builds the ":" separated level string used by search &amp; download.</summary>
    Public Module LevelStringBuilder

        ''' <summary>Compact form used in getGJLevels lists (no level data / password).</summary>
        Public Function BuildList(l As Level) As String
            Dim sb As New StringBuilder()
            AppendCommon(sb, l)
            Return sb.ToString().TrimEnd(":"c)
        End Function

        ''' <summary>Full form used in downloadGJLevel (adds data, dates, password).</summary>
        Public Function BuildDownload(l As Level) As String
            Dim sb As New StringBuilder()
            AppendCommon(sb, l)
            A(sb, 4, l.LevelString)
            A(sb, 27, EncodePassword(l.Password))
            A(sb, 28, l.UploadDate)
            A(sb, 29, l.UpdateDate)
            A(sb, 36, l.ExtraString)
            A(sb, 40, l.Ldm)
            A(sb, 52, l.SongIDs)
            A(sb, 53, l.SfxIDs)
            A(sb, 57, l.Ts)
            Return sb.ToString().TrimEnd(":"c)
        End Function

        Private Sub AppendCommon(sb As StringBuilder, l As Level)
            A(sb, 1, l.LevelID)
            A(sb, 2, l.LevelName)
            A(sb, 3, l.LevelDesc)
            A(sb, 5, l.Version)
            A(sb, 6, l.UserID)
            A(sb, 8, If(l.Stars > 0, 10, 0))          ' difficulty denominator
            A(sb, 9, l.Difficulty * 10)               ' difficulty numerator (face*10)
            A(sb, 10, l.Downloads)
            A(sb, 12, l.Audiotrack)
            A(sb, 13, l.GameVersion)
            A(sb, 14, l.Likes)
            A(sb, 15, l.Length)
            A(sb, 17, l.Demon)
            A(sb, 18, l.Stars)
            A(sb, 19, l.Featured)
            A(sb, 25, l.Auto)
            A(sb, 30, l.Original)
            A(sb, 31, l.TwoPlayer)
            A(sb, 35, l.SongID)
            A(sb, 37, l.Coins)
            A(sb, 38, If(l.RateCoins > 0, 1, 0))       ' verified coins
            A(sb, 39, l.RequestedStars)
            A(sb, 42, l.Epic)
            A(sb, 43, l.DemonDiff)
            A(sb, 45, l.Objects)
            A(sb, 47, 2)                               ' editor time flag
        End Sub

        ''' <summary>Passwords are sent as "1" + zero-padded number XOR is done client-side.</summary>
        Private Function EncodePassword(pass As String) As String
            If String.IsNullOrEmpty(pass) OrElse pass = "0" Then Return "0"
            Return "1" & pass
        End Function

        Private Sub A(sb As StringBuilder, key As Integer, value As Object)
            sb.Append(key).Append(":").Append(value).Append(":")
        End Sub

    End Module

End Namespace
