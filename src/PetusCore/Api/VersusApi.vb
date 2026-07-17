Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection
Imports System.Text.Json
Imports PetusCore.Data
Imports PetusCore.Data.Models
Imports PetusCore.Services
Imports PetusCore.Endpoints

Namespace Api

    ''' <summary>
    ''' Versus mode REST API — competitive lobbies consumed by the in-game mod.
    ''' All endpoints require a player bearer token (the launcher/game token).
    ''' Realtime state is polled: the mod GETs lobby/match state a few times a
    ''' second and POSTs its own position. Rewards are written to the DB once,
    ''' at results time, for ranked lobbies only.
    ''' </summary>
    Public Module VersusApi

        Public Sub Map(app As WebApplication)
            Dim a = DirectCast(app, WebApplication)
            Dim db = a.Services.GetRequiredService(Of Database)()
            Dim tokens = a.Services.GetRequiredService(Of TokenService)()
            Dim hub = a.Services.GetRequiredService(Of VersusHub)()

            ' --- eligibility: Versus is open to every non-banned account. The
            ' progress gate is gone — balance fairness is handled at reward time
            ' by the poorest-player stake cap, so even a fresh account can play
            ' (it just risks/wins little). The mod calls this to unlock the tab. ---
            a.MapGet("/api/versus/eligible", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.Ok(New With {.eligible = False})
                Return RestApi.Ok(New With {.eligible = acc.IsBanned = False, .name = acc.UserName})
            End Function)

            ' --- top players to invite (by stars) ---
            a.MapGet("/api/versus/top", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim top = db.Users.Read(Function(rows) rows.
                    Where(Function(u) u.IsRegistered = 1 AndAlso IsNumeric(u.ExtID) AndAlso u.ExtID <> acc.AccountID.ToString()).
                    OrderByDescending(Function(u) u.Stars).Take(20).
                    Select(Function(u) New With {.accountID = CInt(Val(u.ExtID)), .name = u.UserName, .stars = u.Stars}).ToList())
                Return RestApi.Ok(top)
            End Function)

            ' --- create lobby ---
            a.MapPost("/api/versus/lobby/create", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim body = RestApi.ReadJson(ctx)
                Dim ranked = RestApi.Num(body, "ranked", 0) <> 0
                Dim lob = hub.CreateLobby(MakeMember(db, acc), ranked)
                Return RestApi.Ok(LobbyDto(lob, acc.AccountID))
            End Function)

            ' --- invite a player (by name or accountID) ---
            a.MapPost("/api/versus/lobby/invite", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                If lob.HostID <> acc.AccountID Then Return RestApi.[Error](ctx, 403, "not_host")
                Dim body = RestApi.ReadJson(ctx)
                Dim targetId = CInt(RestApi.Num(body, "accountID", 0))
                Dim targetName = RestApi.Str(body, "name", "")
                If targetId <= 0 AndAlso targetName <> "" Then
                    Dim tacc = db.FindAccountByName(targetName)
                    If tacc IsNot Nothing Then targetId = tacc.AccountID
                End If
                If targetId <= 0 Then Return RestApi.[Error](ctx, 404, "player_not_found")
                hub.Invite(lob.Id, targetId)
                Return RestApi.Ok(New With {.ok = True, .invited = targetId})
            End Function)

            ' --- my pending invites (mod polls this) ---
            a.MapGet("/api/versus/invites", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim list = hub.InvitesFor(acc.AccountID).Select(Function(l)
                    Dim hostName = db.FindAccount(l.HostID)?.UserName
                    Return New With {.lobbyId = l.Id, .host = hostName, .players = l.Members.Count, .ranked = l.Ranked}
                End Function).ToList()
                Return RestApi.Ok(list)
            End Function)

            ' --- respond to an invite ---
            a.MapPost("/api/versus/invite/respond", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim body = RestApi.ReadJson(ctx)
                Dim lobbyId = RestApi.Str(body, "lobbyId", "")
                Dim accept = RestApi.Num(body, "accept", 0) <> 0
                If Not accept Then
                    hub.DeclineInvite(lobbyId, acc.AccountID)
                    Return RestApi.Ok(New With {.ok = True, .joined = False})
                End If
                Dim r = hub.AcceptInvite(lobbyId, MakeMember(db, acc))
                If Not r.ok Then Return RestApi.[Error](ctx, 400, r.err)
                Return RestApi.Ok(LobbyDto(r.lobby, acc.AccountID))
            End Function)

            ' --- lobby state (mod polls) ---
            a.MapGet("/api/versus/lobby/{id}", Function(ctx As HttpContext, id As String)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.Find(id)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "lobby_gone")
                hub.Tick(lob)
                Return RestApi.Ok(LobbyDto(lob, acc.AccountID))
            End Function)

            ' --- host sets the level pool ---
            a.MapPost("/api/versus/lobby/levels", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                Dim body = RestApi.ReadJson(ctx)
                Dim levels As New List(Of VersusHub.PoolLevel)()
                Dim arr As JsonElement
                If body.ValueKind = JsonValueKind.Object AndAlso body.TryGetProperty("levels", arr) AndAlso arr.ValueKind = JsonValueKind.Array Then
                    For Each el In arr.EnumerateArray()
                        Dim lid = CInt(RestApi.Num(el, "id", 0))
                        If lid <= 0 Then Continue For
                        levels.Add(New VersusHub.PoolLevel With {
                            .LevelID = lid,
                            .Name = RestApi.Str(el, "name", "Level " & lid),
                            .PlaySeconds = Math.Max(5, CInt(RestApi.Num(el, "seconds", 30)))})
                    Next
                End If
                Dim err = hub.SetLevels(lob, acc.AccountID, levels)
                If err <> "" Then Return RestApi.[Error](ctx, 400, err)
                Return RestApi.Ok(LobbyDto(lob, acc.AccountID))
            End Function)

            ' --- host sets the time multiplier (2/5/10) ---
            a.MapPost("/api/versus/lobby/timelimit", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                Dim mult = CInt(RestApi.Num(RestApi.ReadJson(ctx), "multiplier", 5))
                Dim err = hub.SetTimeMultiplier(lob, acc.AccountID, mult)
                If err <> "" Then Return RestApi.[Error](ctx, 400, err)
                Return RestApi.Ok(LobbyDto(lob, acc.AccountID))
            End Function)

            ' --- ready toggle ---
            a.MapPost("/api/versus/lobby/ready", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                hub.SetReady(lob, acc.AccountID, RestApi.Num(RestApi.ReadJson(ctx), "ready", 1) <> 0)
                Return RestApi.Ok(LobbyDto(lob, acc.AccountID))
            End Function)

            ' --- vote a level ---
            a.MapPost("/api/versus/lobby/vote", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                hub.Vote(lob, acc.AccountID, CInt(RestApi.Num(RestApi.ReadJson(ctx), "levelID", 0)))
                Return RestApi.Ok(LobbyDto(lob, acc.AccountID))
            End Function)

            ' --- host starts the match ---
            a.MapPost("/api/versus/lobby/start", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                Dim r = hub.Start(lob, acc.AccountID)
                If Not r.ok Then Return RestApi.[Error](ctx, 400, r.err)
                Return RestApi.Ok(LobbyDto(lob, acc.AccountID))
            End Function)

            ' --- leave ---
            a.MapPost("/api/versus/lobby/leave", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob IsNot Nothing Then hub.Leave(lob.Id, acc.AccountID)
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' --- live position report (mod POSTs ~10Hz during play) ---
            a.MapPost("/api/versus/match/pos", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                Dim body = RestApi.ReadJson(ctx)
                hub.UpdatePos(lob, acc.AccountID,
                    RestApi.Num(body, "x", 0),
                    CInt(RestApi.Num(body, "percent", 0)),
                    CInt(RestApi.Num(body, "attempts", 0)),
                    RestApi.Num(body, "dead", 0) <> 0)
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' --- live match state (mod polls ~10Hz) ---
            a.MapGet("/api/versus/match/state", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                hub.Tick(lob)
                Return RestApi.Ok(MatchDto(lob, acc.AccountID))
            End Function)

            ' --- report completion ---
            a.MapPost("/api/versus/match/finish", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                hub.Finish(lob, acc.AccountID, CInt(RestApi.Num(RestApi.ReadJson(ctx), "deaths", 0)))
                hub.Tick(lob)
                Return RestApi.Ok(New With {.ok = True})
            End Function)

            ' --- results + rewards (idempotent; applies rewards once) ---
            a.MapGet("/api/versus/match/results", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.[Error](ctx, 401, "unauthorized")
                Dim lob = hub.LobbyOf(acc.AccountID)
                If lob Is Nothing Then Return RestApi.[Error](ctx, 404, "no_lobby")
                hub.Tick(lob)
                If lob.State <> VersusHub.StateResults Then Return RestApi.Ok(New With {.ready = False, .state = lob.State})
                Dim ranked = lob.Ranked
                Dim ordered = hub.Rank(lob)
                Dim rr = ApplyRewards(db, lob, ordered)
                Dim rewards = rr.rewards
                Dim stake = rr.stake
                Return RestApi.Ok(New With {
                    .ready = True,
                    .ranked = ranked,
                    .stake = stake,
                    .level = lob.ChosenLevel,
                    .levelName = lob.ChosenLevelName,
                    .rankings = ordered.Select(Function(m, i) New With {
                        .place = i + 1,
                        .accountID = m.AccountID,
                        .name = m.Name,
                        .icon = m.IconData,
                        .finished = m.Finished AndAlso m.FinishMs <= lob.PlayEndsMs,
                        .percent = m.Percent,
                        .deaths = m.Deaths,
                        .timeMs = If(m.Finished, m.FinishMs - lob.CountdownEndsMs, 0),
                        .reward = rewards.GetValueOrDefault(m.AccountID).starDelta,
                        .starDelta = rewards.GetValueOrDefault(m.AccountID).starDelta,
                        .coinDelta = rewards.GetValueOrDefault(m.AccountID).coinDelta
                    }).ToList()
                })
            End Function)
        End Sub

        ' Build a lobby member from the account + its GdUser (for cube rendering).
        Private Function MakeMember(db As Database, acc As Account) As VersusHub.Member
            Dim u = db.FindUserByExt(acc.AccountID.ToString())
            Return New VersusHub.Member With {
                .AccountID = acc.AccountID,
                .Name = acc.UserName,
                .IconData = IconString(u)}
        End Function

        ' ":"-packed cube icon for the mod to render other players.
        ' iconID:color1:color2:color3:glow
        Private Function IconString(u As GdUser) As String
            If u Is Nothing Then Return "1:0:3:-1:0"
            Dim cube = If(u.AccIcon > 0, u.AccIcon, Math.Max(1, u.Icon))
            Return $"{cube}:{u.Color1}:{u.Color2}:{u.Color3}:{If(u.AccGlow > 0, 1, 0)}"
        End Function

        Private Function LobbyDto(lob As VersusHub.Lobby, viewerId As Integer) As Object
            SyncLock lob.Gate
                Return New With {
                    .id = lob.Id,
                    .state = lob.State,
                    .ranked = lob.Ranked,
                    .host = lob.HostID,
                    .isHost = lob.HostID = viewerId,
                    .timeMultiplier = lob.TimeMultiplier,
                    .timeLimitSec = lob.TimeLimitSec,
                    .chosenLevel = lob.ChosenLevel,
                    .chosenLevelName = lob.ChosenLevelName,
                    .countdownMs = Math.Max(0, lob.CountdownEndsMs - VersusHub.NowMs()),
                    .members = lob.Members.Select(Function(m) New With {
                        .accountID = m.AccountID, .name = m.Name, .icon = m.IconData,
                        .ready = m.Ready, .vote = m.Vote}).ToList(),
                    .pool = lob.Pool.Select(Function(p) New With {.id = p.LevelID, .name = p.Name, .seconds = p.PlaySeconds}).ToList()
                }
            End SyncLock
        End Function

        Private Function MatchDto(lob As VersusHub.Lobby, viewerId As Integer) As Object
            SyncLock lob.Gate
                Return New With {
                    .state = lob.State,
                    .level = lob.ChosenLevel,
                    .countdownMs = Math.Max(0, lob.CountdownEndsMs - VersusHub.NowMs()),
                    .remainingMs = Math.Max(0, lob.PlayEndsMs - VersusHub.NowMs()),
                    .players = lob.Members.Select(Function(m) New With {
                        .accountID = m.AccountID, .name = m.Name, .icon = m.IconData,
                        .x = m.PosX, .percent = m.Percent, .attempts = m.Attempts,
                        .deaths = m.Deaths, .finished = m.Finished}).ToList()
                }
            End SyncLock
        End Function

        ''' <summary>
        ''' Resolve and apply the match payout. Applied exactly once per lobby
        ''' (guarded by RewardsApplied).
        '''
        ''' Ranked with a real stake: the stake is sized by the POOREST player so a
        ''' rich player can't drain a poor one — stakePerPlayer = Floor(minStars*0.10)
        ''' (and a coin stake from the poorest coin balance). The 1st-place finisher
        ''' takes each loser's stake; every non-winner loses their stake, capped at
        ''' their current balance (never negative), and the winner only collects what
        ''' was actually available. If nobody finished in time it's a no-contest (0s).
        '''
        ''' Unranked lobbies — and ranked lobbies where the stake resolves to 0
        ''' (poorest player under 10 stars, "nothing to fight for") — pay completion
        ''' style instead: finishers get the level's baseStars+baseCoins, non-finishers
        ''' a small positive consolation. No stars are ever lost in this path.
        ''' </summary>
        Private Function ApplyRewards(db As Database, lob As VersusHub.Lobby, ordered As List(Of VersusHub.Member)) _
                As (rewards As Dictionary(Of Integer, (starDelta As Integer, coinDelta As Integer)), stake As Integer)
            Dim result As New Dictionary(Of Integer, (starDelta As Integer, coinDelta As Integer))()
            SyncLock lob.Gate
                If lob.RewardsApplied Then
                    Return (result, 0)
                End If
                lob.RewardsApplied = True
            End SyncLock

            If ordered.Count = 0 Then Return (result, 0)

            ' Snapshot each member's current balance — used both for the poorest-player
            ' stake sizing and for capping loser deductions at what they actually have.
            Dim balances As New Dictionary(Of Integer, (stars As Integer, coins As Integer))()
            For Each m In ordered
                Dim gu = db.FindUserByExt(m.AccountID.ToString())
                balances(m.AccountID) = (If(gu IsNot Nothing, gu.Stars, 0), If(gu IsNot Nothing, gu.UserCoins, 0))
            Next

            ' Base pot from the chosen level (min 10 stars so unrated levels still pay).
            Dim baseStars As Integer = 10
            Dim baseCoins As Integer = 0
            Dim lvl = db.Levels.Read(Function(rows) rows.FirstOrDefault(Function(l) l.LevelID = lob.ChosenLevel))
            If lvl IsNot Nothing Then
                baseStars = Math.Max(10, lvl.Stars)
                baseCoins = lvl.Coins
            End If

            ' Stake sized by the poorest participant (ranked only).
            Dim stakePerPlayer As Integer = 0
            Dim coinStake As Integer = 0
            If lob.Ranked Then
                Dim minStars = ordered.Min(Function(m) balances(m.AccountID).stars)
                Dim minCoins = ordered.Min(Function(m) balances(m.AccountID).coins)
                stakePerPlayer = Math.Max(0, CInt(Math.Floor(minStars * 0.1)))
                coinStake = Math.Max(0, CInt(Math.Floor(minCoins * 0.1)))
            End If

            Dim completionMode = (Not lob.Ranked) OrElse (stakePerPlayer <= 0)

            If completionMode Then
                ' Completion-style payout: finishers get the level reward, non-finishers
                ' a small positive consolation. Never negative.
                Dim consolation = Math.Max(1, CInt(Math.Floor(baseStars * 0.25)))
                For Each m In ordered
                    Dim finishedInTime = m.Finished AndAlso m.FinishMs <= lob.PlayEndsMs
                    If finishedInTime Then
                        result(m.AccountID) = (baseStars, baseCoins)
                    Else
                        result(m.AccountID) = (consolation, 0)
                    End If
                Next
            Else
                ' Ranked stake battle. Winner = 1st place, but only if they finished in
                ' time; otherwise no-contest (nobody won → nobody loses).
                Dim winner = ordered(0)
                Dim winnerWon = winner.Finished AndAlso winner.FinishMs <= lob.PlayEndsMs
                If winnerWon Then
                    Dim potStars As Integer = 0
                    Dim potCoins As Integer = 0
                    For i = 1 To ordered.Count - 1
                        Dim m = ordered(i)
                        ' A loser can't lose more than they hold (no negative balance).
                        Dim lossStars = Math.Min(stakePerPlayer, balances(m.AccountID).stars)
                        Dim lossCoins = Math.Min(coinStake, balances(m.AccountID).coins)
                        result(m.AccountID) = (-lossStars, -lossCoins)
                        potStars += lossStars
                        potCoins += lossCoins
                    Next
                    ' Winner collects only what was actually available.
                    result(winner.AccountID) = (potStars, potCoins)
                Else
                    For Each m In ordered
                        result(m.AccountID) = (0, 0)
                    Next
                End If
            End If

            ' Persist. Math.Max(0, ...) keeps balances non-negative even if they moved
            ' since the snapshot.
            For Each m In ordered
                Dim d = result(m.AccountID)
                Dim aid = m.AccountID
                db.Users.Write(Sub(rows)
                                   Dim u = rows.FirstOrDefault(Function(x) x.ExtID = aid.ToString())
                                   If u IsNot Nothing Then
                                       u.Stars = Math.Max(0, u.Stars + d.starDelta)
                                       u.UserCoins = Math.Max(0, u.UserCoins + d.coinDelta)
                                   End If
                               End Sub)
            Next
            Return (result, stakePerPlayer)
        End Function

    End Module

End Namespace
