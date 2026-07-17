Imports System.Collections.Concurrent

Namespace Services

    ''' <summary>
    ''' In-memory manager for Versus mode: competitive lobbies where players race
    ''' to complete the same level. Realtime state (positions, votes, countdown)
    ''' lives here — only the final rewards touch the database. Thread-safe via
    ''' ConcurrentDictionary + per-lobby locks. Stale lobbies are swept out.
    ''' </summary>
    Public Class VersusHub

        ' ---- lobby states ----
        Public Const StateWaiting As String = "waiting"     ' gathering players, host picks levels
        Public Const StateVoting As String = "voting"       ' everyone votes a level
        Public Const StateCountdown As String = "countdown" ' 5-second countdown before play
        Public Const StatePlaying As String = "playing"     ' racing
        Public Const StateResults As String = "results"     ' finished, rewards applied

        Public Const MaxMembers As Integer = 5
        Public Const MaxLevels As Integer = 10

        Public Class Member
            Public Property AccountID As Integer
            Public Property Name As String = ""
            Public Property IconData As String = ""     ' ":"-packed cube icon info for rendering
            Public Property Ready As Boolean = False
            Public Property Vote As Integer = 0         ' chosen levelID (0 = none)
            ' live match state
            Public Property PosX As Double = 0           ' live x (client interpolates)
            Public Property BestX As Double = 0          ' monotonic furthest x — never rewinds on respawn
            Public Property Percent As Integer = 0
            Public Property Attempts As Integer = 0
            Public Property Deaths As Integer = 0
            Public Property Finished As Boolean = False
            Public Property FinishMs As Long = 0
            Public Property LastSeen As Long = 0
        End Class

        Public Class PoolLevel
            Public Property LevelID As Integer
            Public Property Name As String = ""
            Public Property PlaySeconds As Integer = 30   ' host-reported level duration
        End Class

        Public Class Lobby
            Public ReadOnly Gate As New Object()
            Public Property Id As String = ""
            Public Property HostID As Integer
            Public Property State As String = StateWaiting
            Public Property Ranked As Boolean = False
            Public Property TimeMultiplier As Integer = 5   ' level time × {2,5,10}
            Public Property TimeLimitSec As Integer = 0      ' resolved once level chosen
            Public Property ChosenLevel As Integer = 0
            Public Property ChosenLevelName As String = ""
            Public Property CountdownEndsMs As Long = 0
            Public Property PlayEndsMs As Long = 0
            Public Property CreatedMs As Long = 0
            Public Property Members As New List(Of Member)()
            Public Property Pool As New List(Of PoolLevel)()
            Public Property RewardsApplied As Boolean = False
        End Class

        Private ReadOnly _lobbies As New ConcurrentDictionary(Of String, Lobby)()
        ' pending invites: target accountID -> set of lobbyIds
        Private ReadOnly _invites As New ConcurrentDictionary(Of Integer, ConcurrentDictionary(Of String, Byte))()
        Private ReadOnly _rng As New Random()

        Public Shared Function NowMs() As Long
            Return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        End Function

        Private Function NewId() As String
            Return Guid.NewGuid().ToString("N").Substring(0, 8)
        End Function

        ' ---- lobby lifecycle ------------------------------------------------

        Public Function CreateLobby(host As Member, ranked As Boolean) As Lobby
            Sweep()
            ' A player can only host/be in one active lobby; drop them from others.
            LeaveAll(host.AccountID)
            Dim lob As New Lobby With {
                .Id = NewId(), .HostID = host.AccountID, .Ranked = ranked,
                .CreatedMs = NowMs(), .State = StateWaiting}
            host.LastSeen = NowMs()
            lob.Members.Add(host)
            _lobbies(lob.Id) = lob
            Return lob
        End Function

        Public Function Find(lobbyId As String) As Lobby
            Dim lob As Lobby = Nothing
            _lobbies.TryGetValue(If(lobbyId, ""), lob)
            Return lob
        End Function

        ''' <summary>The active lobby a player currently belongs to, if any.</summary>
        Public Function LobbyOf(accountID As Integer) As Lobby
            For Each lob In _lobbies.Values
                SyncLock lob.Gate
                    If lob.Members.Any(Function(m) m.AccountID = accountID) Then Return lob
                End SyncLock
            Next
            Return Nothing
        End Function

        Public Sub Invite(lobbyId As String, targetAccountID As Integer)
            Dim set0 = _invites.GetOrAdd(targetAccountID, Function(k) New ConcurrentDictionary(Of String, Byte)())
            set0(lobbyId) = 1
        End Sub

        Public Function InvitesFor(accountID As Integer) As List(Of Lobby)
            Dim res As New List(Of Lobby)()
            Dim set0 As ConcurrentDictionary(Of String, Byte) = Nothing
            If _invites.TryGetValue(accountID, set0) Then
                For Each lid In set0.Keys.ToList()
                    Dim lob = Find(lid)
                    If lob Is Nothing OrElse lob.State <> StateWaiting Then
                        Dim dummy As Byte
                        set0.TryRemove(lid, dummy)
                    Else
                        res.Add(lob)
                    End If
                Next
            End If
            Return res
        End Function

        Public Function AcceptInvite(lobbyId As String, m As Member) As (ok As Boolean, err As String, lobby As Lobby)
            Dim lob = Find(lobbyId)
            ClearInvite(m.AccountID, lobbyId)
            If lob Is Nothing Then Return (False, "lobby_gone", Nothing)
            SyncLock lob.Gate
                If lob.State <> StateWaiting Then Return (False, "already_started", Nothing)
                If lob.Members.Count >= MaxMembers Then Return (False, "lobby_full", Nothing)
                If lob.Members.Any(Function(x) x.AccountID = m.AccountID) Then Return (True, "", lob)
                LeaveAll(m.AccountID)
                m.LastSeen = NowMs()
                lob.Members.Add(m)
            End SyncLock
            Return (True, "", lob)
        End Function

        Public Sub DeclineInvite(lobbyId As String, accountID As Integer)
            ClearInvite(accountID, lobbyId)
        End Sub

        Private Sub ClearInvite(accountID As Integer, lobbyId As String)
            Dim set0 As ConcurrentDictionary(Of String, Byte) = Nothing
            If _invites.TryGetValue(accountID, set0) Then
                Dim dummy As Byte
                set0.TryRemove(lobbyId, dummy)
            End If
        End Sub

        Public Sub Leave(lobbyId As String, accountID As Integer)
            Dim lob = Find(lobbyId)
            If lob Is Nothing Then Return
            Dim remove As Boolean = False
            SyncLock lob.Gate
                lob.Members.RemoveAll(Function(m) m.AccountID = accountID)
                If lob.Members.Count = 0 Then
                    remove = True
                ElseIf lob.HostID = accountID Then
                    ' Host left: promote the next member.
                    lob.HostID = lob.Members(0).AccountID
                End If
            End SyncLock
            If remove Then
                Dim dummy As Lobby
                _lobbies.TryRemove(lobbyId, dummy)
            End If
        End Sub

        Public Sub LeaveAll(accountID As Integer)
            For Each lob In _lobbies.Values.ToList()
                If lob.Members.Any(Function(m) m.AccountID = accountID) Then
                    Leave(lob.Id, accountID)
                End If
            Next
        End Sub

        ' ---- host configuration --------------------------------------------

        Public Function SetLevels(lob As Lobby, hostID As Integer, levels As List(Of PoolLevel)) As String
            SyncLock lob.Gate
                If lob.HostID <> hostID Then Return "not_host"
                If lob.State <> StateWaiting Then Return "already_started"
                lob.Pool = levels.Take(MaxLevels).ToList()
            End SyncLock
            Return ""
        End Function

        Public Function SetTimeMultiplier(lob As Lobby, hostID As Integer, mult As Integer) As String
            SyncLock lob.Gate
                If lob.HostID <> hostID Then Return "not_host"
                If mult <> 2 AndAlso mult <> 5 AndAlso mult <> 10 Then Return "bad_multiplier"
                lob.TimeMultiplier = mult
            End SyncLock
            Return ""
        End Function

        Public Sub SetReady(lob As Lobby, accountID As Integer, ready As Boolean)
            SyncLock lob.Gate
                Dim m = lob.Members.FirstOrDefault(Function(x) x.AccountID = accountID)
                If m IsNot Nothing Then m.Ready = ready
            End SyncLock
        End Sub

        Public Sub Vote(lob As Lobby, accountID As Integer, levelID As Integer)
            SyncLock lob.Gate
                Dim m = lob.Members.FirstOrDefault(Function(x) x.AccountID = accountID)
                If m IsNot Nothing AndAlso lob.Pool.Any(Function(p) p.LevelID = levelID) Then m.Vote = levelID
            End SyncLock
        End Sub

        ''' <summary>
        ''' Host starts the match: tally votes (majority wins, random on tie/none),
        ''' resolve the time limit from the level's play time × multiplier, and put
        ''' the lobby into a 5-second countdown.
        ''' </summary>
        Public Function Start(lob As Lobby, hostID As Integer) As (ok As Boolean, err As String)
            SyncLock lob.Gate
                If lob.HostID <> hostID Then Return (False, "not_host")
                If lob.State <> StateWaiting AndAlso lob.State <> StateVoting Then Return (False, "already_started")
                If lob.Pool.Count = 0 Then Return (False, "no_levels")

                ' Tally votes.
                Dim tally = lob.Members.Where(Function(m) m.Vote > 0).
                    GroupBy(Function(m) m.Vote).
                    Select(Function(gp) (Level:=gp.Key, Count:=gp.Count())).
                    OrderByDescending(Function(t) t.Count).ToList()

                Dim chosen As Integer
                If tally.Count = 0 Then
                    chosen = lob.Pool(_rng.Next(lob.Pool.Count)).LevelID   ' nobody voted -> random
                Else
                    Dim top = tally(0).Count
                    Dim leaders = tally.Where(Function(t) t.Count = top).Select(Function(t) t.Level).ToList()
                    chosen = leaders(_rng.Next(leaders.Count))               ' random among ties
                End If

                Dim pl = lob.Pool.FirstOrDefault(Function(p) p.LevelID = chosen)
                lob.ChosenLevel = chosen
                lob.ChosenLevelName = pl?.Name
                Dim play = Math.Max(10, If(pl IsNot Nothing, pl.PlaySeconds, 30))
                lob.TimeLimitSec = play * lob.TimeMultiplier
                lob.State = StateCountdown
                lob.CountdownEndsMs = NowMs() + 5000
                lob.PlayEndsMs = lob.CountdownEndsMs + CLng(lob.TimeLimitSec) * 1000

                ' Reset per-match member state.
                For Each m In lob.Members
                    m.PosX = 0 : m.BestX = 0 : m.Percent = 0 : m.Attempts = 0 : m.Deaths = 0
                    m.Finished = False : m.FinishMs = 0 : m.LastSeen = NowMs()
                Next
            End SyncLock
            Return (True, "")
        End Function

        ''' <summary>Advance countdown -> playing -> results based on the clock.</summary>
        Public Sub Tick(lob As Lobby)
            SyncLock lob.Gate
                Dim now = NowMs()
                If lob.State = StateCountdown AndAlso now >= lob.CountdownEndsMs Then
                    lob.State = StatePlaying
                ElseIf lob.State = StatePlaying Then
                    ' End when everyone still present has finished, or the clock runs out.
                    ' Players who leave are removed from Members (see Leave), so they can't
                    ' block the "all finished" check. DNF players stay in the list until
                    ' the time limit, then get ranked as non-finishers.
                    Dim allDone = lob.Members.All(Function(m) m.Finished)
                    If allDone OrElse now >= lob.PlayEndsMs Then lob.State = StateResults
                End If
            End SyncLock
        End Sub

        ' ---- live match state ----------------------------------------------

        Public Sub UpdatePos(lob As Lobby, accountID As Integer, x As Double, percent As Integer, attempts As Integer, dead As Boolean)
            SyncLock lob.Gate
                Dim m = lob.Members.FirstOrDefault(Function(mm) mm.AccountID = accountID)
                If m Is Nothing Then Return
                ' Live x follows the client (which interpolates/smooths on its side).
                ' On death the client will report a small x for the fresh attempt —
                ' that's fine for the live ghost, but BestX/Percent stay monotonic so
                ' a respawn never rewinds the player's ranking progress.
                m.PosX = x
                m.BestX = Math.Max(m.BestX, x)
                m.Percent = Math.Max(m.Percent, percent)
                If attempts > m.Attempts Then m.Attempts = attempts
                If dead Then m.Deaths += 1
                m.LastSeen = NowMs()
            End SyncLock
        End Sub

        Public Sub Finish(lob As Lobby, accountID As Integer, deaths As Integer)
            SyncLock lob.Gate
                Dim m = lob.Members.FirstOrDefault(Function(mm) mm.AccountID = accountID)
                If m Is Nothing OrElse m.Finished Then Return
                m.Finished = True
                m.Percent = 100
                m.FinishMs = NowMs()
                If deaths > m.Deaths Then m.Deaths = deaths
            End SyncLock
        End Sub

        ''' <summary>
        ''' Final ranking: finishers first (by finish time, only those within the
        ''' time limit), then non-finishers by best percent. Players who didn't
        ''' complete within the time limit are marked DNF (not ranked as winners).
        ''' </summary>
        Public Function Rank(lob As Lobby) As List(Of Member)
            SyncLock lob.Gate
                Dim finishers = lob.Members.
                    Where(Function(m) m.Finished AndAlso m.FinishMs <= lob.PlayEndsMs).
                    OrderBy(Function(m) m.FinishMs).ToList()
                Dim rest = lob.Members.Except(finishers).
                    OrderByDescending(Function(m) m.Percent).
                    ThenBy(Function(m) m.Deaths).ToList()
                Return finishers.Concat(rest).ToList()
            End SyncLock
        End Function

        ' ---- housekeeping ---------------------------------------------------

        Public Sub Sweep()
            Dim now = NowMs()
            For Each kv In _lobbies.ToList()
                Dim lob = kv.Value
                Dim drop As Boolean = False
                SyncLock lob.Gate
                    ' Results linger a couple minutes, everything else 30 min max.
                    Dim age = now - lob.CreatedMs
                    If lob.State = StateResults AndAlso now - lob.PlayEndsMs > 180000 Then drop = True
                    If age > 1800000 Then drop = True
                    If lob.Members.Count = 0 Then drop = True
                End SyncLock
                If drop Then
                    Dim dummy As Lobby
                    _lobbies.TryRemove(kv.Key, dummy)
                End If
            Next
        End Sub

    End Class

End Namespace
