Imports Microsoft.AspNetCore.Builder
Imports System.Text
Imports Microsoft.AspNetCore.Http
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services

Namespace Endpoints

    ''' <summary>
    ''' Social systems: private messages, friend requests, friendships,
    ''' blocking and the friends/blocked user lists.
    ''' </summary>
    Public Module SocialEndpoints

        Public Sub Map(app As Object, db As Database, pw As PasswordService)
            Dim a = DirectCast(app, WebApplication)

            ' ================= MESSAGES =================================

            ' Send a message.
            a.MapPost("/uploadGJMessage20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim toAccount = GdHelpers.FormInt(ctx, "toAccountID")
                If toAccount <= 0 OrElse db.FindAccount(toAccount) Is Nothing Then Return GdHelpers.Text("-1")
                Dim msg As New Message With {
                    .MessageID = db.NextId("messageID"),
                    .AccountID = toAccount,
                    .AccountIDFrom = accountID,
                    .Subject = GdHelpers.Form(ctx, "subject"),
                    .Body = GdHelpers.Form(ctx, "body"),
                    .IsNew = 1,
                    .Timestamp = GdHelpers.Now()
                }
                db.Messages.Write(Sub(r) r.Add(msg))
                Return GdHelpers.Text("1")
            End Function)

            ' List messages (inbox by default, sent if getSent=1).
            a.MapPost("/getGJMessages20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim sent = GdHelpers.FormInt(ctx, "getSent")
                Dim page = GdHelpers.FormInt(ctx, "page")
                Dim perPage = 10
                Dim list = db.Messages.All().
                    Where(Function(m) If(sent = 1, m.AccountIDFrom = accountID, m.AccountID = accountID)).
                    OrderByDescending(Function(m) m.Timestamp).ToList()
                Dim total = list.Count
                Dim items = list.Skip(page * perPage).Take(perPage).ToList()
                If items.Count = 0 Then Return GdHelpers.Text("-2")
                Dim sb As New StringBuilder()
                For Each m In items
                    Dim otherAcc = If(sent = 1, m.AccountID, m.AccountIDFrom)
                    Dim otherUser = db.FindUserByExt(otherAcc.ToString())
                    If sb.Length > 0 Then sb.Append("|")
                    sb.Append(BuildMessage(m, otherAcc, otherUser, sent = 1, includeBody:=False))
                Next
                sb.Append("#").Append($"{total}:{page * perPage}:{perPage}")
                Return GdHelpers.Text(sb.ToString())
            End Function)

            ' Read one message (returns the body, marks as read).
            a.MapPost("/downloadGJMessage20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim messageID = GdHelpers.FormInt(ctx, "messageID")
                Dim m = db.Messages.Read(Function(r) r.Find(Function(x) x.MessageID = messageID))
                If m Is Nothing OrElse (m.AccountID <> accountID AndAlso m.AccountIDFrom <> accountID) Then Return GdHelpers.Text("-1")
                Dim isSent = m.AccountIDFrom = accountID
                If Not isSent Then
                    db.Messages.Write(Sub(r)
                                          Dim mm = r.Find(Function(x) x.MessageID = messageID)
                                          If mm IsNot Nothing Then mm.IsNew = 0
                                      End Sub)
                End If
                Dim otherAcc = If(isSent, m.AccountID, m.AccountIDFrom)
                Dim otherUser = db.FindUserByExt(otherAcc.ToString())
                Return GdHelpers.Text(BuildMessage(m, otherAcc, otherUser, isSent, includeBody:=True))
            End Function)

            ' Delete message(s).
            a.MapPost("/deleteGJMessages20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim single_ = GdHelpers.FormInt(ctx, "messageID")
                Dim many = GdHelpers.Form(ctx, "messages")
                Dim ids As New List(Of Integer)()
                If single_ > 0 Then ids.Add(single_)
                If many <> "" Then
                    For Each part In many.Split(","c)
                        Dim n As Integer
                        If Integer.TryParse(part, n) Then ids.Add(n)
                    Next
                End If
                db.Messages.Write(Sub(r) r.RemoveAll(Function(x) ids.Contains(x.MessageID) AndAlso (x.AccountID = accountID OrElse x.AccountIDFrom = accountID)))
                Return GdHelpers.Text("1")
            End Function)

            ' ================= FRIEND REQUESTS =========================

            a.MapPost("/uploadFriendRequest20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim toAccount = GdHelpers.FormInt(ctx, "toAccountID")
                If toAccount <= 0 OrElse toAccount = accountID Then Return GdHelpers.Text("-1")
                ' Already friends?
                If AreFriends(db, accountID, toAccount) Then Return GdHelpers.Text("-1")
                ' Duplicate request?
                Dim dup = db.FriendRequests.Read(Function(r) r.Find(Function(x) x.AccountIDFrom = accountID AndAlso x.AccountID = toAccount))
                If dup IsNot Nothing Then Return GdHelpers.Text("-1")
                db.FriendRequests.Write(Sub(r) r.Add(New FriendRequest With {
                    .ID = db.NextId("friendReqID"),
                    .AccountID = toAccount,
                    .AccountIDFrom = accountID,
                    .Comment = GdHelpers.Form(ctx, "comment"),
                    .IsNew = 1,
                    .UploadDate = GdHelpers.Now()
                }))
                Return GdHelpers.Text("1")
            End Function)

            a.MapPost("/getGJFriendRequests20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim sent = GdHelpers.FormInt(ctx, "getSent")
                Dim page = GdHelpers.FormInt(ctx, "page")
                Dim perPage = 10
                Dim list = db.FriendRequests.All().
                    Where(Function(f) If(sent = 1, f.AccountIDFrom = accountID, f.AccountID = accountID)).
                    OrderByDescending(Function(f) f.UploadDate).ToList()
                Dim total = list.Count
                Dim items = list.Skip(page * perPage).Take(perPage).ToList()
                If items.Count = 0 Then Return GdHelpers.Text("-2")
                Dim sb As New StringBuilder()
                For Each f In items
                    Dim otherAcc = If(sent = 1, f.AccountID, f.AccountIDFrom)
                    Dim u = db.FindUserByExt(otherAcc.ToString())
                    If sb.Length > 0 Then sb.Append("|")
                    sb.Append(BuildFriendRequest(f, otherAcc, u))
                Next
                sb.Append("#").Append($"{total}:{page * perPage}:{perPage}")
                Return GdHelpers.Text(sb.ToString())
            End Function)

            a.MapPost("/readGJFriendRequest20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim reqID = GdHelpers.FormInt(ctx, "requestID")
                db.FriendRequests.Write(Sub(r)
                                            Dim f = r.Find(Function(x) x.ID = reqID AndAlso x.AccountID = accountID)
                                            If f IsNot Nothing Then f.IsNew = 0
                                        End Sub)
                Return GdHelpers.Text("1")
            End Function)

            a.MapPost("/acceptGJFriendRequest20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim reqID = GdHelpers.FormInt(ctx, "requestID")
                Dim targetAccount = GdHelpers.FormInt(ctx, "targetAccountID")
                Dim req = db.FriendRequests.Read(Function(r) r.Find(Function(x) x.ID = reqID AndAlso x.AccountID = accountID))
                If req Is Nothing Then Return GdHelpers.Text("-1")
                db.Friendships.Write(Sub(r) r.Add(New Friendship With {
                    .ID = db.NextId("friendshipID"),
                    .Account1 = req.AccountIDFrom,
                    .Account2 = accountID,
                    .IsNew1 = 1, .IsNew2 = 0
                }))
                db.FriendRequests.Write(Sub(r) r.RemoveAll(Function(x) x.ID = reqID))
                Return GdHelpers.Text("1")
            End Function)

            a.MapPost("/deleteGJFriendRequests20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim targetAccount = GdHelpers.FormInt(ctx, "targetAccountID")
                Dim isSender = GdHelpers.FormInt(ctx, "isSender")
                db.FriendRequests.Write(Sub(r) r.RemoveAll(Function(x)
                    If isSender = 1 Then
                        Return x.AccountIDFrom = accountID AndAlso x.AccountID = targetAccount
                    Else
                        Return x.AccountID = accountID AndAlso x.AccountIDFrom = targetAccount
                    End If
                End Function))
                Return GdHelpers.Text("1")
            End Function)

            ' ================= FRIENDSHIPS / LISTS =====================

            a.MapPost("/removeGJFriend20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim targetAccount = GdHelpers.FormInt(ctx, "targetAccountID")
                db.Friendships.Write(Sub(r) r.RemoveAll(Function(x) _
                    (x.Account1 = accountID AndAlso x.Account2 = targetAccount) OrElse
                    (x.Account2 = accountID AndAlso x.Account1 = targetAccount)))
                Return GdHelpers.Text("1")
            End Function)

            ' Friends list (type 0) or blocked list (type 1).
            a.MapPost("/getGJUserList20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim type_ = GdHelpers.FormInt(ctx, "type")
                Dim sb As New StringBuilder()
                If type_ = 1 Then
                    ' blocked
                    For Each b In db.Blocks.All().Where(Function(x) x.AccountID = accountID)
                        Dim u = db.FindUserByExt(b.BlockedID.ToString())
                        If u IsNot Nothing Then
                            If sb.Length > 0 Then sb.Append("|")
                            sb.Append(BuildListUser(u, b.BlockedID))
                        End If
                    Next
                Else
                    ' friends
                    For Each f In db.Friendships.All().Where(Function(x) x.Account1 = accountID OrElse x.Account2 = accountID)
                        Dim otherAcc = If(f.Account1 = accountID, f.Account2, f.Account1)
                        Dim u = db.FindUserByExt(otherAcc.ToString())
                        If u IsNot Nothing Then
                            If sb.Length > 0 Then sb.Append("|")
                            sb.Append(BuildListUser(u, otherAcc))
                        End If
                    Next
                End If
                If sb.Length = 0 Then Return GdHelpers.Text("-2")
                Return GdHelpers.Text(sb.ToString())
            End Function)

            ' ================= BLOCKING ================================

            a.MapPost("/blockGJUser20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim targetAccount = GdHelpers.FormInt(ctx, "targetAccountID")
                If targetAccount <= 0 Then Return GdHelpers.Text("-1")
                db.Blocks.Write(Sub(r)
                                    If r.Find(Function(x) x.AccountID = accountID AndAlso x.BlockedID = targetAccount) Is Nothing Then
                                        r.Add(New Block With {.ID = db.NextId("blockID"), .AccountID = accountID, .BlockedID = targetAccount})
                                    End If
                                End Sub)
                ' Blocking removes any friendship.
                db.Friendships.Write(Sub(r) r.RemoveAll(Function(x) _
                    (x.Account1 = accountID AndAlso x.Account2 = targetAccount) OrElse
                    (x.Account2 = accountID AndAlso x.Account1 = targetAccount)))
                Return GdHelpers.Text("1")
            End Function)

            a.MapPost("/unblockGJUser20.php", Function(ctx As HttpContext)
                Dim accountID = GdAuth.Authenticate(ctx, db, pw)
                If accountID <= 0 Then Return GdHelpers.Text("-1")
                Dim targetAccount = GdHelpers.FormInt(ctx, "targetAccountID")
                db.Blocks.Write(Sub(r) r.RemoveAll(Function(x) x.AccountID = accountID AndAlso x.BlockedID = targetAccount))
                Return GdHelpers.Text("1")
            End Function)
        End Sub

        ' ---------------- helpers -------------------------------------
        Public Function AreFriends(db As Database, a As Integer, b As Integer) As Boolean
            Return db.Friendships.Read(Function(r) r.Any(Function(x) _
                (x.Account1 = a AndAlso x.Account2 = b) OrElse (x.Account2 = a AndAlso x.Account1 = b)))
        End Function

        Private Function BuildMessage(m As Message, otherAcc As Integer, u As GdUser, isSent As Boolean, includeBody As Boolean) As String
            Dim sb As New StringBuilder()
            sb.Append("6:").Append(If(u IsNot Nothing, u.UserName, "undefined"))
            sb.Append(":3:").Append(If(u IsNot Nothing, u.UserID, 0))
            sb.Append(":2:").Append(otherAcc)
            sb.Append(":1:").Append(m.MessageID)
            sb.Append(":4:").Append(m.Subject)
            If includeBody Then sb.Append(":5:").Append(m.Body)
            sb.Append(":8:").Append(If(m.IsNew = 0, "1", "0"))
            sb.Append(":9:").Append(If(isSent, "1", "0"))
            sb.Append(":7:").Append(CommentEndpoints.AgoString(m.Timestamp))
            Return sb.ToString()
        End Function

        Private Function BuildFriendRequest(f As FriendRequest, otherAcc As Integer, u As GdUser) As String
            Dim sb As New StringBuilder()
            sb.Append("1:").Append(If(u IsNot Nothing, u.UserName, "undefined"))
            sb.Append(":2:").Append(If(u IsNot Nothing, u.UserID, 0))
            sb.Append(":9:").Append(If(u IsNot Nothing, u.Icon, 0))
            sb.Append(":10:").Append(If(u IsNot Nothing, u.Color1, 0))
            sb.Append(":11:").Append(If(u IsNot Nothing, u.Color2, 3))
            sb.Append(":14:").Append(If(u IsNot Nothing, u.IconType, 0))
            sb.Append(":15:").Append(If(u IsNot Nothing, u.Special, 0))
            sb.Append(":16:").Append(otherAcc)
            sb.Append(":32:").Append(f.ID)
            sb.Append(":35:").Append(f.Comment)
            sb.Append(":41:").Append(If(f.IsNew = 1, "1", "0"))
            sb.Append(":37:").Append(CommentEndpoints.AgoString(f.UploadDate))
            Return sb.ToString()
        End Function

        Private Function BuildListUser(u As GdUser, acc As Integer) As String
            Dim sb As New StringBuilder()
            sb.Append("1:").Append(u.UserName)
            sb.Append(":2:").Append(u.UserID)
            sb.Append(":9:").Append(u.Icon)
            sb.Append(":10:").Append(u.Color1)
            sb.Append(":11:").Append(u.Color2)
            sb.Append(":14:").Append(u.IconType)
            sb.Append(":15:").Append(u.Special)
            sb.Append(":16:").Append(acc)
            sb.Append(":18:0:41:0")
            Return sb.ToString()
        End Function

    End Module

End Namespace
