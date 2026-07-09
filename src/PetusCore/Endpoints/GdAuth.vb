Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>Shared authentication for GD endpoints (accountID + gjp/gjp2).</summary>
    Public Module GdAuth

        ''' <summary>Return the authenticated accountID, or -1 if the GJP/GJP2 is invalid.</summary>
        Public Function Authenticate(ctx As HttpContext, db As Database, pw As PasswordService) As Integer
            Dim accountID = GdHelpers.FormInt(ctx, "accountID")
            If accountID <= 0 Then Return -1
            Dim acc = db.FindAccount(accountID)
            If acc Is Nothing OrElse acc.IsBanned Then Return -1

            Dim gjp2 = GdHelpers.Form(ctx, "gjp2")
            Dim gjp = GdHelpers.Form(ctx, "gjp")
            If gjp2 <> "" AndAlso pw.VerifyGjp2(gjp2, acc) Then Return accountID
            If gjp <> "" AndAlso pw.VerifyGjp(gjp, acc) Then Return accountID
            Return -1
        End Function

    End Module

End Namespace
