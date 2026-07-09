Imports System.Security.Cryptography
Imports System.Text
Imports PetusCore.Data
Imports PetusCore.Data.Models

Namespace Services

    ''' <summary>
    ''' Issues and validates opaque session tokens for the REST API used by the
    ''' PetusGDPS website. Tokens are random, stored server-side in tokens.json
    ''' and carry an expiry. Simple and revocable — no JWT complexity needed.
    ''' </summary>
    Public Class TokenService

        Private Const TtlSeconds As Long = 60L * 60L * 24L * 7L ' 7 days

        Private ReadOnly _db As Database

        Public Sub New(db As Database)
            _db = db
        End Sub

        Public Function Issue(accountID As Integer) As ApiToken
            Dim now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            Dim tok As New ApiToken With {
                .Token = NewToken(),
                .AccountID = accountID,
                .IssuedAt = now,
                .ExpiresAt = now + TtlSeconds
            }
            _db.Tokens.Write(Sub(r)
                                 ' Drop expired tokens opportunistically.
                                 r.RemoveAll(Function(t) t.ExpiresAt < now)
                                 r.Add(tok)
                             End Sub)
            Return tok
        End Function

        ''' <summary>Return the account for a valid token, or Nothing.</summary>
        Public Function Validate(token As String) As Account
            If String.IsNullOrEmpty(token) Then Return Nothing
            Dim now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            Dim row = _db.Tokens.Read(Function(r) r.Find(Function(t) t.Token = token))
            If row Is Nothing OrElse row.ExpiresAt < now Then Return Nothing
            Return _db.FindAccount(row.AccountID)
        End Function

        Public Sub Revoke(token As String)
            _db.Tokens.Write(Sub(r) r.RemoveAll(Function(t) t.Token = token))
        End Sub

        Private Shared Function NewToken() As String
            Dim buf(31) As Byte
            RandomNumberGenerator.Fill(buf)
            Dim sb As New StringBuilder(buf.Length * 2)
            For Each b In buf
                sb.Append(b.ToString("x2"))
            Next
            Return sb.ToString()
        End Function

    End Class

End Namespace
