Imports System.IO
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Http
Imports Microsoft.Extensions.DependencyInjection
Imports PetusCore.Services

Namespace Api

    ''' <summary>
    ''' The "special API" the PetusGDPS DevelNext launcher/installer talks to.
    ''' Two routes: a manifest describing the packaged client, and a streaming
    ''' download that reports Content-Length so the installer can show a real
    ''' progress bar. The zip itself lives outside the repo — point at it with
    ''' PETUS_GAME_ZIP (path to a prebuilt PetusGDPS.zip).
    ''' </summary>
    Public Module LauncherApi

        Public Sub Map(app As WebApplication)
            Dim cfg = app.Services.GetRequiredService(Of ServerConfig)()

            ' ---- Manifest: what to download and how big it is ------------
            app.MapGet("/api/launcher/manifest", Function()
                Dim size As Long = 0
                Dim available = False
                If cfg.GameZipPath <> "" AndAlso File.Exists(cfg.GameZipPath) Then
                    size = New FileInfo(cfg.GameZipPath).Length
                    available = True
                End If
                Return Results.Json(New With {
                    .name = cfg.ServerName,
                    .version = ServerConfig.Version,
                    .gdVersion = "2.2",
                    .exe = cfg.GameExe,
                    .size = size,
                    .available = available,
                    .url = "/api/launcher/download"
                })
            End Function)

            ' ---- Download: stream the packaged client with Content-Length -
            app.MapGet("/api/launcher/download", Function(ctx As HttpContext)
                If cfg.GameZipPath = "" OrElse Not File.Exists(cfg.GameZipPath) Then
                    Return Results.NotFound(New With {.error = "game_zip_not_configured"})
                End If
                ' enableRangeProcessing lets the installer resume / seek.
                Return Results.File(cfg.GameZipPath, "application/zip", "PetusGDPS.zip", enableRangeProcessing:=True)
            End Function)
        End Sub

    End Module

End Namespace
