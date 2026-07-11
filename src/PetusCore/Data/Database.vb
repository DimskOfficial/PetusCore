Imports PetusCore.Data.Models
Imports Npgsql

Namespace Data

    ''' <summary>
    ''' The whole PetusGDPS database, backed by PostgreSQL. Each entity is a real
    ''' relational table (typed columns, not JSON). Provides typed table access,
    ''' atomic auto-increment IDs and a first-boot seed so the server is instantly
    ''' usable. Configure the connection with PETUS_DB_URL.
    ''' </summary>
    Public Class Database

        Private ReadOnly _ds As NpgsqlDataSource

        Public ReadOnly Property Accounts As PgTable(Of Account)
        Public ReadOnly Property Users As PgTable(Of GdUser)
        Public ReadOnly Property Levels As PgTable(Of Level)
        Public ReadOnly Property Comments As PgTable(Of Comment)
        Public ReadOnly Property AccountComments As PgTable(Of AccountComment)
        Public ReadOnly Property Songs As PgTable(Of Song)
        Public ReadOnly Property Scores As PgTable(Of LevelScore)
        Public ReadOnly Property FriendRequests As PgTable(Of FriendRequest)
        Public ReadOnly Property Friendships As PgTable(Of Friendship)
        Public ReadOnly Property Blocks As PgTable(Of Block)
        Public ReadOnly Property Messages As PgTable(Of Message)
        Public ReadOnly Property ModActions As PgTable(Of ModAction)
        Public ReadOnly Property Tokens As PgTable(Of ApiToken)
        Public ReadOnly Property MapPacks As PgTable(Of MapPack)
        Public ReadOnly Property Gauntlets As PgTable(Of Gauntlet)
        Public ReadOnly Property Quests As PgTable(Of Quest)
        Public ReadOnly Property Music As PgTable(Of MusicFile)
        Public ReadOnly Property DefaultLevels As PgTable(Of DefaultLevel)
        Public ReadOnly Property LevelLikes As PgTable(Of LevelLike)
        Public ReadOnly Property ModApplications As PgTable(Of ModApplication)

        Private ReadOnly _counters As PgTable(Of Counter)
        Private ReadOnly _idGate As New Object()

        Public Sub New(connectionString As String)
            If String.IsNullOrWhiteSpace(connectionString) Then
                Throw New InvalidOperationException("PETUS_DB_URL is required (PostgreSQL connection string).")
            End If
            _ds = NpgsqlDataSource.Create(connectionString)

            Accounts = New PgTable(Of Account)(_ds, "accounts")
            Users = New PgTable(Of GdUser)(_ds, "users")
            Levels = New PgTable(Of Level)(_ds, "levels")
            Comments = New PgTable(Of Comment)(_ds, "comments")
            AccountComments = New PgTable(Of AccountComment)(_ds, "account_comments")
            Songs = New PgTable(Of Song)(_ds, "songs")
            Scores = New PgTable(Of LevelScore)(_ds, "scores")
            FriendRequests = New PgTable(Of FriendRequest)(_ds, "friend_requests")
            Friendships = New PgTable(Of Friendship)(_ds, "friendships")
            Blocks = New PgTable(Of Block)(_ds, "blocks")
            Messages = New PgTable(Of Message)(_ds, "messages")
            ModActions = New PgTable(Of ModAction)(_ds, "mod_actions")
            Tokens = New PgTable(Of ApiToken)(_ds, "tokens")
            MapPacks = New PgTable(Of MapPack)(_ds, "map_packs")
            Gauntlets = New PgTable(Of Gauntlet)(_ds, "gauntlets")
            Quests = New PgTable(Of Quest)(_ds, "quests")
            Music = New PgTable(Of MusicFile)(_ds, "music_files")
            DefaultLevels = New PgTable(Of DefaultLevel)(_ds, "default_levels")
            LevelLikes = New PgTable(Of LevelLike)(_ds, "level_likes")
            ModApplications = New PgTable(Of ModApplication)(_ds, "mod_applications")
            _counters = New PgTable(Of Counter)(_ds, "counters")
        End Sub

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

        ''' <summary>Find the account linked to a given Petus ID subject, if any.</summary>
        Public Function FindAccountByPetusId(petusId As String) As Account
            If String.IsNullOrEmpty(petusId) Then Return Nothing
            Return Accounts.Read(Function(r) r.Find(Function(a) a.PetusId = petusId))
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
                ' Allocate the seed song's ID through the counter so later uploads
                ' (which use NextId) never collide with it.
                Dim seedId = NextId("songID")
                Songs.Write(Sub(r)
                                r.Add(New Song With {.ID = seedId, .Name = "PetusGDPS Theme", .ArtistID = 1, .ArtistName = "Petus", .Size = 3.14, .Download = ""})
                            End Sub)
            End If
            ' Everything else is created on demand.
        End Sub

    End Class

End Namespace
