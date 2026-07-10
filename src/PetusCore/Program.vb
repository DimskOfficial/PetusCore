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
        ' DB path can be overridden with PETUS_DB_PATH (Dokploy volume mount).
        Dim dbPath = Environment.GetEnvironmentVariable("PETUS_DB_PATH")
        If String.IsNullOrWhiteSpace(dbPath) Then
            dbPath = Path.Combine(builder.Environment.ContentRootPath, "..", "..", "database")
        End If
        dbPath = Path.GetFullPath(dbPath)

        Dim cfg = ServerConfig.Load(builder.Configuration, dbPath)

        ' --- Services ------------------------------------------------------
        builder.Services.AddSingleton(Of ServerConfig)(cfg)
        builder.Services.AddSingleton(Of Database)(New Database(cfg.DatabasePath))
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
        GdEndpoints.Map(app)
        RestApi.Map(app)
        LauncherApi.Map(app)
        PetusIdApi.Map(app)

        ' Simple health check for Dokploy.
        app.MapGet("/health", Function() Results.Ok(New With {.status = "ok", .server = "PetusCore", .version = ServerConfig.Version}))
        app.MapGet("/", Function() Results.Text(
            $"PetusCore {ServerConfig.Version} — PetusGDPS server core is running." & vbLf &
            "Game endpoints: /accounts/*, /getGJLevels21.php, etc." & vbLf &
            "REST API: /api/*  |  Health: /health", "text/plain"))

        app.Run()
    End Sub

End Module
