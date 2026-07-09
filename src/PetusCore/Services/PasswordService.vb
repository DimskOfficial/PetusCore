Imports System.Security.Cryptography
Imports System.Text

Namespace Services

    ''' <summary>
    ''' Password / login compatibility with the real Geometry Dash client and
    ''' with GMDprivateServer databases.
    '''
    ''' Two schemes are supported:
    '''   • GJP2 (GD 2.2+): client sends sha1(password + salt) as hex; we store
    '''     BCrypt(that hex) in Account.Gjp2 and verify with BCrypt.
    '''   • GJP  (legacy):  client sends base64url(XOR(password, xorKey)); we
    '''     decode it back to the raw password and verify against Account.Password.
    ''' BCrypt is used everywhere so PHP ($2y$) and .NET ($2a$) hashes interop.
    ''' </summary>
    Public Class PasswordService

        Private ReadOnly _config As ServerConfig

        Public Sub New(config As ServerConfig)
            _config = config
        End Sub

        ''' <summary>sha1(password + salt) as lowercase hex — the GJP2 the client sends.</summary>
        Public Function Gjp2FromPassword(password As String) As String
            Return Sha1Hex(password & _config.Gjp2Salt)
        End Function

        ''' <summary>Value stored in Account.Gjp2 = BCrypt(sha1(password + salt)).</summary>
        Public Function HashGjp2(password As String) As String
            Return BCrypt.Net.BCrypt.HashPassword(Gjp2FromPassword(password))
        End Function

        ''' <summary>Value stored in Account.Password = BCrypt(raw password).</summary>
        Public Function HashPassword(password As String) As String
            Return BCrypt.Net.BCrypt.HashPassword(password)
        End Function

        ''' <summary>Verify a raw password (used by the website login form / GJP path).</summary>
        Public Function VerifyRawPassword(rawPassword As String, account As Data.Models.Account) As Boolean
            If account Is Nothing Then Return False
            If Not String.IsNullOrEmpty(account.Password) AndAlso SafeVerify(rawPassword, account.Password) Then Return True
            ' Fall back to GJP2 store if the raw password store is empty.
            If Not String.IsNullOrEmpty(account.Gjp2) Then Return SafeVerify(Gjp2FromPassword(rawPassword), account.Gjp2)
            Return False
        End Function

        ''' <summary>Verify the GJP2 hex string the game client sends.</summary>
        Public Function VerifyGjp2(gjp2FromClient As String, account As Data.Models.Account) As Boolean
            If account Is Nothing OrElse String.IsNullOrEmpty(account.Gjp2) Then Return False
            Return SafeVerify(gjp2FromClient, account.Gjp2)
        End Function

        ''' <summary>Verify the legacy GJP token the game client sends.</summary>
        Public Function VerifyGjp(gjpFromClient As String, account As Data.Models.Account) As Boolean
            Dim raw = DecodeGjp(gjpFromClient)
            If raw Is Nothing Then Return False
            Return VerifyRawPassword(raw, account)
        End Function

        ''' <summary>Decode a legacy GJP token back to the raw password.</summary>
        Public Function DecodeGjp(gjp As String) As String
            If String.IsNullOrEmpty(gjp) Then Return Nothing
            Try
                Dim b64 = gjp.Replace("_", "/").Replace("-", "+")
                Dim decoded = Encoding.[Default].GetString(Convert.FromBase64String(PadBase64(b64)))
                Return XorCipher(decoded, _config.GjpXorKey)
            Catch
                Return Nothing
            End Try
        End Function

        ''' <summary>Symmetric XOR cipher (matches GMDprivateServer XORCipher).</summary>
        Public Shared Function XorCipher(text As String, key As String) As String
            If String.IsNullOrEmpty(text) Then Return ""
            Dim sb As New StringBuilder(text.Length)
            For i = 0 To text.Length - 1
                sb.Append(ChrW(AscW(text(i)) Xor AscW(key(i Mod key.Length))))
            Next
            Return sb.ToString()
        End Function

        Public Shared Function Sha1Hex(input As String) As String
            Using sha = SHA1.Create()
                Dim bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input))
                Dim sb As New StringBuilder(bytes.Length * 2)
                For Each b In bytes
                    sb.Append(b.ToString("x2"))
                Next
                Return sb.ToString()
            End Using
        End Function

        Private Shared Function SafeVerify(value As String, hash As String) As Boolean
            Try
                Return BCrypt.Net.BCrypt.Verify(value, hash)
            Catch
                Return False
            End Try
        End Function

        Private Shared Function PadBase64(s As String) As String
            Dim mod4 = s.Length Mod 4
            If mod4 > 0 Then s &= New String("="c, 4 - mod4)
            Return s
        End Function

    End Class

End Namespace
