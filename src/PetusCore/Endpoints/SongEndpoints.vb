Imports System.Text
Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>Song info endpoints (Newgrounds / custom songs stored in JSON).</summary>
    Public Module SongEndpoints

        Public Sub Map(app As Object, db As Database)
            Dim a = DirectCast(app, Microsoft.AspNetCore.Builder.WebApplication)

            a.MapPost("/getGJSongInfo.php", Function(ctx As HttpContext)
                Dim songID = GdHelpers.FormInt(ctx, "songID")
                Dim song = db.Songs.Read(Function(r) r.Find(Function(s) s.ID = songID))
                If song Is Nothing Then Return GdHelpers.Text("-1")
                Return GdHelpers.Text(BuildSongString(song))
            End Function)
        End Sub

        ''' <summary>Build the "~|~" delimited song string GD expects.</summary>
        Public Function BuildSongString(s As Song) As String
            Dim url = Uri.EscapeDataString(If(s.Download, ""))
            Dim sb As New StringBuilder()
            sb.Append("1~|~").Append(s.ID)
            sb.Append("~|~2~|~").Append(s.Name)
            sb.Append("~|~3~|~").Append(s.ArtistID)
            sb.Append("~|~4~|~").Append(s.ArtistName)
            sb.Append("~|~5~|~").Append(s.Size)
            sb.Append("~|~6~|~")
            sb.Append("~|~8~|~1")
            sb.Append("~|~10~|~").Append(url)
            Return sb.ToString()
        End Function

    End Module

End Namespace
