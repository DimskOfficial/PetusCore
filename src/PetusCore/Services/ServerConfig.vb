Imports Microsoft.Extensions.Configuration

Namespace Services

    ''' <summary>
    ''' Central server configuration, read from environment variables /
    ''' appsettings.json. Everything has a sensible default so the server
    ''' boots with zero config for local testing.
    ''' </summary>
    Public Class ServerConfig

        Public Const Version As String = "2.6.0-bplevels"

        ''' <summary>PostgreSQL connection string (PETUS_DB_URL).</summary>
        Public Property DbUrl As String

        ''' <summary>Salt appended before hashing GJP2 passwords (matches GD).</summary>
        Public Property Gjp2Salt As String = "mI29fmAnxgTs"

        ''' <summary>XOR key used by the legacy GJP login scheme.</summary>
        Public Property GjpXorKey As String = "37526"

        ''' <summary>Salt used for the level/leaderboard integrity hashes.</summary>
        Public Property LevelHashSalt As String = "xI25fpAapCQg"

        ''' <summary>Secret used to sign REST API session tokens.</summary>
        Public Property ApiSecret As String = "change-me-in-production"

        ''' <summary>Server display name shown on the site / API.</summary>
        Public Property ServerName As String = "PetusGDPS"

        ''' <summary>If true, new accounts are active immediately.</summary>
        Public Property PreactivateAccounts As Boolean = True

        ''' <summary>If true, green (unregistered) accounts may upload etc.</summary>
        Public Property UnregisteredSubmissions As Boolean = False

        ''' <summary>Direct download URL for the modified GD client (site button).</summary>
        Public Property GameDownloadUrl As String = ""

        ''' <summary>Absolute path to the packaged game .zip served by the launcher.</summary>
        Public Property GameZipPath As String = ""

        ''' <summary>Executable name the launcher runs after install (inside the zip).</summary>
        Public Property GameExe As String = "GeometryDash.exe"

        ''' <summary>Current game build version the launcher should install.</summary>
        Public Property GameVersion As String = "1.0.6"

        ''' <summary>Download URL of the current game .zip (versioned, CDN-cache-proof).</summary>
        Public Property GameZipUrl As String = "https://cdn.petus.goonhost.rocks/game/petusgdps-1.0.6.zip"

        ''' <summary>Username auto-promoted to admin on login (bootstrap the first admin).</summary>
        Public Property AdminUser As String = ""

        ''' <summary>
        ''' Public base URL of the server (e.g. https://gdps.petus.ru). When set,
        ''' getAccountURL / getCustomContentURL return this instead of the request
        ''' host — required behind a reverse proxy so the GD client saves accounts
        ''' and downloads content from the real domain, not the internal address.
        ''' </summary>
        Public Property PublicUrl As String = ""

        Public Shared Function Load(config As IConfiguration, dbUrl As String) As ServerConfig
            Dim c As New ServerConfig With {.DbUrl = dbUrl}

            c.ApiSecret = Env("PETUS_API_SECRET", config("ApiSecret"), c.ApiSecret)
            c.ServerName = Env("PETUS_SERVER_NAME", config("ServerName"), c.ServerName)
            c.Gjp2Salt = Env("PETUS_GJP2_SALT", config("Gjp2Salt"), c.Gjp2Salt)
            c.GameDownloadUrl = Env("PETUS_GAME_DOWNLOAD_URL", config("GameDownloadUrl"), c.GameDownloadUrl)
            c.GameZipPath = Env("PETUS_GAME_ZIP", config("GameZipPath"), c.GameZipPath)
            c.GameExe = Env("PETUS_GAME_EXE", config("GameExe"), c.GameExe)
            c.GameVersion = Env("PETUS_GAME_VERSION", config("GameVersion"), c.GameVersion)
            c.GameZipUrl = Env("PETUS_GAME_ZIP_URL", config("GameZipUrl"), c.GameZipUrl)
            c.AdminUser = Env("PETUS_ADMIN_USER", config("AdminUser"), c.AdminUser)
            c.PublicUrl = If(Env("PETUS_PUBLIC_URL", config("PublicUrl"), ""), "").TrimEnd("/"c)

            Dim pre = Env("PETUS_PREACTIVATE", config("PreactivateAccounts"), Nothing)
            If pre IsNot Nothing Then Boolean.TryParse(pre, c.PreactivateAccounts)

            Return c
        End Function

        Private Shared Function Env(name As String, ParamArray fallbacks As String()) As String
            Dim v = Environment.GetEnvironmentVariable(name)
            If Not String.IsNullOrWhiteSpace(v) Then Return v
            For Each f In fallbacks
                If Not String.IsNullOrWhiteSpace(f) Then Return f
            Next
            Return Nothing
        End Function

    End Class

End Namespace
