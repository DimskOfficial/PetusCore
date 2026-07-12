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

            ' --- eligibility: Versus tab is only for non-empty (registered, with
            ' at least some progress) accounts. The mod calls this to decide
            ' whether to unlock the tab. ---
            a.MapGet("/api/versus/eligible", Function(ctx As HttpContext)
                Dim acc = RestApi.RequireAuth(ctx, tokens)
                If acc Is Nothing Then Return RestApi.Ok(New With {.eligible = False})
                Dim u = db.FindUserByExt(acc.AccountID.ToString())
                Dim hasProgress = u IsNot Nothing AndAlso (u.Stars > 0 OrElse u.Diamonds > 0 OrElse u.UserCoins > 0 OrElse u.CompletedLvls > 0)
                Return RestApi.Ok(New With {.eligible = acc.IsBanned = False AndAlso hasProgress, .name = acc.UserName})
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
                Dim rewards = ApplyRewards(db, lob, ordered)
                Return RestApi.Ok(New With {
                    .ready = True,
                    .ranked = ranked,
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
                        .reward = rewards.GetValueOrDefault(m.AccountID, 0)
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
        ''' Reward the ranked lobby: winner gets 3× the level's stars+coins, 2nd
        ''' 2×, 3rd 1×, everyone else who finished a small consolation, and players
        ''' who didn't finish LOSE stars. Unranked lobbies award nothing. Applied
        ''' exactly once per lobby (guarded by RewardsApplied).
        ''' </summary>
        Private Function ApplyRewards(db As Database, lob As VersusHub.Lobby, ordered As List(Of VersusHub.Member)) As Dictionary(Of Integer, Integer)
            Dim result As New Dictionary(Of Integer, Integer)()
            SyncLock lob.Gate
                If lob.RewardsApplied OrElse Not lob.Ranked Then
                    Return result
                End If
                lob.RewardsApplied = True
            End SyncLock

            ' Base pot from the chosen level (min 10 stars so unrated levels still pay).
            Dim baseStars As Integer = 10
            Dim baseCoins As Integer = 0
            Dim lvl = db.Levels.Read(Function(rows) rows.FirstOrDefault(Function(l) l.LevelID = lob.ChosenLevel))
            If lvl IsNot Nothing Then
                baseStars = Math.Max(10, lvl.Stars)
                baseCoins = lvl.Coins
            End If

            ' Multipliers by place. Finishers within the limit only.
            For i = 0 To ordered.Count - 1
                Dim m = ordered(i)
                Dim finishedInTime = m.Finished AndAlso m.FinishMs <= lob.PlayEndsMs
                Dim starDelta As Integer
                If finishedInTime Then
                    Select Case i
                        Case 0 : starDelta = baseStars * 3
                        Case 1 : starDelta = baseStars * 2
                        Case 2 : starDelta = baseStars * 1
                        Case Else : starDelta = CInt(baseStars * 0.5)
                    End Select
                Else
                    starDelta = -Math.Max(2, CInt(baseStars * 0.25))   ' DNF loses stars
                End If

                Dim coinDelta As Integer = 0
                If finishedInTime Then
                    Select Case i
                        Case 0 : coinDelta = baseCoins * 3
                        Case 1 : coinDelta = baseCoins * 2
                        Case 2 : coinDelta = baseCoins
                    End Select
                End If

                result(m.AccountID) = starDelta
                Dim aid = m.AccountID
                db.Users.Write(Sub(rows)
                                   Dim u = rows.FirstOrDefault(Function(x) x.ExtID = aid.ToString())
                                   If u IsNot Nothing Then
                                       u.Stars = Math.Max(0, u.Stars + starDelta)
                                       u.UserCoins = Math.Max(0, u.UserCoins + coinDelta)
                                   End If
                               End Sub)
            Next
            Return result
        End Function

    End Module

End Namespace
