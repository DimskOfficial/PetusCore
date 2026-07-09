Imports System.IO
Imports PetusCore.Data.Models

Namespace Data

    ''' <summary>
    ''' The whole PetusGDPS database: a set of JSON-backed tables living in one
    ''' folder. Provides typed table access, atomic auto-increment IDs and a
    ''' first-boot seed so the server is instantly usable.
    ''' </summary>
    Public Class Database

        Public ReadOnly Property Root As String

        Public ReadOnly Property Accounts As JsonTable(Of Account)
        Public ReadOnly Property Users As JsonTable(Of GdUser)
        Public ReadOnly Property Levels As JsonTable(Of Level)
        Public ReadOnly Property Comments As JsonTable(Of Comment)
        Public ReadOnly Property AccountComments As JsonTable(Of AccountComment)
        Public ReadOnly Property Songs As JsonTable(Of Song)
        Public ReadOnly Property Scores As JsonTable(Of LevelScore)
        Public ReadOnly Property FriendRequests As JsonTable(Of FriendRequest)
        Public ReadOnly Property Friendships As JsonTable(Of Friendship)
        Public ReadOnly Property Blocks As JsonTable(Of Block)
        Public ReadOnly Property Messages As JsonTable(Of Message)
        Public ReadOnly Property ModActions As JsonTable(Of ModAction)
        Public ReadOnly Property Tokens As JsonTable(Of ApiToken)

        Private ReadOnly _counters As JsonTable(Of Counter)
        Private ReadOnly _idGate As New Object()

        Public Sub New(root As String)
            Me.Root = root
            Directory.CreateDirectory(root)

            Accounts = New JsonTable(Of Account)(P("accounts"))
            Users = New JsonTable(Of GdUser)(P("users"))
            Levels = New JsonTable(Of Level)(P("levels"))
            Comments = New JsonTable(Of Comment)(P("comments"))
            AccountComments = New JsonTable(Of AccountComment)(P("acccomments"))
            Songs = New JsonTable(Of Song)(P("songs"))
            Scores = New JsonTable(Of LevelScore)(P("scores"))
            FriendRequests = New JsonTable(Of FriendRequest)(P("friendreqs"))
            Friendships = New JsonTable(Of Friendship)(P("friendships"))
            Blocks = New JsonTable(Of Block)(P("blocks"))
            Messages = New JsonTable(Of Message)(P("messages"))
            ModActions = New JsonTable(Of ModAction)(P("modactions"))
            Tokens = New JsonTable(Of ApiToken)(P("tokens"))
            _counters = New JsonTable(Of Counter)(P("counters"))
        End Sub

        Private Function P(name As String) As String
            Return Path.Combine(Root, name & ".json")
        End Function

        ''' <summary>Atomically allocate the next ID for a named sequence.</summary>
        Public Function NextId(sequence As String) As Integer
            SyncLock _idGate
                Return _counters.WriteReturn(Function(rows)
                                                 Dim c = rows.Find(Function(x) x.Name = sequence)
                                                 If c Is Nothing Then
                                                     c = New Counter With {.Name = sequence, .Value = 0}
                                                     rows.Add(c)
                                                 End If
                                                 c.Value += 1
                                                 Return c.Value
                                             End Function)
            End SyncLock
        End Function

        ' --- Convenience lookups ------------------------------------------

        Public Function FindAccountByName(userName As String) As Account
            Return Accounts.Read(Function(r) r.Find(Function(a) String.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase)))
        End Function

        Public Function FindAccount(accountID As Integer) As Account
            Return Accounts.Read(Function(r) r.Find(Function(a) a.AccountID = accountID))
        End Function

        ''' <summary>Get (or lazily create) the in-game user row for an account/UDID.</summary>
        Public Function ResolveUser(extID As String, userName As String) As GdUser
            Dim existing = Users.Read(Function(r) r.Find(Function(u) u.ExtID = extID))
            If existing IsNot Nothing Then Return existing

            Dim isReg = If(IsNumeric(extID), 1, 0)
            Dim newUser As New GdUser With {
                .UserID = NextId("userID"),
                .ExtID = extID,
                .UserName = userName,
                .IsRegistered = isReg
            }
            Users.Write(Sub(r) r.Add(newUser))
            Return newUser
        End Function

        Public Function FindUserById(userID As Integer) As GdUser
            Return Users.Read(Function(r) r.Find(Function(u) u.UserID = userID))
        End Function

        Public Function FindUserByExt(extID As String) As GdUser
            Return Users.Read(Function(r) r.Find(Function(u) u.ExtID = extID))
        End Function

        Public Sub SaveUser(user As GdUser)
            Users.Write(Sub(r)
                            Dim idx = r.FindIndex(Function(u) u.UserID = user.UserID)
                            If idx >= 0 Then r(idx) = user Else r.Add(user)
                        End Sub)
        End Sub

        Public Sub SaveAccount(acc As Account)
            Accounts.Write(Sub(r)
                               Dim idx = r.FindIndex(Function(a) a.AccountID = acc.AccountID)
                               If idx >= 0 Then r(idx) = acc Else r.Add(acc)
                           End Sub)
        End Sub

        Public Sub Log(accountID As Integer, action As String, target As String, value As String)
            ModActions.Write(Sub(r) r.Add(New ModAction With {
                .ID = NextId("modaction"),
                .AccountID = accountID,
                .Action = action,
                .Target = target,
                .Value = value,
                .Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }))
        End Sub

        ' --- First-boot seed ----------------------------------------------

        Public Sub EnsureSeeded()
            If Songs.Count() = 0 Then
                Songs.Write(Sub(r)
                                r.Add(New Song With {.ID = 1, .Name = "PetusGDPS Theme", .ArtistID = 1, .ArtistName = "Petus", .Size = 3.14, .Download = ""})
                            End Sub)
            End If
            ' Everything else is created on demand.
        End Sub

    End Class

End Namespace
