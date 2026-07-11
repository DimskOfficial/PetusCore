Namespace Data.Models

    ''' <summary>A registered account (login credentials + profile socials).</summary>
    Public Class Account
        Public Property AccountID As Integer
        Public Property UserName As String = ""
        ''' <summary>BCrypt hash of the raw password (legacy GJP path).</summary>
        Public Property Password As String = ""
        ''' <summary>BCrypt hash of the GJP2 pre-hash (sha1(pass+salt)).</summary>
        Public Property Gjp2 As String = ""
        Public Property Email As String = ""
        Public Property IsActive As Boolean = True
        ''' <summary>0 = user. Higher values reserved; admin flag below is authoritative for the site.</summary>
        Public Property IsAdmin As Integer = 0
        Public Property RegisterDate As Long = 0
        Public Property FriendsCount As Integer = 0
        ' Privacy: messages / friend-requests / comment-history state
        Public Property MS As Integer = 0
        Public Property FrS As Integer = 0
        Public Property CS As Integer = 0
        ' Socials
        Public Property Youtube As String = ""
        Public Property Twitter As String = ""
        Public Property Twitch As String = ""
        Public Property Discord As String = ""
        Public Property Instagram As String = ""
        Public Property Tiktok As String = ""
        ' Roles / moderation
        Public Property ModLevel As Integer = 0     ' 0 none, 1 mod (elder=2)
        Public Property IsBanned As Boolean = False
        ' Account recovery
        Public Property RecoveryCode As String = ""
        Public Property RecoveryExpires As Long = 0
        ''' <summary>Linked Petus ID subject (OAuth `sub`). Empty = not linked.</summary>
        Public Property PetusId As String = ""
    End Class

    ''' <summary>In-game user (stats + icons), linked to an account via ExtID.</summary>
    Public Class GdUser
        Public Property UserID As Integer
        Public Property IsRegistered As Integer = 1
        ''' <summary>Account ID (as string) for registered users, or a UDID for green accounts.</summary>
        Public Property ExtID As String = ""
        Public Property UserName As String = "undefined"
        Public Property Stars As Integer = 0
        Public Property Demons As Integer = 0
        Public Property Diamonds As Integer = 0
        Public Property Moons As Integer = 0
        Public Property Coins As Integer = 0
        Public Property UserCoins As Integer = 0
        Public Property Orbs As Integer = 0
        Public Property CompletedLvls As Integer = 0
        Public Property CreatorPoints As Double = 0
        ' Icons / cosmetics
        Public Property Icon As Integer = 0
        Public Property Color1 As Integer = 0
        Public Property Color2 As Integer = 3
        Public Property Color3 As Integer = -1
        Public Property IconType As Integer = 0
        Public Property Special As Integer = 0
        Public Property AccIcon As Integer = 0
        Public Property AccShip As Integer = 0
        Public Property AccBall As Integer = 0
        Public Property AccBird As Integer = 0
        Public Property AccDart As Integer = 0
        Public Property AccRobot As Integer = 0
        Public Property AccGlow As Integer = 0
        Public Property AccSpider As Integer = 0
        Public Property AccSwing As Integer = 0
        Public Property AccJetpack As Integer = 0
        Public Property AccExplosion As Integer = 0
        ' Misc
        Public Property GameVersion As Integer = 0
        Public Property Secret As String = "none"
        Public Property IP As String = "127.0.0.1"
        Public Property LastPlayed As Long = 0
        Public Property IsBanned As Integer = 0
        Public Property IsCreatorBanned As Integer = 0
        ' Chests (daily/weekly rewards)
        Public Property Chest1Time As Long = 0
        Public Property Chest2Time As Long = 0
        Public Property Chest1Count As Integer = 0
        Public Property Chest2Count As Integer = 0
    End Class

    ''' <summary>An uploaded level.</summary>
    Public Class Level
        Public Property LevelID As Integer
        Public Property UserID As Integer
        Public Property ExtID As String = ""
        Public Property UserName As String = ""
        Public Property LevelName As String = ""
        Public Property LevelDesc As String = ""
        ''' <summary>The actual (compressed/encoded) level data.</summary>
        Public Property LevelString As String = ""
        Public Property Version As Integer = 1
        Public Property GameVersion As Integer = 22
        Public Property BinaryVersion As Integer = 42
        Public Property Downloads As Integer = 0
        Public Property Likes As Integer = 0
        Public Property Dislikes As Integer = 0
        Public Property Length As Integer = 0
        Public Property Audiotrack As Integer = 0
        Public Property SongID As Integer = 0
        Public Property Password As String = "0"
        Public Property Unlisted As Integer = 0
        Public Property Objects As Integer = 0
        Public Property Coins As Integer = 0
        Public Property RequestedStars As Integer = 0
        Public Property TwoPlayer As Integer = 0
        Public Property SongIDs As String = ""
        Public Property SfxIDs As String = ""
        Public Property Ts As Integer = 0             ' verification / editor time
        Public Property Wt As Integer = 0
        Public Property Wt2 As Integer = 0
        Public Property SettingsString As String = ""
        Public Property ExtraString As String = ""
        Public Property LevelInfo As String = ""
        Public Property Original As Integer = 0
        Public Property Ldm As Integer = 0
        ' Rating (assigned by mods)
        Public Property Stars As Integer = 0
        Public Property Difficulty As Integer = 0      ' 0-5 star difficulty face
        Public Property Demon As Integer = 0
        Public Property DemonDiff As Integer = 0
        Public Property Auto As Integer = 0
        Public Property Featured As Integer = 0        ' feature score (0 = not featured)
        Public Property Epic As Integer = 0            ' 0 none, 1 epic, 2 legendary, 3 mythic
        Public Property RateCoins As Integer = 0       ' verified/silver coins
        Public Property IsDaily As Integer = 0         ' 0 no, 1 daily, 2 weekly
        Public Property DailyID As Integer = 0
        ' Timestamps
        Public Property UploadDate As Long = 0
        Public Property UpdateDate As Long = 0
        Public Property RatedAt As Long = 0
        Public Property RatedBy As Integer = 0
        ''' <summary>Preview image URL (imgbb), captured by the mod at 50% on verify.</summary>
        Public Property PreviewUrl As String = ""
    End Class

    ''' <summary>A comment left on a level.</summary>
    Public Class Comment
        Public Property CommentID As Integer
        Public Property UserID As Integer
        Public Property LevelID As Integer
        Public Property Content As String = ""     ' base64 comment text
        Public Property Likes As Integer = 0
        Public Property Percent As Integer = 0
        Public Property IsSpam As Integer = 0
        Public Property Timestamp As Long = 0
    End Class

    ''' <summary>A comment left on an account's profile.</summary>
    Public Class AccountComment
        Public Property CommentID As Integer
        Public Property AccountID As Integer
        Public Property UserID As Integer
        Public Property Content As String = ""     ' base64 comment text
        Public Property Likes As Integer = 0
        Public Property IsSpam As Integer = 0
        Public Property Timestamp As Long = 0
    End Class

    ''' <summary>A Newgrounds / custom song entry.</summary>
    Public Class Song
        Public Property ID As Integer
        Public Property Name As String = ""
        Public Property ArtistID As Integer = 0
        Public Property ArtistName As String = ""
        Public Property Size As Double = 0
        Public Property Download As String = ""      ' direct mp3 url
        Public Property IsDisabled As Integer = 0
        Public Property UploadedBy As Integer = 0    ' accountID who added it via the site
    End Class

    ''' <summary>Per-level score (percent leaderboard).</summary>
    Public Class LevelScore
        Public Property LevelID As Integer
        Public Property AccountID As Integer
        Public Property UserID As Integer
        Public Property Percent As Integer = 0
        Public Property Attempts As Integer = 0
        Public Property Coins As Integer = 0
        Public Property Timestamp As Long = 0
    End Class

    Public Class FriendRequest
        Public Property ID As Integer
        Public Property AccountID As Integer       ' recipient
        Public Property AccountIDFrom As Integer   ' sender
        Public Property Comment As String = ""
        Public Property IsNew As Integer = 1
        Public Property UploadDate As Long = 0
    End Class

    Public Class Friendship
        Public Property ID As Integer
        Public Property Account1 As Integer
        Public Property Account2 As Integer
        Public Property IsNew1 As Integer = 0
        Public Property IsNew2 As Integer = 0
    End Class

    Public Class Block
        Public Property ID As Integer
        Public Property AccountID As Integer
        Public Property BlockedID As Integer
    End Class

    Public Class Message
        Public Property MessageID As Integer
        Public Property AccountID As Integer        ' recipient
        Public Property AccountIDFrom As Integer    ' sender
        Public Property Subject As String = ""
        Public Property Body As String = ""
        Public Property IsNew As Integer = 1
        Public Property Timestamp As Long = 0
    End Class

    ''' <summary>A moderation action (audit log).</summary>
    Public Class ModAction
        Public Property ID As Integer
        Public Property AccountID As Integer        ' who did it
        Public Property Action As String = ""
        Public Property Target As String = ""
        Public Property Value As String = ""
        Public Property Timestamp As Long = 0
    End Class

    ''' <summary>A REST API session token issued to the website.</summary>
    Public Class ApiToken
        Public Property Token As String = ""
        Public Property AccountID As Integer
        Public Property IssuedAt As Long = 0
        Public Property ExpiresAt As Long = 0
    End Class

    ''' <summary>Simple key/value counter row used for auto-increment IDs.</summary>
    Public Class Counter
        Public Property Name As String = ""
        Public Property Value As Integer = 0
    End Class

    ''' <summary>A map pack (a themed bundle of levels with a reward).</summary>
    Public Class MapPack
        Public Property ID As Integer
        Public Property Name As String = ""
        Public Property Levels As String = ""      ' comma-separated level IDs
        Public Property Stars As Integer = 0
        Public Property Coins As Integer = 0
        Public Property Difficulty As Integer = 0  ' 0 auto .. 5 insane, 6 demon
        Public Property Color As String = "255,255,255"   ' text/RGB
        Public Property Color2 As String = "255,255,255"  ' bar/RGB
    End Class

    ''' <summary>A gauntlet (fixed 5-level challenge set).</summary>
    Public Class Gauntlet
        Public Property ID As Integer              ' gauntlet type id (1..15+)
        Public Property Levels As String = ""      ' comma-separated 5 level IDs
    End Class

    ''' <summary>A quest / challenge (getGJChallenges).</summary>
    Public Class Quest
        Public Property ID As Integer
        Public Property Type As Integer = 0        ' 1 orbs, 2 coins, 3 stars
        Public Property Amount As Integer = 0
        Public Property Reward As Integer = 0
        Public Property Name As String = ""
    End Class

    ''' <summary>Uploaded music file bytes, stored in the DB (no filesystem).
    ''' Data is base64 so it maps to a plain text column.</summary>
    Public Class MusicFile
        Public Property ID As Integer
        Public Property Data As String = ""        ' base64-encoded mp3
    End Class

    ''' <summary>An overridable built-in "Play" level slot (Stereo Madness, ...).
    ''' Points a fixed slot at a real uploaded level, with a display-name override.</summary>
    Public Class DefaultLevel
        Public Property Slot As Integer            ' 1..N, the in-game main-level id
        Public Property LevelID As Integer = 0     ' catalog level to serve here
        Public Property Name As String = ""        ' display name override (optional)
        Public Property Enabled As Integer = 1
    End Class

    ''' <summary>Records that an account liked a level, to prevent double-likes
    ''' from the website.</summary>
    Public Class LevelLike
        Public Property ID As Integer
        Public Property LevelID As Integer
        Public Property AccountID As Integer
        Public Property Value As Integer = 1       ' 1 like, -1 dislike
    End Class

End Namespace
