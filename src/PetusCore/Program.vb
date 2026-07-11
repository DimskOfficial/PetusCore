Imports System.IO
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Hosting
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports PetusCore.Api
Imports PetusCore.Data
Imports PetusCore.Endpoints
Imports PetusCore.Services

''' <summary>
''' PetusCore — Geometry Dash 2.2 private server core (PetusGDPS).
''' Serves both the Boomlings-style endpoints the game client talks to
''' and a clean REST API consumed by the PetusGDPS website.
''' Storage is plain JSON files inside the "database" folder — no SQL needed.
''' </summary>
Module Program

    Sub Main(args As String())
        Dim builder = WebApplication.CreateBuilder(args)

        ' --- Configuration -------------------------------------------------
        ' PostgreSQL connection string (required). Dokploy: point at your PG service.
        Dim dbUrl = Environment.GetEnvironmentVariable("PETUS_DB_URL")
        If String.IsNullOrWhiteSpace(dbUrl) Then dbUrl = builder.Configuration("DbUrl")
        If String.IsNullOrWhiteSpace(dbUrl) Then
            Throw New InvalidOperationException("PETUS_DB_URL is required (PostgreSQL connection string).")
        End If

        Dim cfg = ServerConfig.Load(builder.Configuration, dbUrl)

        ' --- Services ------------------------------------------------------
        builder.Services.AddSingleton(Of ServerConfig)(cfg)
        builder.Services.AddSingleton(Of Database)(New Database(dbUrl))
        builder.Services.AddSingleton(Of PasswordService)()
        builder.Services.AddSingleton(Of HashService)()
        builder.Services.AddSingleton(Of TokenService)()
        builder.Services.AddCors()
        builder.Services.AddHttpContextAccessor()

        ' Listen on 0.0.0.0:PORT (Dokploy provides PORT, defaults to 8080).
        Dim port = Environment.GetEnvironmentVariable("PORT")
        If String.IsNullOrWhiteSpace(port) Then port = "8080"
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}")

        Dim app = builder.Build()

        ' Ensure the database is seeded on first boot.
        app.Services.GetRequiredService(Of Database)().EnsureSeeded()

        app.UseCors(Function(policy) policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod())
        app.UseStaticFiles()

        ' URL-prefix stripping: when the GD client is hex-patched to point at
        ' e.g. http://127.0.0.1:8080/gdp, every request arrives as
        ' /gdp/database/<endpoint>. Strip a configurable prefix so the handlers
        ' (registered at /accounts/*, /getGJLevels21.php, ...) still match.
        ' Configure with PETUS_PATH_PREFIX (default "/database" also stripped).
        Dim rawPrefix = Environment.GetEnvironmentVariable("PETUS_PATH_PREFIX")
        Dim prefixes = New List(Of String)()
        If Not String.IsNullOrWhiteSpace(rawPrefix) Then prefixes.Add("/" & rawPrefix.Trim("/"c))
        prefixes.Add("/gd")       ' hex-patched client points at cgdps.petus.ru/gd/database/*
        prefixes.Add("/database") ' GD always appends /database/<endpoint>
        app.Use(Async Function(context As HttpContext, nextMiddleware As Func(Of Task))
                    Dim p = context.Request.Path.Value
                    If p IsNot Nothing Then
                        For Each pre In prefixes
                            If p.StartsWith(pre & "/", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(p, pre, StringComparison.OrdinalIgnoreCase) Then
                                Dim rest = p.Substring(pre.Length)
                                If rest = "" Then rest = "/"
                                context.Request.Path = New PathString(rest)
                                p = rest
                            End If
                        Next
                    End If
                    Await nextMiddleware()
                End Function)

        ' Explicit UseRouting so the rewrite above runs BEFORE route matching
        ' (otherwise WebApplication auto-inserts routing at the pipeline start).
        app.UseRouting()

        ' Map the GD game endpoints and the website REST API.
        GdAuth.Init(app.Services.GetRequiredService(Of TokenService)())
        GdEndpoints.Map(app)
        RestApi.Map(app)
        LauncherApi.Map(app)
        PetusIdApi.Map(app)
        SiteApi.Map(app)

        ' Simple health check for Dokploy.
        app.MapGet("/health", Function() Results.Ok(New With {.status = "ok", .server = "PetusCore", .version = ServerConfig.Version}))

        ' Plain white landing page (VK-2010 style) for cgdps.petus.ru.
        Dim landing = "<!doctype html><html lang=""ru""><head><meta charset=""utf-8"">" &
            "<meta name=""viewport"" content=""width=device-width, initial-scale=1"">" &
            "<title>PetusCore</title><style>" &
            "html,body{height:100%;margin:0}" &
            "body{background:#fff;color:#333;font-family:Tahoma,Geneva,sans-serif;" &
            "display:flex;align-items:center;justify-content:center;text-align:center}" &
            ".box{max-width:520px;padding:24px}" &
            ".t{font-size:22px;color:#2b587a;font-weight:bold;margin-bottom:8px}" &
            ".s{font-size:13px;color:#555;line-height:1.6}" &
            "a{color:#2b587a;text-decoration:none}a:hover{text-decoration:underline}" &
            "</style></head><body><div class=""box"">" &
            "<div class=""t"">PetusCore</div>" &
            "<div class=""s"">Игровое ядро приватного сервера Geometry Dash.<br>" &
            "Это технический сервис. Основной сайт: <a href=""https://gdps.petus.ru"">gdps.petus.ru</a>.</div>" &
            "</div></body></html>"
        app.MapGet("/", Function() Results.Content(landing, "text/html; charset=utf-8"))

        app.Run()
    End Sub

End Module
