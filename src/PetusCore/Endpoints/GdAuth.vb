Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>Shared authentication for GD endpoints (accountID + gjp/gjp2).</summary>
    Public Module GdAuth

        ''' <summary>
        ''' Prefix marking a launcher session token carried in the gjp2 field.
        ''' Petus accounts have a random password the game never knows, so the
        ''' mod puts "petus:&lt;token&gt;" into m_GJP2 and we authenticate by token.
        ''' </summary>
        Public Const TokenPrefix As String = "petus:"

        ' Set once at startup so every GD endpoint can authenticate launcher
        ' tokens without threading TokenService through 27 call sites.
        Private _tokens As TokenService

        Public Sub Init(tokens As TokenService)
            _tokens = tokens
        End Sub

        ''' <summary>Return the authenticated accountID, or -1 if the GJP/GJP2 is invalid.</summary>
        Public Function Authenticate(ctx As HttpContext, db As Database, pw As PasswordService, Optional tokens As TokenService = Nothing) As Integer
            Dim accountID = GdHelpers.FormInt(ctx, "accountID")
            If accountID <= 0 Then Return -1
            Dim acc = db.FindAccount(accountID)
            If acc Is Nothing OrElse acc.IsBanned Then Return -1

            Dim gjp2 = GdHelpers.Form(ctx, "gjp2")
            Dim gjp = GdHelpers.Form(ctx, "gjp")

            ' Launcher token path: gjp2 = "petus:<token>".
            Dim tk = If(tokens, _tokens)
            If tk IsNot Nothing AndAlso gjp2.StartsWith(TokenPrefix) Then
                Dim tok = gjp2.Substring(TokenPrefix.Length)
                Dim tokAcc = tk.Validate(tok)
                If tokAcc IsNot Nothing AndAlso tokAcc.AccountID = accountID Then Return accountID
                Return -1
            End If

            If gjp2 <> "" AndAlso pw.VerifyGjp2(gjp2, acc) Then Return accountID
            If gjp <> "" AndAlso pw.VerifyGjp(gjp, acc) Then Return accountID
            Return -1
        End Function

    End Module

End Namespace
