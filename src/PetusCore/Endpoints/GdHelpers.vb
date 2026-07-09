Imports System.Text
Imports Microsoft.AspNetCore.Http

Namespace Endpoints

    ''' <summary>
    ''' Helpers for the GD Boomlings-style endpoints: reading POST form fields,
    ''' cleaning input, building the ":"/"|" separated response strings and
    ''' returning plaintext results the game client understands.
    ''' </summary>
    Public Module GdHelpers

        ''' <summary>Read a POST form value (returns "" if missing).</summary>
        Public Function Form(ctx As HttpContext, key As String, Optional dflt As String = "") As String
            If ctx.Request.HasFormContentType AndAlso ctx.Request.Form.ContainsKey(key) Then
                Return ctx.Request.Form(key).ToString()
            End If
            Return dflt
        End Function

        Public Function FormInt(ctx As HttpContext, key As String, Optional dflt As Integer = 0) As Integer
            Dim v = Form(ctx, key)
            Dim n As Integer
            If Integer.TryParse(v, n) Then Return n
            Return dflt
        End Function

        ''' <summary>Basic exploit patch: strip control/SQL-ish separators (like GMDPS).</summary>
        Public Function Clean(s As String) As String
            If s Is Nothing Then Return ""
            Dim cut = s.Replace(Chr(0), "")
            For Each sep In {":", "|", "~", "#", ")"}
                Dim idx = cut.IndexOf(sep)
                If idx >= 0 Then cut = cut.Substring(0, idx)
            Next
            Return cut.Trim()
        End Function

        Public Function CleanNumber(s As String) As String
            If s Is Nothing Then Return ""
            Dim sb As New StringBuilder()
            For Each c In s
                If Char.IsDigit(c) Then sb.Append(c)
            Next
            Return sb.ToString()
        End Function

        Public Function Now() As Long
            Return DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        End Function

        Public Function ClientIp(ctx As HttpContext) As String
            Dim fwd = ctx.Request.Headers("X-Forwarded-For").ToString()
            If Not String.IsNullOrWhiteSpace(fwd) Then Return fwd.Split(","c)(0).Trim()
            Return If(ctx.Connection.RemoteIpAddress?.ToString(), "127.0.0.1")
        End Function

        ''' <summary>
        ''' Build a GD "dict" string: key/value pairs joined by a separator.
        ''' e.g. Dict(":", (1, "a"), (2, "b")) -> "1:a:2:b".
        ''' </summary>
        Public Function Dict(separator As String, ParamArray pairs As (Integer, String)()) As String
            Dim sb As New StringBuilder()
            For i = 0 To pairs.Length - 1
                If i > 0 Then sb.Append(separator)
                sb.Append(pairs(i).Item1)
                sb.Append(separator)
                sb.Append(pairs(i).Item2)
            Next
            Return sb.ToString()
        End Function

        Public Function Text(body As String) As IResult
            Return Results.Text(body, "text/plain")
        End Function

    End Module

End Namespace
