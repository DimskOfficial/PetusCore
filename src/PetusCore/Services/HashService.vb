Imports System.Security.Cryptography
Imports System.Text
Imports PetusCore.Data.Models

Namespace Services

    ''' <summary>
    ''' Integrity hashes and "chk" tokens the GD client validates. Salts and
    ''' XOR keys match the real game / GMDprivateServer so responses verify.
    ''' </summary>
    Public Class HashService

        ' Well-known GD salts.
        Public Const SaltLevel As String = "xI25fpAapCQg"
        Public Const SaltComment As String = "xPT6iUrtws0J"
        Public Const SaltLike As String = "ysg6pUrtjn0J"
        Public Const SaltLevelScore As String = "yPg6pUrtdn0J"
        Public Const SaltUserScore As String = "xI35fsAapCRg"
        Public Const SaltRating As String = "ahpfUV5TabIY" ' legacy

        ' XOR keys used to build "chk" tokens (base64url(XOR(sha1(...), key))).
        Public Const KeyLevel As String = "41274"
        Public Const KeyComment As String = "29481"
        Public Const KeyLike As String = "58281"
        Public Const KeyRewards As String = "59182"
        Public Const KeyQuests As String = "19847"

        ''' <summary>Hash for the getGJLevels response (list of levels).</summary>
        Public Function GenMulti(levels As IEnumerable(Of Level)) As String
            Dim sb As New StringBuilder()
            For Each lvl In levels
                Dim id = lvl.LevelID.ToString()
                sb.Append(id(0))
                sb.Append(id(id.Length - 1))
                sb.Append(lvl.Stars)
                sb.Append(If(lvl.RateCoins > 0, "1", "0"))
            Next
            Return Sha1Hex(sb.ToString() & SaltLevel)
        End Function

        ''' <summary>Hash for a downloaded level string (downloadGJLevel).</summary>
        Public Function GenSolo(levelString As String) As String
            Dim len = levelString.Length
            If len < 41 Then Return Sha1Hex(levelString & SaltLevel)
            Dim salt = SaltLevel
            ' 40 sampled chars + the salt. Array length must be exactly 40+salt.
            Dim hash As Char() = New Char(40 + salt.Length - 1) {}
            Dim m = len \ 40
            For i = 0 To 39
                hash(i) = levelString(i * m)
            Next
            For i = 0 To salt.Length - 1
                hash(40 + i) = salt(i)
            Next
            Return Sha1Hex(New String(hash))
        End Function

        Public Function GenWithSalt(value As String, salt As String) As String
            Return Sha1Hex(value & salt)
        End Function

        ''' <summary>Build a base64url(XOR(sha1(value+salt), key)) chk token.</summary>
        Public Function Chk(value As String, salt As String, key As String) As String
            Dim hash = Sha1Hex(value & salt)
            Dim xored = PasswordService.XorCipher(hash, key)
            Return Base64Url(xored)
        End Function

        Public Shared Function Base64Url(text As String) As String
            Dim b64 = Convert.ToBase64String(Encoding.[Default].GetBytes(text))
            Return b64.Replace("+", "-").Replace("/", "_")
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

    End Class

End Namespace
