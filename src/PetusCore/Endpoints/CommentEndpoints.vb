Imports System.Text
Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>Level comments and profile (account) comments.</summary>
    Public Module CommentEndpoints

        Public Sub Map(app As Object, db As Database, pw As PasswordService, hash As HashService)
            Dim a = DirectCast(app, Microsoft.AspNetCore.Builder.WebApplication)

            ' --- Upload level comment -------------------------------------
            For Each path In {"/uploadGJComment21.php", "/uploadGJComment20.php", "/uploadGJComment.php"}
                a.MapPost(path, Function(ctx As HttpContext)
                    Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                    Dim extID = If(accountID > 0, accountID.ToString(), GdHelpers.Clean(GdHelpers.Form(ctx, "udid")))
                    If extID = "" Then Return GdHelpers.Text("-1")
                    Dim user = db.ResolveUser(extID, GdHelpers.Clean(GdHelpers.Form(ctx, "userName")))
                    Dim levelID = GdHelpers.FormInt(ctx, "levelID")
                    Dim content = GdHelpers.Form(ctx, "comment")
                    If content = "" Then Return GdHelpers.Text("-1")
                    Dim c As New Comment With {
                        .CommentID = db.NextId("commentID"),
                        .UserID = user.UserID,
                        .LevelID = levelID,
                        .Content = content,
                        .Percent = GdHelpers.FormInt(ctx, "percent"),
                        .Timestamp = GdHelpers.Now()
                    }
                    db.Comments.Write(Sub(r) r.Add(c))
                    Return GdHelpers.Text(c.CommentID.ToString())
                End Function)
            Next

            ' --- Get level comments ---------------------------------------
            For Each path In {"/getGJComments21.php", "/getGJComments20.php", "/getGJComments.php"}
                a.MapPost(path, Function(ctx As HttpContext)
                    Dim levelID = GdHelpers.FormInt(ctx, "levelID")
                    Dim page = GdHelpers.FormInt(ctx, "page")
                    Dim mode = GdHelpers.FormInt(ctx, "mode")
                    Dim perPage = 20
                    Dim list = db.Comments.All().Where(Function(x) x.LevelID = levelID).ToList()
                    If mode = 1 Then list = list.OrderByDescending(Function(x) x.Likes).ToList() _
                                   Else list = list.OrderByDescending(Function(x) x.Timestamp).ToList()
                    Dim total = list.Count
                    Dim items = list.Skip(page * perPage).Take(perPage).ToList()

                    Dim sb As New StringBuilder()
                    For Each c In items
                        Dim u = db.FindUserById(c.UserID)
                        If sb.Length > 0 Then sb.Append("|")
                        sb.Append(BuildComment(c, u))
                    Next
                    sb.Append("#").Append($"{total}:{page * perPage}:{perPage}")
                    Return GdHelpers.Text(sb.ToString())
                End Function)
            Next

            ' --- Upload profile comment -----------------------------------
            a.MapPost("/uploadGJAccComment20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim user = db.ResolveUser(accountID.ToString(), GdHelpers.Clean(GdHelpers.Form(ctx, "userName")))
                Dim c As New AccountComment With {
                    .CommentID = db.NextId("accCommentID"),
                    .AccountID = accountID,
                    .UserID = user.UserID,
                    .Content = GdHelpers.Form(ctx, "comment"),
                    .Timestamp = GdHelpers.Now()
                }
                db.AccountComments.Write(Sub(r) r.Add(c))
                Return GdHelpers.Text(c.CommentID.ToString())
            End Function)

            ' --- Get profile comments -------------------------------------
            a.MapPost("/getGJAccountComments20.php", Function(ctx As HttpContext)
                Dim accountID = GdHelpers.FormInt(ctx, "accountID")
                Dim page = GdHelpers.FormInt(ctx, "page")
                Dim perPage = 10
                Dim list = db.AccountComments.All().Where(Function(x) x.AccountID = accountID).
                              OrderByDescending(Function(x) x.Timestamp).ToList()
                Dim total = list.Count
                Dim items = list.Skip(page * perPage).Take(perPage).ToList()
                Dim sb As New StringBuilder()
                For Each c In items
                    If sb.Length > 0 Then sb.Append("|")
                    sb.Append($"2~{c.Content}~4~{c.Likes}~6~{c.CommentID}~9~{AgoString(c.Timestamp)}")
                Next
                sb.Append("#").Append($"{total}:{page * perPage}:{perPage}")
                Return GdHelpers.Text(sb.ToString())
            End Function)

            ' --- Delete level comment -------------------------------------
            a.MapPost("/deleteGJComment20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim commentID = GdHelpers.FormInt(ctx, "commentID")
                Dim user = db.FindUserByExt(accountID.ToString())
                db.Comments.Write(Sub(r) r.RemoveAll(Function(x) x.CommentID = commentID AndAlso (user IsNot Nothing AndAlso x.UserID = user.UserID)))
                Return GdHelpers.Text("1")
            End Function)

            ' --- Delete profile comment -----------------------------------
            a.MapPost("/deleteGJAccComment20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim commentID = GdHelpers.FormInt(ctx, "commentID")
                db.AccountComments.Write(Sub(r) r.RemoveAll(Function(x) x.CommentID = commentID AndAlso x.AccountID = accountID))
                Return GdHelpers.Text("1")
            End Function)
        End Sub

        Private Function BuildComment(c As Comment, u As GdUser) As String
            Dim inner As New StringBuilder()
            inner.Append("2~").Append(c.Content)
            inner.Append("~3~").Append(If(u IsNot Nothing, u.UserID, 0))
            inner.Append("~4~").Append(c.Likes)
            inner.Append("~5~0")
            inner.Append("~6~").Append(c.CommentID)
            inner.Append("~7~").Append(c.IsSpam)
            inner.Append("~9~").Append(AgoString(c.Timestamp))
            inner.Append("~10~").Append(c.Percent)
            Dim userPart As New StringBuilder()
            If u IsNot Nothing Then
                userPart.Append("1~").Append(u.UserName)
                userPart.Append("~9~").Append(u.Icon)
                userPart.Append("~10~").Append(u.Color1)
                userPart.Append("~11~").Append(u.Color2)
                userPart.Append("~14~").Append(u.IconType)
                userPart.Append("~15~").Append(u.Special)
                userPart.Append("~16~").Append(u.ExtID)
            End If
            Return inner.ToString() & ":" & userPart.ToString()
        End Function

        ''' <summary>Human "x days ago" string GD shows next to comments.</summary>
        Public Function AgoString(ts As Long) As String
            Dim diff = GdHelpers.Now() - ts
            If diff < 60 Then Return diff & " seconds"
            If diff < 3600 Then Return (diff \ 60) & " minutes"
            If diff < 86400 Then Return (diff \ 3600) & " hours"
            If diff < 2592000 Then Return (diff \ 86400) & " days"
            If diff < 31536000 Then Return (diff \ 2592000) & " months"
            Return (diff \ 31536000) & " years"
        End Function

    End Module

End Namespace
